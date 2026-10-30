"""TOWN EDITOR — the page that edits a town's plan by hand, and the small local server behind it.

    python tools/town-editor/serve.py            # then open http://127.0.0.1:8765

The page edits data/towns/<town>.json, the plan a town piece is built from: its streets, canal, areas, bridges, mounds,
gates, every house, tree and prop, and its ground raised or lowered by hand. Save writes the plan (the one before it is
kept in data/towns/backup/). Rebuild asks the open Unity editor to build the town piece from the plan and then the world
around it, the same calls the world-authoring skill makes, then photographs the town's pieces again; it refuses while
Unity is playing or compiling. Take pictures asks Unity only for the photographs (TownPictures: every piece, plant and the
bridge seen from above, into pictures/<town>/); the ground's tiles are drawn from their own pictures in GroundTiles.
Opening a town whose pictures are missing, or were taken of other pieces than its plan places now, takes them. Only
towns with a plan file are offered.
"""
import http.server, json, os, re, shutil, subprocess, sys, tempfile, time, urllib.parse

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TOWNS = os.path.join(ROOT, "data", "towns")
BACKUP = os.path.join(TOWNS, "backup")
UNITY_PROJECT = os.path.join(ROOT, "unity")
PAGE = os.path.join(os.path.dirname(__file__), "index.html")
VIEW3D = os.path.join(os.path.dirname(__file__), "view3d.js")
PICTURES = os.path.join(os.path.dirname(__file__), "pictures")
TILES = os.path.join(UNITY_PROJECT, "Assets", "1_Authored", "Art", "GroundTiles")
ID = re.compile(r"^[a-z0-9_-]+$")
FILE = re.compile(r"^[A-Za-z0-9_.()-]+\.png$")
sys.path.insert(0, os.path.join(ROOT, "tools", "town-plan"))
import build_town_plan   # a new town's blank plan is written by the plan tool, the one writer of plan files


def plan_path(town):
    if not ID.match(town or ""): raise ValueError("bad town id")
    path = os.path.join(TOWNS, town + ".json")
    if not os.path.exists(path): raise ValueError("no plan for " + town)
    return path


def startable(t):
    """A town may start blank only with no plan and no built piece: a hand-made piece (Lastline's) would be replaced by
    the blank plan's at the next rebuild."""
    piece = os.path.join(UNITY_PROJECT, "Assets", "1_Authored", "Prefabs", "Towns", t["name"] + ".prefab")
    return not os.path.exists(os.path.join(TOWNS, t["id"] + ".json")) and not os.path.exists(piece)


def variants(plan):
    """How many numbered models each kind has on disk ({n} in its prefab path)."""
    out = {}
    for k in plan.get("kit", []):
        n = 0
        while "{n}" in k["prefab"] and os.path.exists(os.path.join(UNITY_PROJECT, k["prefab"].replace("{n}", str(n + 1)))):
            n += 1
        out[k["kind"]] = max(1, n)
    return out


def write_plan(path, plan):
    """The plan as the town tools write it, with the hand-raised heights on one line instead of one line per number."""
    heights = None
    if isinstance(plan.get("lift"), dict) and isinstance(plan["lift"].get("h"), list):
        heights = plan["lift"]["h"]
        plan["lift"]["h"] = "@@HEIGHTS@@"
    text = json.dumps(plan, indent=1)
    if heights is not None:
        text = text.replace('"@@HEIGHTS@@"', json.dumps([round(v, 3) for v in heights], separators=(",", ":")))
    os.makedirs(BACKUP, exist_ok=True)
    shutil.copyfile(path, os.path.join(BACKUP, os.path.basename(path)))
    tmp = path + ".saving"
    with open(tmp, "w", encoding="utf-8") as f: f.write(text)
    os.replace(tmp, path)


