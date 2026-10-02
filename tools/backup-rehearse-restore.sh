#!/usr/bin/env bash
# Rebuild a SPARE copy of the database as it was at a chosen moment, check it, say how long that took, and throw
# the spare away. The live database is never connected to, and port 5432 is never used.
#
# WHY THIS EXISTS: a restore that has never been tried may not work, and nobody knows how long it takes — at the
# one moment it is needed. This is the same rebuild a real restore would do, run on a spare, so both questions
# are answered in advance and the steps are known to work.
#
# WHAT IT DOES, in order:
#   1. picks the newest verified full copy (tools/backup-full-copy.sh) that finished before the moment;
#   2. unpacks it into a scratch folder and tells it to replay archived change-log files up to the moment;
#   3. starts a spare Postgres on that folder, on RESTORE_REHEARSAL_PORT (5433 unless set), answering this
#      computer only, with archiving off so it can never write into the live archive folder;
#   4. waits for the replay to finish, then runs the daily dump's checks — the account, character and
#      refresh_token tables exist, and how many characters there are — and lists the five most recently
#      changed characters, so a rehearsal can see whether a change made just before or after the moment is in;
#   5. stops the spare by its own port and deletes the scratch folder, whether it worked or not.
#
# USAGE:  tools/backup-rehearse-restore.sh "2026-10-02 14:30:30"            # a moment in this computer's local time
#         tools/backup-rehearse-restore.sh "2026-10-02 14:30:30" --dry-run  # only say which copy it would use
#
# EXIT: 0 = rebuilt and checked; 1 = the rebuild or a check failed; 2 = setup problem.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/load-settings.sh"
. "$REPO/tools/platform.sh"
BASE_BACKUP_DIR="${BASE_BACKUP_DIR:-$REPO/backups/base}"
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-$REPO/backups/wal}"
SPARE_PORT="${RESTORE_REHEARSAL_PORT:-5433}"
# Longest the replay may take before the rehearsal gives up. A small database replays a day of changes in seconds.
WAIT_SECONDS=3600

fail() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 2; }
lost() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 1; }

MOMENT=""; DRY=0
for arg in "$@"; do
  case "$arg" in
    -h|--help) sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    --dry-run) DRY=1 ;;
    -*) fail "Unknown option '$arg'. Try --help." ;;
    *) MOMENT="$arg" ;;
  esac
done
[ -n "$MOMENT" ] || fail 'Which moment? e.g.  tools/backup-rehearse-restore.sh "2026-10-02 14:30:30"'
OFFSET="$(time_offset "$MOMENT")" || fail "Not a moment in the shape YYYY-MM-DD HH:MM:SS: '$MOMENT'"
[[ "$MOMENT" < "$(date '+%Y-%m-%d %H:%M:%S')" ]] || fail "$MOMENT has not happened yet."

for tool in pg_ctl pg_isready psql tar; do
  command -v "$tool" >/dev/null 2>&1 || fail "No $tool on this machine. Install the PostgreSQL server tools first."
done
[ -n "${DATABASE_URL:-}" ] || fail "DATABASE_URL is not set in settings.env or in this shell."

# The spare is reached with the live database's own user, password and database name — they are inside the copy —
# but at this computer's address and the spare's port. The live address is never dialled.
case "$DATABASE_URL" in *://*) ;; *) fail "DATABASE_URL is not in the postgres://... shape this script reads." ;; esac
__auth="${DATABASE_URL#*://}"; __auth="${__auth%%[/?]*}"; __auth="${__auth##*@}"
LIVE_PORT=5432
case "$__auth" in *\]:[0-9]*|[!\[]*:[0-9]*) LIVE_PORT="${__auth##*:}" ;; esac
SPARE_URL="$(printf '%s' "$DATABASE_URL" | sed -E "s#^(postgres(ql)?://([^@/?]*@)?)[^/?]*#\\1127.0.0.1:$SPARE_PORT#")"

case "$SPARE_PORT" in ''|*[!0-9]*) fail "RESTORE_REHEARSAL_PORT must be a port number, not '$SPARE_PORT'." ;; esac
[ "$SPARE_PORT" != 5432 ] && [ "$SPARE_PORT" != "$LIVE_PORT" ] \
  || fail "RESTORE_REHEARSAL_PORT is $SPARE_PORT — the live database's port, or Postgres's standard 5432. The spare must never use either."
if port_info "$SPARE_PORT" >/dev/null 2>&1; then
  fail "Something is already listening on port $SPARE_PORT. This script never stops what it did not start —
   pick another port with RESTORE_REHEARSAL_PORT=..."
