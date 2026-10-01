#!/bin/sh
# Start one of Claude Code's connections (.mcp.json) on whichever computer this is.
#
# WHY THIS EXISTS: .mcp.json is one file shared by every machine, and it used to name the Mac's own paths
# (/Users/vince/.unity/bin/unity, the project's folder on that Mac) and an interpreter by name (python3, which on
# Windows is a shortcut that only prints an error). On the other machine both connections failed to start, and a
# connection that cannot start just shows as "failed" with no reason. Everything machine-specific is worked out
# here instead, through tools/platform.sh, so the shared file names nothing that differs between machines.
#
# USAGE (from .mcp.json):  sh tools/start-mcp.sh unity-editor | game
set -e

REPO="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
. "$REPO/tools/platform.sh"

case "${1:-}" in
  unity-editor)
    CLI="$(unity_cli)" || exit 1
    exec "$CLI" mcp --project-path "$(native_path "$REPO/unity")" ;;
  game)
    PY="$(find_python)" || { echo "No working Python found (tried python3, python, py)." >&2; exit 1; }
    cd "$REPO"
    exec "$PY" tools/agent-play/mcp_server.py ;;
  *)
    echo "Say which connection to start: unity-editor | game" >&2
    exit 2 ;;
esac
