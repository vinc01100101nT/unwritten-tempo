#!/usr/bin/env bash
# Turn on continuous change-log archiving: from the next Postgres restart on, every finished change-log file is
# copied into WAL_ARCHIVE_DIR, at least once a minute, so the database can be rebuilt as it was at any moment.
#
# WHY THIS EXISTS: the daily dump (tools/backup-database.sh) can only put the database back as it was at 04:10.
# Every change since then is written in Postgres's change log (its "write-ahead log", or WAL) and nowhere else.
# Keeping a copy of every finished change-log file, next to a weekly full copy (tools/backup-full-copy.sh), lets
# the database be rebuilt to any second those files cover — so a dead disk costs players about the last minute,
# not the last day.
#
# WHY IT DOES NOT RESTART POSTGRES: archiving only starts after a restart, and a restart drops every player for a
# few seconds. When to do that is a person's decision for a quiet hour, so this prints the command and stops.
#
# SAFE TO RUN AGAIN: it compares what Postgres is set to with what it should be and writes only what differs.
# Once archiving is running, running it again reports whether files are actually arriving in the folder.
#
# USAGE:  tools/backup-start-archiving.sh              # set it up; prints the restart command if one is needed
#         tools/backup-start-archiving.sh --dry-run    # only say what it would change; changes nothing
#         WAL_ARCHIVE_DIR=/var/backups/unwritten/wal tools/backup-start-archiving.sh
#
# EXIT: 0 = set, or already running; 1 = Postgres refused a change, or archiving is running and failing;
#       2 = setup problem.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/load-settings.sh"
. "$REPO/tools/platform.sh"
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-$REPO/backups/wal}"

# A change-log file is closed and archived once it is full OR this many seconds after the last change, whichever
# comes first. 60 is the "at most about the last minute" the plan promises.
ARCHIVE_TIMEOUT=60

fail() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 2; }
lost() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 1; }
warn() { printf '\033[33m%s\033[0m\n' "$*"; }

DRY=0
case "${1:-}" in
  -h|--help) sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
  --dry-run) DRY=1 ;;
  '') ;;
  *) fail "Unknown option '$1'. Try --help." ;;
esac

command -v psql >/dev/null 2>&1 || fail "No psql on this machine. Install the PostgreSQL client tools first."
[ -n "${DATABASE_URL:-}" ] || fail "DATABASE_URL is not set in settings.env or in this shell."

# The copy command runs ON THE DATABASE'S MACHINE and writes to a folder there. So this only makes sense for a
# database on this machine: pointed at another one, it would tell that machine to copy into a folder it has not
# got, and every change-log file would fail to archive.
case "$DATABASE_URL" in
  *://*) __auth="${DATABASE_URL#*://}"; __auth="${__auth%%[/?]*}"; __auth="${__auth##*@}"
         case "$__auth" in \[*) HOST="${__auth%%]*}]" ;; *) HOST="${__auth%%:*}" ;; esac ;;
  *) fail "DATABASE_URL is not in the postgres://... shape this script reads." ;;
esac
case "$HOST" in
  ''|localhost|127.0.0.1|'[::1]'|%2F*|%2f*) ;;
  *) fail "DATABASE_URL points at '$HOST', not this machine. Run this ON the database's machine, against localhost." ;;
esac

