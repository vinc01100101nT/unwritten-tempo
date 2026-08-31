#!/bin/sh
# Start a project hook with whatever Python THIS machine actually has.
#
# WHY THIS EXISTS: the hook command used to name one interpreter — `python` — which exists on
# Windows and does not exist on macOS. When the project moved machines the gate could not start,
# and a hook that cannot start fails silently: the edit goes through exactly as if the guard had
# approved it. Five weeks, nothing guarded, nobody told. No single name is correct on both
# systems, so the name is no longer written into the settings file at all — it is chosen here,
# on the machine, every time.
#
# Claude Code passes a hook command to a shell: sh on macOS and Linux, Git Bash on Windows.
# This script is plain POSIX sh, so the same line works on all three.
#
# Usage: run-hook.sh <path/to/hook.py relative to the repo root> [args...]
set -e

# Claude Code hands every hook the repo root in CLAUDE_PROJECT_DIR; fall back to this file's
# own location when it is run by hand. On Windows that root arrives with backslashes, which a
# POSIX shell would read as escapes, so it is normalised to forward slashes — Git Bash, which
# is the shell Claude Code uses there, understands those fine.
REPO=${CLAUDE_PROJECT_DIR:-}
if [ -z "$REPO" ]; then
  REPO=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
fi
REPO=$(printf '%s' "$REPO" | tr '\\' '/')

NAME=$1
TARGET="$REPO/$NAME"
shift

if [ ! -f "$TARGET" ]; then
  echo "the guard $NAME is configured but its file is missing at $TARGET" >&2
  exit 1
fi

for PY in python3 python py; do
  if command -v "$PY" >/dev/null 2>&1; then
    exec "$PY" "$TARGET" "$@"
  fi
done

echo "no Python found on this machine (tried python3, python, py) — the guard $NAME cannot run" >&2
exit 1
