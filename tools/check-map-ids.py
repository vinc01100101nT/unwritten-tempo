#!/usr/bin/env python3
"""Every map id the game uses names a map that exists — and the game has exactly one owner of its map id.

THE SOURCE OF TRUTH: the seamless world's own id, `public const string MapId` in
unity/Assets/Scripts/Editor/SeamlessWorldBuilder.cs (read out of the code, never typed here). That id is the scene
file name (Assets/Scenes/<id>.unity), what /tp and tools/gm.py take, and what a character's save names. Everything
else below is a COPY, and this check fails the moment a copy drifts:

  - SeamlessWorldBuilder.cs declaring MapId other than exactly once, the world's scene missing, holding no chunks,
    or not enabled in Build Settings
  - a scene in Build Settings whose file does not exist
  - a chunk the world names that is not an address in the Addressables World group
  - the starting map (Systems.unity's firstMap, SavePoint.DefaultMap) or a map in the music table naming no scene,
    or the starting map being the Systems scene itself
  - a door (Portal3D: targetScene / spawnId) in any scene leading to a scene that does not exist, or to an arrival
    point that scene does not have
  - the world map's atlas (WorldAtlas.asset) listing a map, or a door end, that has no scene
  - a place in WorldPlaces.json with no id, or two places sharing one id
  - a Map Painter file whose file name and saved name differ

USAGE:  tools/check-map-ids.py [--root <repo>]
EXIT:    0 = every map id names a real map, 1 = something disagrees (each named).
"""
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if "--root" in sys.argv:
    REPO = os.path.abspath(sys.argv[sys.argv.index("--root") + 1])

