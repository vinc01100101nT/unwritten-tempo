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

USAGE:   tools/check-map-ids.py [--root <repo>]
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
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


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
    print(GRN + "map ids clean" + OFF + " — %d maps in the layout, %d scenes, %d doors, all agreeing."
          % (len(maps), len(world_scenes), sum(len(d) for d in doors.values())))
    return 0


if __name__ == "__main__":
    sys.exit(main())
