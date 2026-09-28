#!/usr/bin/env bash
# Save a copy of the whole database to a file, and PROVE the file is readable before calling it a backup.
#
# WHY THIS EXISTS: the database is the only thing in this project that exists nowhere else. Every character,
# every save, every account is one row in it; the game builds can be rebuilt and the code is in git, but a lost
# database is lost players. Nothing was backing it up.
#
# WHY IT VERIFIES: a backup nobody has ever read is not a backup, it is a file. This one reads the dump back
# and counts the characters inside it — without touching the live database — so the number in the report comes
# from the FILE, not from the thing the file was supposed to copy.
#
# USAGE:  tools/backup-database.sh                    # write one backup, keep the last 14
#         BACKUP_DIR=/var/backups/unwritten tools/backup-database.sh
#         KEEP=30 tools/backup-database.sh
#
# ON A HOSTED MACHINE: run it from cron, e.g. every day at 04:10 —
#         10 4 * * * BACKUP_DIR=/var/backups/unwritten /srv/unwritten/tools/backup-database.sh >> /var/log/unwritten-backup.log 2>&1
#
# TO PUT ONE BACK (destroys what is in the database now — read it twice):
#         pg_restore --clean --if-exists -d "$DATABASE_URL" backups/unwritten-YYYY-MM-DD-HHMM.dump
#
# EXIT: 0 = backed up and verified, 1 = the backup failed or could not be verified, 2 = setup problem.

set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKUP_DIR="${BACKUP_DIR:-$REPO/backups}"
KEEP="${KEEP:-14}"

fail() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 2; }
lost() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 1; }

command -v pg_dump    >/dev/null 2>&1 || fail "No pg_dump on this machine. Install the PostgreSQL client tools first."
command -v pg_restore >/dev/null 2>&1 || fail "No pg_restore on this machine. Install the PostgreSQL client tools first."

# The database address lives in settings.env, with every other setting, and there is only one of it. Loaded
# the one way every script here loads it; a DATABASE_URL already set in this shell wins.
. "$REPO/tools/load-settings.sh"
[ -n "${DATABASE_URL:-}" ] || fail "DATABASE_URL is not set in settings.env or in this shell.
   Copy settings.env.example to settings.env and fill it in, or run this with DATABASE_URL=..."

mkdir -p "$BACKUP_DIR" || fail "Cannot write to $BACKUP_DIR"

STAMP="$(date '+%Y-%m-%d-%H%M')"
OUT="$BACKUP_DIR/unwritten-$STAMP.dump"

# -Fc: the compressed format pg_restore reads. It keeps the schema and the data together and can be restored
# whole or one table at a time, which the plain-SQL format cannot.
if ! pg_dump -Fc --no-owner --no-privileges -f "$OUT" "$DATABASE_URL"; then
  rm -f "$OUT"
  lost "pg_dump failed. NOTHING was backed up."
fi

[ -s "$OUT" ] || { rm -f "$OUT"; lost "The backup file came out empty. NOTHING was backed up."; }

# ---- Proof, read out of the FILE ---------------------------------------------------------------------------
# The table of contents must list the things we expect, and the character rows must actually be in there. Both
# are read from the dump with the live database uninvolved, so a database that vanished a second ago cannot
# make this pass.
TOC="$(pg_restore --list "$OUT" 2>/dev/null)" || { lost "The backup file cannot be read back. Treat it as lost."; }

for table in account character refresh_token; do
  printf '%s\n' "$TOC" | grep -q "TABLE DATA public $table " \
    || lost "The backup does not contain the '$table' rows. Treat it as lost."
done

# Count the character rows sitting in the dump. Asking for that one table leaves exactly one COPY block in the
# output, and the rows are the lines between it and the \. that closes it. Matched on the bare word COPY on
# purpose: the table's name is a reserved word, so pg_dump writes it quoted — public."character" — and a
# pattern that spelled the name out reported zero rows for a dump that held every one of them.
CHARACTERS="$(pg_restore --data-only --table=character -f - "$OUT" 2>/dev/null \
  | awk '/^COPY /{inside=1; next} inside && /^\\\.$/{inside=0} inside{n++} END{print n+0}')"

# ---- Retention ---------------------------------------------------------------------------------------------
# Only ever touches files this script itself named. Anything else in the folder is left alone.
DELETED=0
if [ "$KEEP" -gt 0 ] 2>/dev/null; then
  while IFS= read -r old; do
    [ -n "$old" ] || continue
    rm -f "$old" && DELETED=$((DELETED + 1))
  done <<< "$(ls -1t "$BACKUP_DIR"/unwritten-*.dump 2>/dev/null | tail -n +$((KEEP + 1)))"
fi

SIZE="$(du -h "$OUT" | cut -f1 | tr -d ' ')"
KEPT="$(ls -1 "$BACKUP_DIR"/unwritten-*.dump 2>/dev/null | wc -l | tr -d ' ')"

printf '\033[32mBacked up and verified.\033[0m\n'
printf '  file        %s (%s)\n' "$OUT" "$SIZE"
printf '  characters  %s, read back out of the file\n' "$CHARACTERS"
printf '  kept        %s backups here' "$KEPT"
[ "$DELETED" -gt 0 ] && printf ', %s older one(s) removed' "$DELETED"
printf '\n'