ASSETS = os.path.join(REPO, "unity", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
BUILD = os.path.join(REPO, "unity", "ProjectSettings", "EditorBuildSettings.asset")
WORLD_BUILDER = os.path.join(ASSETS, "Scripts", "Editor", "SeamlessWorldBuilder.cs")
WORLD_GROUP = os.path.join(ASSETS, "AddressableAssetsData", "AssetGroups", "World.asset")
ATLAS = os.path.join(ASSETS, "3_Data", "Resources", "WorldAtlas.asset")
PLACES = os.path.join(ASSETS, "3_Data", "Resources", "WorldPlaces.json")
BGM = os.path.join(ASSETS, "3_Data", "Resources", "MapBgmTable.asset")
SIDECARS = os.path.join(REPO, "tools", "map-painter", "exports")
SYSTEMS_SCENE = "Systems"
FIRST_MAP_SOURCES = [
    (os.path.join(SCENES, "Systems.unity"), r"^\s*firstMap: (.+)$"),
    # The one constant the code's start map reads (GameRoot's default and the Systems scene builder both use it).
    (os.path.join(ASSETS, "Scripts", "SavePoint.cs"), r'public const string DefaultMap = "([^"]+)";'),
]
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def rel(path):
    return os.path.relpath(path, REPO)


def world_map_id(faults):
    """The seamless world's map id, read from SeamlessWorldBuilder.cs, its owner. None when it cannot be read."""
    text = read(WORLD_BUILDER) if os.path.exists(WORLD_BUILDER) else ""
    hits = re.findall(r'public\s+const\s+string\s+MapId\s*=\s*"([^"]*)"\s*;', text)
    if len(hits) != 1 or not hits[0]:
        faults.append("world: %s declares `public const string MapId` %d times (needs exactly 1, not empty)"
                      % (rel(WORLD_BUILDER), len(hits)))
        return None
    return hits[0]


def main():
    faults = []
    scene_files = {f[:-6] for f in os.listdir(SCENES) if f.endswith(".unity")}
    build_text = read(BUILD)
    build_scenes = re.findall(r"^  - enabled: (\d)\s*\n\s+path: (.+)$", build_text, re.M)
    enabled = {p.strip() for on, p in build_scenes if on == "1"}

    # 1. The world's own id, its scene, its chunks and Build Settings.
    world = world_map_id(faults)
    chunks = []
    if world is not None:
        if world not in scene_files:
            faults.append("world: map id '%s' has no scene file Assets/Scenes/%s.unity" % (world, world))
        else:
            text = read(os.path.join(SCENES, world + ".unity"))
            chunks = [c.strip() for c in re.findall(r"^  - address: (.+)$", text, re.M)]
            if not chunks:
                faults.append("world: Assets/Scenes/%s.unity names no chunks — it is not a built seamless world" % world)
            if "Assets/Scenes/%s.unity" % world not in enabled:
                faults.append("world: Assets/Scenes/%s.unity is not enabled in Build Settings" % world)

    # 2. Build Settings name only scenes that exist.
    for _on, path in build_scenes:
        if not os.path.exists(os.path.join(REPO, "unity", path.strip())):
            faults.append("build settings: lists %s, which does not exist" % path.strip())

    # 3. Every chunk the world names is an address in the World group.
    addresses = {a.strip() for a in re.findall(r"m_Address: (.+)", read(WORLD_GROUP))} if os.path.exists(WORLD_GROUP) else set()
    for address in chunks:
        if address not in addresses:
            faults.append("world '%s': chunk '%s' is not an address in the World group" % (world, address))

    # 4. The starting map and the music table name real maps.
    for path, pattern in FIRST_MAP_SOURCES:
        m = re.search(pattern, read(path), re.M)
        if not m:
            faults.append("starting map: no starting map found in %s" % rel(path))
            continue
        name = m.group(1).strip()
        if name not in scene_files:
            faults.append("starting map: %s names '%s', which has no scene" % (rel(path), name))
        elif name == SYSTEMS_SCENE:
            faults.append("starting map: %s names the Systems scene, which is not a map" % rel(path))
    for s in re.findall(r"^\s*- sceneName: (.+)$", read(BGM), re.M):
        if s.strip() not in scene_files:
            faults.append("music table: '%s' has no scene" % s.strip())

    # 5. Every door in any scene leads to a scene that exists, at an arrival point it has.
    texts = {s: read(os.path.join(SCENES, s + ".unity")) for s in scene_files}
    arrivals = {s: set(x.strip() for x in re.findall(r"^\s*id: (.+)$", t, re.M)) for s, t in texts.items()}
    doors = 0
    for s, t in sorted(texts.items()):
        for target, spawn in re.findall(r"targetScene: (.*)\n\s*spawnId: (.*)", t):
            doors += 1
            target, spawn = target.strip(), spawn.strip()
            if target not in scene_files:
                faults.append("door: '%s' leads to '%s', which has no scene" % (s, target))
            elif spawn and spawn not in arrivals[target]:
                faults.append("door: '%s' -> '%s' arrives at '%s', which '%s' does not have" % (s, target, spawn, target))

    # 6. The world map's atlas names only maps that exist.
    if os.path.exists(ATLAS):
        atlas = read(ATLAS)
        for a in sorted({x.strip() for x in re.findall(r"^\s*- scene: (.+)$", atlas, re.M)} - scene_files):
            faults.append("world map: WorldAtlas lists '%s', which has no scene" % a)
        for a, b in re.findall(r"^\s*- a: (.+)\r?\n\s+b: (.+)$", atlas, re.M):
            for end in (a.strip(), b.strip()):
                if end not in scene_files:
                    faults.append("world map: WorldAtlas has a door into '%s', which has no scene" % end)

    # 7. The world's places (what /tp and tools/gm.py resolve a name to): every one has an id, no id twice.
    places = []
    if os.path.exists(PLACES):
        places = json.loads(read(PLACES)).get("places", [])
        ids = [p.get("id") for p in places]
        for n, p in enumerate(places):
            if not p.get("id"):
                faults.append("places: entry %d in %s has no id" % (n, rel(PLACES)))
        for i in sorted({i for i in ids if i and ids.count(i) > 1}):
            faults.append("places: id '%s' is used by more than one place in %s" % (i, rel(PLACES)))

    # 8. Map Painter files: file name == saved name.
    if os.path.isdir(SIDECARS):
        for f in sorted(os.listdir(SIDECARS)):
            if not f.endswith(".map.json"):
                continue
            name = json.loads(read(os.path.join(SIDECARS, f))).get("name")
            if name != f[: -len(".map.json")]:
                faults.append("map painter: '%s' says its name is '%s'" % (f, name))

    if faults:
        print(RED + "map ids disagree (%d):" % len(faults) + OFF)
        for f in faults:
            print("  - " + f)
        return 1
    print(GRN + "map ids clean" + OFF + " — the world is '%s' (%d chunks, all addressed), %d scenes, %d doors, %d places, "
          "starting map and music table all naming real maps." % (world, len(chunks), len(scene_files), doors, len(places)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
