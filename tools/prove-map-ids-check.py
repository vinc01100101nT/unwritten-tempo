#!/usr/bin/env python3
"""Prove tools/check-map-ids.py still SEES every drift it exists for — and is quiet about the real project.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This plants one known
fault at a time into a throwaway mirror of the repo (tools/prove_mirror.py — the real files are never opened for
writing), runs the checker against the mirror, and demands it fail AND name the fault. Then it demands the real
project still passes. Every anchor is read out of the real files at run time, so a fault always lands on today's data.

A planted fault only counts as caught when the checker says something about the mirror that it does NOT say about
the real project — so the cases still prove sight on a day the real project is failing for a reason of its own
(the world map waiting for a re-bake, say). That day the prover still exits 1 and says the real project fails.

USAGE:   tools/prove-map-ids-check.py
EXIT:    0 = every planted fault caught and the real project passes, 1 = blind, crying wolf, or the real project fails.
"""
import importlib.util
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


BASELINE = ""   # what the checker says about the real project, untouched


def checker_module():
    spec = importlib.util.spec_from_file_location("check_map_ids", CHECK)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def transform_text(scene_text, name, upto):
    """The saved text of the named object's Transform, from its owner line down to its `upto` line — the exact
    anchor a planted fault swaps."""
    owner = re.search(r"^--- !u!1 &(\d+)\nGameObject:\n(?:  .*\n)*?  m_Name: %s\n" % re.escape(name), scene_text, re.M)
    if owner is None:
        return None
    found = re.search(r"m_GameObject: \{fileID: %s\}\n(?:  .*\n)*?  %s: \{[^}]*\}" % (owner.group(1), upto), scene_text)
    return found.group(0) if found else None


def stays_quiet(description, rel, old, new, must_not_say):
    """The opposite of a case: this change must NOT make the checker say any of these."""
    root, _hits = mirror_with_change(REPO, rel, old, new)
    if root is None:
        print(RED + " WRONG" + OFF + " %s — the anchor was not found in %s, so this proves nothing" % (description, rel))
        return 1
    try:
        _code, out = run(root)
    finally:
        discard(root)
    said = [w for w in must_not_say if w in out]
    if not said:
        print(GRN + "  ok  " + OFF + description)
        return 0
    print(RED + " WRONG" + OFF + " %s (it said %r)" % (description, said))
    print("\n".join(["         " + l[:300] for l in out.splitlines() if any(w in l for w in said)][:4]))
    return 1


def case(description, rel, old, new, must_say):
    if old is None:
        print(RED + " WRONG" + OFF + " %s — nothing in %s to plant it on, so this proves nothing" % (description, rel))
        return 1
    if must_say in BASELINE:
        print(RED + " WRONG" + OFF + " %s — the real project already says %r, so catching it proves nothing" % (description, must_say))
        return 1
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


def finish(bad, code, out):
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


