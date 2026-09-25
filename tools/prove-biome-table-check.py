#!/usr/bin/env python3
"""Prove tools/check-biome-table.py still SEES every fault it exists for — and is quiet about the real project.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This plants one known
fault at a time into a throwaway mirror of the repo (tools/prove_mirror.py — the real files are never opened for
writing), runs the checker against the mirror, and demands it fail AND name the fault. Then it demands the real
project still passes. Every anchor is read out of the real file at run time, so a fault always lands on today's data.

USAGE:   tools/prove-biome-table-check.py
EXIT:    0 = every planted fault caught and the real project passes, 1 = blind or crying wolf.
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from prove_mirror import mirror_with_change, discard  # noqa: E402

CHECK = os.path.join(HERE, "check-biome-table.py")
TABLE = os.path.join("unity", "Assets", "3_Data", "Recipes", "BiomeTable.asset")
ENUM = os.path.join("unity", "Assets", "Scripts", "MapRecipe.cs")
WEB = os.path.join("tools", "world-creator", "index.html")
TEST_MAP = os.path.join("unity", "Assets", "Scenes", "test_desert.unity")
HELD = os.path.join("unity", "Assets", "3_Data", "Resources", "BiomeWeather.asset")
SANDSTONE_META = os.path.join(REPO, "unity", "Assets", "1_Authored", "Art", "GroundTiles", "PATH_OR_SIDEWALK_sandstone.png.meta")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=None):
    args = [sys.executable, CHECK] + (["--root", root] if root else [])
    p = subprocess.run(args, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def read(rel):
    with open(os.path.join(REPO, rel), encoding="utf-8") as fh:
        return fh.read()


def row_of(biome_index):
    """The real text of one biome row, from its '- biome: N' line up to the next row."""
    t = read(TABLE)
    m = re.search(r"\n  - biome: %d\n.*?(?=\n  - biome: |\Z)" % biome_index, t, re.S)
    return m.group(0)


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

    coast = row_of(10)       # Coast — beach sand, rocks only
    sandstone = re.search(r"^guid:\s*([0-9a-f]{32})", open(SANDSTONE_META).read(), re.M).group(1)
    beach = re.search(r"baseTile: \{fileID: 2800000, guid: ([0-9a-f]{32})", coast).group(1)
    first_rock = re.search(r"guid: ([0-9a-f]{32}), type: 3\}", coast.split("- category: 3", 1)[1]).group(1)
    rocks_block = re.search(r"    - category: 3\n      prefabs:\n(?:      - .*\n)+", coast + "\n").group(0)
    rocks_mult = re.search(r"    - category: 3\n      prefabs:\n(?:      - .*\n)+      countMultiplier: [^\n]*\n", coast + "\n").group(0)
    rocks_mult_line = rocks_mult.splitlines()[-1] + "\n"
    grass = row_of(0)
    web = read(WEB)
    scene = read(TEST_MAP)
    pos = re.search(r"- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: 3\}\n\s+propertyPath: m_LocalPosition\.x", scene)

    bad = 0
    bad += case("a colour wash left over a biome's own tiles", TABLE, coast,
                coast.replace("tint: {r: 1, g: 1, b: 1, a: 1}", "tint: {r: 0.8, g: 1, b: 1, a: 1}"), "colour wash")
    bad += case("a road-only tile laid as a biome's main ground", TABLE, coast,
                coast.replace(beach, sandstone, 1), "does not allow BASE")
    bad += case("a biome in the list with no row", TABLE, coast,
                coast.replace("  - biome: 10\n", "  - biome: 9\n", 1), "Coast has no row")
    bad += case("a biome whose every field-template category is empty", TABLE, coast,
                coast.replace(rocks_block, "    - category: 3\n      prefabs: []\n"), "places no props")
    bad += case("a prop pointing at no prefab", TABLE, coast,
                coast.replace(first_rock, "0123456789abcdef0123456789abcdef", 1), "points at no prefab")
    bad += case("a prop entry saved before its count multiplier existed", TABLE, coast,
                coast.replace(rocks_mult, rocks_mult.replace(rocks_mult_line, ""), 1), "no saved count multiplier")
    bad += case("a prop entry that lists props but places none", TABLE, coast,
                coast.replace(rocks_mult, rocks_mult.replace(rocks_mult_line, "      countMultiplier: 0\n"), 1), "places none")
    bad += case("a held weather level above 1", HELD, "    alwaysPuddles: 1\n", "    alwaysPuddles: 1.5\n",
                "outside 0..1")
    bad += case("a test map marked with the wrong biome", TEST_MAP, "Assembly-CSharp::MapBiome\n  biome: 2\n",
                "Assembly-CSharp::MapBiome\n  biome: 3\n", "but its name says Desert")
    bad += case("a test map with no biome marker", TEST_MAP, "Assembly-CSharp::MapBiome\n  biome: 2\n",
                "Assembly-CSharp::MapBiome\n", "carries 0 biome markers")
    bad += case("the Grassland row changed", TABLE, grass,
                grass.replace("patchAmount: 0.9", "patchAmount: 0.5", 1), "Grassland's look is pinned")
    bad += case("a biome added to the list with no row", ENUM, "Haunted, Mist }", "Haunted, Mist, Tundra }",
                "Tundra has no row")
    bad += case("the World Creator missing a biome", WEB, '"coast",', "", "biome list has no coast")
    bad += case("a saved prop position pointing at a part the prefab lacks", TEST_MAP, pos.group(0),
                pos.group(0).replace("fileID: %s," % pos.group(1), "fileID: 424242,", 1), "no longer has")

    code_after, out_after = run()
    if code_after != 0 or out_after != out:
        print(RED + " WRONG" + OFF + " the real project's own answer changed while proving, so this proves nothing")
        bad += 1
    print()
    if bad:
        print(RED + "%d case(s) wrong — the biome check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the biome check sees every planted fault and the real project passes." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
