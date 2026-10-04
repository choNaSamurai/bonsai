#!/usr/bin/env bash
# Prints Google Play download size of an .aab via bundletool (all devices + arm64 device spec).
# Usage: scripts/size-check/android-download-size.sh <path/to/app.aab>
# Env: UNITY_VERSION (default 6000.6.4f1)
set -euo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.6.4f1}"
PLAYER="/Applications/Unity/Hub/Editor/$UNITY_VERSION/PlaybackEngines/AndroidPlayer"
JAVA="$PLAYER/OpenJDK/bin/java"
BUNDLETOOL="$(ls "$PLAYER"/Tools/bundletool-all-*.jar | head -1)"

AAB="$1"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
echo '{"supportedAbis":["arm64-v8a"],"supportedLocales":["en"],"screenDensity":480,"sdkVersion":34}' > "$TMP/device.json"

"$JAVA" -jar "$BUNDLETOOL" build-apks --bundle="$AAB" --output="$TMP/app.apks" >/dev/null
echo "== all devices (MIN,MAX bytes)"
"$JAVA" -jar "$BUNDLETOOL" get-size total --apks="$TMP/app.apks"
echo "== arm64 device spec (budget metric: <= 25 MB, hard cap 30 MB)"
"$JAVA" -jar "$BUNDLETOOL" get-size total --apks="$TMP/app.apks" --device-spec="$TMP/device.json"
