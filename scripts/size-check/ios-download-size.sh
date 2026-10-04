#!/usr/bin/env bash
# Measures an unsigned iOS .xcarchive: .app install size, zip lower bound and encrypted-__TEXT upper bound of App Store download.
# Usage: scripts/size-check/ios-download-size.sh <path/to/App.xcarchive>  (budget: download <= 45 MB, install <= 60 MB)
A=$1; APP=$(ls -d "$A"/Products/Applications/*.app)
echo "app_bytes $(du -sk "$APP" | cut -f1) KiB (du)"; 
echo "app_apparent $(find "$APP" -type f -exec stat -f %z {} + | awk '{s+=$1} END{print s}') bytes"
T=$(mktemp -d); mkdir -p "$T/Payload"; cp -R "$APP" "$T/Payload/"; (cd "$T" && zip -qr -9 app.ipa Payload); echo "ipa_zip $(stat -f %z "$T/app.ipa") bytes"; rm -rf "$T"
find "$APP" -type f -exec stat -f "%z %N" {} + | sort -rn | head -12 | sed "s#$APP/##"
# Upper-bound App Store download estimate: App Store encrypts __TEXT of Mach-O binaries (incompressible),
# so = zip(all files with Mach-O binaries removed) + raw size of Mach-O binaries.
T=$(mktemp -d); mkdir -p "$T/Payload"; cp -R "$APP" "$T/Payload/"; BIN=0
while IFS= read -r f; do if file "$f" | grep -q Mach-O; then s=$(stat -f %z "$f"); BIN=$((BIN+s)); rm "$f"; fi; done < <(find "$T/Payload" -type f)
(cd "$T" && zip -qr -9 rest.zip Payload); R=$(stat -f %z "$T/rest.zip"); rm -rf "$T"
echo "macho_raw $BIN bytes; rest_zip $R bytes; encrypted_download_est $((BIN+R)) bytes"
