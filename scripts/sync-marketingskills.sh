#!/usr/bin/env bash
# Re-vendors coreyhaines31/marketingskills into .claude/skills and vendor/marketingskills.
# Usage: scripts/sync-marketingskills.sh [commit-ish]
set -euo pipefail

REPO_URL="https://github.com/coreyhaines31/marketingskills"
REF="${1:-main}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

git clone --quiet "$REPO_URL" "$TMP/ms"
git -C "$TMP/ms" checkout --quiet "$REF"
COMMIT="$(git -C "$TMP/ms" rev-parse HEAD)"

for dir in "$TMP/ms/skills"/*/; do
  name="$(basename "$dir")"
  rm -rf "$ROOT/.claude/skills/$name"
  cp -R "$dir" "$ROOT/.claude/skills/$name"
done

rm -rf "$ROOT/vendor/marketingskills/tools"
cp -R "$TMP/ms/tools" "$ROOT/vendor/marketingskills/tools"
cp "$TMP/ms/LICENSE" "$TMP/ms/README.md" "$TMP/ms/VERSIONS.md" "$ROOT/vendor/marketingskills/"

sed -i '' -E "s/^- \*\*Commit\*\*: .*/- **Commit**: \`$COMMIT\`/" "$ROOT/vendor/marketingskills/VENDORED.md"
find "$ROOT/.claude/skills" "$ROOT/vendor/marketingskills" -name .DS_Store -delete

echo "marketingskills synced to $COMMIT. Review with: git status .claude/skills vendor/marketingskills"
