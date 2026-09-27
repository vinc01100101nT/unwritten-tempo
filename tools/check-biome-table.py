#!/usr/bin/env python3
"""Check that every biome's LOOK is whole, and that the biome test maps hold their props where they were put.

WHY THIS EXISTS: a biome row that is half right generates without a single error. A road tile laid as the main
ground, a colour wash left over a biome's own tile, a biome whose every prop list is empty, a biome in the list with
no row at all (the generator stops with a dialog only when somebody happens to pick it) — each one bakes a map that
looks almost fine. And a map whose saved prop positions point at a part of the prop that no longer exists puts every
prop at the map centre, which is what happened to 40 older maps. None of that shows until someone walks the map.

Reads the files on disk, never the editor's copy, so what it checks is what the generator will actually read:
  · the biome list, parsed out of MapRecipe.cs (never copied here);
  · the Biome Table asset: one row per biome, each tile used only for a job its file name allows
    (BASE = main ground, PATCH = patches, PATH = road), no colour wash, every prop pointing at a real file,
    at least one prop somewhere in the field template's categories, and a saved count multiplier on every entry;
  · the Grassland row, pinned exactly as it is (the starting country's look changes only on purpose);
  · the browser World Creator's biome list, which must name every biome;
  · every biome test map (Scenes/test_*.unity): each saved prop position must point at a part its prefab has, and
    the map carries one biome marker naming the biome in its own name (test_forest_a → Forest);
  · the per-biome weather list: each biome at most once, every held level between 0 and 1.

USAGE:   tools/check-biome-table.py [--root <repo>]
EXIT:    0 = clean, 1 = problems found, 2 = setup problem.
"""
import os
import re
import sys

RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if "--root" in sys.argv:
    REPO = os.path.abspath(sys.argv[sys.argv.index("--root") + 1])
ASSETS = os.path.join(REPO, "unity", "Assets")
MAP_RECIPE_CS = os.path.join(ASSETS, "Scripts", "MapRecipe.cs")
TABLE = os.path.join(ASSETS, "3_Data", "Recipes", "BiomeTable.asset")
FIELD_TEMPLATE = os.path.join(ASSETS, "3_Data", "Recipes", "FieldRecipe.asset")
TILES = os.path.join(ASSETS, "1_Authored", "Art", "GroundTiles")
PREFABS = os.path.join(ASSETS, "1_Authored", "Prefabs")
SCENES = os.path.join(ASSETS, "Scenes")
WORLD_CREATOR = os.path.join(REPO, "tools", "world-creator", "index.html")
BIOME_WEATHER = os.path.join(ASSETS, "3_Data", "Resources", "BiomeWeather.asset")
MAP_BIOME_META = os.path.join(ASSETS, "Scripts", "MapBiome.cs.meta")

# The Grassland row, pinned: meadow grass, soil patches, the recipe's own road, no wash, the same 0.9 patch amount
# every biome uses (the user's call, 2026-09-25), and all five field-template prop categories listed (an open meadow,
# 2026-09-25), and ground in 3 levels up to 1.2 high, hills 16 wide, slopes up to 20 degrees (the user's call,
# 2026-09-27: "higher ground and lower ground should be obvious", "make the slope greater than 5 degree").
# Change it on purpose or not at all.
GRASSLAND_PIN = {"base": "BASE_OR_PATCH_meadow_grass", "patch": "BASE_OR_PATCH_OR_PATH_soil", "road": None,
                 "tint": (1.0, 1.0, 1.0, 1.0), "patchAmount": 0.9, "props": 5,
                 "bumpHeight": 1.2, "bumpWidth": 16.0, "bumpLevels": 3, "bumpSteepestDegrees": 20.0}

problems = []


def rel(p):
    return os.path.relpath(p, REPO)


