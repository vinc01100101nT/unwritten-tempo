#!/usr/bin/env python3
"""Check the creature roster against the agreed ladder, and the bosses against where they live.

GEAR (from each creature's own file):
  · a normal creature wears the grade its level gives it: Raw 1-24, F 25-49, E 50-74, D 75 and up;
  · 14 C elites: grade C, level 50-70, gear typed (not rolled), one per weapon type — every weapon type covered once;
  · 4 B elites: grade B, all at one level, rolling weapon and armour each life;
  · every shadow wears A.
PLACEMENT (WorldSpawns, SummonRecipes, WorldTownsfolk):
  · every C elite has exactly one spawn row, population 1;
  · at most one C elite per zone — a field zone is the map-id prefix (lst_fild01 and lst_fild03 are one zone), a
    dungeon floor is its own;
  · every dungeon's deepest floor has a C elite;
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

DEEPEST = ["olk_dun02", "hlt_dun03", "vlm_dun03", "cdl_dun03", "khn_dun04", "khn_dun06", "khn_dun09", "lst_dun02"]
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


def grade_for_level(level):
    return 3 if level >= 75 else 2 if level >= 50 else 1 if level >= 25 else 0


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
            "grade": int(field(t, "gearQuality") or 0), "rolls": field(t, "rollsGearEachLife") == "1",
            "weapon": weapon, "material": items.get(mat.group(1), ("",))[0] if mat else "",
        }

    c_elites = {i: d for i, d in defs.items() if d["kind"] == 1 and not d["rolls"]}
    b_elites = {i: d for i, d in defs.items() if d["kind"] == 1 and d["rolls"]}
    shadows = {i: d for i, d in defs.items() if d["kind"] == 2}

    for i, d in defs.items():
        if d["kind"] == 0 and d["grade"] != grade_for_level(d["level"]):
            faults.append(f"{i} (level {d['level']}) wears grade {d['grade']}, not {grade_for_level(d['level'])}")
    if len(c_elites) != 14:
        faults.append(f"there are {len(c_elites)} C elites, not 14")
    seen_weapons = {}
    for i, d in c_elites.items():
        if d["grade"] != 4:
            faults.append(f"C elite {i} wears grade {d['grade']}, not C")
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
    for i, d in b_elites.items():
        if d["grade"] != 5:
            faults.append(f"B elite {i} wears grade {d['grade']}, not B")
    for i, d in shadows.items():
        if d["grade"] != 6:
            faults.append(f"shadow {i} wears grade {d['grade']}, not A")

    spawns = open(os.path.join(res, "WorldSpawns.asset"), encoding="utf-8").read().split("\n  - ")[1:]
    rows = []
    for r in spawns:
        m, c, p = re.search(r"map: (\S+)", r), re.search(r"creature: (\S*)", r), re.search(r"population: (\d+)", r)
        if m and c and c.group(1):
            rows.append((m.group(1), c.group(1), int(p.group(1)) if p else 0))
    zones = {}
    for i in c_elites:
        mine = [r for r in rows if r[1] == i]
        if len(mine) != 1 or mine[0][2] != 1:
            faults.append(f"C elite {i} has {len(mine)} spawn rows (populations {[r[2] for r in mine]}), not one of 1")
        for r in mine:
            zone = r[0] if "_dun" in r[0] else r[0].split("_")[0]
            zones.setdefault(zone, []).append(i)
    for z, who in zones.items():
        if len(who) > 1:
            faults.append(f"zone {z} holds {len(who)} C elites: {', '.join(sorted(who))}")
    for floor in DEEPEST:
        if not any(r[0] == floor and r[1] in c_elites for r in rows):
            faults.append(f"deepest floor {floor} has no C elite")

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
