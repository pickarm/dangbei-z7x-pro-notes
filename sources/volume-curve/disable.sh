#!/system/bin/sh
set -e
# The old curve can be much louder at the same number, so leave output muted.
settings put system codex_volume_curve_enabled 0
if [ -x /data/local/codex-volume/reapply.sh ]; then
  /data/local/codex-volume/reapply.sh
fi
echo 'Smooth volume disabled. Output was muted to prevent a loud jump.'
