#!/usr/bin/env python3
"""Run GM commands on the live game servers from a terminal, with no game client open.

The line goes to the character server, which hands it to the map server holding that map; that server runs it
through the same command list the in-game chat uses and sends back what the command answered. Every player in the
world sees the effect exactly as if a GM had typed it.

USAGE
  gm                                        open a session: pick a map once, then type commands directly
  gm --map <map>                            open a session already on that map
  gm --map <map> "/weather rain"            one command and back — a command that changes the world
  gm --map <map> --as <character> /heal     one command acting on a person — they must be in that map
  gm --map <map> status                     time of day, weather and who is in that map

  <map> is the map id — the game is one seamless map, `world` — or a place in it ("old oak", lastline), which
  means the map that place is in.

Needs CHAR_SERVER_URL and SERVER_SECRET in settings.env — the same secret the servers use with each other, so
whoever can run this can do anything a GM can.
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from settings_ask import setting  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# The world's map id is owned by the code that builds it; its places by the file the layout tool writes.
WORLD_BUILDER = os.path.join(REPO, "unity", "Assets", "Scripts", "Editor", "SeamlessWorldBuilder.cs")
PLACES = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "WorldPlaces.json")

SESSION_HELP = """  maps             the maps open right now under the map server holding each, numbered — only these take commands
  maps all         every map in the world, the open ones marked
  map <which>      choose the map every command goes to: a number from 'maps', the id (world), or a place in it"""


def world_maps():
    """Every map in the game as (id, name, kind): the seamless world, its id read from SeamlessWorldBuilder.MapId."""
    try:
        with open(WORLD_BUILDER, encoding="utf-8") as f:
            ids = re.findall(r'public\s+const\s+string\s+MapId\s*=\s*"([^"]+)"\s*;', f.read())
    except OSError:
        ids = []
    return [(ids[0], "the world", "seamless")] if len(ids) == 1 else []


def world_places():
    """Every named place in the world as (id, name) — a place resolves to the map it is in."""
    try:
        with open(PLACES, encoding="utf-8") as f:
            return [(p["id"], p.get("name") or "") for p in json.load(f).get("places", []) if p.get("id")]
    except (OSError, ValueError, AttributeError):
        return []


def map_servers():
    """Every map server running and the maps each is holding, from the character server's public status read — the
    same one the website's Server Status page shows. None when it did not answer."""
    url = setting("CHAR_SERVER_URL").rstrip("/")
    try:
        with urllib.request.urlopen(url + "/stats", timeout=5) as reply:
            stats = json.loads(reply.read().decode("utf-8"))
    except (urllib.error.URLError, OSError, ValueError):
        return None
    return stats.get("servers", [])


def open_maps():
    servers = map_servers()
    return None if servers is None else sorted({m for s in servers for m in s.get("maps", [])})


class MapList:
    """The list 'maps' last printed, so 'map 2' means the second line the user just read."""
    shown = []


def describe(mid, names):
    name, kind = names.get(mid, ("", ""))
    return "%-14s %s%s" % (mid, name, (" (%s)" % kind) if kind else "")


def show_maps(everything):
    names = {mid: (name, kind) for mid, name, kind in world_maps()}
    if not everything:
        show_open_maps(names)
        return
    live = open_maps()
    if live is None:
        print("The character server did not answer, so which maps are open is unknown.", file=sys.stderr)
        live = []
    ids = [mid for mid, _, _ in world_maps()]
    if not ids:
        print("The world's map id could not be read from %s." % os.path.relpath(WORLD_BUILDER, REPO))
        MapList.shown = []
        return
    MapList.shown = ids
    for n, mid in enumerate(ids, 1):
        print("%3d  %s%s" % (n, "open  " if mid in live else "      ", describe(mid, names)))


def show_open_maps(names):
    """The open maps under the map server holding each, numbered straight through so 'map <n>' picks from it."""
    servers = map_servers()
    MapList.shown = []
    if servers is None:
        print("The character server did not answer, so which maps are open is unknown.", file=sys.stderr)
        return
    if not any(s.get("maps") for s in servers):
        print("No map is open right now — a map opens when somebody walks into it.")
        return
    for s in sorted(servers, key=lambda s: s.get("id", "")):
        print("map server %s — %s player(s), %s creature(s), last heard %ss ago" % (
            s.get("id", "?"), s.get("players", 0), s.get("creatures", 0), s.get("lastHeardSecondsAgo", "?")))
        for mid in sorted(s.get("maps", [])):
            MapList.shown.append(mid)
            print("  %3d  %s" % (len(MapList.shown), describe(mid, names)))


