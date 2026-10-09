#!/bin/zsh
# PA-102X-S phase 2a: install + run the spike app on the connected phone, wait for done.txt, pull results.
# usage: run-device.sh <apk> <label> [timeout_s=900]
# Only installs/launches/stops the spike app, reads its files, logcat and dumpsys (no device settings changed).
set -u
ADB=/Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
PKG=com.pocketarbor.spike.pa102xs
APK=$1; LABEL=$2; TMO=${3:-900}
OUT=/Users/pavlyshyn/Developing/games/bonsai/.agent-workspace/pa102xs/device/$LABEL
REMOTE=/sdcard/Android/data/$PKG/files/pa102xs
mkdir -p $OUT
$ADB get-state >/dev/null || { echo "no device"; exit 2; }
if $ADB shell dumpsys window | grep -q "isKeyguardShowing=true"; then echo "LOCKED: keyguard is showing"; exit 5; fi
$ADB shell dumpsys thermalservice > $OUT/thermal_before.txt
$ADB shell dumpsys battery > $OUT/battery_before.txt
$ADB install -r $APK > $OUT/install.txt 2>&1 || { cat $OUT/install.txt; exit 3; }
$ADB shell am force-stop $PKG
$ADB shell rm -rf $REMOTE
$ADB logcat -c
ACT=$($ADB shell cmd package resolve-activity --brief $PKG | tail -1 | tr -d '\r')
echo "launch $ACT"
start=$(date +%s)
$ADB shell am start -W -n $ACT > $OUT/start.txt 2>&1
( $ADB logcat -v time -s Unity:V PA102XS:V > $OUT/logcat.txt 2>&1 ) & LC=$!
done=0
while [ $(( $(date +%s) - start )) -lt $TMO ]; do
  sleep 10
  if $ADB shell ls $REMOTE/done.txt >/dev/null 2>&1; then done=1; break; fi
  if ! $ADB shell pidof $PKG >/dev/null 2>&1; then echo "app not running"; break; fi
done
sleep 2
kill $LC 2>/dev/null
$ADB shell dumpsys thermalservice > $OUT/thermal_after.txt
$ADB shell dumpsys battery > $OUT/battery_after.txt
$ADB shell dumpsys meminfo $PKG > $OUT/meminfo.txt 2>&1
$ADB pull $REMOTE $OUT/ > $OUT/pull.txt 2>&1
$ADB shell am force-stop $PKG
echo "done=$done · $(( $(date +%s) - start )) s · $OUT"
grep -E "PA102XS\|" $OUT/logcat.txt | sed 's/^.*PA102XS| //' | tail -40
grep -E "Thermal Status|VIRTUAL-SKIN|skin_therm1" $OUT/thermal_before.txt | head -3
grep -E "Thermal Status|VIRTUAL-SKIN|skin_therm1" $OUT/thermal_after.txt | head -3
