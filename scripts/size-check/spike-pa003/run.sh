#!/bin/bash
# usage: run.sh <Proj> <method> <logname> [extra args...]
P=$1; M=$2; L=$3; shift 3
D=$(cd "$(dirname "$0")" && pwd)
/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit \
  -projectPath "$D/$P" -executeMethod "$M" -logFile "$D/logs/$L.log" "$@"
echo "EXIT $?"
