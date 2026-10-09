#!/bin/zsh
# PA-102X-S interactive app: install + launch on the connected phone, collect logcat, take control screenshots.
# usage: run-interactive.sh <apk> <label> [selftest]
#   no 3rd arg  -> launch like the icon (no extras), wait 20 s, screenshot, leave the app running
#   selftest    -> launch with "-e scenario selftest" (virtual-touch script inside the app), screenshot each step
# Only installs/launches/stops the spike app, reads logcat/dumpsys, takes screencaps (no device settings changed).
set -u
ADB=/Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
PKG=com.pocketarbor.spike.pa102xs
APK=$1; LABEL=$2; MODE=${3:-}
OUT=/Users/pavlyshyn/Developing/games/bonsai/.agent-workspace/pa102xs/device/$LABEL
mkdir -p $OUT
$ADB get-state >/dev/null || { echo "no device"; exit 2; }
if $ADB shell dumpsys window | grep -q "isKeyguardShowing=true"; then echo "LOCKED: keyguard is showing"; exit 5; fi
if ! $ADB shell dumpsys power | grep -q "mWakefulness=Awake"; then echo "ASLEEP: screen is off"; exit 6; fi
$ADB install -r $APK > $OUT/install.txt 2>&1 || { cat $OUT/install.txt; exit 3; }
$ADB shell am force-stop $PKG
$ADB logcat -c
ACT=$($ADB shell cmd package resolve-activity --brief $PKG | tail -1 | tr -d '\r')
echo "launch $ACT ${MODE:-icon}"
if [ -n "$MODE" ]; then
  $ADB shell am start -W -n $ACT -e scenario $MODE > $OUT/start.txt 2>&1
else
  $ADB shell am start -W -n $ACT > $OUT/start.txt 2>&1
fi
sleep 1
PID=$($ADB shell pidof $PKG | tr -d '\r')
echo "pid $PID"
( $ADB logcat -v time --pid=$PID > $OUT/logcat_app.txt 2>&1 ) & LC=$!
( $ADB logcat -v time AndroidRuntime:E DEBUG:F libc:F '*:S' > $OUT/logcat_crash.txt 2>&1 ) & LE=$!
shot() { $ADB exec-out screencap -p > $OUT/$1.png; echo "shot $1 $(stat -f %z $OUT/$1.png) B"; }

if [ "$MODE" = "selftest" ]; then
  start=$(date +%s)
  for step in menu_open menu_closed highlight cut scrub scrub_baked growing grown zoom zoom_reset quality view species done; do
    while ! grep -q "PA102XS_IT step=$step" $OUT/logcat_app.txt 2>/dev/null; do
      sleep 0.5
      if [ $(( $(date +%s) - start )) -gt 420 ]; then echo "timeout waiting for $step"; break 2; fi
      if ! $ADB shell pidof $PKG >/dev/null 2>&1; then echo "app not running"; break 2; fi
    done
    [ "$step" != "done" ] && shot it_$step
  done
else
  sleep 20
  shot icon_launch
fi
sleep 1
kill $LC $LE 2>/dev/null
$ADB shell dumpsys meminfo $PKG > $OUT/meminfo.txt 2>&1
$ADB shell dumpsys gfxinfo $PKG > $OUT/gfxinfo.txt 2>&1
grep -E "PA102XS" $OUT/logcat_app.txt | sed 's/^.*PA102XS/PA102XS/' | tail -30
echo "--- errors/exceptions (app pid):"
grep -E " E/| F/|Exception|NullReference" $OUT/logcat_app.txt | head -20
echo "--- crash buffer:"
head -20 $OUT/logcat_crash.txt
