#!/bin/sh
# Start the character server.
#
# WHY THIS EXISTS: the settings it needs live in the sign-in service's .env, and loading them by hand is one
# line with a trap in it — `. .env` makes zsh search your PATH instead of this folder, so it reports "no such
# file or directory" while the file is sitting right there. That cost two rounds. This is the same work with
# nothing to mistype.
#
# USAGE:  charserver/run.sh                 # uses SERVER_SECRET from your environment, or a local default
#         SERVER_SECRET=... charserver/run.sh
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
ENV_FILE="$HERE/../accounts/.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "No settings at $ENV_FILE — copy accounts/.env.example to accounts/.env and fill it in." >&2
  exit 2
fi

# The SAME database and the SAME signing secret the sign-in service uses. A different value for either is a
# bug, not a configuration: the two would be looking at different databases, or refusing each other's players.
set -a
. "$ENV_FILE"
set +a

# The two servers recognise each other by this. It is NOT a player's sign-in token and never interchangeable
# with one. Set it yourself for anything that leaves this machine.
: "${SERVER_SECRET:=change-me}"
export SERVER_SECRET

if [ "$SERVER_SECRET" = "change-me" ]; then
  echo "note: using the default SERVER_SECRET. The game server must be started with the same value:" >&2
  echo "      SERVER_SECRET=change-me" >&2
fi

cd "$HERE"
exec dotnet run "$@"
