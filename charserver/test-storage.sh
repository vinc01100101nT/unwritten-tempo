#!/bin/sh
# Prove an account's STORAGE end to end — on a THROWAWAY database, never the one settings.env names.
#
# WHY IT MAKES ITS OWN DATABASE: storage is SQL the compiler never reads. The first run of this found a take of a whole
# stack answering 500, because writing a count of 0 breaks the table's own rule — nothing short of running the statements
# could have said so. And a test that writes to the real database leaves debris (see SERVER.md), so this one never
# touches its tables: it creates `unwritten_storage_test` beside it on the same postgres, applies every migration, runs a
# copy of this server built into a temporary folder on a spare port, and removes all three when it ends.
#
# WHAT IT PROVES: a store and a take ride the progress save and apply ONCE however often that save is sent; a move that
# cannot be made (storage full, a piece already stored or still carried, nothing left to take) answers 422 and writes
# NOTHING, progress included; every character of an account sees one storage and a stranger sees none of it; a stack
# taken to nothing leaves no row.
#
# USAGE:  charserver/test-storage.sh          (STORAGE_TEST_PORT=18099 by default)
# EXIT:   0 = every check passed, 1 = a check failed, 2 = it could not set itself up.
REPO="$(cd "$(dirname "$0")/.." && pwd)"
. "$REPO/tools/load-settings.sh"
. "$REPO/tools/platform.sh"
PY="$(find_python)" || { echo "no working Python"; exit 2; }
PORT="${STORAGE_TEST_PORT:-18099}"
SCRATCH_DB=unwritten_storage_test
LIVE_URL="${DATABASE_URL:?DATABASE_URL is not set in settings.env}"
case "$LIVE_URL" in *\?*) echo "DATABASE_URL carries options after a '?', which this test cannot rewrite — stopping"; exit 2 ;; esac
SCRATCH_URL="${LIVE_URL%/*}/$SCRATCH_DB"
[ "$SCRATCH_URL" != "$LIVE_URL" ] || { echo "the test database would be the real one — stopping"; exit 2; }
command -v psql >/dev/null 2>&1 || { echo "psql is not on the PATH"; exit 2; }
[ -z "$(sh "$REPO/tools/platform.sh" port-info "$PORT" 2>/dev/null)" ] \
  || { echo "port $PORT is taken — set STORAGE_TEST_PORT to a free one"; exit 2; }

SECRET=storage-test-secret-used-by-this-run-only
BUILD="$(grep -oE 'unwritten-character-v[0-9]+' "$REPO/charserver/MapServers.cs" | head -1)"
BASE="http://127.0.0.1:$PORT"
WORK="$(mktemp -d)"
started=0
cleanup() {
  [ "$started" = 1 ] && sh "$REPO/tools/platform.sh" stop-port "$PORT" >/dev/null 2>&1
  sleep 1
  psql "$LIVE_URL" -q -c "drop database if exists $SCRATCH_DB" >/dev/null 2>&1
  rm -rf "$WORK"
}
trap cleanup EXIT

fails=0
check() { # what, got, wanted
  if [ "$2" = "$3" ]; then echo "  ok    $1"; else echo "  WRONG $1 — got [$2], wanted [$3]"; fails=$((fails + 1)); fi
}

