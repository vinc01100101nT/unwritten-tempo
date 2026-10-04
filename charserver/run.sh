#!/bin/sh
# Start the character server.
#
# WHY THIS EXISTS: the settings it needs live in settings.env at the top of the project, and loading them by
# hand is one line with a trap in it — `. .env` makes zsh search your PATH instead of this folder, so it
# reports "no such file or directory" while the file is sitting right there. That cost two rounds. This is the
# same work with nothing to mistype.
#
# USAGE:  charserver/run.sh                 # every setting comes from settings.env (a terminal value wins)
#
# IT STARTS THE SERVER AGAIN IF IT STOPS. A crash used to leave nobody able to sign in until somebody noticed and
# started it by hand. Now it is built once and run as a program, and started again 5 seconds after any exit —
# except a clean one, and except exit 2, which is the server refusing to start because its port is already taken
# (starting again cannot fix that). Ctrl+C stops it for good.
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

# Which computer this is, for the program's name (".exe" on Windows).
. "$REPO/tools/platform.sh"

cd "$HERE"
# BUILT ONCE, into the same folder `dotnet run` always used — so a firewall permission already given to that
# program still applies — and then run directly, so starting it again after a crash does not rebuild. Ctrl+C ends
# this script, so a restart from restart-servers.sh runs it afresh and does rebuild.
dotnet build --nologo -v quiet
PROGRAM="$HERE/bin/Debug/net10.0/CharServer$EXE"

# Ctrl+C has to be told apart from a crash by the shell itself: on Windows the server leaves with an ordinary exit
# code, not by the signal, so the shell would otherwise take it for a crash and launch the SAME program again — and a
# restart from restart-servers.sh would keep running the old build and never reach the rebuild above.
STOPPED=
trap 'STOPPED=1' INT

while :; do
  "$PROGRAM" "$@" && exit 0
  code=$?
  [ -n "$STOPPED" ] && exit 130
  [ "$code" -eq 2 ] && exit 2
  echo "[run] the character server stopped (exit $code) — starting it again in 5 seconds. Ctrl+C stops it." >&2
  sleep 5
done
