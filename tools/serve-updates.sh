#!/bin/sh
# Serve the update folder — the download page, the launcher, and every game update — to this computer AND to
# every device on the same network, until Ctrl+C.
#
# WHY THIS EXISTS: copying a game build to each device by hand is the slow way to test with other people. The
# launcher already downloads the game and every later update from this folder, so a device only needs the launcher
# once — and opening this computer's address in its browser offers it.
#
# USAGE:  tools/serve-updates.sh            # port 8090, or the port in PATCH_URL from settings.env
#
# THEN, on another device:  open http://<this computer's address>:<port>  (the address: tools/platform.sh lan-address)
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

PORT=8090
case "${PATCH_URL:-}" in
  *:[0-9]*) PORT="${PATCH_URL##*:}"; PORT="${PORT%%/*}" ;;
esac

echo "Serving $FOLDER on port $PORT to every device on this network — open http://$(lan_address):$PORT"
echo "Ctrl+C stops it."
cd "$FOLDER"
exec "$(find_python)" -m http.server "$PORT" --bind 0.0.0.0