def unity(*args, timeout=60):
    exe = shutil.which("unity")
    if not exe: raise RuntimeError("the Unity command line (unity) is not on this machine's PATH")
    r = subprocess.run([exe, "cmd", "--project-path", UNITY_PROJECT, *args], capture_output=True, text=True, timeout=timeout)
    return r.stdout + r.stderr


PICTURES_CODE = 'string p; try { p = TownPictures.Take("%s"); } catch (System.Exception e) { p = "FAIL " + e; }\n'


def rebuild(town):
    source = picture_source(town)
    code = ('string t; try { t = TownPieceBuilder.Build("%s"); } catch (System.Exception e) { t = "FAIL " + e; }\n'
            'string w = "not built, the town failed"; if (!t.StartsWith("FAIL")) { try { w = SeamlessWorldBuilder.BuildCore(); } catch (System.Exception e) { w = "FAIL " + e; } }\n'
            + PICTURES_CODE +
            'UnityEngine.Debug.Log("TOWN EDITOR REBUILD %s\\n" + t + "\\n" + w + "\\n" + p);\n'
            'return "TOWN\\n" + t + "\\nWORLD\\n" + w + "\\nPICTURES\\n" + p;\n') % (town, town, town)
    report = in_unity(code, "rebuild")
    if "FAIL" not in report.split("PICTURES", 1)[-1]: write_source(town, source)
    return report


def take_pictures(town):
    source = picture_source(town)
    report = in_unity(PICTURES_CODE % town + 'return p;\n', "take pictures")
    if "FAIL" not in report: write_source(town, source)
    return report


PICTURES_MADE = 2   # raised whenever TownPictures writes something new into the index, so every town's are taken again


def picture_source(town):
    """What a town's pictures are taken of: every kind its plan can place (its model, size and role) and every plant."""
    with open(plan_path(town), encoding="utf-8") as f: plan = json.load(f)
    return {"made": PICTURES_MADE, "kit": [[k.get("kind"), k.get("prefab"), k.get("scale", 0), k.get("role")] for k in plan.get("kit", [])],
            "plants": [[p.get("kind"), p.get("prefab")] for p in plan.get("plants", [])]}


def write_source(town, source):
    with open(os.path.join(PICTURES, town, "source.json"), "w", encoding="utf-8") as f: json.dump(source, f)


def pictures_current(town):
    """True when the town's pictures were taken of exactly what its plan can place now."""
    try:
        with open(os.path.join(PICTURES, town, "source.json"), encoding="utf-8") as f: return json.load(f) == picture_source(town)
    except (OSError, ValueError):
        return False


def send_file(handler, folder, name):
    if not FILE.match(name or ""): raise ValueError("bad file name")
    with open(os.path.join(folder, name), "rb") as f: handler.send(200, f.read(), "image/png")


def in_unity(code, what):
    """Runs C# in the open Unity editor, refusing while it plays or compiles; returns what it printed and how long it took."""
    status = unity("editor_status", "--json")
    mode = re.search(r'"playMode"\s*:\s*"(\w+)"', status)
    if not mode: raise RuntimeError("the Unity editor is not answering — open the project in Unity first")
    if mode.group(1) != "stopped": raise RuntimeError("Unity is in Play mode — stop Play, then " + what)
    if re.search(r'"compiling"\s*:\s*true', status): raise RuntimeError("Unity is compiling — wait for it to finish, then " + what)
    fd, path = tempfile.mkstemp(suffix=".cs", prefix="town_editor_")
    with os.fdopen(fd, "w", encoding="utf-8") as f: f.write(code)
    try:
        started = time.time()
        out = unity("--timeout", "1800", "--result-only", "eval_file", "--", "--file", path, "--timeout", "1800000", timeout=1900).strip()
        try:   # the command line answers with an envelope around what the code returned
            j = json.loads(out)
            if isinstance(j, dict) and "result" in j:
                out = str(j["result"]) if j.get("success") else "FAIL " + json.dumps(j.get("diagnostics") or j, indent=1)
        except ValueError:
            pass
        return "%s\n(took %d s)" % (out, time.time() - started)
    finally:
        os.remove(path)