fi

# ---- Which full copy -----------------------------------------------------------------------------------------
# The newest one that FINISHED before the moment: replay can only move forward from a copy, and a copy is only
# usable from the moment it finished. Both times are this computer's local time, so they compare as text.
COPY=""; COPY_FINISHED=""
for v in "$BASE_BACKUP_DIR"/base-*/verified; do
  [ -f "$v" ] || continue
  f="$(sed -n 's/^finished=//p' "$v" | tr -d '\r')"
  if [[ "$f" < "$MOMENT" ]] && [[ "$f" > "$COPY_FINISHED" ]]; then COPY="$(dirname "$v")"; COPY_FINISHED="$f"; fi
done
[ -n "$COPY" ] || fail "No verified full copy in $BASE_BACKUP_DIR finished before $MOMENT. Take one with tools/backup-full-copy.sh."
[ -d "$WAL_ARCHIVE_DIR" ] || fail "No archive folder at $WAL_ARCHIVE_DIR — without it nothing after the copy can be replayed."
case "$WAL_ARCHIVE_DIR" in /*|[A-Za-z]:[/\\]*) ;; *) fail "WAL_ARCHIVE_DIR must be a full path, not '$WAL_ARCHIVE_DIR'." ;; esac

TARGET="$MOMENT$OFFSET"
RESTORE_COMMAND="$(wal_restore_command "$WAL_ARCHIVE_DIR")" || fail "Could not build the fetch command for this computer."

echo "Rebuilding a spare database as it was at $MOMENT"
echo "  from the full copy  $COPY (finished $COPY_FINISHED)"
echo "  replaying from      $WAL_ARCHIVE_DIR"
echo "  spare port          $SPARE_PORT (the live database is on $LIVE_PORT and is not touched)"
[ "$DRY" = 1 ] && { echo "Dry run — nothing was unpacked or started."; exit 0; }

START="$(date +%s)"
SCRATCH="$(mktemp -d)" || fail "Cannot make a scratch folder."
DATA="$SCRATCH/data"
LOG="$SCRATCH/spare.log"
SPARE_PID=""

# ---- Always stop the spare and delete the scratch folder, however this ends ----------------------------------
cleanup() {
  if [ -f "$DATA/postmaster.pid" ]; then
    pg_ctl -D "$DATA" stop -m fast -w >/dev/null 2>&1
  fi
  # Backstop, by port: only if what still listens there is the spare THIS run started. The port was checked
  # free before starting, and anything else on it is somebody else's.
  if [ -n "$SPARE_PID" ] && [ "$(port_pids "$SPARE_PORT")" = "$SPARE_PID" ]; then
    stop_port "$SPARE_PORT" >/dev/null
  fi
  # Windows can hold a file for a moment after its program exits.
  for _ in 1 2 3 4 5; do rm -rf "$SCRATCH" 2>/dev/null && [ ! -e "$SCRATCH" ] && break; sleep 1; done
  [ -e "$SCRATCH" ] && printf 'Could not delete the scratch folder %s — delete it by hand.\n' "$SCRATCH" >&2
}
trap cleanup EXIT
trap 'exit 1' INT TERM

mkdir -p "$DATA/pg_wal" || lost "Cannot write to $DATA"
# tar is run from inside the copy's folder so it is handed a bare file name: GNU tar reads a "D:" in a path as the
# name of another computer.
( cd "$COPY" && tar -xzf base.tar.gz -C "$DATA" && tar -xzf pg_wal.tar.gz -C "$DATA/pg_wal" ) \
  || lost "The full copy $COPY cannot be unpacked."
# Postgres refuses to start on a data folder anyone else can read (on a Mac or Linux; Windows ignores this).
chmod 700 "$DATA"

# A copy of a Debian/Ubuntu database has no settings files: they live in /etc there, outside the copied folder.
# Empty ones mean Postgres's defaults; the access file lets in this computer only, with the live user's password.
[ -f "$DATA/postgresql.conf" ] || echo "# Written by backup-rehearse-restore.sh: the copy came without one." > "$DATA/postgresql.conf"
[ -f "$DATA/pg_ident.conf" ]   || : > "$DATA/pg_ident.conf"
[ -f "$DATA/pg_hba.conf" ] || printf '%s\n' "# Written by backup-rehearse-restore.sh: this computer only." \
  "host all all 127.0.0.1/32 scram-sha-256" "host all all ::1/128 scram-sha-256" > "$DATA/pg_hba.conf"

# The recovery settings. A settings file treats a backslash as an escape and ends a value at a quote, so both are
# doubled — a Windows path is full of backslashes. Written into the COPY's own settings, in the scratch folder.
conf_quote() { printf '%s' "$1" | sed "s/\\\\/\\\\\\\\/g; s/'/''/g"; }
cat >> "$DATA/postgresql.auto.conf" <<EOF

# ---- Added by tools/backup-rehearse-restore.sh, for this spare copy only ----
restore_command = '$(conf_quote "$RESTORE_COMMAND")'
recovery_target_time = '$TARGET'
recovery_target_action = 'promote'
hot_standby = off
listen_addresses = 'localhost'
unix_socket_directories = ''
logging_collector = off
log_destination = 'stderr'
EOF
: > "$DATA/recovery.signal"

# The port and archive_mode go on the command line too: nothing in any settings file can override that, so the
# spare can never take the live port or copy its own change log into the live archive folder.
pg_ctl -D "$DATA" -l "$LOG" -o "-p $SPARE_PORT -c archive_mode=off" -W start >/dev/null \
  || { tail -n 20 "$LOG" 2>/dev/null >&2; lost "The spare Postgres would not start."; }

# ---- Wait for the replay to finish ---------------------------------------------------------------------------
# It turns away connections while replaying and accepts them once it has reached the moment and opened.
WAITED=0
until pg_isready -q -h 127.0.0.1 -p "$SPARE_PORT"; do
  [ -z "$SPARE_PID" ] && [ -f "$DATA/postmaster.pid" ] && SPARE_PID="$(head -n 1 "$DATA/postmaster.pid" | tr -d '\r ')"
  if ! pg_ctl -D "$DATA" status >/dev/null 2>&1 && [ "$WAITED" -ge 2 ]; then
    tail -n 20 "$LOG" >&2
    if grep -q "recovery ended before configured recovery target was reached" "$LOG"; then
      lost "The archive has no change to the database AFTER $MOMENT yet, so the replay cannot tell it has reached it.
   Make any change after that moment (a character saving is one), wait a minute for its file to be archived, and run this again."
    fi
    lost "The spare Postgres stopped during the replay. Its log is above."
  fi
  [ "$WAITED" -lt "$WAIT_SECONDS" ] || { tail -n 20 "$LOG" >&2; lost "The replay did not finish in $WAIT_SECONDS seconds."; }
  sleep 1; WAITED=$((WAITED + 1))
done
[ -z "$SPARE_PID" ] && SPARE_PID="$(head -n 1 "$DATA/postmaster.pid" | tr -d '\r ')"

sq() { psql "$SPARE_URL" -X -A -t -q -v ON_ERROR_STOP=1 "$@"; }
IN_RECOVERY="$(sq -c "select pg_is_in_recovery()")" || lost "The spare opened but cannot be queried."
[ "$IN_RECOVERY" = f ] || lost "The spare is still replaying after it opened — it did not finish."

# ---- The daily dump's checks, read out of the SPARE -----------------------------------------------------------
TABLES=" $(sq -c "select string_agg(tablename, ' ') from pg_tables where schemaname = 'public'") "
for table in account character refresh_token; do
  case "$TABLES" in *" $table "*) ;; *) lost "The rebuilt database has no '$table' table." ;; esac
done
CHARACTERS="$(sq -c 'select count(*) from public."character"')" || lost "Cannot count the characters in the spare."
NEWEST="$(sq -F '  ' -c "select to_char(updated_at, 'YYYY-MM-DD HH24:MI:SS'), 'level ' || coalesce(progress->>'level', '-'), name
                         from public.\"character\" where deleted_at is null order by updated_at desc limit 5")"
STOPPED_AT="$(grep -m 1 -E 'recovery stopping (before|after)' "$LOG" | sed 's/.*\(recovery stopping\)/\1/')"

END="$(date +%s)"
TOOK=$((END - START))

printf '\033[32mRebuilt and checked.\033[0m\n'
printf '  as it was at  %s\n' "$MOMENT"
[ -n "$STOPPED_AT" ] && printf '  replay        %s\n' "$STOPPED_AT"
printf '  tables        account, character, refresh_token all present\n'
printf '  characters    %s\n' "$CHARACTERS"
printf '  last changed  (newest first)\n'
printf '%s\n' "$NEWEST" | sed 's/^/    /'
printf '  took          %dm %02ds, from unpacking to the last check\n' $((TOOK / 60)) $((TOOK % 60))
echo "Stopping the spare and deleting its scratch folder."
exit 0
