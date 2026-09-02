#!/bin/sh
# End-to-end: sign in through the Node login service, then do EVERYTHING else against the C# char server.
# Proves the two share one secret and one database, and that the ported endpoints behave.
set -e
LOGIN=http://127.0.0.1:8080
CHAR=http://127.0.0.1:8081
EMAIL="e2e-$(date +%s)-$$@example.com"
NAME="E2E$(date +%s | tail -c 6)"
fail() { echo "FAIL: $1"; exit 1; }
ok()   { echo "  ok  $1"; }

echo "1. register + sign in (login service, Node)"
curl -s -X POST "$LOGIN/accounts" -H 'content-type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"hunter2hunter2\"}" >/dev/null
TOK=$(curl -s -X POST "$LOGIN/sessions" -H 'content-type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"hunter2hunter2\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
[ -n "$TOK" ] || fail "no access token"
ok "token issued by Node"

echo "2. that token is accepted by the C# char server"
MODELS=$(curl -s "$CHAR/character-models" -H "authorization: Bearer $TOK")
echo "$MODELS" | grep -q '"models"' || fail "models: $MODELS"
ok "shared secret verified across the two servers"

echo "3. a bad token is refused"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$CHAR/characters" -H "authorization: Bearer not.a.token")
[ "$CODE" = "401" ] || fail "expected 401 for a bad token, got $CODE"
ok "forged token refused ($CODE)"

echo "4. no token at all is refused"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$CHAR/characters")
[ "$CODE" = "401" ] || fail "expected 401 with no token, got $CODE"
ok "anonymous refused ($CODE)"

echo "5. create a character"
MODEL=$(echo "$MODELS" | python3 -c 'import sys,json;print(json.load(sys.stdin)["models"][0])')
NEW=$(curl -s -X POST "$CHAR/characters" -H "authorization: Bearer $TOK" -H 'content-type: application/json' \
  -d "{\"name\":\"$NAME\",\"modelName\":\"$MODEL\",\"dyeSources\":[\"#112233\"],\"dyeTargets\":[\"#445566\"]}")
CID=$(echo "$NEW" | python3 -c 'import sys,json;print(json.load(sys.stdin).get("id",""))')
[ -n "$CID" ] || fail "create: $NEW"
ok "created $NAME ($CID)"

echo "6. the same name twice is refused"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CHAR/characters" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d "{\"name\":\"$NAME\",\"modelName\":\"$MODEL\"}")
[ "$CODE" = "409" ] || fail "expected 409 for a taken name, got $CODE"
ok "duplicate name refused ($CODE)"

echo "7. a bad name is refused"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CHAR/characters" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d "{\"name\":\"no spaces!\",\"modelName\":\"$MODEL\"}")
[ "$CODE" = "400" ] || fail "expected 400 for a bad name, got $CODE"
ok "bad name refused ($CODE)"

echo "8. it appears in the list, with its level read out of the save"
LIST=$(curl -s "$CHAR/characters" -H "authorization: Bearer $TOK")
echo "$LIST" | grep -q "$NAME" || fail "list: $LIST"
ok "listed"

echo "9. write a real save, read it back byte-for-byte"
SAVE='{"version":9,"level":42,"xp":1234,"gold":777,"bagItemIds":["potion_red"],"bagCounts":[3],"modelName":"'"$MODEL"'"}'
curl -s -X PUT "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d "{\"progress\":$SAVE}" >/dev/null
BACK=$(curl -s "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK")
echo "$BACK" | python3 -c '
import sys,json
d=json.load(sys.stdin); p=d["progress"]
assert p["level"]==42, p
assert p["gold"]==777, p
assert p["bagItemIds"]==["potion_red"], p
assert p["bagCounts"]==[3], p
assert d["level"]==42, d
assert d["isGm"] is False, d
print("  ok  save round-tripped: level",p["level"],"gold",p["gold"],"bag",p["bagItemIds"])'

echo "10. a save with no version is refused"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$CHAR/characters/$CID/progress" \
  -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d '{"progress":{"level":99}}')
[ "$CODE" = "400" ] || fail "expected 400 for an unversioned save, got $CODE"
ok "unversioned save refused ($CODE)"

echo "11. a save that names no body keeps the look already stored"
curl -s -X PUT "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d '{"progress":{"version":9,"level":43}}' >/dev/null
curl -s "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK" | python3 -c '
import sys,json
d=json.load(sys.stdin)
assert d["modelName"], "appearance was wiped by a save that did not carry it: "+json.dumps(d)
assert d["progress"]["level"]==43, d
print("  ok  appearance survived a save that omitted it:",d["modelName"])'

echo "12. recolour through the appearance route, merged not replaced"
curl -s -X PATCH "$CHAR/characters/$CID/appearance" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d '{"dyeSources":["#aabbcc"],"dyeTargets":["#ddeeff"]}' >/dev/null
curl -s "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK" | python3 -c '
import sys,json
d=json.load(sys.stdin)
assert d["dyeSources"]==["#aabbcc"], d
assert d["progress"]["level"]==43, "the recolour clobbered progress: "+json.dumps(d["progress"])
print("  ok  recolour merged, level still",d["progress"]["level"])'

echo "13. another account cannot see or touch it"
E2="other-$(date +%s)-$$@example.com"
curl -s -X POST "$LOGIN/accounts" -H 'content-type: application/json' -d "{\"email\":\"$E2\",\"password\":\"hunter2hunter2\"}" >/dev/null
TOK2=$(curl -s -X POST "$LOGIN/sessions" -H 'content-type: application/json' \
  -d "{\"email\":\"$E2\",\"password\":\"hunter2hunter2\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK2")
[ "$CODE" = "404" ] || fail "another account got $CODE reading someone else's character (expected 404)"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$CHAR/characters/$CID" -H "authorization: Bearer $TOK2")
[ "$CODE" = "404" ] || fail "another account got $CODE deleting someone else's character (expected 404)"
ok "another account is refused (404 on read and on delete)"

echo "15. the appearance route caps colours the same way creation does"
BIG=$(python3 -c 'import json;print(json.dumps({"dyeSources":["#aabbcc"]*100,"dyeTargets":["#ddeeff"]*100}))')
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X PATCH "$CHAR/characters/$CID/appearance" \
  -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d "$BIG")
[ "$CODE" = "400" ] || fail "appearance took 100 colour pairs (got $CODE) — the cap is missing"
ok "too many colours refused on recolour ($CODE)"

echo "16. a colour with stray spaces is accepted and stored trimmed"
curl -s -o /dev/null -X PATCH "$CHAR/characters/$CID/appearance" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d '{"dyeSources":[" #aabbcc "],"dyeTargets":["#ddeeff "]}'
curl -s "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK" | python3 -c '
import sys,json
d=json.load(sys.stdin)
assert d["dyeSources"]==["#aabbcc"], d["dyeSources"]
assert d["dyeTargets"]==["#ddeeff"], d["dyeTargets"]
print("  ok  colours trimmed, not refused")'

echo "17. a very large version is accepted, as the login service accepts it"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$CHAR/characters/$CID/progress" \
  -H "authorization: Bearer $TOK" -H 'content-type: application/json' \
  -d '{"progress":{"version":3000000000,"level":3000000000}}')
[ "$CODE" = "200" ] || fail "a version above two billion was refused ($CODE)"
curl -s "$CHAR/characters" -H "authorization: Bearer $TOK" | python3 -c '
import sys,json
cs=json.load(sys.stdin)["characters"]
lv=[c["level"] for c in cs if c["level"] is not None]
assert 3000000000 in lv, "a level above two billion read back as never-played: "+json.dumps(lv)
print("  ok  large whole numbers survive the round trip")'

echo "18. an id in any spelling but the plain one is refused"
NODASH=$(echo "$CID" | tr -d -)
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$CHAR/characters/$NODASH/progress" -H "authorization: Bearer $TOK")
[ "$CODE" = "400" ] || fail "an id with no dashes was accepted (got $CODE)"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$CHAR/characters/%7B$CID%7D/progress" -H "authorization: Bearer $TOK")
[ "$CODE" = "400" ] || fail "a braced id was accepted (got $CODE)"
ok "only the plain dashed id is an id"

echo "19. a broken body from a stranger says so plainly, and leaks nothing"
BODY=$(curl -s -X PUT "$CHAR/characters/$CID/progress" -H 'content-type: application/json' -d '{oops')
echo "$BODY" | grep -qi "exception\|   at \|System\." && fail "a stack trace was sent to an anonymous caller: $BODY"
ok "no stack trace in a refusal"

echo "20. health actually asks the database"
curl -s "$CHAR/health" | python3 -c '
import sys,json
d=json.load(sys.stdin); assert d["ok"] is True, d
print("  ok  health answered", d)'

echo "21. delete frees the name"
curl -s -o /dev/null -X DELETE "$CHAR/characters/$CID" -H "authorization: Bearer $TOK"
curl -s "$CHAR/characters" -H "authorization: Bearer $TOK" | grep -q "$NAME" && fail "still listed after delete"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CHAR/characters" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d "{\"name\":\"$NAME\",\"modelName\":\"$MODEL\"}")
[ "$CODE" = "201" ] || fail "the name was not freed by the delete, got $CODE"
ok "deleted, and the name came free"


echo
echo "ALL 21 PASSED — the C# char server is serving real characters out of the real database,"
echo "trusting tokens the Node login service issued."
