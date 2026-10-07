#!/bin/sh
# Serve the update folder — the website with its Download page, the launcher, and every game update — to this computer AND to
# every device on the same network, until Ctrl+C.
#
# WHY THIS EXISTS: copying a game build to each device by hand is the slow way to test with other people. The
# launcher already downloads the game and every later update from this folder, so a device only needs the launcher
# once — and opening this computer's address in its browser offers it.
#
# USAGE:  tools/serve-updates.sh            # on PATCH_PORT from settings.env
#
# THEN, on another device:  open one of the addresses it prints. It prints only the addresses that answered when it
# tried them from this computer, and says in red when PATCH_URL — the address players are given — does not answer.
# Printed again whenever this computer's addresses change. (tools/serve-folder.py does the serving and the testing.)
#
# The launcher must have been built with PATCH_URL set to that same address (tools/build-launcher.sh reads it from
# settings.env), and the game published into the folder (tools/publish-update.py). Windows may ask once to let
# Python through the firewall: allow it, or no other device can connect.
set -eu

REPO="$(cd "$(dirname "$0")/.." && pwd)"
. "$REPO/tools/platform.sh"
. "$REPO/tools/load-settings.sh"

FOLDER="$REPO/builds/patch"
[ -f "$FOLDER/version.txt" ] || {
  echo "Nothing published yet in $FOLDER — run tools/publish-update.py first." >&2; exit 2; }

[ -n "${PATCH_PORT:-}" ] || { echo "PATCH_PORT is not set in settings.env — it names the port to serve on." >&2; exit 2; }
PORT="$PATCH_PORT"
exec "$(find_python)" "$(native_path "$REPO/tools/serve-folder.py")" "$(native_path "$FOLDER")" "$PORT" PATCH_URL
