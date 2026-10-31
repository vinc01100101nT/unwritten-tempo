#!/usr/bin/env python3
"""Run the project's checkers the moment a turn tries to END, and refuse to end it while
something is broken.

WHY THIS EXISTS: the checkers only helped when someone REMEMBERED to run them, so the one
time they were forgotten is exactly the time the reply said "done" over code that doesn't
compile or data that silently does nothing. A rule you have to remember is a rule you can
drop; this can't be dropped. It is the same shape as the read-before-you-edit gate that
already guards edits, moved to the other end of the turn.

Two checkers, each run ONLY when its own files actually changed since the last clean pass:
  · the game's C#            -> tools/check-unity-code.sh   (never touches the Unity editor)
  · the saved skill data     -> tools/check-skill-data.py

ONLY MY OWN CHANGES. This gate exists to stop ME shipping a broken edit, and nothing else.
A repo can be mid-edit for a dozen reasons that have nothing to do with this turn — another
session, the user's own unfinished work, an editor writing files underneath us — and a fault
in code I never opened is not mine to fix, not mine to guess at, and not a reason to hold the
turn hostage. So a group is skipped outright unless THIS session edited one of its files, and
a fault only speaks if it names a file THIS session edited. Everything else is somebody else's
work in progress and is passed over in silence.

Cheap by design: unchanged files mean it does nothing at all and adds no waiting. It never
blocks twice in a row for the same turn (the payload says when it has already fired), and
it never blocks when a checker could not run at all — a missing tool is not a red build.
"""
import hashlib
import io
import json
import os
import re
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
TOOLS = os.path.join(REPO, "tools")
SCRIPTS = os.path.join(REPO, "unity", "Assets", "Scripts")
SKILLS = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "Skills")
AUTHORED = os.path.join(REPO, "unity", "Assets", "1_Authored")
GENERATED = os.path.join(REPO, "unity", "Assets", "2_Generated")
DATA = os.path.join(REPO, "unity", "Assets", "3_Data")
SCENES = os.path.join(REPO, "unity", "Assets", "Scenes")
EDITOR = os.path.join(SCRIPTS, "Editor")
PROJECT_SETTINGS = os.path.join(REPO, "unity", "ProjectSettings")
PIPELINE_SETTINGS = os.path.join(REPO, "unity", "Assets", "Settings")
MANIFEST = os.path.join(REPO, "unity", "Packages", "manifest.json")
ACCOUNTS = os.path.join(REPO, "accounts")
CHARSERVER = os.path.join(REPO, "charserver")
LEARNINGS = os.path.join(REPO, "docs", "5-ai-learnings", "README.md")
sys.path.insert(0, os.path.join(TOOLS, "hook-analytics"))
import guard_check   # the one implementation of "is every guard alive"
import tool_writes   # the one answer to "which files does this call write?"
import repeat_watch  # the one answer to "has this session already tried this once?"

ANSI = re.compile(r"\033\[[0-9;]*m")

STATE = os.path.join(tempfile.gettempdir(),
                     "check-before-done-%s.json" % hashlib.sha1(REPO.encode()).hexdigest()[:12])