echo "1. a throwaway database with every migration applied"
psql "$LIVE_URL" -q -c "drop database if exists $SCRATCH_DB" >/dev/null 2>&1
psql "$LIVE_URL" -q -c "create database $SCRATCH_DB" || { echo "could not create the test database"; exit 2; }
for f in "$REPO"/accounts/sql/*.sql; do
  psql "$SCRATCH_URL" -q -v ON_ERROR_STOP=1 -f "$f" >/dev/null 2>"$WORK/mig.err" \
    || { echo "  WRONG migration $(basename "$f") failed:"; cat "$WORK/mig.err"; exit 1; }
done
echo "  ok    $(ls "$REPO"/accounts/sql/*.sql | wc -l | tr -d ' ') migrations applied"
psql "$SCRATCH_URL" -q -v ON_ERROR_STOP=1 -f "$REPO/accounts/sql/012_account_storage.sql" >/dev/null 2>"$WORK/mig.err"
check "012_account_storage.sql runs a second time without error" "$?" "0"

q() { psql "$SCRATCH_URL" -A -t -q -c "$1" | head -1; }
ACC=$(q "insert into account (email, password_hash) values ('storage-test@example.com', 'x') returning id")
ACC2=$(q "insert into account (email, password_hash) values ('storage-stranger@example.com', 'x') returning id")
C1=$(q "insert into character (account_id, name, progress) values ('$ACC', 'StorageOne', '{\"version\":18,\"level\":1}') returning id")
C2=$(q "insert into character (account_id, name, progress) values ('$ACC', 'StorageTwo', '{\"version\":18,\"level\":1}') returning id")
C3=$(q "insert into character (account_id, name, progress) values ('$ACC2', 'Stranger', '{\"version\":18,\"level\":1}') returning id")

echo "2. this server, built into a temporary folder and run against that database on port $PORT"
( cd "$REPO/charserver" && dotnet build --nologo -v quiet -o "$(native_path "$WORK/build")" >"$WORK/build.log" 2>&1 ) \
  || { echo "the server did not build:"; tail -20 "$WORK/build.log"; exit 2; }
( cd "$WORK/build" && DATABASE_URL="$SCRATCH_URL" JWT_SECRET=storage-test-jwt-secret-storage-test-jwt CHAR_SERVER_PORT="$PORT" \
  BIND_HOST=127.0.0.1 SERVER_SECRET="$SECRET" PATCH_DIR= \
  CHARACTER_MODELS_PATH="$(native_path "$REPO/accounts/data/character-models.json")" \
  dotnet CharServer.dll >"$WORK/server.log" 2>&1 & )
started=1
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30; do
  curl -s -o /dev/null "$BASE/health" && break
  sleep 0.5
done
[ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health")" = "200" ] \
  || { echo "the test server did not come up:"; tail -20 "$WORK/server.log"; exit 2; }
echo "  ok    answering"

uuid() { "$PY" -c "import uuid;print(uuid.uuid4())"; }
# put <character> <level> <bagCopyIds as json> <storage as json>  → prints the http code; the body is in $WORK/reply.json
put() {
  curl -s -o "$WORK/reply.json" -w '%{http_code}' -X PUT "$BASE/server/characters/$1/progress" -H "x-server-secret: $SECRET" -H "x-character-build: $BUILD" \
    -H 'content-type: application/json' \
    -d "{\"progress\":{\"version\":18,\"level\":$2,\"bagItemIds\":[],\"bagCopyIds\":$3,\"equipCopyIds\":[]},\"quests\":[],\"handover\":0,\"storage\":$4}"
}
# move <id> <kind> <item> <count> <folds> <roll> <copy id> <limit>
move() { echo "{\"moveId\":\"$1\",\"kind\":\"$2\",\"itemId\":\"$3\",\"count\":$4,\"refine\":$5,\"cards\":\"\",\"roll\":\"$6\",\"copyId\":\"$7\",\"limit\":$8}"; }
stored() {
  curl -s "$BASE/server/characters/$1/storage" -H "x-server-secret: $SECRET" -H "x-character-build: $BUILD" | "$PY" -c "
import sys, json
d = json.load(sys.stdin)
print('rev', d['revision'], ';'.join('%s x%d %s' % (r['itemId'], r['count'], r['copyId']) for r in d['rows']))"
}
why() { "$PY" -c "import sys, json; print(json.load(open(sys.argv[1]))['error'])" "$(native_path "$WORK/reply.json")"; }
level() { q "select progress->>'level' from character where id = '$1'"; }

echo "3. the read door"
check "refuses a caller with no secret" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/server/characters/$C1/storage")" "401"
check "an account that never stored anything reads as nothing" "$(stored "$C1")" "rev 0 "

echo "4. storing"
M1=$(uuid)
check "5 Fly Wings go in" "$(put "$C1" 2 '[]' "$(move "$M1" store item.fly-wing 5 0 '' '' 600)")" "200"
check "  storage" "$(stored "$C1")" "rev 1 item.fly-wing x5 "
check "the SAME save sent again is accepted" "$(put "$C1" 2 '[]' "$(move "$M1" store item.fly-wing 5 0 '' '' 600)")" "200"
check "  and applied once" "$(stored "$C1")" "rev 1 item.fly-wing x5 "
check "3 more join the stack" "$(put "$C1" 3 '[]' "$(move "$(uuid)" store item.fly-wing 3 0 '' '' 600)")" "200"
check "  storage" "$(stored "$C1")" "rev 2 item.fly-wing x8 "
check "a save carrying no move (an empty move id) is an ordinary save" "$(put "$C1" 4 '[]' "$(move '' '' '' 0 0 '' '' 0)")" "200"
check "  storage untouched" "$(stored "$C1")" "rev 2 item.fly-wing x8 "
check "  progress written" "$(level "$C1")" "4"
check "a piece of gear goes in as itself" "$(put "$C1" 5 '[]' "$(move "$(uuid)" store item.sword 1 3 rollA sw-1 600)")" "200"
check "  storage" "$(stored "$C1")" "rev 3 item.fly-wing x8 ;item.sword x1 sw-1"
check "  with its folds and its roll" "$(curl -s "$BASE/server/characters/$C1/storage" -H "x-server-secret: $SECRET" -H "x-character-build: $BUILD" \
  | "$PY" -c "import sys, json; r = json.load(sys.stdin)['rows'][1]; print(r['refine'], r['roll'])")" "3 rollA"

echo "5. a store that cannot be made takes the whole save back with it"
check "the same piece a second time" "$(put "$C1" 99 '[]' "$(move "$(uuid)" store item.sword 1 3 rollA sw-1 600)")" "422"
check "  says why" "$(why)" "that piece is already stored"
check "  and wrote nothing: the level is still 5" "$(level "$C1")" "5"
check "the same piece from a stranger's account" "$(put "$C3" 7 '[]' "$(move "$(uuid)" store item.sword 1 3 rollA sw-1 600)")" "422"
check "a piece the same save still carries" "$(put "$C1" 99 '["sw-2"]' "$(move "$(uuid)" store item.sword 1 0 '' sw-2 600)")" "422"
check "  says why" "$(why)" "that piece is still carried"
check "a new thing into a full storage (limit 2, 2 stored)" "$(put "$C1" 99 '[]' "$(move "$(uuid)" store item.antidote 1 0 '' '' 2)")" "422"
check "  says why" "$(why)" "storage is full"
check "more of a stack already stored still fits when full" "$(put "$C1" 6 '[]' "$(move "$(uuid)" store item.fly-wing 1 0 '' '' 2)")" "200"
check "  storage" "$(stored "$C1")" "rev 4 item.fly-wing x9 ;item.sword x1 sw-1"

echo "6. one storage for the whole account"
check "another character of the account sees the same" "$(stored "$C2")" "rev 4 item.fly-wing x9 ;item.sword x1 sw-1"
check "a stranger's character sees none of it" "$(stored "$C3")" "rev 0 "

echo "7. taking"
check "the other character takes 4 Fly Wings" "$(put "$C2" 2 '[]' "$(move "$(uuid)" take item.fly-wing 4 0 '' '' 600)")" "200"
check "  storage" "$(stored "$C1")" "rev 5 item.fly-wing x5 ;item.sword x1 sw-1"
check "more than is stored" "$(put "$C2" 99 '[]' "$(move "$(uuid)" take item.fly-wing 6 0 '' '' 600)")" "422"
check "  says why" "$(why)" "that is no longer in storage"
check "  and wrote nothing: the level is still 2" "$(level "$C2")" "2"
check "a piece the bag being written does not hold" "$(put "$C2" 99 '[]' "$(move "$(uuid)" take item.sword 1 3 rollA sw-1 600)")" "422"
check "the piece, with it in the bag being written" "$(put "$C2" 3 '["sw-1"]' "$(move "$(uuid)" take item.sword 1 3 rollA sw-1 600)")" "200"
check "  storage" "$(stored "$C1")" "rev 6 item.fly-wing x5 "
check "the piece a second time" "$(put "$C2" 99 '["sw-1"]' "$(move "$(uuid)" take item.sword 1 3 rollA sw-1 600)")" "422"
check "the whole of what is left of a stack" "$(put "$C1" 7 '[]' "$(move "$(uuid)" take item.fly-wing 5 0 '' '' 600)")" "200"
check "  storage is empty" "$(stored "$C1")" "rev 7 "
check "  and no row of nothing is left in the table" "$(q "select count(*) from account_storage")" "0"

echo "8. a move that is not one"
check "neither store nor take" "$(put "$C1" 99 '[]' "$(move "$(uuid)" steal item.fly-wing 1 0 '' '' 600)")" "400"
check "a count of nothing" "$(put "$C1" 99 '[]' "$(move "$(uuid)" store item.fly-wing 0 0 '' '' 600)")" "400"
check "two of one piece of gear" "$(put "$C1" 99 '[]' "$(move "$(uuid)" store item.sword 2 0 '' sw-9 600)")" "400"
check "a character that does not exist" "$(put 00000000-0000-0000-0000-000000000000 1 '[]' "$(move "$(uuid)" store item.fly-wing 1 0 '' '' 600)")" "404"

echo "9. the table"
check "its three indexes exist" "$(q "select count(*) from pg_indexes where indexname in ('account_storage_by_account','account_storage_one_stack','account_storage_one_copy')")" "3"
q "delete from account where id = '$ACC'" >/dev/null
check "deleting the account takes its storage with it" "$(q "select count(*) from account_storage_head where account_id = '$ACC'")" "0"

echo
if [ "$fails" = 0 ]; then echo "ALL PASSED — storage does what it says, and the test database is removed on the way out."; exit 0; fi
echo "$fails check(s) wrong."
exit 1