class Handler(http.server.BaseHTTPRequestHandler):
    def send(self, code, body, kind="application/json"):
        data = body.encode("utf-8") if isinstance(body, str) else body
        self.send_response(code)
        self.send_header("Content-Type", kind + "; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        u = urllib.parse.urlparse(self.path); q = urllib.parse.parse_qs(u.query)
        try:
            if u.path in ("/", "/index.html"):
                with open(PAGE, "rb") as f: return self.send(200, f.read(), "text/html")
            if u.path == "/view3d.js":
                with open(VIEW3D, "rb") as f: return self.send(200, f.read(), "text/javascript")
            if u.path == "/api/towns":
                return self.send(200, json.dumps(sorted(f[:-5] for f in os.listdir(TOWNS) if f.endswith(".json"))))
            if u.path == "/api/new-towns":
                with open(os.path.join(ROOT, "data", "world-layout.json"), encoding="utf-8") as f: towns = json.load(f)["towns"]
                return self.send(200, json.dumps([{"id": t["id"], "name": t["name"]} for t in towns if startable(t)]))
            if u.path == "/api/plan":
                with open(plan_path(q.get("id", [""])[0]), encoding="utf-8") as f: plan = json.load(f)
                return self.send(200, json.dumps({"plan": plan, "variants": variants(plan)}))
            if u.path == "/api/pictures":
                town = q.get("id", [""])[0]; plan_path(town)
                index = os.path.join(PICTURES, town, "index.json")
                if not os.path.exists(index): return self.send(200, json.dumps({"current": False}))
                with open(index, encoding="utf-8") as f: pictures = json.load(f)
                pictures["current"] = pictures_current(town)
                return self.send(200, json.dumps(pictures))
            if u.path == "/api/tiles":
                return self.send(200, json.dumps(sorted(f[:-4] for f in os.listdir(TILES) if f.endswith(".png"))))
            parts = u.path.strip("/").split("/")
            if len(parts) == 3 and parts[0] == "pictures" and ID.match(parts[1]):
                return send_file(self, os.path.join(PICTURES, parts[1]), parts[2])
            if len(parts) == 2 and parts[0] == "tiles":
                return send_file(self, TILES, parts[1])
            self.send(404, json.dumps({"error": "not found"}))
        except Exception as e:
            self.send(400, json.dumps({"error": str(e)}))

    def do_POST(self):
        u = urllib.parse.urlparse(self.path); q = urllib.parse.parse_qs(u.query)
        town = q.get("id", [""])[0]
        try:
            if u.path == "/api/plan":
                plan = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))).decode("utf-8"))
                if plan.get("id") != town: raise ValueError("the plan is not " + town + "'s")
                write_plan(plan_path(town), plan)
                return self.send(200, json.dumps({"ok": True}))
            if u.path == "/api/new-town":
                if not ID.match(town): raise ValueError("bad town id")
                with open(os.path.join(ROOT, "data", "world-layout.json"), encoding="utf-8") as f: towns = json.load(f)["towns"]
                if not any(t["id"] == town and startable(t) for t in towns): raise ValueError(town + " cannot start blank: it has a plan or a hand-made piece")
                problems = build_town_plan.write(build_town_plan.blank(town), whole_kit=True)
                if problems: raise ValueError("; ".join(problems))
                return self.send(200, json.dumps({"ok": True}))
            if u.path == "/api/rebuild":
                return self.send(200, json.dumps({"report": rebuild(town)}))
            if u.path == "/api/pictures":
                return self.send(200, json.dumps({"report": take_pictures(town)}))
            self.send(404, json.dumps({"error": "not found"}))
        except Exception as e:
            self.send(400, json.dumps({"error": str(e)}))

    def log_message(self, *a): pass


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    print("Town editor: http://127.0.0.1:%d   (Ctrl+C stops it)" % port)
    http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