# Postgres runs the copy from inside its own data folder, so the archive folder must be a full path. A quote in it
# would break the command Postgres runs.
case "$WAL_ARCHIVE_DIR" in
  /*|[A-Za-z]:[/\\]*) ;;
  *) fail "WAL_ARCHIVE_DIR must be a full path, not '$WAL_ARCHIVE_DIR'." ;;
esac
case "$WAL_ARCHIVE_DIR" in *\'*|*\"*) fail "WAL_ARCHIVE_DIR must not contain a quote: '$WAL_ARCHIVE_DIR'." ;; esac
WANT_COMMAND="$(wal_archive_command "$WAL_ARCHIVE_DIR")" || fail "Could not build the copy command for this computer."

q() { psql "$DATABASE_URL" -X -A -t -q -v ON_ERROR_STOP=1 "$@"; }

SUPER="$(q -c "select rolsuper from pg_roles where rolname = current_user" 2>&1)" \
  || fail "Cannot reach the database at DATABASE_URL: $SUPER"
[ "$SUPER" = t ] || fail "The user in DATABASE_URL is not a superuser, and only a superuser can change these settings."

# Two different answers per setting, and both matter:
#   RUNNING — what Postgres is doing right now.
#   WRITTEN — what its settings files say; this is what it will do after the next restart. The last file read
#             wins, which is how Postgres itself decides. Nothing written means Postgres's built-in default.
#             (Not filtered on its "error" column: a setting that waits for a restart is marked there too, and
#             skipping it made a second run write archive_mode again.)
running() { q -c "select setting from pg_settings where name = '$1'"; }
written() { q -c "select setting from pg_file_settings where name = '$1' order by seqno desc limit 1"; }

RUN_MODE="$(running archive_mode)";      RUN_LEVEL="$(running wal_level)"
RUN_TIMEOUT="$(running archive_timeout)"; RUN_COMMAND="$(running archive_command)"

# archive_mode=on refuses to start unless the change log carries enough detail; 'minimal' is the only value that
# does not, and it is not the default. Raised to the default only if somebody lowered it.
WANT_LEVEL="$RUN_LEVEL"
W="$(written wal_level)"; [ -n "$W" ] && WANT_LEVEL="$W"
[ "$WANT_LEVEL" = minimal ] && WANT_LEVEL=replica

CHANGES=0
set_if_needed() {   # set_if_needed <name> <wanted> <built-in default>
  __now="$(written "$1")"; [ -n "$__now" ] || __now="$3"
  [ "$__now" = "$2" ] && return 0
  CHANGES=$((CHANGES + 1))
  if [ "$DRY" = 1 ]; then
    printf '  would set %-16s %s\n  %-26s (now: %s)\n' "$1" "$2" "" "${__now:-empty}"
    return 0
  fi
  # The value travels as a psql variable and is quoted by psql itself, so the quotes and backslashes in a
  # Windows copy command reach Postgres exactly as written.
  q -v v="$2" <<SQL || lost "Postgres refused to set $1. Nothing after it was changed."
ALTER SYSTEM SET $1 = :'v';
SQL
  printf '  set %-16s %s\n' "$1" "$2"
}

if [ "$DRY" = 1 ]; then echo "Dry run — nothing is changed."; fi
set_if_needed wal_level       "$WANT_LEVEL"      replica
set_if_needed archive_mode    on                 off
set_if_needed archive_timeout "$ARCHIVE_TIMEOUT" 0
set_if_needed archive_command "$WANT_COMMAND"    ''

if [ "$DRY" = 0 ]; then
  mkdir -p "$WAL_ARCHIVE_DIR" || fail "Cannot create $WAL_ARCHIVE_DIR"
fi

# ---- What it takes for the written settings to be the running ones ----------------------------------------
# archive_mode and wal_level only change on a restart. The copy command and the timeout change on a reload,
# which drops nobody. While archive_mode is off Postgres reports the command as "(disabled)", so it is only
# compared once archiving is running.
NEED_RESTART=0; NEED_RELOAD=0
[ "$RUN_MODE" = on ] || NEED_RESTART=1
[ "$RUN_LEVEL" = "$WANT_LEVEL" ] || NEED_RESTART=1
if [ "$NEED_RESTART" = 0 ]; then
  { [ "$RUN_TIMEOUT" = "$ARCHIVE_TIMEOUT" ] && [ "$RUN_COMMAND" = "$WANT_COMMAND" ]; } || NEED_RELOAD=1
fi

echo
echo "  archive folder  $WAL_ARCHIVE_DIR"
[ "$CHANGES" = 0 ] && echo "  settings        already written, nothing to change"

if [ "$NEED_RESTART" = 1 ]; then
  [ "$DRY" = 1 ] && { echo; echo "After a real run, Postgres would need a restart to start archiving."; exit 0; }
  echo
  warn "Archiving starts after Postgres restarts. A restart disconnects every player for a few seconds, so do it in a quiet hour:"
  echo
  echo "    tools/platform.sh db-stop"
  echo "    tools/platform.sh db-start"
  echo
  echo "Then run this script again: it reports whether the first file has arrived."
  case "$PLATFORM" in
    windows) echo "Postgres runs as its own Windows account and must be able to write to the archive folder." ;;
    *)       echo "The account Postgres runs as (usually 'postgres') must be able to write to the archive folder." ;;
  esac
  exit 0
fi

if [ "$NEED_RELOAD" = 1 ]; then
  [ "$DRY" = 1 ] && { echo; echo "After a real run, Postgres would need a reload (nobody is disconnected) to use the new copy command or timeout."; exit 0; }
  echo
  warn "Archiving is running, but with an older copy command or timeout. A reload applies the new one and disconnects nobody:"
  echo
  echo "    psql \"\$DATABASE_URL\" -c 'select pg_reload_conf()'"
  echo
  echo "Then run this script again."
  exit 0
fi

# ---- Archiving is running: is it actually working? ---------------------------------------------------------
# Postgres keeps its own count of files archived and failed. A failure after the last success means it is failing
# NOW — and while it fails, the change log piles up on the database's own disk instead of being cleared.
IFS='|' read -r ARCHIVED LAST_WAL LAST_TIME FAILED LAST_FAILED FAILED_TIME FAILING <<EOF
$(q -F '|' -c "select archived_count, coalesce(last_archived_wal, ''),
                      coalesce(to_char(last_archived_time, 'YYYY-MM-DD HH24:MI:SS'), ''),
                      failed_count, coalesce(last_failed_wal, ''),
                      coalesce(to_char(last_failed_time, 'YYYY-MM-DD HH24:MI:SS'), ''),
                      last_failed_time is not null
                        and (last_archived_time is null or last_failed_time > last_archived_time)
               from pg_stat_archiver")
EOF
IN_FOLDER="$(ls -1 "$WAL_ARCHIVE_DIR" 2>/dev/null | wc -l | tr -d ' ')"

if [ "$FAILING" = t ]; then
  lost "Archiving is ON but FAILING: the last attempt, $LAST_FAILED at $FAILED_TIME, did not reach the folder.
   Postgres's own log says why. Check the folder exists, the account Postgres runs as can write to it, and no
   file of that name is already there (the copy never overwrites one)."
fi

printf '\033[32mArchiving is on.\033[0m\n'
printf '  files archived  %s since Postgres last reset its counts (%s in the folder now)\n' "$ARCHIVED" "$IN_FOLDER"
if [ -n "$LAST_WAL" ]; then
  printf '  newest          %s at %s' "$LAST_WAL" "$LAST_TIME"
  if [ -f "$WAL_ARCHIVE_DIR/$LAST_WAL" ]; then printf ', and it is in the folder\n'
  else printf '\n'; warn "  ...but that file is NOT in $WAL_ARCHIVE_DIR — Postgres is archiving somewhere else."; exit 1; fi
else
  echo "  newest          none yet — a file arrives within a minute of the first change to the database"
fi
exit 0
