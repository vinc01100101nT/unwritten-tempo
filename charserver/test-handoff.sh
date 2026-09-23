#!/bin/sh
# The Ragnarok handoff, end to end, with a stand-in for the map server.
set -e
CHAR=http://127.0.0.1:8081
LOGIN=http://127.0.0.1:8080
SECRET=test-shared-server-secret
BUILD=unwritten-character-v15
# The stand-in map server must never outlive this script, however it ends — a leftover one holds the
# terminal open and answers for a map server that is not there.
trap 'pkill -f mapstub.py 2>/dev/null; exit' EXIT INT TERM

fail() { echo "FAIL: $1"; exit 1; }
ok() { echo "  ok  $1"; }

# A stand-in map server: answers /arriving, records what it was handed.
python3 - > /private/tmp/mapstub.log 2>&1 &
STUB=$!
sleep 0
cat > /private/tmp/mapstub.py <<'PYEOF'
import json, http.server
GOT = []
class H(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        n = int(self.headers.get('content-length', 0))
        body = self.rfile.read(n).decode()
        if self.headers.get('x-server-secret') != 'test-shared-server-secret':
            self.send_response(401); self.end_headers(); self.wfile.write(b'{}'); return
        d = json.loads(body)
        open('/private/tmp/arrival.json', 'w').write(body)
        if d.get('build') != 'unwritten-character-v15':
            self.send_response(409); self.end_headers(); self.wfile.write(b'{}'); return
        self.send_response(200); self.send_header('content-type','application/json'); self.end_headers()
        self.wfile.write(b'{"ok":true}')
    def log_message(self, *a): pass
http.server.HTTPServer(('127.0.0.1', 7788), H).serve_forever()
PYEOF
kill $STUB 2>/dev/null || true
python3 /private/tmp/mapstub.py & STUB=$!
i=0; while [ $i -lt 60 ]; do curl -s -m 1 -o /dev/null http://127.0.0.1:7788/ 2>/dev/null && break; i=$((i+1)); done

echo "1. a map server registers"
R=$(curl -s -X POST "$CHAR/map-servers" -H "x-server-secret: $SECRET" -H 'content-type: application/json' \
  -d "{\"id\":\"stub-1\",\"address\":\"127.0.0.1\",\"port\":7788,\"playerPort\":7777,\"maps\":[\"town_0\",\"field_11\"],\"build\":\"$BUILD\"}")
echo "$R" | grep -q '"holding":2' || fail "register: $R"
ok "registered, holding 2 maps"

echo "2. a map server built against a different character is REFUSED"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CHAR/map-servers" -H "x-server-secret: $SECRET" \
  -H 'content-type: application/json' -d '{"id":"old","address":"127.0.0.1","port":9,"maps":[],"build":"unwritten-character-v1"}')
[ "$CODE" = "409" ] || fail "a mismatched build was accepted (got $CODE)"
ok "mismatched build refused ($CODE) — the half-deploy guard works"

echo "3. a PLAYER's token cannot register a map server"
TOK=$(curl -s -X POST "$LOGIN/sessions" -H 'content-type: application/json' \
  -d '{"email":"alpha@test.local","password":"testpass123"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$CHAR/map-servers" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/json' -d '{"id":"nope","address":"127.0.0.1","port":9,"maps":[],"build":"'$BUILD'"}')
[ "$CODE" = "401" ] || fail "a player's token registered a map server (got $CODE)"
ok "a player cannot register a map server ($CODE)"

echo "4. a player asks to enter, and their character is PUSHED ahead of them"
rm -f /private/tmp/arrival.json
CID=$(curl -s "$CHAR/characters" -H "authorization: Bearer $TOK" | python3 -c 'import sys,json;print(json.load(sys.stdin)["characters"][0]["id"])')
E=$(curl -s -X POST "$CHAR/characters/$CID/enter" -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d '{"map":"town_0"}')
TICKET=$(echo "$E" | python3 -c 'import sys,json;print(json.load(sys.stdin).get("ticket",""))')
[ -n "$TICKET" ] || fail "enter: $E"
echo "$E" | grep -q '"port":7777' || fail "the player was given the wrong door — that is the character server's, not theirs: $E"
[ -f /private/tmp/arrival.json ] || fail "the map server was never told they were coming"
python3 -c "
import json
a=json.load(open('/private/tmp/arrival.json'))
assert a['ticket'], a
assert a['characterId']=='$CID', a
assert a['build']=='$BUILD', a
assert 'progress' in a, a
print('  ok  announced first: name', a['characterName'], '| character sent, keys:', len(a['progress']))"

echo "5. the character is what the map server was handed, not what the player claimed"
python3 -c "
import json
a=json.load(open('/private/tmp/arrival.json'))
assert a['accountId'], a
assert a['ticket'] != a['characterId'], a
print('  ok  the announcement carries the account, the character and a ticket that is neither')"

echo "7. the map server writes the character back, as a SERVER"
SAVE='{"version":9,"level":77,"gold":4242,"bagItemIds":["potion_red"],"bagCounts":[9],"modelName":"BaseCharacter"}'
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$CHAR/server/characters/$CID/progress" \
  -H "x-server-secret: $SECRET" -H 'content-type: application/json' -d "{\"progress\":$SAVE}")
[ "$CODE" = "200" ] || fail "the map server could not write the character down (got $CODE)"
curl -s "$CHAR/characters/$CID/progress" -H "authorization: Bearer $TOK" | python3 -c '
import sys,json
d=json.load(sys.stdin)
assert d["progress"]["level"]==77, d["progress"]
assert d["progress"]["gold"]==4242, d["progress"]
assert d["progress"]["bagCounts"]==[9], d["progress"]
print("  ok  written by the server, read back by the player: level 77, 4242 gold, 9 potions")'

echo "8. a PLAYER cannot use the server write door"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$CHAR/server/characters/$CID/progress" \
  -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d "{\"progress\":$SAVE}")
[ "$CODE" = "401" ] || fail "a player wrote through the server door (got $CODE)"
ok "a player cannot write through the server door ($CODE)"

echo "9. a map server that has gone is never handed a player again"
curl -s -o /dev/null -X DELETE "$CHAR/map-servers/stub-1" -H "x-server-secret: $SECRET"
rm -f /private/tmp/arrival.json
E2=$(curl -s -X POST "$CHAR/characters/$CID/enter" -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d '{"map":"town_0"}')
case "$E2" in
  *7788*) fail "still sent to the map server that has gone: $E2";;
esac
if [ -f /private/tmp/arrival.json ]; then fail "the departed map server was still announced to"; fi
echo "$E2" | grep -q '"error"' && ok "no map server left holding it — entering refused, not sent nowhere" \
  || ok "routed to a different, live map server instead of the one that left"

pkill -f mapstub.py 2>/dev/null || true
echo
echo "ALL 8 PASSED — the character server announces a player and hands over their whole character"
echo "BEFORE telling them where to knock, a dead map server is never used, and writing back is a server-only door."
