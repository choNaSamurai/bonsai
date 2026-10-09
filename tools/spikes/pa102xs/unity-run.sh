#!/bin/zsh
# PA-102X-S spike: run ONE batchmode Unity on the project copy, never in parallel.
#
# usage:
#   unity-run.sh compile                     — import + compile, report CS errors
#   unity-run.sh edit [testFilter]           — EditMode tests (-nographics)
#   unity-run.sh exec <Class.Method> [args…] — -executeMethod WITH GPU (rendering harness)
#
# What it does:
#   1. takes an atomic lock ($W/unity.lock, mkdir) — waits up to 40 min for another agent;
#   2. rsyncs the main project (Assets/Packages/ProjectSettings) into the copy $P,
#      keeping $P/Assets/_Project/Spikes untouched;
#   3. rsyncs the spike sources $SRC -> $P/Assets/_Project/Spikes/PA102XS (with --delete);
#   4. runs Unity (watchdog 25 min), then copies NEW .meta files back into $SRC
#      so GUIDs stay stable;
#   5. prints a summary; full log in $W/pa102xs/runs/.
#
# The owner's own Unity Editor may be open on the MAIN project — that's fine, we never touch it.
set -u
W=/Users/pavlyshyn/Developing/games/bonsai/.agent-workspace
MAIN=${MAIN_PROJECT:-/Users/pavlyshyn/Developing/games/bonsai}   # e.g. the spike worktree
P=${PROJ:-$W/proj}                                          # e.g. $W/proj2 for another branch
SRC=$W/pa102xs/src
DST=$P/Assets/_Project/Spikes/PA102XS
U=/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity
RUNS=$W/pa102xs/runs
LOCK=$W/unity.lock
mkdir -p $RUNS $SRC

mode=${1:-}; [ -z "$mode" ] && { sed -n '2,9p' $0; exit 2; }; shift

# 1. lock
waited=0
until mkdir $LOCK 2>/dev/null; do
  if [ -f $LOCK/pid ] && ! kill -0 $(cat $LOCK/pid) 2>/dev/null; then echo "stale lock (pid $(cat $LOCK/pid)) — removing"; rm -rf $LOCK; continue; fi
  [ $waited -eq 0 ] && echo "waiting for lock held by: $(cat $LOCK/who 2>/dev/null)"
  sleep 10; waited=$((waited+10))
  [ $waited -ge 2400 ] && { echo "lock wait timeout"; exit 4; }
done
echo $$ > $LOCK/pid; echo "${AGENT:-unknown} $mode $(date +%H:%M:%S)" > $LOCK/who
trap 'rm -rf $LOCK' EXIT INT TERM
# safety: no other batchmode Unity on the copy
if pgrep -f "Unity.*-batchmode.*-projectPath $P" >/dev/null || pgrep -f "Unity.*-batchMode.*-projectPath $P" >/dev/null; then echo "another batchmode Unity runs on $P"; exit 3; fi

# 2-3. sync
rsync -a --delete --exclude Library --exclude Temp --exclude Logs --exclude .git --exclude UserSettings \
  --exclude '*.csproj' --exclude '*.sln' --exclude '*.slnx' --exclude '_Project/Spikes/' \
  $MAIN/Assets $MAIN/Packages $MAIN/ProjectSettings $P/
mkdir -p $DST
rsync -a --delete $SRC/ $DST/

# 4. run
ts=$(date +%m%d-%H%M%S); log=$RUNS/$ts-$mode.log
case $mode in
  compile) cmd=($U -batchmode -nographics -projectPath $P -quit -logFile $log);;
  edit)    xml=$RUNS/$ts-edit.xml; cmd=($U -batchmode -nographics -projectPath $P -runTests -testPlatform EditMode ${1:+-testFilter} ${1:-} -testResults $xml -logFile $log);;
  exec)    m=$1; shift; cmd=($U -batchmode -projectPath $P -executeMethod $m "$@" -quit -logFile $log);;
  *) echo "unknown mode $mode"; exit 2;;
esac
start=$(date +%s)
"${cmd[@]}" & up=$!
# watchdog polls every 5 s and exits with Unity — no orphan sleeps holding stdout
( t=0; while kill -0 $up 2>/dev/null; do sleep 5; t=$((t+5)); [ $t -ge 1500 ] && { echo "watchdog: killing Unity after 25 min" >> $log; kill $up; break; }; done ) >/dev/null 2>&1 &
wait $up; code=$?
rsync -a --ignore-existing --include='*/' --include='*.meta' --exclude='*' $DST/ $SRC/

# 5. summary
echo "exit $code · $(( $(date +%s)-start )) s · log $log"
grep -E "error CS[0-9]+" $log | sed 's/^.*Spikes\/PA102XS\///' | sort -u | head -40
grep -E "Scripts have compiler errors|Aborting batchmode|Exception" $log | sort -u | head -10
[ "$mode" = edit ] && [ -f ${xml:-/nonexistent} ] && grep -m1 '<test-run' $xml | grep -o 'result="[^"]*" total="[0-9]*" passed="[0-9]*" failed="[0-9]*"'
exit $code
