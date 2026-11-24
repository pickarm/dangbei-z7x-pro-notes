#!/system/bin/sh
set -e
umask 077
base=/data/local/codex-volume
policy_changed=0
restore_policy(){
  expected=$(sha256sum "$base/policy-before" | cut -d ' ' -f 1)
  restore_file="$base/policy-before"
  for attempt in 1 2 3; do
    size=$(wc -c < "$restore_file")
    dd if="$restore_file" of=/sys/fs/selinux/load bs="$size" count=1 2>/dev/null || return 1
    actual=$(sha256sum /sys/fs/selinux/policy | cut -d ' ' -f 1)
    [ "$actual" = "$expected" ] && return 0
    cat /sys/fs/selinux/policy > "$base/policy-order-$attempt"
    restore_file="$base/policy-order-$attempt"
  done
  return 1
}
cleanup(){
  [ "$policy_changed" = 0 ] || restore_policy
  setprop vendor.xgimi.system.debug.cmd ''
  setprop vendor.xgimi.system.debugserver ''
}
trap cleanup EXIT
if [ "$(settings get system codex_volume_curve_enabled)" != 1 ]; then
  if [ -e "$base/enabled" ]; then
    # Reverting at a high displayed index could become much louder. Mute first.
    cmd media_session volume --stream 3 --set 0 >/dev/null
    sleep 1
    rm -f "$base/enabled"
    sleep 2
  fi
  settings put system codex_volume_curve_status disabled
  settings delete system codex_volume_curve_pid
  settings delete system codex_volume_curve_resume_pending
  settings delete system codex_volume_curve_resume_index
  exit 0
fi
target=$(pidof vendor.mediatek.hardware.audio.service)
if [ -z "$target" ]; then
  settings put system codex_volume_curve_status waiting-for-audio
  exit 0
fi
boot=$(cat /proc/sys/kernel/random/boot_id)
loaded=$(settings get system codex_volume_curve_pid)
loaded_boot=$(settings get system codex_volume_curve_boot)
version=$(settings get system codex_volume_curve_version)
if [ "$target" = "$loaded" ] && [ "$boot" = "$loaded_boot" ] && [ "$version" = 1 ] && [ -e "$base/enabled" ]; then
  settings put system codex_volume_curve_boot_count "$(settings get global boot_count)"
  settings put system codex_volume_curve_status healthy
  exit 0
fi
id="$boot:$target:1"
[ ! -f "$base/failed-id" ] || [ "$(cat "$base/failed-id")" != "$id" ] || exit 0
volume=$(cmd media_session volume --stream 3 --get | sed -n 's/.*volume is \([0-9]*\).*/\1/p')
case "$volume" in ''|*[!0-9]*) exit 1;; esac
[ "$volume" -ge 0 ] && [ "$volume" -le 100 ]
printf 'framework_before_load=%s\n' "$volume" > "$base/load-info.txt"
printf 'restore_after_ready=%s\n' "$volume" >> "$base/load-info.txt"
printf '%s\n' "$volume" > "$base/volume-before-load"
# Keep output muted until the new native hook is confirmed ready.
cmd media_session volume --stream 3 --set 0 >/dev/null
sleep 1
sed 's/__INITIAL_INDEX__/0/g' "$base/volume-curve.js.in" > "$base/volume-curve.js"
touch "$base/enabled" "$base/agent.log"
: > "$base/agent.log"
chmod 0644 "$base/enabled" "$base/volume-curve.js"
chown audioserver:audioserver "$base/agent.log"
chmod 0644 "$base/agent.log"
cat /sys/fs/selinux/policy > "$base/policy-before"
policy_changed=1
settings put system codex_volume_curve_status loading
if ! /data/local/codex-voice/frida-inject -p "$target" -s "$base/volume-curve.js" -e > "$base/inject-last.log" 2>&1; then
  printf '%s\n' "$id" > "$base/failed-id"
  settings put system codex_volume_curve_status failed-for-current-process
  exit 1
fi
restore_policy
policy_changed=0
if grep -q "\"event\":\"ready\",\"pid\":$target," "$base/agent.log"; then
  settings put system codex_volume_curve_pid "$target"
  settings put system codex_volume_curve_boot "$boot"
  settings put system codex_volume_curve_version 1
  settings put system codex_volume_curve_boot_count "$(settings get global boot_count)"
  settings put system codex_volume_curve_status healthy
  rm -f "$base/failed-id"
  cmd media_session volume --stream 3 --set "$volume" >/dev/null
  settings delete system codex_volume_curve_resume_pending
  settings delete system codex_volume_curve_resume_index
else
  printf '%s\n' "$id" > "$base/failed-id"
  settings put system codex_volume_curve_status failed-for-current-process
  exit 1
fi
