#!/usr/bin/env bash
# Take a full copy of the running database's files (a "base backup"), PROVE every file in it is intact, keep the
# newest few, and clear out change-log files no kept copy can use any more.
#
# WHY THIS EXISTS: archived change-log files (tools/backup-start-archiving.sh) only say what CHANGED. To rebuild
# the database at a chosen moment, Postgres needs a full copy taken before that moment and then replays the
# changes on top of it. This makes that full copy. Run it weekly.
#
# WHY IT VERIFIES: a copy nobody has read is not a backup, it is a file. Postgres writes a list of every file in
# the copy with its checksum; this unpacks the copy into a scratch folder and checks every file against that
# list, and checks the change log the copy needs is inside it. The live database is not involved in the check.
#
# WHY IT DELETES ARCHIVED CHANGE-LOG FILES: a change-log file older than the oldest kept copy can never be used —
# a rebuild always starts from a copy and replays forward. Kept, they would only fill the disk.
#
# USAGE:  tools/backup-full-copy.sh              # take, verify and keep a copy; trim the old ones
#         tools/backup-full-copy.sh --dry-run    # only say what it would do; touches nothing
#         BASE_BACKUP_KEEP=4 tools/backup-full-copy.sh
#
# ON A HOSTED MACHINE: run it from cron once a week, e.g. Sundays at 04:30 —
#         30 4 * * 0 /srv/unwritten/tools/backup-full-copy.sh >> /var/log/unwritten-backup.log 2>&1
#
# EXIT: 0 = copied and verified; 1 = the copy failed or could not be verified (nothing was kept, nothing was
#       trimmed); 2 = setup problem.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$REPO/tools/load-settings.sh"
BASE_BACKUP_DIR="${BASE_BACKUP_DIR:-$REPO/backups/base}"
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-$REPO/backups/wal}"
# Three weekly copies: the newest can be up to 6 days old, so a moment 14 days back needs the one before the one
# before it. Fewer than three and the oldest days of the 14 cannot be rebuilt.
KEEP="${BASE_BACKUP_KEEP:-3}"

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

for tool in pg_basebackup pg_verifybackup pg_archivecleanup psql tar; do
  command -v "$tool" >/dev/null 2>&1 || fail "No $tool on this machine. Install the PostgreSQL client tools first."
done
[ -n "${DATABASE_URL:-}" ] || fail "DATABASE_URL is not set in settings.env or in this shell."
case "$KEEP" in ''|*[!0-9]*) fail "BASE_BACKUP_KEEP must be a whole number, not '$KEEP'." ;; esac

# Only copies that finished AND passed the check carry a 'verified' file, so a copy that was interrupted halfway
# is never counted, kept in place of a good one, or picked to rebuild from. Folder names sort by date.
verified_copies() { ls -1d "$BASE_BACKUP_DIR"/base-*/ 2>/dev/null | sed 's#/$##' | while IFS= read -r d; do
                      [ -f "$d/verified" ] && echo "$d"; done | sort -r; }

if [ "$DRY" = 1 ]; then
  echo "Dry run — nothing is copied, kept or deleted."
  echo "  would copy into  $BASE_BACKUP_DIR/base-<date-time>"
  echo "  would keep       the newest $KEEP verified copies (0 = keep all)"
  echo "  copies there now:"
  verified_copies | sed 's#^#    #'
  if [ "$KEEP" -gt 0 ]; then
    OLD="$(verified_copies | tail -n +"$KEEP")"
    [ -n "$OLD" ] && { echo "  after a new copy, would delete:"; printf '%s\n' "$OLD" | sed 's#^#    #'; }
  fi
  echo "  would clear      change-log files in $WAL_ARCHIVE_DIR older than the oldest kept copy"
  exit 0
fi

# A full copy without archiving can still be put back — but only as it was when the copy finished, never at a
# later moment. Worth saying; not worth refusing over.
MODE="$(psql "$DATABASE_URL" -X -A -t -q -c "show archive_mode" 2>&1)" || fail "Cannot reach the database at DATABASE_URL: $MODE"
[ "$MODE" = on ] || warn "Change-log archiving is OFF, so this copy can only be put back as it was when it finished.
   Turn archiving on with tools/backup-start-archiving.sh."

mkdir -p "$BASE_BACKUP_DIR" || fail "Cannot write to $BASE_BACKUP_DIR"
STAMP="$(date '+%Y-%m-%d-%H%M%S')"
OUT="$BASE_BACKUP_DIR/base-$STAMP"

