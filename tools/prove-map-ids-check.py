#!/usr/bin/env python3
"""Prove tools/check-map-ids.py still SEES every drift it exists for — and is quiet about the real project.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This plants one known
fault at a time into a throwaway mirror of the repo (tools/prove_mirror.py — the real files are never opened for
writing), runs the checker against the mirror, and demands it fail AND name the fault. Then it demands the real
project still passes. Every anchor is read out of the real files at run time, so a fault always lands on today's data.

USAGE:   tools/prove-map-ids-check.py
EXIT:    0 = every planted fault caught and the real project passes, 1 = blind or crying wolf.
"""
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from prove_mirror import mirror_with_change, discard  # noqa: E402

CHECK = os.path.join(HERE, "check-map-ids.py")
LAYOUT = os.path.join("tools", "world-creator", "worlds", "world-regions.json")
BUILD = os.path.join("unity", "ProjectSettings", "EditorBuildSettings.asset")
ATLAS = os.path.join("unity", "Assets", "3_Data", "Resources", "WorldAtlas.asset")
BGM = os.path.join("unity", "Assets", "3_Data", "Resources", "MapBgmTable.asset")
SYSTEMS = os.path.join("unity", "Assets", "Scenes", "Systems.unity")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=None):
    args = [sys.executable, CHECK] + (["--root", root] if root else [])
    p = subprocess.run(args, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def read(rel):
    with open(os.path.join(REPO, rel), encoding="utf-8") as fh:
        return fh.read()


def case(description, rel, old, new, must_say):
    root, hits = mirror_with_change(REPO, rel, old, new)
    if root is None:
        print(RED + " WRONG" + OFF + " %s — the anchor was not found in %s, so this proves nothing" % (description, rel))
        return 1
    try:
        code, out = run(root)
    finally:
        discard(root)
    if code == 1 and must_say in out:
        print(GRN + "  ok  " + OFF + description)
        return 0
    print(RED + " WRONG" + OFF + " %s (exit %d, expected to say %r)" % (description, code, must_say))
    print("\n".join("         " + l for l in out.splitlines()[:6]))
    return 1


def main():
    code, out = run()
    if code != 0:
        print(RED + "the real project does not pass the check, so no planted fault can be told apart:" + OFF)
        print(out)
        return 1

    layout = json.loads(read(LAYOUT))
    scenes = os.path.join(REPO, "unity", "Assets", "Scenes")
    # A generated map with at least one door, and that door's target and arrival point.
    for m in layout["maps"]:
        path = os.path.join("unity", "Assets", "Scenes", m["id"] + ".unity")
        if not os.path.exists(os.path.join(REPO, path)):
            continue
        door = re.search(r"targetScene: (.*)\n\s*spawnId: (.*)", read(path))
        if door:
            break
    scene, target, spawn = m["id"], door.group(1), door.group(2)
    special = next(x for x in layout["maps"] if x["type"] == "special" and not os.path.exists(os.path.join(scenes, x["id"] + ".unity")))
    sidecar = os.path.join("tools", "map-painter", "exports", scene + ".map.json")
    first = re.search(r"^\s*firstMap: (.+)$", read(SYSTEMS), re.M).group(0)
    bgm = re.search(r"^\s*- sceneName: (.+)$", read(BGM), re.M).group(0)

    bad = 0
    bad += case("an id that is not the snake_case of its name", LAYOUT, '"id": "%s"' % scene,
                '"id": "%s_old"' % scene, "its id must be '%s'" % scene)
    field = next(x["id"] for x in layout["maps"] if x["type"] == "field")
    bad += case("a field whose id breaks <town code>_fildNN", LAYOUT, '"id": "%s"' % field,
                '"id": "%s_x"' % field, "a field's id is <town code>_fildNN")
    dungeon = next(x["id"] for x in layout["maps"] if x["type"] == "dungeon")
    bad += case("a dungeon whose id breaks <town code>_dunNN", LAYOUT, '"id": "%s"' % dungeon,
                '"id": "%s_x"' % dungeon, "a dungeon floor's id is <town code>_dunNN")
    txt = read(LAYOUT)
    block = re.search(r'\{\s*"id": "%s".*?"type": "special"' % re.escape(special["id"]), txt, re.S).group(0)
    bad += case("a generated map with no scene file", LAYOUT, block, block.replace('"type": "special"', '"type": "field"'),
                "has no scene file '%s.unity'" % special["id"])
    bad += case("a world scene missing from Build Settings", BUILD, "path: Assets/Scenes/%s.unity" % scene,
                "path: Assets/Scenes/%s_gone.unity" % scene, "'%s' is not in the scene list" % scene)
    bad += case("a door leading to a map that is not in the layout", os.path.join("unity", "Assets", "Scenes", scene + ".unity"),
                "targetScene: %s\n" % target, "targetScene: %s_old\n" % target, "not a map in the layout")
    bad += case("a door arriving at a point its map does not have", os.path.join("unity", "Assets", "Scenes", scene + ".unity"),
                "spawnId: %s\n" % spawn, "spawnId: %s_old\n" % spawn, "does not have")
    bad += case("the in-game world map listing a map the layout does not have", ATLAS, "- scene: %s\n" % scene,
                "- scene: %s_old\n" % scene, "not in the layout (re-bake")
    bad += case("a Map Painter file whose saved name differs", sidecar, '"name": "%s"' % scene,
                '"name": "%s_old"' % scene, "says its name is")
    bad += case("the starting map naming no scene", SYSTEMS, first, first + "_old", "starting map:")
    bad += case("the music table naming no scene", BGM, bgm, bgm + "_old", "music table:")

    code_after, out_after = run()
    if code_after != 0 or out_after != out:
        print(RED + " WRONG" + OFF + " the real project's own answer changed while proving, so this proves nothing")
        bad += 1
    print()
    if bad:
        print(RED + "%d case(s) wrong — the map id check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the map id check sees every planted fault and the real project passes." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
