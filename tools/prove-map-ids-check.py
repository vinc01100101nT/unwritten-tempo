#!/usr/bin/env python3
"""Prove tools/check-map-ids.py still SEES every drift it exists for — and is quiet about the real project.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This plants one known
fault at a time into a throwaway mirror of the repo (tools/prove_mirror.py — the real files are never opened for
writing), runs the checker against the mirror, and demands it fail AND name the fault. Then it demands the real
project still passes. Every anchor is read out of the real files at run time, so a fault always lands on today's data.

A planted fault only counts as caught when the checker says something about the mirror that it does NOT say about
the real project — so the cases still prove sight on a day the real project is failing for a reason of its own.
That day the prover still exits 1 and says the real project fails.

USAGE:   tools/prove-map-ids-check.py
EXIT:    0 = every planted fault caught and the real project passes, 1 = blind, crying wolf, or the real project fails.
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
RES = os.path.join("unity", "Assets", "3_Data", "Resources")
ATLAS = os.path.join(RES, "WorldAtlas.asset")
PLACES = os.path.join(RES, "WorldPlaces.json")
BGM = os.path.join(RES, "MapBgmTable.asset")
SYSTEMS = os.path.join("unity", "Assets", "Scenes", "Systems.unity")
BUILD = os.path.join("unity", "ProjectSettings", "EditorBuildSettings.asset")
BUILDER = os.path.join("unity", "Assets", "Scripts", "Editor", "SeamlessWorldBuilder.cs")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=None):
    args = [sys.executable, CHECK] + (["--root", root] if root else [])
    p = subprocess.run(args, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def read(rel):
    with open(os.path.join(REPO, rel), encoding="utf-8") as fh:
        return fh.read()


BASELINE = ""   # what the checker says about the real project, untouched


def case(description, rel, old, new, must_say):
    if old is None:
        print(RED + " WRONG" + OFF + " %s — nothing in %s to plant it on, so this proves nothing" % (description, rel))
        return 1
    if must_say in BASELINE:
        print(RED + " WRONG" + OFF + " %s — the real project already says %r, so catching it proves nothing" % (description, must_say))
        return 1
    root, hits = mirror_with_change(REPO, rel, old, new)
    if root is None or not hits:
        if root is not None:
            discard(root)
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


def first(pattern, rel):
    m = re.search(pattern, read(rel), re.M)
    return m.group(0) if m else None


def main():
    global BASELINE
    code, out = run()
    BASELINE = out

    map_id = first(r'public\s+const\s+string\s+MapId\s*=\s*"[^"]*"\s*;', BUILDER)
    world = re.search(r'"([^"]*)"', map_id).group(1) if map_id else ""
    world_scene = os.path.join("unity", "Assets", "Scenes", world + ".unity")
    chunk = first(r"^  - address: .+$", world_scene) if world and os.path.exists(os.path.join(REPO, world_scene)) else None
    first_map = first(r"^\s*firstMap: .+$", SYSTEMS)
    bgm = first(r"^\s*- sceneName: .+$", BGM)
    world_in_build = first(r"^  - enabled: 1\s*\n\s+path: Assets/Scenes/%s\.unity$" % re.escape(world), BUILD)
    places = json.loads(read(PLACES)).get("places", [])
    place_a = '"id": "%s"' % places[0]["id"] if len(places) > 1 else None
    place_b = places[1]["id"] if len(places) > 1 else ""

    bad = 0
    bad += case("the world's map id losing its one declaration", BUILDER, map_id,
                map_id and map_id.replace("MapId", "MapIdWas"), "declares `public const string MapId` 0 times")
    bad += case("the world's map id naming no scene", BUILDER, map_id,
                map_id and map_id.replace('"%s"' % world, '"%s_gone"' % world), "has no scene file")
    bad += case("the world's scene switched off in Build Settings", BUILD, world_in_build,
                world_in_build and world_in_build.replace("enabled: 1", "enabled: 0"), "is not enabled in Build Settings")
    bad += case("Build Settings listing a scene that does not exist", BUILD, world_in_build,
                world_in_build and world_in_build + "\n  - enabled: 1\n    path: Assets/Scenes/lst_fild01.unity",
                "lists Assets/Scenes/lst_fild01.unity, which does not exist")
    bad += case("the world naming a chunk the World group does not hold", world_scene, chunk,
                chunk and chunk + "_gone", "is not an address in the World group")
    bad += case("the starting map naming no scene", SYSTEMS, first_map, first_map and first_map + "_old", "starting map:")
    bad += case("the starting map being the Systems scene", SYSTEMS, first_map,
                first_map and re.sub(r"firstMap: .+", "firstMap: Systems", first_map), "names the Systems scene")
    bad += case("the music table naming no scene", BGM, bgm, bgm and bgm + "_old", "music table:")
    bad += case("a door leading to a scene that does not exist", SYSTEMS, first_map,
                first_map and first_map + "\n  targetScene: lst_fild01\n  spawnId: w1_from_world",
                "leads to 'lst_fild01', which has no scene")
    bad += case("a door arriving at a point its scene does not have", SYSTEMS, first_map,
                first_map and first_map + "\n  targetScene: %s\n  spawnId: nowhere_at_all" % world,
                "arrives at 'nowhere_at_all'")
    bad += case("the world map's atlas listing a map with no scene", ATLAS, first(r"^  maps: .*$", ATLAS),
                "  maps:\n  - scene: lst_fild01", "WorldAtlas lists 'lst_fild01'")
    bad += case("the world map's atlas keeping a door into a map with no scene", ATLAS, first(r"^  links: .*$", ATLAS),
                "  links:\n  - a: %s\n    b: lst_fild01" % world, "door into 'lst_fild01'")
    bad += case("two places sharing one id", PLACES, place_a, '"id": "%s"' % place_b,
                "id '%s' is used by more than one place" % place_b)
    bad += case("a place with no id", PLACES, place_a, '"id": ""', "has no id")

    code_after, out_after = run()
    if code_after != 0 or out_after != out:
        print(RED + " WRONG" + OFF + " the real project's own answer changed while proving, so this proves nothing")
        bad += 1
    print()
    if bad:
        print(RED + "%d case(s) wrong — the map id check's silence proves nothing." % bad + OFF)
        return 1
    if code != 0:
        print(GRN + "the map id check sees every planted fault" + OFF + RED + " — but the real project does not pass it:" + OFF)
        print(out)
        return 1
    print(GRN + "the map id check sees every planted fault and the real project passes." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
