#!/usr/bin/env python3
"""Check the creature roster against the agreed ladder, and the bosses against where they live.

GEAR AND DIFFICULTY (from each creature's own file; the grade it wears comes from its level, checked by the maths
checks, never typed):
  · every creature has its multiplier written, 1 or more — a file without one was never given one;
  · 14 C elites: level 50-70, gear typed (not rolled), one per weapon type — every weapon type covered once;
  · 4 B elites: all at one level, rolling weapon and armour each life.
DUNGEONS AND FIELDS:
  · a dungeon floor's (a map whose id holds "_dun") normal creatures are all Tainted or Blighted versions (versionOf set);
  · every original normal lives in at least one field (decided 2026-10-29: the world's field patches hold originals
    near towns and Tainted and Blighted farther out — tools/world-spawns/build_world_spawns.py writes them).
PLACEMENT (WorldSpawns, SummonRecipes, WorldTownsfolk):
  · every C elite has exactly two field spawn rows, population 1 each, on two different areas;
  · at most one C elite per area;
  · no B elite and no shadow has a spawn row: each is summoned, has exactly one recipe and exactly one summoner;
  · every recipe material is some C or B elite's own material.

USAGE:   tools/check-creature-roster.py [--root DIR]     (DIR = the repo; default: this checker's repo)
EXIT:    0 = clean, 1 = something named below is wrong.
"""
import argparse
import glob
import os
import re
import sys

WEAPON_TYPES = {1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15}
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def field(text, name):
    m = re.search(r"^  " + re.escape(name) + r": ?(.*)$", text, re.M)
    return m.group(1).strip() if m else ""


