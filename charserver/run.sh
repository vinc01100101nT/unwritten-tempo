#!/bin/sh
# Start the character server.
#
# WHY THIS EXISTS: the settings it needs live in settings.env at the top of the project, and loading them by
# hand is one line with a trap in it — `. .env` makes zsh search your PATH instead of this folder, so it
# reports "no such file or directory" while the file is sitting right there. That cost two rounds. This is the
# same work with nothing to mistype.
#
# USAGE:  charserver/run.sh                 # every setting comes from settings.env (a terminal value wins)
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/.." && pwd)"

if [ ! -f "${SETTINGS_FILE:-$REPO/settings.env}" ]; then
  echo "No settings at $REPO/settings.env — copy settings.env.example to settings.env and fill it in." >&2
  exit 2
fi

# The SAME database and the SAME signing secret the sign-in service uses. A different value for either is a
# bug, not a configuration: the two would be looking at different databases, or refusing each other's players.
. "$REPO/tools/load-settings.sh"

# The two servers recognise each other by this. It is NOT a player's sign-in token and never interchangeable
# with one. There is deliberately NO default: a guessable value here is a door that writes any save to any
# character, and the server once ran with the literal "change-me" for weeks. Put it in settings.env.
if [ -z "${SERVER_SECRET:-}" ] || [ "$SERVER_SECRET" = "change-me" ]; then
  echo "SERVER_SECRET is not set (or is still 'change-me'). Add a real one to $REPO/settings.env:" >&2
  echo "      SERVER_SECRET=\$(openssl rand -base64 32)" >&2
  exit 2
fi
export SERVER_SECRET

cd "$HERE"
exec dotnet run "$@"