GROUPS = {
    "code": {
        "roots": [(SCRIPTS, ".cs")],
        "run": ["bash", os.path.join(TOOLS, "check-unity-code.sh")],
        "headline": "The game's C# does not compile. Fix these before saying anything is done:",
    },
    # Reading PERFORMANCE.md is guarded before an edit; this is the other half — that what was
    # written actually follows it. Only faults in files THIS session touched ever speak.
    "perf": {
        "roots": [(SCRIPTS, ".cs")],
        "run": [sys.executable, os.path.join(TOOLS, "check-perf-hotpaths.py")],
        "headline": "A per-frame path breaks a rule in PERFORMANCE.md. Fix it, or mark the line "
                    "`// perf-ok: <why>` if it genuinely only runs once:",
    },
    "skills": {
        "roots": [(SKILLS, ".asset")],
        "run": [sys.executable, os.path.join(TOOLS, "check-skill-data.py")],
        "headline": "A saved skill is wired in a way that silently does nothing in the running game:",
    },
    # The checker that guards the data is itself guarded: edit it and we re-prove it still
    # SEES faults. Without this, a checker could quietly go blind and its silence would read
    # exactly like good news.
    "skill-checker": {
        "files": [os.path.join(TOOLS, "check-skill-data.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-skill-data-check.py")],
        "headline": "The skill-data checker no longer catches every fault it is supposed to, "
                    "so a clean run from it proves nothing until this passes:",
        "mine_by_construction": True,
    },
    # The guard check is guarded the same way: it only runs here because I just edited it,
    # and a proof that names no file of mine would otherwise be filtered into silence.
    # The game's arithmetic, pinned. A wrong number is silent — a hit that does 47 instead of 52 looks
    # exactly like a hit — so nothing but a check like this ever finds one before a player does.
    "math": {
        "roots": [(SCRIPTS, ".cs"), (os.path.join(TOOLS, "game-math-tests"), ".cs")],
        "run": ["sh", os.path.join(TOOLS, "check-game-math.sh")],
        "headline": "A number the game works out is no longer what it was:",
    },
    # And the check is guarded like the rest: edit it and we re-prove it still catches a moved number AND
    # still names the file, because a failure that names nothing is filtered into silence.
    "math-checker": {
        "files": [os.path.join(TOOLS, "check-game-math.sh")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-game-math-check.py")],
        "headline": "The game-math check can no longer see a moved number, so its silence proves "
                    "nothing until this passes:",
        "mine_by_construction": True,
    },
    # Does everything the game loads still point at something that exists? A stale reference is
    # silent — the engine drops it, the code falls back, and the game runs wearing the wrong art.
    "wiring": {
        "roots": [(DATA, ".asset"), (AUTHORED, ".mat"), (AUTHORED, ".prefab"),
                  (GENERATED, ".prefab"), (SCENES, ".unity")],
        "run": [sys.executable, os.path.join(TOOLS, "check-asset-wiring.py")],
        "headline": "Something the game loads points at a file that is not there:",
    },
    # And the sweep is guarded like every other checker: edit it and we re-prove it still sees a
    # planted break, because a blind sweep prints exactly what a sound project prints.
    "wiring-checker": {
        "files": [os.path.join(TOOLS, "check-asset-wiring.py"),
                  os.path.join(TOOLS, "unity_assets.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-asset-wiring-check.py")],
        "headline": "The asset-wiring sweep can no longer see a broken reference, so its silence "
                    "proves nothing until this passes:",
        "mine_by_construction": True,
    },
    "guard-checker": {
        "files": [os.path.join(TOOLS, "hook-analytics", "guard_check.py")],
        "run": [sys.executable, os.path.join(TOOLS, "hook-analytics", "prove-guard-check.py")],
        "headline": "The guard check can no longer see a broken guard, so its silence proves "
                    "nothing until this passes:",
        "mine_by_construction": True,
    },
    # ---- added 2026-09-20, after an audit found half its findings broke rules that were written and never
    # checked. Each pair below is a checker and the prover that keeps it honest; the rulebooks are
    # unity/Assets/ASSETS.md, SERVER.md, and CLAUDE.md's NOEDITORWRITES / NOCLIENTMATH.
    "assets": {
        "roots": [(AUTHORED, ".meta"), (AUTHORED, ".prefab"), (GENERATED, ".prefab"),
                  (PROJECT_SETTINGS, ".asset"), (PIPELINE_SETTINGS, ".asset")],
        "files": [MANIFEST, os.path.join(TOOLS, "packages-in-use.txt")],
        "run": [sys.executable, os.path.join(TOOLS, "check-asset-rules.py")],
        "headline": "A saved asset breaks a rule in unity/Assets/ASSETS.md — an import setting, a prefab, a "
                    "project setting or a package that will cost memory, draws or a silently dropped field:",
    },
    "assets-checker": {
        "files": [os.path.join(TOOLS, "check-asset-rules.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-asset-rules-check.py")],
        "headline": "The asset-rules checker no longer sees every planted fault, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    # What each biome looks like, and whether the biome test maps hold their props. A half-right row bakes a map
    # that looks almost fine, so nothing but this finds it before somebody walks the map.
    "biomes": {
        "files": [os.path.join(DATA, "Recipes", "BiomeTable.asset"), os.path.join(SCRIPTS, "MapRecipe.cs"),
                  os.path.join(DATA, "Resources", "BiomeWeather.asset")],
        "run": [sys.executable, os.path.join(TOOLS, "check-biome-table.py")],
        "headline": "A biome's look is incomplete, or a biome test map has props stuck at its centre:",
    },
    "biomes-checker": {
        "files": [os.path.join(TOOLS, "check-biome-table.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-biome-table-check.py")],
        "headline": "The biome checker no longer sees every planted fault, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    # The world layout and its map are written together by one tool, so editing the tool re-runs it: the file every
    # world build reads and the picture of it (data/world-layout.html) never fall behind the places it decides. A
    # layout that breaks its own rules (a river climbing, a town in the sea, a pit nobody can leave) fails here.
    "world-layout": {
        "files": [os.path.join(TOOLS, "world-layout", "build_world_layout.py")],
        "run": [sys.executable, os.path.join(TOOLS, "world-layout", "build_world_layout.py")],
        "headline": "The world layout broke one of its own rules, so its file and map were not rewritten:",
        "mine_by_construction": True,
    },
    # Each town's plan and its picture (data/towns/<town>.json and .html) are written together by one tool, the same way.
    "town-plans": {
        "files": [os.path.join(TOOLS, "town-plan", "build_town_plan.py")],
        "run": [sys.executable, os.path.join(TOOLS, "town-plan", "build_town_plan.py")],
        "headline": "A town plan broke one of its own rules (a piece in the wall, on a street, or over another), "
                    "so its file and picture were not rewritten:",
        "mine_by_construction": True,
    },
    # The world's map id is SeamlessWorldBuilder.MapId; its scene, Build Settings, its chunks' addresses, the starting
    # map, the music table, every door, the world map's atlas and the places are copies or users of map ids, and one
    # that drifts sends a player (or /tp) to a map that isn't there.
    "map-ids": {
        "roots": [(os.path.join(REPO, "unity", "Assets", "Scenes"), ".unity"),
                  (os.path.join(REPO, "tools", "map-painter", "exports"), ".json")],
        "files": [os.path.join(SCRIPTS, "Editor", "SeamlessWorldBuilder.cs"), os.path.join(SCRIPTS, "SavePoint.cs"),
                  os.path.join(DATA, "Resources", "WorldAtlas.asset"), os.path.join(DATA, "Resources", "MapBgmTable.asset"),
                  os.path.join(DATA, "Resources", "WorldPlaces.json"),
                  os.path.join(REPO, "unity", "Assets", "AddressableAssetsData", "AssetGroups", "World.asset"),
                  os.path.join(REPO, "unity", "ProjectSettings", "EditorBuildSettings.asset"),
                  os.path.join(SCRIPTS, "GameRoot.cs"), os.path.join(SCRIPTS, "Editor", "SystemsSceneBuilder.cs")],
        "run": [sys.executable, os.path.join(TOOLS, "check-map-ids.py")],
        "headline": "A map id names a map that does not exist (the world's id is SeamlessWorldBuilder.MapId):",
    },
    "map-ids-checker": {
        "files": [os.path.join(TOOLS, "check-map-ids.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-map-ids-check.py")],
        "headline": "The map id checker no longer sees every planted fault, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    # Every shadow's body and outfit colours. A body the roster lacks is refused by the server, and a colour on a part
    # the body does not have is silently drawn as nothing — neither shows until somebody meets the creature.
    "shadow-looks": {
        "roots": [(os.path.join(DATA, "Resources", "Monsters"), ".asset")],
        "files": [os.path.join(DATA, "Resources", "CharacterDyeParts.asset"),
                  os.path.join(DATA, "Resources", "HeadPieces.asset"),
                  os.path.join(REPO, "unity", "Assets", "AddressableAssetsData", "AssetGroups", "Characters.asset")],
        "run": [sys.executable, os.path.join(TOOLS, "check-shadow-looks.py")],
        "headline": "A shadow's look names a body the game cannot load, or a colour on a part that body does not have:",
    },
    "shadow-looks-checker": {
        "files": [os.path.join(TOOLS, "check-shadow-looks.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-shadow-looks-check.py")],
        "headline": "The shadow-look checker no longer sees every planted fault, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    # What everybody wears on their head. A hairstyle or a hat named by a text that matches nothing is drawn as no
    # hair or no hat, and a body address left on the model file draws the model's own hair under the chosen one.
    "head-pieces": {
        "roots": [(os.path.join(DATA, "Resources", "Items"), ".asset"),
                  (os.path.join(REPO, "unity", "Assets", "2_Generated", "Characters"), ".prefab")],
        "files": [os.path.join(DATA, "Resources", "HeadPieces.asset"),
                  os.path.join(DATA, "Resources", "WorldTownsfolk.asset"),
                  os.path.join(REPO, "unity", "Assets", "AddressableAssetsData", "AssetGroups", "Characters.asset")],
        "run": [sys.executable, os.path.join(TOOLS, "check-head-pieces.py")],
        "headline": "Something worn on the head names a hairstyle, a hat or a body the game cannot draw:",
    },
    "head-pieces-checker": {
        "files": [os.path.join(TOOLS, "check-head-pieces.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-head-pieces-check.py")],
        "headline": "The hair and headgear checker no longer sees every planted fault, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    # The material only one creature drops, and every piece of gear. Their tier and value are worked out by the editor
    # on save (DerivedItemValues), but a file edited as text skips that, so the saved numbers are checked against the rule.
    "creature-materials": {
        "roots": [(os.path.join(DATA, "Resources", "Items"), ".asset"),
                  (os.path.join(DATA, "Resources", "Monsters"), ".asset")],
        "files": [os.path.join(SCRIPTS, "CreatureMaterials.cs"), os.path.join(SCRIPTS, "CreaturePay.cs"),
                  os.path.join(SCRIPTS, "CreatureLoot.cs"), os.path.join(SCRIPTS, "CraftingMath.cs")],
        "run": [sys.executable, os.path.join(TOOLS, "check-creature-materials.py")],
        "headline": "A creature's own material is missing, shared, or sells for a price the rule does not give:",
    },
    "creature-materials-checker": {
        "files": [os.path.join(TOOLS, "check-creature-materials.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-creature-materials-check.py")],
        "headline": "The creature material checker no longer sees every planted fault, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    "editor-writes": {
        "roots": [(SCRIPTS, ".cs")],
        "run": [sys.executable, os.path.join(TOOLS, "check-editor-writes.py")],
        "headline": "Editor code can write a saved file with nobody having clicked (RULE NOEDITORWRITES). Move "
                    "the write behind a menu item, or say why it is the user's own action:",
    },
    "editor-writes-checker": {
        "files": [os.path.join(TOOLS, "check-editor-writes.py"), os.path.join(TOOLS, "cs_methods.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-editor-writes-check.py")],
        "headline": "The editor-writes checker no longer sees an unclicked write, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    # A shader the game looks up by name must be in a BUILT game, or Shader.Find answers null there and only there —
    # the 2026-10-12 afterimage threw inside a network message and disconnected every Thousand Petals dash.
    "runtime-shaders": {
        "roots": [(SCRIPTS, ".cs")],
        "files": [os.path.join(REPO, "unity", "ProjectSettings", "GraphicsSettings.asset")],
        "run": [sys.executable, os.path.join(TOOLS, "check-runtime-shaders.py")],
        "headline": "Game code looks up a shader by name that a built game would not carry (it works in the editor "
                    "and is null in a build). Use a shader on Always Included Shaders:",
    },
    "runtime-shaders-checker": {
        "files": [os.path.join(TOOLS, "check-runtime-shaders.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-runtime-shaders-check.py")],
        "headline": "The runtime-shaders checker no longer sees a missing shader, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    # The other half of NOEDITORWRITES: Unity's own components saved into a scene that rewrite themselves in the
    # editor. One of them stopped every build at "Modified Scenes must be saved" (2026-10-09).
    "scene-stays-clean": {
        "roots": [(SCENES, ".unity"), (AUTHORED, ".prefab"), (GENERATED, ".prefab"), (DATA, ".prefab")],
        "run": [sys.executable, os.path.join(TOOLS, "check-scene-stays-clean.py")],
        "headline": "Something saved into a scene rewrites itself in the editor, which marks the scene modified "
                    "and stops every build:",
    },
    "scene-stays-clean-checker": {
        "files": [os.path.join(TOOLS, "check-scene-stays-clean.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-scene-stays-clean-check.py")],
        "headline": "The scene-stays-clean checker no longer sees every planted fault, so its silence proves "
                    "nothing until this passes:",
        "mine_by_construction": True,
    },
    "client-math": {
        "roots": [(SCRIPTS, ".cs")],
        "files": [os.path.join(TOOLS, "client-math-baseline.txt")],
        "run": [sys.executable, os.path.join(TOOLS, "check-client-math.py")],
        "headline": "A NEW sum on a game number where a player's machine can run it (RULE NOCLIENTMATH). Send "
                    "the finished value from the server, move the sum into a static math class, or say why:",
    },
    "client-math-checker": {
        "files": [os.path.join(TOOLS, "check-client-math.py"), os.path.join(TOOLS, "cs_methods.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-client-math-check.py")],
        "headline": "The client-math checker no longer sees a new client-side sum, so its silence proves "
                    "nothing until this passes:",
        "mine_by_construction": True,
    },
    "server": {
        "roots": [(os.path.join(ACCOUNTS, "src"), ".ts"), (os.path.join(ACCOUNTS, "sql"), ".sql"),
                  (CHARSERVER, ".cs"), (CHARSERVER, ".sh")],
        "run": [sys.executable, os.path.join(TOOLS, "check-server-rules.py")],
        "headline": "One of the two services breaks a rule in SERVER.md — a defaulted secret, an unbounded pool, "
                    "a character route on the sign-in service, or an ordered read with no index:",
    },
    "server-checker": {
        "files": [os.path.join(TOOLS, "check-server-rules.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-server-rules-check.py")],
        "headline": "The server-rules checker no longer sees every planted fault, so its silence proves nothing "
                    "until this passes:",
        "mine_by_construction": True,
    },
    "gate-checker": {
        "files": [os.path.join(TOOLS, "hook-analytics", "perf_gate.py")],
        "run": [sys.executable, os.path.join(TOOLS, "hook-analytics", "prove-read-first-gate.py")],
        "headline": "The read-first gate no longer refuses an unread rulebook, or refuses what it should not, "
                    "until this passes:",
        "mine_by_construction": True,
    },
    "skill-router-checker": {
        "files": [os.path.join(TOOLS, "hook-analytics", "skill_router.py")],
        "run": [sys.executable, os.path.join(TOOLS, "hook-analytics", "prove_skill_router.py")],
        "headline": "The skill router no longer sends a message it exists for to its skill, or sends one it "
                    "should not, until this passes:",
        "mine_by_construction": True,
    },
}


def watched(group):
    """Every file this group cares about — whole trees by extension, plus named files."""
    out = list(group.get("files", []))
    for root, ext in group.get("roots", []):
        for dirpath, _dirs, files in os.walk(root):
            out.extend(os.path.join(dirpath, f) for f in files if f.endswith(ext))
    return out


def fingerprint(paths):
    """What the watched files look like right now — path, size and modification time. Cheap
    (no reading of contents) and enough to notice any edit, by us or by the editor."""
    marks = []
    for p in paths:
        try:
            st = os.stat(p)
        except OSError:
            continue
        marks.append("%s|%d|%d" % (p, st.st_size, st.st_mtime_ns))
    marks.sort()
    return hashlib.sha1("\n".join(marks).encode()).hexdigest()


def session_edits(payload):
    """Every file THIS session actually wrote, read back out of the turn transcript. The
    transcript is the only honest record of it — a file's modification time says a file moved,
    never who moved it, and this gate turns on exactly that difference.

    A shell command counts as an edit, and is read for what it writes by the same shared answer
    the read-first gate uses. Counting only the dedicated editing tools is what let a whole
    session of shell-written C# arrive here looking like a session that wrote nothing at all,
    so neither checker ran."""
    path = payload.get("transcript_path")
    if not path or not os.path.isfile(path):
        return None                     # can't tell whose work it is → caller stays silent
    touched = set()

    def walk(node):
        if isinstance(node, dict):
            if node.get("type") == "tool_use":
                for f in tool_writes.files_written(node.get("name") or "", node.get("input")):
                    touched.add(os.path.realpath(f))
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    try:
        with open(path, "r", encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    walk(json.loads(line))
                except ValueError:
                    continue
    except OSError:
        return None
    return touched


def mine_only(body, touched):
    """Keep only the fault lines that name a file this session edited. A checker compiles the
    whole project, so its output mixes my faults in with everyone's; this is what separates
    them. Lines naming no file at all (headings, counts) ride along with the ones kept."""
    kept, saw_fault = [], False
    for line in body.splitlines():
        # Every kind of file a checker can blame, not just the three the first checkers
        # happened to report on. A kind missing from this list is never recognised as a
        # file at all, so its fault line is kept unconditionally and every pre-existing
        # one in the project rides along on every turn — the opposite of this filter's job.
        hit = re.search(r"([\w./\\-]+\.(?:cs|asset|py|mat|prefab|unity|sh|md|json|shader|meta|ts|sql|txt))\s*[(:]", line)
        if not hit:
            kept.append(line)
            continue
        candidate = hit.group(1)
        real = os.path.realpath(candidate if os.path.isabs(candidate)
                                else os.path.join(REPO, candidate))
        if real in touched:
            kept.append(line)
            saw_fault = True
    return "\n".join(kept).strip() if saw_fault else ""


# Words too common in commands and checker names to say what went wrong.
_ABOUT_NOISE = {"tools", "check", "checker", "with", "this", "that", "from", "into", "these", "before", "does",
                "fix", "turn", "finish", "your", "game", "unwritten", "app"}
LEARNINGS_SHOWN = 5


def learnings_index(about=""):
    """The few AI-learnings symptom lines that share words with what just went wrong, plus where the whole
    index lives — read fresh so this can never drift from the folder it points at. A fix that did not take is
    exactly the moment those pages are for, and a blocked turn is the one place in a session where that moment
    is visible to a machine instead of only to memory.

    Only the matching lines, never all of them: the whole index is over a hundred lines, about five thousand
    tokens pasted into every blocked turn, and a session that needs more opens the README itself."""
    try:
        with io.open(LEARNINGS, encoding="utf-8") as f:
            text = f.read()
    except OSError:
        return ""
    words = {w for w in re.findall(r"[a-z]{4,}", about.lower()) if w not in _ABOUT_NOISE}
    scored = []
    for line in text.splitlines():
        line = line.strip()
        if not line.startswith("|") or line.startswith("| ---"):
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        if len(cells) != 2 or cells[0].lower().startswith("you are seeing"):
            continue
        link = re.match(r"\[([^\]]+)\]\(([^)]+)\)", cells[1])
        if not link:
            continue
        symptom = cells[0].lower()
        score = sum(1 for w in words if w in symptom)
        if score:
            scored.append((-score, len(scored), "  \u00b7 %s\n      -> docs/5-ai-learnings/%s" % (cells[0], link.group(2))))
    rows = [r for _, _, r in sorted(scored)[:LEARNINGS_SHOWN]]
    pointer = ("the whole index, one symptom line per page, is docs/5-ai-learnings/README.md \u2014 open it if "
               "nothing here fits")
    if not rows:
        return "\n\nThis is a second attempt now. Before making one, check whether a lesson already covers it: " + pointer + "."
    return ("\n\nThis is a second attempt now. Before making one, check whether one of these "
            "already covers what went wrong \u2014 open the page, do not work from the line:\n"
            + "\n".join(rows) + "\n  (" + pointer + ")")


def load_state():
    try:
        with open(STATE, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save_state(state):
    try:
        with open(STATE, "w", encoding="utf-8") as f:
            json.dump(state, f)
    except OSError:
        pass                       # the gate must still work with nowhere to remember


def main():
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        payload = {}

    guard_check.beat(os.path.basename(__file__), payload.get("session_id", ""))

    # Already blocked once for this turn — say nothing, or the turn can never finish.
    if payload.get("stop_hook_active"):
        return 0

    state = load_state()

    # Is every guard this project configured actually able to run? Asked first, because a
    # broken guard is broken whether or not this turn touched anything it watches.
    dead = guard_check.problems()
    if dead:
        mark = hashlib.sha1("\n".join(dead).encode()).hexdigest()
        if state.get("hooks:told") != mark:
            state["hooks:told"] = mark
            save_state(state)
            reason = ("A guard configured for this project cannot run, so it has been passing "
                      "everything silently:\n  \u00b7 " + "\n  \u00b7 ".join(dead) +
                      "\n\nRepair it or tell the user plainly that it is off. A gate nobody can "
                      "see fail is worse than no gate at all.")
            print(json.dumps({"decision": "block", "reason": reason}))
            sys.stderr.write(reason + "\n")
            return 2
    elif state.pop("hooks:told", None) is not None:
        save_state(state)

    # A SECOND ATTEMPT IS A FINDING TOO, even when nothing is broken and nothing was written.
    #
    # ASKED HERE, ABOVE THE "wrote nothing" EXIT, ON PURPOSE. Everything below this line only
    # speaks when the turn EDITED FILES, and the day that earned this rule edited almost none: it
    # was builds that came out unusable and creatures that did nothing, over and over, with the
    # C# compiling clean every single time. Nothing was ever written, so nothing ever blocked, so
    # the learnings pages — written for exactly that moment — were never once offered. Put below
    # that exit, this check would have been just as silent as the one it replaces.
    #
    # Running the same heavy command twice IS the second attempt the rule names, and it needs no
    # opinion about what "failed" means for a given tool. Said ONCE per command, so it lands at
    # the moment it helps and never becomes wallpaper.
    session_id = payload.get("session_id", "")
    fresh = {k: n for k, n in repeat_watch.repeats(session_id).items()
             if not repeat_watch.told(session_id, k)}
    if fresh:
        repeat_watch.mark_told(session_id, list(fresh))
        again = ", ".join("%s (%d×)" % (k, n) for k, n in sorted(fresh.items()))
        reason = ("You have now run the same thing more than once this session: " + again +
                  "\n\nThat is a SECOND ATTEMPT, which is the exact moment this project's rule "
                  "says to stop and read — before trying a third time. A repeat means the "
                  "last attempt did not settle it, and the pages below were paid for by rounds "
                  "already lost to this." +
                  learnings_index(" ".join(fresh)) +
                  "\n\nIf none of them fit, say so in one line and carry on — but do not "
                  "simply try again on a new theory. Get a measurement first.")
        print(json.dumps({"decision": "block", "reason": reason}))
        sys.stderr.write(reason + "\n")
        return 2

    touched = session_edits(payload)
    if not touched:
        return 0                       # this session wrote nothing, or we can't tell — say nothing

    # Files were written, so every before-a-tool-runs guard must have been consulted by now. One
    # that has not reported in all session is switched off in practice, whatever the settings say.
    absent = guard_check.never_ran_this_session(payload.get("session_id") or "")
    if absent:
        mark = hashlib.sha1("\n".join(absent).encode()).hexdigest()
        if state.get("absent:told") != mark:
            state["absent:told"] = mark
            save_state(state)
            reason = ("A guard was configured and silent for this whole session:\n  \u00b7 "
                      + "\n  \u00b7 ".join(absent) +
                      "\n\nSay so plainly to the user before anything else, and fix what it is "
                      "attached to. Configuration is a wish; the heartbeat is the evidence.")
            print(json.dumps({"decision": "block", "reason": reason}))
            sys.stderr.write(reason + "\n")
            return 2
    elif state.pop("absent:told", None) is not None:
        save_state(state)

    findings, checked, unavailable, already = [], [], [], []

    for name, group in GROUPS.items():
        if not all(os.path.isdir(r) for r, _ in group.get("roots", [])):
            continue
        if not all(os.path.isfile(f) for f in group.get("files", [])):
            continue
        watch = watched(group)
        if not touched.intersection(os.path.realpath(p) for p in watch):
            continue                   # nothing of MINE in here — whatever else is going on isn't my business
        now = fingerprint(watch)
        if state.get(name) == now:
            continue                                    # nothing changed since it last passed
        try:
            done = subprocess.run(group["run"], cwd=REPO, capture_output=True, text=True, timeout=180)
        except (OSError, subprocess.TimeoutExpired) as e:
            unavailable.append("%s (%s)" % (name, e.__class__.__name__))
            continue
        if done.returncode == 0:
            state[name] = now                           # clean — don't check it again until it moves
            state.pop(name + ":told", None)
            checked.append(name)
        elif done.returncode == 1:
            body = ANSI.sub("", (done.stdout or "") + (done.stderr or "")).strip()
            # A group that only runs BECAUSE I edited its own file is mine by construction —
            # filtering it by filename would drop a failure that names no file and report the
            # silence as good news.
            if not group.get("mine_by_construction"):
                body = mine_only(body, touched)
            if not body:
                continue               # it is broken, but not by me — not this gate's business
            # Say a given fault ONCE. Having been told, the turn is free to end — the same rule
            # the in-game gate uses when it reports an unauthored pile: report per fault, not
            # per attempt. Anything NEW, or the same fault after the files moved, speaks again.
            mark = hashlib.sha1(("%s|%s" % (now, body)).encode()).hexdigest()
            if state.get(name + ":told") == mark:
                already.append(name)
            else:
                state[name + ":told"] = mark
                findings.append(group["headline"] + "\n" + body)
        else:
            unavailable.append("%s: %s" % (name, ((done.stderr or done.stdout).strip().splitlines() or [""])[0]))

    save_state(state)

    if findings:
        reason = ("\n\n".join(findings) +
                  "\n\nFix these, then finish the turn. If any of it was already broken before "
                  "this turn's work, say so plainly instead of quietly repairing it." +
                  learnings_index(" ".join(f.split("\n", 1)[0] for f in findings)))
        print(json.dumps({"decision": "block", "reason": reason}))
        sys.stderr.write(reason + "\n")
        return 2

    notes = []
    if unavailable:
        notes.append("could not run: " + "; ".join(unavailable))
    if already:
        notes.append("still unfixed (already reported): " + ", ".join(already))
    if checked:
        notes.append("checked and clean: " + ", ".join(checked))
    if notes:
        print(json.dumps({"suppressOutput": True, "systemMessage": " · ".join(notes)}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
