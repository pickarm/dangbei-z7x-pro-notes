#!/system/bin/sh
set -e
umask 077
base=/data/local/codex-voice
policy_changed=0
restore_policy() {
    expected=$(sha256sum "$base/policy-before" | cut -d ' ' -f 1)
    restore_file="$base/policy-before"
    size=$(wc -c < "$restore_file")
    for attempt in 1 2 3; do
        dd if="$restore_file" of=/sys/fs/selinux/load bs="$size" count=1 2>/dev/null || return 1
        actual=$(sha256sum /sys/fs/selinux/policy | cut -d ' ' -f 1)
        if [ "$actual" = "$expected" ]; then return 0; fi
        cat /sys/fs/selinux/policy > "$base/policy-order-$attempt"
        restore_file="$base/policy-order-$attempt"
    done
    return 1
}
cleanup() {
    if [ "$policy_changed" = 1 ]; then restore_policy || true; fi
    setprop vendor.xgimi.system.debug.cmd ''
    setprop vendor.xgimi.system.debugserver ''
}
trap cleanup EXIT
settings put system codex_voice_enabled 1
settings put system codex_voice_observe 0
target=$(pidof com.dangbei.lerad.rocky)
if [ -z "$target" ]; then
    settings put system codex_voice_loader waiting-for-voice-service
    exit 0
fi
boot=$(cat /proc/sys/kernel/random/boot_id)
loaded=$(settings get system codex_voice_patch_pid)
version=$(settings get system codex_voice_patch_version)
loaded_boot=$(settings get system codex_voice_patch_boot)
if [ "$target" = "$loaded" ] && [ "$version" = 5 ] && [ "$boot" = "$loaded_boot" ]; then
    settings put system codex_voice_loader healthy
    exit 0
fi
if [ -f "$base/failed-id" ] && [ "$(cat "$base/failed-id")" = "$boot:$target" ]; then exit 0; fi
cat /sys/fs/selinux/policy > "$base/policy-before"
policy_changed=1
settings put system codex_voice_loader loading
"$base/frida-inject" -p "$target" -s "$base/voice-patch.js" -e > "$base/inject-last.log" 2>&1
restore_policy
policy_changed=0
loaded=$(settings get system codex_voice_patch_pid)
version=$(settings get system codex_voice_patch_version)
if [ "$target" = "$loaded" ] && [ "$version" = 5 ]; then
    settings put system codex_voice_patch_boot "$boot"
    settings put system codex_voice_loader healthy
    rm -f "$base/failed-id"
else
    printf '%s:%s\n' "$boot" "$target" > "$base/failed-id"
    settings put system codex_voice_loader failed-for-current-process
fi