def resolve_map(typed):
    """The map id a user means: a number from the last list, an exact id, or the one map whose id or name contains
    every word typed. Returns (id, None), or (None, why) — several matches are listed, never guessed between."""
    typed = typed.strip()
    if typed.isdigit() and MapList.shown:
        n = int(typed)
        return (MapList.shown[n - 1], None) if 1 <= n <= len(MapList.shown) else \
            (None, "There is no map %d in the last list (1-%d)." % (n, len(MapList.shown)))
    maps = world_maps()
    for mid, name, _ in maps:
        if typed.lower() in (mid.lower(), name.lower()):
            return mid, None
    words = typed.lower().split()
    found = [(mid, name) for mid, name, _ in maps if all(w in (mid + " " + name).lower() for w in words)]
    if not found and len(maps) == 1:
        # Every place is inside the one seamless map, so a place's id or name means that map.
        key = lambda t: re.sub(r"[^a-z0-9]", "", t.lower())
        if any(key(typed) in (key(pid), key(pname)) for pid, pname in world_places()):
            return maps[0][0], None
    if len(found) == 1:
        return found[0][0], None
    if not found:
        return None, "No map matches '%s'. Type maps all to see every map." % typed
    return None, "'%s' matches %d maps — say which:\n%s" % (
        typed, len(found), "\n".join("  %-14s %s" % f for f in found))


SESSION_HELP += """
  as <character>   act as that person — they must be in the map;  'as' alone acts as nobody
  status           time of day, weather and who is in the map
  <command>        any GM command, with or without the '/': weather rain, /heal, help
  quit             leave"""


def ask(path, body):
    url = setting("CHAR_SERVER_URL").rstrip("/")
    secret = setting("SERVER_SECRET")
    if not url or not secret:
        return None, "CHAR_SERVER_URL and SERVER_SECRET must both be set in settings.env"
    request = urllib.request.Request(url + path, data=json.dumps(body).encode("utf-8"), method="POST",
                                     headers={"content-type": "application/json", "x-server-secret": secret})
    try:
        # The map server waits up to twelve seconds for a command's last answer line, so this waits a little longer.
        with urllib.request.urlopen(request, timeout=20) as reply:
            return json.loads(reply.read().decode("utf-8")), None
    except urllib.error.HTTPError as e:
        try:
            return None, json.loads(e.read().decode("utf-8")).get("error") or ("HTTP %d" % e.code)
        except ValueError:
            # The new doors always answer a refusal in words, so a bare 404 is a character server built before them.
            if e.code == 404:
                return None, ("The character server has no terminal door — it was started from an older build. "
                              "Restart it, and rebuild and restart the game server (tools/build-game.sh server).")
            return None, "HTTP %d" % e.code
    except (urllib.error.URLError, OSError) as e:
        return None, "the character server at %s did not answer — %s" % (url, getattr(e, "reason", e))


def run(game_map, as_name, line):
    """Send one line (a GM command, or the word status) and print the answer. True when it was answered."""
    line = line.strip()
    if line == "status":
        answer, error = ask("/server/status", {"map": game_map})
    else:
        if not line.startswith("/"):
            line = "/" + line
        answer, error = ask("/server/gm", {"map": game_map, "line": line, "as": as_name or ""})

    if error:
        print(error, file=sys.stderr)
        return False
    if answer.get("unsure"):
        print("No answer came back within 8 seconds — whether the command ran is unknown; read logs/server.log.",
              file=sys.stderr)
        return False
    for text in answer.get("lines", []):
        print(text)
    return True


def session(game_map, as_name):
    print("GM session. Type 'help' for the session words, 'quit' to leave.")
    # The starting map is checked like one typed with 'map': taken only once a map server answers for it.
    if game_map and not run(game_map, None, "status"):
        game_map = None
    if not game_map:
        print("Maps open right now — choose one with map <number>:")
        show_maps(False)
    while True:
        prompt = "gm[%s%s]> " % (game_map or "no map", (" as " + as_name) if as_name else "")
        try:
            line = input(prompt).strip()
        except (EOFError, KeyboardInterrupt):
            print()
            return 0
        if not line:
            continue
        word, _, rest = line.partition(" ")
        rest = rest.strip()
        if word in ("quit", "exit"):
            return 0
        if word == "help" and not rest:
            print(SESSION_HELP)
            if game_map:
                print("GM commands:")
                run(game_map, as_name, "/help")
            continue
        if word == "maps":
            show_maps(rest == "all")
            continue
        if word == "map":
            if not rest:
                print("Which map? A number from 'maps', the id, or part of its name.")
                continue
            chosen, why = resolve_map(rest)
            if why:
                print(why)
                continue
            # Only taken once a map server answers for it, so a typo never becomes the map every command goes to.
            if run(chosen, None, "status"):
                game_map = chosen
            continue
        if word == "as":
            as_name = rest or None
            continue
        if not game_map:
            print("Choose a map first: map <map id>")
            continue
        run(game_map, as_name, line)


def main(argv):
    p = argparse.ArgumentParser(prog="gm", description=__doc__.split("\n\n")[0])
    p.add_argument("--map", help="the map the command is for: its id (world), or a place in it such as \"old oak\"")
    p.add_argument("--as", dest="as_name", help="the character the command acts on; they must be in that map")
    p.add_argument("line", nargs="*", help='a GM command such as "/weather rain", or the word status')
    # Before anything prints: a Windows terminal otherwise shows every dash as a question mark.
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    a = p.parse_args(argv)

    if a.map:
        chosen, why = resolve_map(a.map)
        if why:
            print(why, file=sys.stderr)
            return 2
        a.map = chosen
    if not a.line:
        return session(a.map, a.as_name)
    if not a.map:
        p.error("--map is required with a command — or run gm alone to open a session")
    return 0 if run(a.map, a.as_name, " ".join(a.line)) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