def main():
    global BASELINE
    code, out = run()
    BASELINE = out

    layout = json.loads(read(LAYOUT))
    first = re.search(r"^\s*firstMap: (.+)$", read(SYSTEMS), re.M).group(0)
    bgm = re.search(r"^\s*- sceneName: (.+)$", read(BGM), re.M).group(0)
    world_scene = os.path.join("unity", "Assets", "Scenes", "world.unity")
    if not layout["maps"]:
        # THE ZONE WORLD IS GONE (2026-10-27, the user's call): the game is the seamless world, so only the rules that
        # still have something to stand on are proven — its chunks, the starting map and the music table.
        bad = 0
        chunk = re.search(r"^  - address: (.+)$", read(world_scene), re.M).group(1).strip()
        bad += case("a seamless world naming a chunk the World group does not hold", world_scene,
                    "  - address: %s\n" % chunk, "  - address: %s_gone\n" % chunk, "is not an address in the World group")
        bad += case("the starting map naming no scene", SYSTEMS, first, first + "_old", "starting map:")
        bad += case("the music table naming no scene", BGM, bgm, bgm + "_old", "music table:")
        return finish(bad, code, out)
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
    if os.path.exists(os.path.join(REPO, world_scene)):
        chunk = re.search(r"^  - address: (.+)$", read(world_scene), re.M).group(1).strip()
        bad += case("a seamless world naming a chunk the World group does not hold", world_scene,
                    "  - address: %s\n" % chunk, "  - address: %s_gone\n" % chunk, "is not an address in the World group")
    bad += case("a door leading to a map that is not in the layout", os.path.join("unity", "Assets", "Scenes", scene + ".unity"),
                "targetScene: %s\n" % target, "targetScene: %s_old\n" % target, "not a map in the layout")
    bad += case("a door arriving at a point its map does not have", os.path.join("unity", "Assets", "Scenes", scene + ".unity"),
                "spawnId: %s\n" % spawn, "spawnId: %s_old\n" % spawn, "does not have")
    bad += case("the in-game world map listing a map the layout does not have", ATLAS, "- scene: %s\n" % scene,
                "- scene: %s_old\n" % scene, "not in the layout (re-bake")
    door = re.search(r"^\s*- a: (.+)\r?\n\s+b: (.+)$", read(ATLAS), re.M)
    if door is None:
        print(RED + " WRONG" + OFF + " the world map has no doors between maps at all, so the door check proves nothing")
        bad += 1
    else:
        bad += case("the in-game world map's doors disagreeing with the layout's portals", ATLAS,
                    "  b: %s" % door.group(2).strip(), "  b: %s_old" % door.group(2).strip(), "door between")

    # ── doors: ids, spots, parents, and the world map's copy ──
    check = checker_module()
    margin, inset, _rim_faults = check.rim_constants()
    size = {x["id"]: x["size"] for x in layout["maps"]}
    has_scene = lambda i: os.path.exists(os.path.join(scenes, i + ".unity"))
    portals = layout["portals"]
    # a rim door whose two maps both have scenes, a door that touches neither of them, and a door with its own spot
    rim = next(p for p in portals if has_scene(p["a"]) and has_scene(p["b"]) and "at" not in p["anchorA"])
    far = next(p for p in portals if not {p["a"], p["b"]} & {rim["a"], rim["b"]})
    spot = next(p for p in portals if "at" in p["anchorA"] or "at" in p["anchorB"])
    rim_scene = os.path.join("unity", "Assets", "Scenes", rim["a"] + ".unity")
    rim_text = read(rim_scene)
    door_name, arrival_name = "Portal_w%d_to_%s" % (rim["id"], rim["b"]), "SpawnPoint_w%d_from_%s" % (rim["id"], rim["b"])
    shove = lambda text: None if text is None else re.sub(r"(m_LocalPosition: \{x: )([^,]+)", lambda m: m.group(1) + repr(float(m.group(2)) + 1.5), text)
    adopt = lambda text: None if text is None else text[: text.rindex("{fileID: 0}")] + "{fileID: 4242}"
    id_line = '"id": %d,\n      "a": "%s"' % (rim["id"], rim["a"])
    bad += case("a door with no id", LAYOUT, id_line, '"a": "%s"' % rim["a"], "has no id")
    bad += case("a door whose id is not a whole number", LAYOUT, id_line, id_line.replace('"id": %d' % rim["id"], '"id": "%d"' % rim["id"]),
                "which is not a whole number")
    bad += case("two doors sharing one id", LAYOUT, id_line, id_line.replace('"id": %d' % rim["id"], '"id": %d' % far["id"]),
                "door id %d is used by more than one door" % far["id"])
    at = spot["anchorA"]["at"] if "at" in spot["anchorA"] else spot["anchorB"]["at"]
    at_text = re.search(r'"at": \[\s*%s,\s*%s\s*\]' % (re.escape(json.dumps(at[0])), re.escape(json.dumps(at[1]))), read(LAYOUT))
    bad += case("a door's own spot lying outside its map's box", LAYOUT, at_text and at_text.group(0),
                at_text and at_text.group(0).replace(json.dumps(at[0]), "4000", 1), "outside its map's")
    door_at = transform_text(rim_text, door_name, "m_LocalPosition")
    bad += case("a door standing somewhere the layout does not put it", rim_scene, door_at, shove(door_at),
                "door: %s in '%s' stands at" % (door_name, rim["a"]))
    arrival_at = transform_text(rim_text, arrival_name, "m_LocalPosition")
    bad += case("an arrival point standing somewhere the layout does not put it", rim_scene, arrival_at, shove(arrival_at),
                "arrival point: %s in '%s' stands at" % (arrival_name, rim["a"]))
    door_father = transform_text(rim_text, door_name, "m_Father")
    bad += case("a door with a parent", rim_scene, door_father, adopt(door_father), "door: %s in '%s' has a parent" % (door_name, rim["a"]))
    stolen = "Portal_w%d_to_%s" % (far["id"], rim["b"])
    bad += case("a door named after an id that belongs to two other maps", rim_scene, "m_Name: %s\n" % door_name, "m_Name: %s\n" % stolen,
                "door: %s in '%s' names door %d, which the layout has between" % (stolen, rim["a"], far["id"]))
    owner_rel = os.path.join("unity", "Assets", "Scripts", "Editor", "WorldLayoutFile.cs")
    declared = re.search(r"const\s+float\s+EdgeMargin\s*=\s*[\d.]+f?\s*;", read(owner_rel))
    declared = declared and declared.group(0)
    bad += case("the rim margin changing in the editor code while the scenes still hold the old spots", owner_rel, declared,
                declared and re.sub(r"=\s*[\d.]+", "= %g" % (margin + 1), declared), "stands at")
    bad += case("the rim margin losing its declaration", owner_rel, declared, declared and declared.replace("EdgeMargin", "EdgeMarginWas"),
                "declares `const float EdgeMargin` 0 times")
    bad += case("a scene missing a door the layout has", rim_scene, "m_Name: %s\n" % door_name, "m_Name: Somewhere_Else\n",
                "'%s' has no %s" % (rim["a"], door_name))

    # The world map's links. The honest copy is worked out here from the layout with the checker's own rule, so these
    # cases prove sight whether or not the real world map has been re-baked yet.
    real_links = re.search(r"^  links:\n(?:  - a: .*\n(?:    \w+: .*\n?)*)*", read(ATLAS), re.M)

    def links_block(change=None, bare=False):
        rows = ["  links:"]
        for p in portals:
            a, b = check.door_point(size[p["a"]], p["anchorA"], margin), check.door_point(size[p["b"]], p["anchorB"], margin)
            row = {"a": p["a"], "b": p["b"], "id": p["id"], "ax": "%g" % a[0], "az": "%g" % a[1], "bx": "%g" % b[0], "bz": "%g" % b[1]}
            if change and p is rim:
                row.update(change)
            rows.append(("  - a: %(a)s\n    b: %(b)s" if bare else
                         "  - a: %(a)s\n    b: %(b)s\n    id: %(id)s\n    ax: %(ax)s\n    az: %(az)s\n    bx: %(bx)s\n    bz: %(bz)s") % row)
        return "\n".join(rows) + "\n"

    real = real_links.group(0) if real_links and real_links.group(0).count("- a:") else None
    if real is None:
        print(RED + " WRONG" + OFF + " the world map has no links block to stand a copy in, so its door spots prove nothing")
        bad += 1
    else:
        bad += stays_quiet("a world map whose links carry every door's id and spots exactly as the layout has them", ATLAS, real,
                           links_block(), ["world map: link", "have no link carrying"])
        wrong_ax = "%g" % (check.door_point(size[rim["a"]], rim["anchorA"], margin)[0] + 2.5)
        bad += case("the world map putting a door somewhere the layout does not", ATLAS, real, links_block({"ax": wrong_ax}),
                    "world map: link %d (%s-%s) puts the door on '%s' at" % (rim["id"], rim["a"], rim["b"], rim["a"]))
        bad += case("the world map carrying a door id the layout does not have", ATLAS, real, links_block({"id": 987654}),
                    "world map: link 987654 is a door the layout does not have")
        if "have no link carrying" in BASELINE:
            print(GRN + "  ok  " + OFF + "the world map's links carrying no ids or spots (the real world map is in exactly this state, and is named)")
        else:
            bad += case("the world map's links carrying no ids or spots", ATLAS, real, links_block(bare=True),
                        "have no link carrying their id and door spots")

    bad += case("a Map Painter file whose saved name differs", sidecar, '"name": "%s"' % scene,
                '"name": "%s_old"' % scene, "says its name is")
    bad += case("the starting map naming no scene", SYSTEMS, first, first + "_old", "starting map:")
    bad += case("the music table naming no scene", BGM, bgm, bgm + "_old", "music table:")
    return finish(bad, code, out)


if __name__ == "__main__":
    sys.exit(main())
