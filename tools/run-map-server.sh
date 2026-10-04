#!/usr/bin/env bash
# Start the world (map) server, with every setting it needs read from settings.env.
#
# WHY THIS EXISTS: it needs the character server's address, the secret the two servers recognise each other by,
# and the address players are told to dial — and typing those in front of the command by hand was the one
# place they were written a second time. This reads them from the one file instead.
#
# USAGE:  tools/run-map-server.sh                       # the build tools/build-game.sh server made
#         tools/run-map-server.sh path/to/server/program
#         LOG_CATEGORIES=hit,cast tools/run-map-server.sh   # anything typed in front still wins
#
# The log goes to logs/server.log as well as the screen; the run before is kept as logs/server-prev.log.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/platform.sh"
PROGRAM="${1:-$(game_program server)}"

[ -f "${SETTINGS_FILE:-$REPO/settings.env}" ] || {
  echo "No settings at $REPO/settings.env — copy settings.env.example to settings.env and fill it in." >&2; exit 2; }
. "$REPO/tools/load-settings.sh"

[ -n "${SERVER_SECRET:-}" ] || { echo "SERVER_SECRET is not set in settings.env — the character server would refuse this server." >&2; exit 2; }
for __setting in CHAR_SERVER_URL MAP_SERVER_ADDRESS MAP_SERVER_ARRIVALS_PORT GAME_SERVER_PORT; do
  eval "__value=\${$__setting:-}"
  [ -n "$__value" ] || { echo "$__setting is not set in settings.env — this server has no default for it." >&2; exit 2; }
done
[ -x "$PROGRAM" ] || { echo "No server program at $PROGRAM — build it with: tools/build-game.sh server" >&2; exit 2; }

cd "$REPO"
mkdir -p logs && mv -f logs/server.log logs/server-prev.log 2>/dev/null
"$PROGRAM" -batchmode -nographics -logFile - 2>&1 | tee logs/server.log