def read(p):
    with open(p, encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def enum_members(cs, name):
    m = re.search(r"public enum %s\s*\{([^}]*)\}" % name, cs)
    if not m:
        return None
    return [x.strip().split("=")[0].strip() for x in m.group(1).split(",") if x.strip()]


def guid_index(root, ext):
    out = {}
    for base, _d, files in os.walk(root, followlinks=True):
        for f in files:
            if f.endswith(ext + ".meta"):
                m = re.search(r"^guid:\s*([0-9a-f]{32})", read(os.path.join(base, f)), re.M)
                if m:
                    out[m.group(1)] = os.path.join(base, f[:-5])
    return out


def uses(tile_name):
    """The jobs a tile's file name allows: the UPPERCASE words before its lowercase name, split on _OR_."""
    m = re.match(r"^((?:[A-Z]+_OR_)*[A-Z]+)_[a-z]", tile_name)
    return set(m.group(1).split("_OR_")) if m else set()


def main():
    for p in (MAP_RECIPE_CS, TABLE, FIELD_TEMPLATE, WORLD_CREATOR, BIOME_WEATHER, MAP_BIOME_META):
        if not os.path.isfile(p):
            print("setup: missing %s" % rel(p))
            return 2
    cs = read(MAP_RECIPE_CS)
    biomes = enum_members(cs, "Biome")
    categories = enum_members(cs, "PropCategory")
    if not biomes or not categories:
        print("setup: could not read the Biome / PropCategory lists out of %s" % rel(MAP_RECIPE_CS))
        return 2
    map_biomes = [b for b in biomes if b != "None"]

    tiles = {g: os.path.splitext(os.path.basename(p))[0] for g, p in guid_index(TILES, ".png").items()}
    prefabs = guid_index(PREFABS, ".prefab")

    # The categories the field template actually places — a biome must fill at least one of them.
    template_cats = {int(c) for c in re.findall(r"^\s+category:\s*(\d+)", read(FIELD_TEMPLATE), re.M)}

    table = read(TABLE)
    chunks = re.split(r"\n  - biome: ", table)[1:]
    if not chunks:
        problems.append("%s: no rows at all" % rel(TABLE))
    seen = {}
    for ch in chunks:
        idx = int(ch.split("\n", 1)[0])
        name = biomes[idx] if idx < len(biomes) else "#%d" % idx
        where = "%s: %s row" % (rel(TABLE), name)
        if name in seen:
            problems.append("%s appears twice" % where)
        seen[name] = True
        if name == "None":
            problems.append("%s: None is the creature-side 'any biome' value, never a map's" % where)

        def tile(field):
            m = re.search(r"^    %s: \{fileID: (\d+)(?:, guid: ([0-9a-f]{32}))?" % field, ch, re.M)
            if not m or m.group(1) == "0":
                return None, False
            return tiles.get(m.group(2)), True

        row = {}
        for field, job, required in (("baseTile", "BASE", True), ("patchTile", "PATCH", True), ("roadTile", "PATH", False)):
            t, set_ = tile(field)
            row[field] = t
            if not set_:
                if required:
                    problems.append("%s has no %s" % (where, field))
                continue
            if t is None:
                problems.append("%s: %s is not a tile in %s" % (where, field, rel(TILES)))
            elif job not in uses(t):
                problems.append("%s: %s is %s, whose name does not allow %s" % (where, field, t, job))

        tm = re.search(r"^    tint: \{r: ([\d.\-e]+), g: ([\d.\-e]+), b: ([\d.\-e]+), a: ([\d.\-e]+)\}", ch, re.M)
        tint = tuple(float(x) for x in tm.groups()) if tm else None
        if tint != (1.0, 1.0, 1.0, 1.0):
            problems.append("%s: colour wash %s over its own tiles (must be white)" % (where, tint))
        pm = re.search(r"^    patchAmount: ([\d.\-e]+)", ch, re.M)
        patch_amount = float(pm.group(1)) if pm else None
        bh = re.search(r"^    bumpHeight: ([\d.\-e]+)", ch, re.M)
        bw = re.search(r"^    bumpWidth: ([\d.\-e]+)", ch, re.M)
        bl = re.search(r"^    bumpLevels: (\d+)", ch, re.M)
        bs = re.search(r"^    bumpSteepestDegrees: ([\d.\-e]+)", ch, re.M)
        if not bh or not bw or not bl or not bs:
            problems.append("%s: no saved ground height, hill width, levels and steepest slope (an unset one reads as flat)" % where)

        # props: category → prefab guids
        listed = {}
        for pb in re.split(r"\n    - category: ", ch)[1:]:
            cat = int(pb.split("\n", 1)[0])
            guids = re.findall(r"guid: ([0-9a-f]{32})", pb)
            listed[cat] = guids
            cm = re.search(r"^      countMultiplier: ([\d.\-e]+)", pb, re.M)
            if not cm:
                problems.append("%s: its %s entry has no saved count multiplier (an unset value reads as 0 = none)"
                                % (where, categories[cat]))
            elif guids and float(cm.group(1)) <= 0:
                problems.append("%s: its %s entry lists props but places none (count multiplier %s)"
                                % (where, categories[cat], cm.group(1)))
            for g in guids:
                if g not in prefabs:
                    problems.append("%s: a %s prop points at no prefab (guid %s)" % (where, categories[cat], g))
        fills = [c for c in template_cats if c not in listed or listed[c]]
        if template_cats and not fills:
            problems.append("%s places no props: every category the field template uses is listed empty" % where)

        if name == "Grassland":
            got = {"base": row["baseTile"], "patch": row["patchTile"], "road": row["roadTile"], "tint": tint,
                   "patchAmount": patch_amount, "props": len(listed),
                   "bumpHeight": float(bh.group(1)) if bh else None, "bumpWidth": float(bw.group(1)) if bw else None,
                   "bumpLevels": int(bl.group(1)) if bl else None,
                   "bumpSteepestDegrees": float(bs.group(1)) if bs else None}
            for k, v in GRASSLAND_PIN.items():
                if got[k] != v:
                    problems.append("%s changed: %s is %r, pinned %r (Grassland's look is pinned; change the pin on purpose)"
                                    % (where, k, got[k], v))

    for b in map_biomes:
        if b not in seen:
            problems.append("%s: %s has no row — a map set to it cannot be generated" % (rel(TABLE), b))

    wm = re.search(r"const BIOMES = \[([^\]]*)\]", read(WORLD_CREATOR))
    web = [x.strip().strip('"') for x in wm.group(1).split(",")] if wm else []
    for b in map_biomes:
        if b.lower() not in web:
            problems.append("%s: the World Creator's biome list has no %s" % (rel(WORLD_CREATOR), b.lower()))

    # The per-biome weather list: each biome once, every held level a real fraction.
    held_seen = set()
    for rb in re.split(r"\n  - biome: ", read(BIOME_WEATHER))[1:]:
        idx = int(rb.split("\n", 1)[0])
        name = biomes[idx] if idx < len(biomes) else "#%d" % idx
        where = "%s: %s row" % (rel(BIOME_WEATHER), name)
        if name in held_seen:
            problems.append("%s appears twice" % where)
        held_seen.add(name)
        for field in ("alwaysPuddles", "alwaysSnowCover", "alwaysFog", "alwaysClouds"):
            fm = re.search(r"^    %s: ([\d.\-e]+)" % field, rb, re.M)
            if not fm:
                problems.append("%s has no saved %s" % (where, field))
            elif not 0.0 <= float(fm.group(1)) <= 1.0:
                problems.append("%s: %s is %s, outside 0..1" % (where, field, fm.group(1)))

    marker_guid = re.search(r"^guid:\s*([0-9a-f]{32})", read(MAP_BIOME_META), re.M).group(1)
    by_lower = {b.lower(): b for b in map_biomes}

    # Every biome test map: each saved prop position must point at a part its prefab still has.
    prefab_ids = {}
    checked_positions = checked_maps = 0
    for f in sorted(os.listdir(SCENES)):
        if not (f.startswith("test_") and f.endswith(".unity")):
            continue
        scene = os.path.join(SCENES, f)
        text = read(scene)
        want = by_lower.get(re.sub(r"_[a-z]$", "", f[len("test_"):-len(".unity")]))
        marks = re.findall(r"m_Script: \{fileID: 11500000, guid: %s, type: 3\}(?:\n  .*)*?\n  biome: (\d+)" % marker_guid, text)
        if len(marks) != 1:
            problems.append("%s carries %d biome markers, not 1 (the running game cannot tell its biome)" % (rel(scene), len(marks)))
        elif want and biomes[int(marks[0])] != want:
            problems.append("%s is marked %s, but its name says %s" % (rel(scene), biomes[int(marks[0])], want))
        orphans = total = instances = 0
        for block in re.split(r"\n--- !u!1001 &", text)[1:]:
            src = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", block)
            if not src or src.group(1) not in prefabs:
                continue
            g = src.group(1)
            instances += 1
            if g not in prefab_ids:
                prefab_ids[g] = set(re.findall(r"^--- !u!\d+ &(-?\d+)", read(prefabs[g]), re.M))
            for t in re.findall(r"- target: \{fileID: (-?\d+), guid: %s, type: 3\}\n\s+propertyPath: m_LocalPosition\.x" % g, block):
                total += 1
                if t not in prefab_ids[g]:
                    orphans += 1
        checked_maps += 1
        checked_positions += total
        if instances and not total:
            problems.append("%s: %d props but no saved position could be read — this check cannot see them"
                            % (rel(scene), instances))
        if orphans:
            problems.append("%s: %d of %d saved prop positions point at a part the prefab no longer has "
                            "(those props sit at the map centre)" % (rel(scene), orphans, total))

    if problems:
        print(RED + "Biome looks / biome test maps — %d problem(s):" % len(problems) + OFF)
        for p in problems:
            print("  · " + p)
        return 1
    print(GRN + "biome table clean" + OFF + " — %d biomes, each with its own row, tiles used as their names allow, "
          "no wash, props real; %d test maps, %d saved prop positions, all pointing at real parts."
          % (len(map_biomes), checked_maps, checked_positions))
    return 0


if __name__ == "__main__":
    sys.exit(main())