def guid_of(meta_path):
    try:
        m = re.search(r"^guid: (\w+)", open(meta_path, encoding="utf-8").read(), re.M)
        return m.group(1) if m else ""
    except OSError:
        return ""


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    root = ap.parse_args().root
    res = os.path.join(root, "unity", "Assets", "3_Data", "Resources")
    faults = []

    items = {}
    for path in glob.glob(os.path.join(res, "Items", "*.asset")):
        t = open(path, encoding="utf-8").read()
        items[guid_of(path + ".meta")] = (field(t, "id"), int(field(t, "slot") or 0), int(field(t, "weaponType") or 0))

    defs = {}
    for path in glob.glob(os.path.join(res, "Monsters", "*.asset")):
        t = open(path, encoding="utf-8").read()
        gear = re.findall(r"guid: (\w+)", t.split("startingGear:", 1)[1].split("\n  ", 1)[0] + "\n" +
                          "\n".join(l for l in t.split("startingGear:", 1)[1].splitlines()[1:8] if l.startswith("  - "))) \
            if "startingGear:" in t else []
        weapon = 0
        for g in gear:
            if g in items and items[g][1] == 3:
                weapon = items[g][2]
        mat = re.search(r"^  material: \{fileID: \d+, guid: (\w+)", t, re.M)
        defs[field(t, "id")] = {
            "kind": int(field(t, "kind") or 0), "level": int(field(t, "level") or 0),
            "multiplier": field(t, "multiplier"), "rolls": field(t, "rollsGearEachLife") == "1",
            "weapon": weapon, "material": items.get(mat.group(1), ("",))[0] if mat else "",
            "version": bool(re.search(r"^  versionOf: \{fileID: [1-9]", t, re.M)),
        }

    c_elites = {i: d for i, d in defs.items() if d["kind"] == 1 and not d["rolls"]}
    b_elites = {i: d for i, d in defs.items() if d["kind"] == 1 and d["rolls"]}
    shadows = {i: d for i, d in defs.items() if d["kind"] == 2}

    for i, d in defs.items():
        try:
            m = float(d["multiplier"])
        except ValueError:
            faults.append(f"{i} has no multiplier written")
            continue
        if m < 1:
            faults.append(f"{i} has multiplier {d['multiplier']}, below 1")
    if len(c_elites) != 14:
        faults.append(f"there are {len(c_elites)} C elites, not 14")
    seen_weapons = {}
    for i, d in c_elites.items():
        if not 50 <= d["level"] <= 70:
            faults.append(f"C elite {i} is level {d['level']}, outside 50-70")
        seen_weapons.setdefault(d["weapon"], []).append(i)
    for w, who in seen_weapons.items():
        if len(who) > 1:
            faults.append(f"weapon type {w} has {len(who)} C elites: {', '.join(sorted(who))}")
    for w in WEAPON_TYPES - set(seen_weapons):
        faults.append(f"weapon type {w} has no C elite")
    if len(b_elites) != 4:
        faults.append(f"there are {len(b_elites)} B elites (rolling gear), not 4")
    if len({d["level"] for d in b_elites.values()}) > 1:
        faults.append("the B elites are not all at one level")

    text = open(os.path.join(res, "WorldSpawns.asset"), encoding="utf-8").read().split("\n  areas:", 1)[0]
    rows = []
    for r in re.split(r"(?m)^  - (?=map: )", text)[1:]:
        m, c, p = re.search(r"map: (\S+)", r), re.search(r"creature: (\S*)", r), re.search(r"population: (\d+)", r)
        a = re.search(r"^    area: (-?\d+)", r, re.M)
        if m and c and c.group(1):
            rows.append((m.group(1), c.group(1), int(p.group(1)) if p else 0, int(a.group(1)) if a else -1))
    on_area = {}
    for i in c_elites:
        mine = [r for r in rows if r[1] == i and "_dun" not in r[0]]
        if len(mine) != 2 or any(r[2] != 1 for r in mine) or any(r[3] < 0 for r in mine) or len({(r[0], r[3]) for r in mine}) != 2:
            faults.append(f"C elite {i} has {len(mine)} field spawn rows (populations {[r[2] for r in mine]}, areas "
                          f"{[r[3] for r in mine]}), not two of 1 on two different areas")
        for r in mine:
            on_area.setdefault((r[0], r[3]), []).append(i)
    for (mp, a), who in on_area.items():
        if a >= 0 and len(who) > 1:
            faults.append(f"area {a} of {mp} holds {len(who)} C elites: {', '.join(sorted(who))}")

    for mp, i, _, _ in (r for r in rows if r[1] in defs and defs[r[1]]["kind"] == 0):
        if "_dun" in mp and not defs[i]["version"]:
            faults.append(f"dungeon floor {mp} holds the original {i}; a floor holds only Tainted or Blighted versions")
    for i, d in defs.items():
        if d["kind"] == 0 and not d["version"] and not any(r[1] == i and "_dun" not in r[0] for r in rows):
            faults.append(f"the original {i} lives in no field")

    recipes_text = open(os.path.join(res, "SummonRecipes.asset"), encoding="utf-8").read() \
        if os.path.exists(os.path.join(res, "SummonRecipes.asset")) else ""
    recipe_bosses = re.findall(r"bossId: (\S+)", recipes_text)
    folk = open(os.path.join(res, "WorldTownsfolk.asset"), encoding="utf-8").read()
    summoned = re.findall(r"summons: (mob\.\S+)", folk)
    elite_mats = {d["material"] for d in list(c_elites.values()) + list(b_elites.values()) if d["material"]}
    for i in list(b_elites) + list(shadows):
        if any(r[1] == i for r in rows):
            faults.append(f"{i} is summon-only but has a spawn row")
        if recipe_bosses.count(i) != 1:
            faults.append(f"{i} has {recipe_bosses.count(i)} recipes, not 1")
        if summoned.count(i) != 1:
            faults.append(f"{i} has {summoned.count(i)} summoners, not 1")
    for m in re.findall(r"itemId: (\S*)", recipes_text):
        if m not in elite_mats:
            faults.append(f"recipe material '{m}' is no C or B elite's material")

    if faults:
        for f in faults:
            print(f"{RED}FAIL{OFF} {f}")
        print(f"\n{len(faults)} problem(s) in the creature roster.")
        return 1
    print(f"{GRN}creature roster clean{OFF} — {len(defs)} creatures, 14 C elites, 4 B elites, {len(shadows)} shadows placed as agreed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
