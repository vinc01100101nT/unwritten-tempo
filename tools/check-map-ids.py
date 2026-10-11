#!/usr/bin/env python3
"""Every map id in the game comes from ONE place — the world layout — and nothing may disagree with it.

THE SOURCE OF TRUTH: tools/world-creator/worlds/world-regions.json. Each map's `id` is its `name` in snake_case
("Old Oak" -> old_oak; a repeated name gets _2, _3 on the later map), exactly as the browser World
Creator sets it — for a town, city or special map. A field's id is <town code>_fildNN and a dungeon floor's
<town code>_dunNN, Ragnarok-style (lst_fild01, olk_dun01); a dungeon with no town near it may instead take the snake_case
of its own name. That id is the scene file name, what /tp takes, and what a character's save names. Everything else
below is a COPY made from the layout by a tool, and this check fails the moment a copy drifts:

  - a town or special map whose id is not the snake_case of its name, a field or dungeon whose id breaks its pattern,
    or two maps with one id
  - a generated map (any type but special) with no scene file of that exact name, or one missing from Build Settings
  - a door in a world scene leading to a map that is not in the layout, or to an arrival point that does not exist
  - the in-game world map (WorldAtlas) listing a different set of maps than the layout
  - a Map Painter file whose file name and saved name differ
  - the starting map, or a map in the music table, naming a scene that does not exist

DOORS. The layout is also the one owner of where every door stands. Each portal carries a permanent whole-number
`id`; a scene names its objects after it — Portal_w{id}_to_{other map} and SpawnPoint_w{id}_from_{other map}.
THE ONE DOOR-POINT RULE (the same in tools/world-creator/index.html and WorldCreator.cs), in metres from the map's
centre, x east and z north:
    door point    = the anchor's `at` when it has one; otherwise the rim point, with half = size/2 - EdgeMargin:
                    N -> (lerp(-hx, hx, t), +hz)   S -> (lerp(-hx, hx, t), -hz)
                    E -> (+hx, lerp(+hz, -hz, t))  W -> (-hx, lerp(+hz, -hz, t))        (t = the anchor's offset)
    arrival point = the anchor's `arriveAt` when it has one; otherwise door point + inward(edge) * SpawnInset
EdgeMargin and SpawnInset are read out of unity/Assets/Scripts/Editor/WorldLayoutFile.cs, their owner — never typed
again here. This check also fails on:
  - a portal with no id, an id that is not a whole number, or two portals sharing one
  - an `at` outside its map's box
  - a scene door or arrival point more than 0.01 m (x, z) from its layout point, one whose id belongs to a different
    pair of maps, one with a parent, one that appears twice, or one the layout has and the scene lacks (a door is
    only expected when the map on its other side has a scene too)
  - the world map's links (WorldAtlas.links: a, b, id, ax, az, bx, bz — each end's door point from its own map's
    centre) differing from the layout in id or in any of the four numbers by more than 0.01

USAGE:  tools/check-map-ids.py [--root <repo>]
EXIT:    0 = every copy agrees with the layout, 1 = something disagrees (each named).
"""
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if "--root" in sys.argv:
    REPO = os.path.abspath(sys.argv[sys.argv.index("--root") + 1])

LAYOUT = os.path.join(REPO, "tools", "world-creator", "worlds", "world-regions.json")
SCENES = os.path.join(REPO, "unity", "Assets", "Scenes")
BUILD = os.path.join(REPO, "unity", "ProjectSettings", "EditorBuildSettings.asset")
ATLAS = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "WorldAtlas.asset")
BGM = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "MapBgmTable.asset")
SIDECARS = os.path.join(REPO, "tools", "map-painter", "exports")
FIRST_MAP_SOURCES = [
    (os.path.join(REPO, "unity", "Assets", "Scenes", "Systems.unity"), r"^\s*firstMap: (.+)$"),
    (os.path.join(REPO, "unity", "Assets", "Scripts", "GameRoot.cs"), r'public string firstMap = "([^"]+)";'),
    (os.path.join(REPO, "unity", "Assets", "Scripts", "Editor", "SystemsSceneBuilder.cs"), r'const string FirstMap = "([^"]+)";'),
]
LAYOUT_FILE_CODE = os.path.join(REPO, "unity", "Assets", "Scripts", "Editor", "WorldLayoutFile.cs")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"
NEAR = 0.01   # metres: how far a copy of a door's spot may sit from the layout's before it counts as drift
INWARD = {"N": (0.0, -1.0), "S": (0.0, 1.0), "E": (-1.0, 0.0), "W": (1.0, 0.0)}
NUM = r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?"


