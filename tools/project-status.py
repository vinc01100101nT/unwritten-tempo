#!/usr/bin/env python3
"""Where the project stands, counted live from the repo — the one home for every count.

A doc never types a count (squares built, creatures, skills, items, quests, townsfolk): it points here, because a typed
count is wrong the moment somebody commits. Run from anywhere:  python3 tools/project-status.py

Reads only files in the repo, so it runs the same on every machine and in a cloud session. Facts that live only on one
machine — characters in the database, builds on disk — are not counted here.
"""
import json
import os
import re
import sys
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "unity", "Assets")
RES = os.path.join(ASSETS, "3_Data", "Resources")

WEAPON_TYPES = {}
CREATURE_KINDS = ["Normal", "Elite", "Shadow"]


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def field(text, name):
    m = re.search(r"^  " + name + r": ?(.*)$", text, re.M)
    return m.group(1).strip() if m else ""


def assets(folder):
    d = os.path.join(RES, folder)
    if not os.path.isdir(d):
        return []
    return [read(os.path.join(d, f)) for f in sorted(os.listdir(d)) if f.endswith(".asset")]


def load_weapon_types():
    src = read(os.path.join(ASSETS, "Scripts", "WeaponType.cs"))
    body = src[src.index("enum WeaponType"):]
    body = body[:body.index("}")]
    for name, num in re.findall(r"^\s*(\w+)\s*=\s*(\d+)", body, re.M):
        WEAPON_TYPES[num] = name


def section(title):
    print(f"\n{title}")


def line(label, value, note=""):
    print(f"  {label:<34} {value:>6}" + (f"   {note}" if note else ""))


def code():
    scripts = os.path.join(ASSETS, "Scripts")
    game = editor = 0
    menus = 0
    for dp, _, files in os.walk(scripts):
        for f in files:
            if not f.endswith(".cs"):
                continue
            if os.sep + "Editor" in dp[len(scripts):]:
                editor += 1
            else:
                game += 1
            menus += len(re.findall(r"\[MenuItem\(\s*\"Tools/unwritten", read(os.path.join(dp, f))))
    charserver = len([f for f in os.listdir(os.path.join(ROOT, "charserver")) if f.endswith(".cs")])
    section("Code")
    line("game C# files", game)
    line("editor tool C# files", editor)
    line("Tools > unwritten menu commands", menus)
    line("character server C# files", charserver)