# -Ft -z: two compressed archives, the data files and the change log written while copying.
# -X stream: that change log is collected alongside, so the copy can be brought to a consistent state on its own.
# -c fast: start straight away instead of waiting for Postgres's next routine write to disk; the database here is
#          small, so the extra write this causes is brief.
if ! pg_basebackup -d "$DATABASE_URL" -D "$OUT" -Ft -z -X stream -c fast -l "unwritten base-$STAMP"; then
  rm -rf "$OUT"
  lost "pg_basebackup failed. NOTHING was copied."
fi
# Written once the copy is complete, so this moment is always AFTER the copy is consistent. A rebuild picks the
# newest copy that finished before the moment it is asked for.
FINISHED="$(date '+%Y-%m-%d %H:%M:%S')"

# ---- Proof, read out of the COPY --------------------------------------------------------------------------
SCRATCH="$(mktemp -d)" || { rm -rf "$OUT"; fail "Cannot make a scratch folder."; }
trap 'rm -rf "$SCRATCH"' EXIT
mkdir -p "$SCRATCH/pg_wal"
# tar is run from inside the copy's folder so it is handed a bare file name: GNU tar reads a "D:" in a path as the
# name of another computer.
if ! ( cd "$OUT" && tar -xzf base.tar.gz -C "$SCRATCH" && tar -xzf pg_wal.tar.gz -C "$SCRATCH/pg_wal" ); then
  rm -rf "$OUT"; lost "The copy cannot be unpacked. Treat it as lost; it was deleted."
fi
if ! pg_verifybackup -q -m "$OUT/backup_manifest" "$SCRATCH"; then
  rm -rf "$OUT"; lost "The copy does not match its own file list. Treat it as lost; it was deleted."
fi
# The first change-log file this copy needs. Everything archived before it is of no use to this copy.
FIRST_WAL="$(sed -n 's/^START WAL LOCATION: .*(file \([0-9A-F]\{24\}\)).*/\1/p' "$SCRATCH/backup_label" | tr -d '\r')"
[ -n "$FIRST_WAL" ] || { rm -rf "$OUT"; lost "The copy has no readable start point. Treat it as lost; it was deleted."; }
printf 'finished=%s\nfirst_wal=%s\n' "$FINISHED" "$FIRST_WAL" > "$OUT/verified"

# ---- Retention ---------------------------------------------------------------------------------------------
# Only ever touches folders this script itself named, and only verified ones.
DELETED=0
if [ "$KEEP" -gt 0 ]; then
  while IFS= read -r old; do
    [ -n "$old" ] || continue
    rm -rf "$old" && DELETED=$((DELETED + 1))
  done <<< "$(verified_copies | tail -n +$((KEEP + 1)))"
fi

# ---- Clear change-log files no kept copy can use ------------------------------------------------------------
TRIMMED=0
OLDEST="$(verified_copies | tail -n 1)"
OLDEST_WAL="$(sed -n 's/^first_wal=//p' "$OLDEST/verified" | tr -d '\r')"
if [ "$KEEP" -gt 0 ] && [ -n "$OLDEST_WAL" ] && [ -d "$WAL_ARCHIVE_DIR" ]; then
  BEFORE="$(ls -1 "$WAL_ARCHIVE_DIR" | wc -l | tr -d ' ')"
  pg_archivecleanup "$WAL_ARCHIVE_DIR" "$OLDEST_WAL" \
    || warn "Could not clear old change-log files from $WAL_ARCHIVE_DIR. The copy itself is fine."
  AFTER="$(ls -1 "$WAL_ARCHIVE_DIR" | wc -l | tr -d ' ')"
  TRIMMED=$((BEFORE - AFTER))
fi

SIZE="$(du -sh "$OUT" | cut -f1 | tr -d ' ')"
KEPT="$(verified_copies | wc -l | tr -d ' ')"

printf '\033[32mFull copy taken and verified.\033[0m\n'
printf '  folder      %s (%s)\n' "$OUT" "$SIZE"
printf '  finished    %s\n' "$FINISHED"
printf '  checked     every file against the list Postgres wrote, read back out of the copy\n'
printf '  kept        %s full copies here' "$KEPT"
[ "$DELETED" -gt 0 ] && printf ', %s older one(s) removed' "$DELETED"
printf '\n'
[ "$TRIMMED" -gt 0 ] && printf '  cleared     %s archived change-log file(s) older than the oldest kept copy\n' "$TRIMMED"
exit 0
