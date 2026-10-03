#!/usr/bin/env bash
# Build the Windows game and the Mac game, then publish both as one update — one command instead of three.
#
# WHY THIS EXISTS: an update for every device on the Wi-Fi took three commands in the right order, with the right
# folders, every time. This is those three. Each step is the existing tool, unchanged; this only runs them in order
# and stops at the first one that fails, so a half-built update is never published.
#
# USAGE:  tools/build-and-publish.sh            # the two games, then publish
#         tools/build-and-publish.sh --dev      # development builds — what devices on your own Wi-Fi need
#         tools/build-and-publish.sh --server   # also rebuild the map server first (stop it before running this)
#
# Unity must be open on this project, with its Mac build module loaded (tools/build-game.sh explains both). The
# editor is switched to the Mac for the Mac build and back again afterwards. Takes about 15 minutes.
# Then each device's launcher downloads only what changed. To serve it to the Wi-Fi: tools/serve-updates.sh

set -uo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/platform.sh"
PY="$(find_python)" || { echo "No working Python found." >&2; exit 2; }

DEV=""
SERVER=""
for arg in "$@"; do
  case "$arg" in
    --dev)    DEV="--dev" ;;
    --server) SERVER=1 ;;
    *) echo "Unknown option: $arg (use --dev and/or --server)" >&2; exit 2 ;;
  esac
done

step() {
  local label="$1"
  shift
  printf '\n\033[36m== %s ==\033[0m\n' "$label"
  "$@" || { printf '\n\033[31mStopped: the %s step failed. Nothing was published.\033[0m\n' "$label" >&2; exit 1; }
}

if [ -n "$SERVER" ]; then
  step "map server" "$REPO/tools/build-game.sh" server
fi
step "Windows game" "$REPO/tools/build-game.sh" client $DEV
step "Mac game"     "$REPO/tools/build-game.sh" client --mac $DEV
step "publish"      "$PY" "$REPO/tools/publish-update.py" \
                    --windows "$REPO/builds/client" --mac "$REPO/builds/mac/client"

printf '\n\033[32mBuilt and published.\033[0m Devices get it the next time they open the launcher.\n'