def world():
    layout = json.load(open(os.path.join(ROOT, "data", "world-layout.json")))
    chunks = os.path.join(ASSETS, "2_Generated", "World", "Chunks")
    built = set()
    if os.path.isdir(chunks):
        for f in os.listdir(chunks):
            m = re.fullmatch(r"square_(-?\d+)_(-?\d+)\.prefab", f)
            if m:
                built.add((int(m.group(1)), int(m.group(2))))
    width = layout["size"][0] // layout["cell"]
    land = set()
    for i, h in enumerate(layout["heights"]):
        if h >= 0:
            land.add(((i % width) * layout["cell"] // 32, (i // width) * layout["cell"] // 32))
    sq = lambda o: (int(o["x"] // 32), int(o["y"] // 32))
    towns = layout["towns"]
    pieces = {f[:-7].lower() for f in os.listdir(os.path.join(ASSETS, "1_Authored", "Prefabs", "Towns")) if f.endswith(".prefab")}
    plans = {f[:-5] for f in os.listdir(os.path.join(ROOT, "data", "towns")) if f.endswith(".json")}

    section("World (one seamless map, built square by square)")
    line("squares holding land", len(land))
    line("squares built", len(built))
    line("land squares not built", len(land - built))
    line("towns in the layout", len(towns))
    line("towns on built ground", sum(sq(t) in built for t in towns))
    line("towns with a built town piece", sum(t["id"].replace("_", "") in pieces or t["id"] in pieces for t in towns))
    line("towns with a plan file", sum(t["id"] in plans for t in towns))
    name = lambda o: o.get("id") or "-".join(o.get("road", [])) or o.get("town") or ">".join((o.get("from", ""), o.get("to", "")))
    at = lambda o: {"x": o["start"][0], "y": o["start"][1]} if "start" in o else o
    missing = [("towns", [t["id"] for t in towns if sq(t) not in built])]
    for key, label in [("landmarks", "landmarks"), ("caves", "caves"), ("ponds", "ponds"), ("bridges", "bridges"),
                       ("dungeon_doors", "dungeon doors"), ("low_tide_paths", "low-tide paths"), ("boats", "boats")]:
        items = layout.get(key, [])
        line(f"{label} built / in the layout", f"{sum(sq(at(o)) in built for o in items)}/{len(items)}")
        missing.append((label, [name(o) for o in items if sq(at(o)) not in built]))
    for label, names in missing:
        if names:
            print(f"  not built — {label}: " + ", ".join(names))


def creatures():
    kinds = Counter()
    versions = Counter()
    for t in assets("Monsters"):
        kind = CREATURE_KINDS[int(field(t, "kind") or 0)]
        kinds[kind] += 1
        name = field(t, "m_Name")
        prefix = name.split("_")[0] if "_" in name else ""
        if prefix in ("Tainted", "Blighted"):
            versions[prefix] += 1
    section("Creatures")
    line("creature definitions", sum(kinds.values()))
    for k in CREATURE_KINDS:
        line(f"  {k.lower()}s", kinds[k])
    line("  of them Tainted versions", versions["Tainted"])
    line("  of them Blighted versions", versions["Blighted"])


def skills_and_items():
    skills = {}
    for t in assets("Skills"):
        skills[field(t, "id")] = t
    items = assets("Items")
    used_by_items = {field(t, "useSkillId") for t in items} | {field(t, "wornSkillId") for t in items}
    used_by_items.discard("")

    by_weapon = Counter()
    published = Counter()
    orphans = []
    for sid, t in skills.items():
        wt = field(t, "weaponType") or "0"
        if wt != "0":
            name = WEAPON_TYPES.get(wt, wt)
            by_weapon[name] += 1
            if field(t, "published") == "1":
                published[name] += 1
        elif sid not in used_by_items and sid != "skill.summon":
            orphans.append(sid)

    section("Skills")
    line("skill definitions", len(skills))
    for name, n in sorted(by_weapon.items()):
        line(f"  {name} skills (published)", f"{n} ({published[name]})")
    line("  carried by an item", sum(s in skills for s in used_by_items))
    line("  belonging to nothing", len(orphans), ", ".join(sorted(orphans)))
    line("weapon families with a published skill", len(published), f"of {len(WEAPON_TYPES) - 1}")

    categories = ["Equip", "Use", "Material", "Etc", "Quest"]
    cat = Counter(categories[int(field(t, "category") or 0)] for t in items)
    weapons = Counter(WEAPON_TYPES.get(field(t, "weaponType") or "0", "?") for t in items
                      if field(t, "slot") == "3")
    section("Items")
    line("item definitions", len(items))
    for c in categories:
        if cat[c]:
            line(f"  {c.lower()}", cat[c])
    line("weapons", sum(weapons.values()), ", ".join(f"{k} {v}" for k, v in sorted(weapons.items())))
    line("cards", len(assets("Cards")))


def people():
    folk = read(os.path.join(RES, "WorldTownsfolk.asset"))
    rows = re.split(r"^  - id:", folk, flags=re.M)[1:]
    keepers = sum(bool(re.search(r"^    keeper: 1", r, re.M)) for r in rows)
    summoners = sum(bool(re.search(r"^    summons: \S", r, re.M)) for r in rows)
    shops = re.findall(r"^  - id: (\S+)", read(os.path.join(RES, "WorldShops.asset")), re.M)
    section("People and things to do")
    line("townsfolk rows", len(rows))
    line("  keepers", keepers)
    line("  shadow summoners", summoners)
    line("  everyone else", len(rows) - keepers - summoners)
    line("shops", len(shops), ", ".join(shops))
    line("quests", len(assets("Quests")))
    line("saved characters", len(assets("SavedCharacters")))


def main():
    load_weapon_types()
    print("unwritten — counted live from the repo")
    code()
    world()
    creatures()
    skills_and_items()
    people()
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except BrokenPipeError:
        sys.exit(0)
