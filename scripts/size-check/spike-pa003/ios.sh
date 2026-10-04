#!/bin/bash
# usage: ios.sh <Proj> <name> [unity args]
D=$(cd "$(dirname "$0")" && pwd); cd "$D"; P=$1; N=$2; shift 2
./run.sh $P Spike.BuildIOS ${N}_ios -buildTarget iOS -out ../out/${N}_ios -report ../out/${N}_ios.txt "$@" || exit 1
head -4 out/${N}_ios.txt
(cd out/${N}_ios && xcodebuild archive -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -destination 'generic/platform=iOS' -archivePath ../${N}_ios.xcarchive -derivedDataPath "$D/dd/$N" \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY="" > ../${N}_ios_archive.log 2>&1); echo "archive exit $?"
./iosmeasure.sh out/${N}_ios.xcarchive
size -m out/${N}_ios.xcarchive/Products/Applications/*.app/Frameworks/UnityFramework.framework/UnityFramework | grep "Segment __TEXT"