def rim_constants():
    """EdgeMargin and SpawnInset, read from WorldLayoutFile.cs, their owner -> (margin, inset, faults)."""
    text = read(LAYOUT_FILE_CODE) if os.path.exists(LAYOUT_FILE_CODE) else ""
    values, faults = {}, []
    for name in ("EdgeMargin", "SpawnInset"):
        hits = re.findall(r"const\s+float\s+%s\s*=\s*([\d.]+)f?\s*;" % name, text)
        if len(hits) != 1:
            faults.append("doors: unity/Assets/Scripts/Editor/WorldLayoutFile.cs declares `const float %s` %d times (needs exactly 1), so no door's spot can be checked" % (name, len(hits)))
        values[name] = float(hits[0]) if len(hits) == 1 else None
    return values["EdgeMargin"], values["SpawnInset"], faults


def is_spot(v):
    return isinstance(v, list) and len(v) == 2 and all(isinstance(n, (int, float)) and not isinstance(n, bool) for n in v)


def door_point(size, anchor, margin):
    """THE ONE DOOR-POINT RULE (see the top of this file)."""
    if is_spot(anchor.get("at")):
        return (float(anchor["at"][0]), float(anchor["at"][1]))
    hx, hz = size[0] / 2.0 - margin, size[1] / 2.0 - margin
    t = min(1.0, max(0.0, float(anchor.get("offset", 0.5))))
    return {"N": (-hx + 2 * hx * t, hz), "S": (-hx + 2 * hx * t, -hz),
            "E": (hx, hz - 2 * hz * t), "W": (-hx, hz - 2 * hz * t)}[anchor["edge"]]


def arrival_point(size, anchor, margin, inset):
    if is_spot(anchor.get("arriveAt")):
        return (float(anchor["arriveAt"][0]), float(anchor["arriveAt"][1]))
    d, i = door_point(size, anchor, margin), INWARD[anchor["edge"]]
    return (d[0] + i[0] * inset, d[1] + i[1] * inset)


def off(have, want):
    return max(abs(have[0] - want[0]), abs(have[1] - want[1]))


def scene_door_objects(text):
    """Every door and arrival object saved in a scene: name -> [(x, z, has a parent)], one entry per object."""
    parts = re.split(r"^--- !u!(\d+) &(\d+).*$", text, flags=re.M)
    names, places = {}, {}
    for i in range(1, len(parts), 3):
        kind, file_id, body = parts[i], parts[i + 1], parts[i + 2]
        if kind == "1":
            name = re.search(r"^  m_Name: (.*)$", body, re.M)
            if name and re.match(r"(Portal_w\d+_to_|SpawnPoint_w\d+_from_).", name.group(1).strip()):
                names[file_id] = name.group(1).strip()
        elif kind == "4":
            owner = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
            pos = re.search(r"m_LocalPosition: \{x: (%s), y: (%s), z: (%s)\}" % (NUM, NUM, NUM), body)
            father = re.search(r"m_Father: \{fileID: (\d+)\}", body)
            if owner and pos and father:
                places[owner.group(1)] = (float(pos.group(1)), float(pos.group(3)), father.group(1) != "0")
    found = {}
    for file_id, name in names.items():
        if file_id in places:
            found.setdefault(name, []).append(places[file_id])
    return found


