#!/usr/bin/env bash
# Link this machine's Claude Code memory path to the repo's committed memory folder.
# Run ONCE per machine after cloning:  ./scripts/link-memory.sh
# Idempotent + safe to re-run. Backs up any real folder already at the target.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_MEM="$REPO/.claude/memory"
[ -d "$REPO_MEM" ] || { echo "Repo memory folder not found: $REPO_MEM" >&2; exit 1; }

# Claude Code names its per-project state dir by the repo's absolute path with
# every ':' '\' '/' replaced by '-' (case preserved). Derive it from THIS clone,
# so the link is correct no matter where the repo was cloned.
ENCODED="$(printf '%s' "$REPO" | sed 's/[:\\/]/-/g')"
HARNESS="$HOME/.claude/projects/$ENCODED/memory"
mkdir -p "$(dirname "$HARNESS")"

if [ -L "$HARNESS" ]; then
  rm "$HARNESS"                       # remove just the symlink, never the target
elif [ -e "$HARNESS" ]; then
  BAK="$HARNESS.bak-$(date +%Y%m%d-%H%M%S)"
  mv "$HARNESS" "$BAK"
  echo "Backed up existing memory folder -> $BAK"
fi

ln -s "$REPO_MEM" "$HARNESS"
echo "Linked Claude Code memory:"
echo "  $HARNESS"
echo "    -> $REPO_MEM"