def snake_case(s):
    # Mirrors snakeCase() in tools/world-creator/index.html — the two must agree character for character.
    return re.sub(r"^_+|_+$", "", re.sub(r"[^a-z0-9]+", "_", (s or "").lower()))


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def main():
    faults = []
    layout = json.loads(read(LAYOUT))
    maps = layout["maps"]

    # 1. The ids themselves. Fields and dungeon floors follow their Ragnarok-style pattern; everything else is the
    # snake_case of its name, unique, assigned in layout order like the tool does.
    for m in maps:
        if m["type"] == "field" and not re.fullmatch(r"[a-z]{3}_fild\d{2}", m["id"]):
            faults.append("layout: field '%s' has id '%s' — a field's id is <town code>_fildNN" % (m.get("name"), m["id"]))
        if m["type"] == "dungeon" and not (re.fullmatch(r"[a-z]{3}_dun\d{2}", m["id"])
                                           or re.fullmatch(re.escape(snake_case(m.get("name"))) + r"(_\d+)?", m["id"])):
            faults.append("layout: dungeon '%s' has id '%s' — a dungeon floor's id is <town code>_dunNN" % (m.get("name"), m["id"]))
    typed = [m for m in maps if m["type"] in ("field", "dungeon")]
    taken = {m["id"] for m in maps if not snake_case(m.get("name")) or m in typed}
    for m in maps:
        base = snake_case(m.get("name"))
        if not base or m in typed:
            continue
        want, n = base, 2
        while want in taken:
            want, n = "%s_%d" % (base, n), n + 1
        taken.add(want)
        if m["id"] != want:
            faults.append("layout: map named '%s' has id '%s' — its id must be '%s'" % (m.get("name"), m["id"], want))
    ids = [m["id"] for m in maps]
    for i in sorted({i for i in ids if ids.count(i) > 1}):
        faults.append("layout: id '%s' is used by more than one map" % i)
    idset = set(ids)

    # 2. Scene files and Build Settings.
    scene_files = {f[:-6] for f in os.listdir(SCENES) if f.endswith(".unity")}
    built = set(re.findall(r"path: Assets/Scenes/(.+)\.unity", read(BUILD)))
    world_scenes = [m["id"] for m in maps if m["id"] in scene_files]
    for m in maps:
        if m["type"] != "special" and m["id"] not in scene_files:
            faults.append("scene: map '%s' has no scene file '%s.unity'" % (m["id"], m["id"]))
    for s in world_scenes:
        if s not in built:
            faults.append("build settings: '%s' is not in the scene list" % s)

    # 3. Every door in a world scene leads to a map of the layout, at an arrival point that exists there.
    arrivals, doors = {}, {}
    for s in world_scenes:
        text = read(os.path.join(SCENES, s + ".unity"))
        arrivals[s] = set(re.findall(r"^\s*id: (.+)$", text, re.M))
        doors[s] = re.findall(r"targetScene: (.*)\n\s*spawnId: (.*)", text)
    for s in world_scenes:
        for target, spawn in doors[s]:
            if target not in idset:
                faults.append("door: '%s' leads to '%s', which is not a map in the layout" % (s, target))
            elif target not in arrivals:
                faults.append("door: '%s' leads to '%s', which has no scene" % (s, target))
            elif spawn and spawn not in arrivals[target]:
                faults.append("door: '%s' -> '%s' arrives at '%s', which '%s' does not have" % (s, target, spawn, target))

    # 4. The in-game world map lists exactly the layout's maps.
    atlas = re.findall(r"^\s*- scene: (.+)$", read(ATLAS), re.M)
    for a in sorted(set(atlas) - idset):
        faults.append("world map: lists '%s', which is not in the layout (re-bake the world map)" % a)
    for a in sorted(idset - set(atlas)):
        faults.append("world map: is missing '%s' (re-bake the world map)" % a)

    # 4b. The doors between maps the game finds its way by (WorldAtlas.links) are exactly the layout's portal pairs.
    atlas_text = read(ATLAS)
    pairs = re.findall(r"^\s*- a: (.+)\r?\n\s+b: (.+)$", atlas_text, re.M)
    have = {frozenset((a.strip(), b.strip())) for a, b in pairs}
    want = {frozenset((p.get("a"), p.get("b"))) for p in layout.get("portals", []) if p.get("a") and p.get("b")}
    for pair in sorted(have - want, key=sorted):
        faults.append("world map: has a door between %s that the layout does not (re-bake the world map)" % " and ".join(sorted(pair)))
    for pair in sorted(want - have, key=sorted):
        faults.append("world map: is missing the door between %s (re-bake the world map)" % " and ".join(sorted(pair)))

    # 4c. Doors: the layout owns each door's id and spot; the scenes and the world map only copy them.
    portals = layout.get("portals", [])
    margin, inset, rim_faults = rim_constants()
    faults.extend(rim_faults)
    size = {m["id"]: m.get("size") or [60, 60] for m in maps}
    whole = lambda v: isinstance(v, int) and not isinstance(v, bool)
    for n, p in enumerate(portals):
        where = "the door between %s and %s (number %d in the list)" % (p.get("a"), p.get("b"), n)
        if "id" not in p:
            faults.append("layout: %s has no id" % where)
        elif not whole(p["id"]) or p["id"] < 0:
            faults.append("layout: %s has id %r, which is not a whole number" % (where, p["id"]))
    all_ids = [p["id"] for p in portals if whole(p.get("id"))]
    for i in sorted({i for i in all_ids if all_ids.count(i) > 1}):
        faults.append("layout: door id %d is used by more than one door" % i)

    sound = []   # (portal, map id, other map id, anchor) for every end that can be placed
    for p in portals:
        if not whole(p.get("id")) or all_ids.count(p["id"]) > 1:
            continue
        for me, other, key in ((p.get("a"), p.get("b"), "anchorA"), (p.get("b"), p.get("a"), "anchorB")):
            anchor = p.get(key)
            if me not in size or other not in size:
                faults.append("layout: door %d joins '%s', which is not a map in the layout" % (p["id"], me if me not in size else other))
                continue
            if not isinstance(anchor, dict) or anchor.get("edge") not in INWARD:
                faults.append("layout: door %d has no N/S/E/W edge on its '%s' end" % (p["id"], me))
                continue
            for field in ("at", "arriveAt"):
                if field in anchor and not is_spot(anchor[field]):
                    faults.append("layout: door %d on '%s' has a `%s` that is not two numbers" % (p["id"], me, field))
            if is_spot(anchor.get("at")) and (abs(anchor["at"][0]) > size[me][0] / 2.0 or abs(anchor["at"][1]) > size[me][1] / 2.0):
                faults.append("layout: door %d on '%s' is at (%g, %g), outside its map's %dx%d box"
                              % (p["id"], me, anchor["at"][0], anchor["at"][1], size[me][0], size[me][1]))
            sound.append((p, me, other, anchor))

    door_objects = 0
    if margin is not None and inset is not None:
        by_id = {p["id"]: p for p in portals if whole(p.get("id")) and all_ids.count(p["id"]) == 1}
        in_scene = {s: scene_door_objects(read(os.path.join(SCENES, s + ".unity"))) for s in world_scenes}
        expected = {}   # (scene, object name) -> (what it is, the layout's point for it)
        for p, me, other, anchor in sound:
            if me not in in_scene:
                continue
            expected[(me, "Portal_w%d_to_%s" % (p["id"], other))] = ("door", door_point(size[me], anchor, margin), other)
            expected[(me, "SpawnPoint_w%d_from_%s" % (p["id"], other))] = ("arrival point", arrival_point(size[me], anchor, margin, inset), other)
        for s in world_scenes:
            for name, objects in sorted(in_scene[s].items()):
                door_objects += len(objects)
                kind = "door" if name.startswith("Portal_") else "arrival point"
                if len(objects) > 1:
                    faults.append("%s: '%s' has %d objects named %s" % (kind, s, len(objects), name))
                if any(parented for _x, _z, parented in objects):
                    faults.append("%s: %s in '%s' has a parent — a door and its arrival point sit at the top of the scene" % (kind, name, s))
                if (s, name) not in expected:
                    named = re.match(r"(?:Portal_w(\d+)_to_|SpawnPoint_w(\d+)_from_)(.+)", name)
                    number, far = int(named.group(1) or named.group(2)), named.group(3)
                    pair = by_id.get(number)
                    faults.append("%s: %s in '%s' names door %d, which the layout %s" % (
                        kind, name, s, number,
                        "does not have" if pair is None else "has between %s and %s, not between %s and %s" % (pair.get("a"), pair.get("b"), s, far)))
                    continue
                _kind, want, _other = expected[(s, name)]
                x, z, _parented = objects[0]
                if off((x, z), want) > NEAR:
                    faults.append("%s: %s in '%s' stands at (%.2f, %.2f) but the layout puts it at (%.2f, %.2f)"
                                  % (kind, name, s, x, z, want[0], want[1]))
        for (s, name), (kind, _want, other) in sorted(expected.items()):
            if other in in_scene and name not in in_scene[s]:
                faults.append("%s: '%s' has no %s, which the layout's door to '%s' needs" % (kind, s, name, other))

        # The world map's own copy: each link carries its door's id and where each end stands.
        block = re.search(r"^  links:\n((?:  - a: .*\n(?:    \w+: .*\n?)*)*)", atlas_text.replace("\r\n", "\n"), re.M)
        links = []
        for entry in re.split(r"^  - ", block.group(1), flags=re.M)[1:] if block else []:
            links.append(dict(re.findall(r"^\s*(\w+): (.*?)\s*$", entry, re.M)))
        number_of = lambda v: float(v) if v is not None and re.fullmatch(NUM, v) else None
        with_id = {}
        for link in links:
            if link.get("id") is not None and re.fullmatch(r"\d+", link["id"]):
                with_id.setdefault(int(link["id"]), []).append(link)
        bare = []
        for p in sorted(by_id.values(), key=lambda p: p["id"]):
            ends = {me: anchor for q, me, _other, anchor in sound if q is p}
            if len(ends) != 2:
                continue
            mine = with_id.get(p["id"], [])
            if len(mine) != 1:
                bare.append("%d (%s-%s)" % (p["id"], p["a"], p["b"]) if not mine else "%d (listed %d times)" % (p["id"], len(mine)))
                continue
            link = mine[0]
            if {link.get("a"), link.get("b")} != {p["a"], p["b"]}:
                faults.append("world map: link %d joins %s and %s, but the layout's door %d joins %s and %s (re-bake the world map)"
                              % (p["id"], link.get("a"), link.get("b"), p["id"], p["a"], p["b"]))
                continue
            for letter in ("a", "b"):
                me = link[letter]
                want = door_point(size[me], ends[me], margin)
                have = (number_of(link.get(letter + "x")), number_of(link.get(letter + "z")))
                if have[0] is None or have[1] is None:
                    faults.append("world map: link %d (%s-%s) has no %sx / %sz for its '%s' end (re-bake the world map)"
                                  % (p["id"], p["a"], p["b"], letter, letter, me))
                elif off(have, want) > NEAR:
                    faults.append("world map: link %d (%s-%s) puts the door on '%s' at (%.2f, %.2f) but the layout puts it at (%.2f, %.2f) (re-bake the world map)"
                                  % (p["id"], p["a"], p["b"], me, have[0], have[1], want[0], want[1]))
        for number in sorted(set(with_id) - set(by_id)):
            faults.append("world map: link %d is a door the layout does not have (re-bake the world map)" % number)
        if bare:
            faults.append("world map: %d of the layout's %d doors have no link carrying their id and door spots (re-bake the world map) — doors %s"
                          % (len(bare), len(by_id), ", ".join(bare)))

    # 5. Map Painter files: file name == saved name.
    if os.path.isdir(SIDECARS):
        for f in sorted(os.listdir(SIDECARS)):
            if not f.endswith(".map.json"):
                continue
            name = json.loads(read(os.path.join(SIDECARS, f))).get("name")
            if name != f[: -len(".map.json")]:
                faults.append("map painter: '%s' says its name is '%s'" % (f, name))

    # 6. The starting map and the music table name real scenes.
    for path, pattern in FIRST_MAP_SOURCES:
        m = re.search(pattern, read(path), re.M)
        if not m:
            faults.append("starting map: no starting map found in %s" % os.path.relpath(path, REPO))
        elif m.group(1).strip() not in scene_files:
            faults.append("starting map: %s names '%s', which has no scene" % (os.path.relpath(path, REPO), m.group(1).strip()))
    for s in re.findall(r"^\s*- sceneName: (.+)$", read(BGM), re.M):
        if s.strip() not in scene_files:
            faults.append("music table: '%s' has no scene" % s.strip())

    if faults:
        print(RED + "map ids disagree with the world layout (%d):" % len(faults) + OFF)
        for f in faults:
            print("  - " + f)
        return 1
    print(GRN + "map ids clean" + OFF + " — %d maps in the layout, %d scenes, %d doors, %d door and arrival objects standing where the layout puts them, all agreeing."
          % (len(maps), len(world_scenes), sum(len(d) for d in doors.values()), door_objects))
    return 0


if __name__ == "__main__":
    sys.exit(main())
