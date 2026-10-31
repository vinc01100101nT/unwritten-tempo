#!/usr/bin/env python3
"""Every creature's own material is whole: each normal creature and elite has its multiplier written and names exactly
ONE material, whose tier is the one its multiplier gives (CreatureMaterials.TierOfMultiplier: x1 Common, x3 Uncommon,
x10 Rare, x30 Epic, x90 Legendary), no material belongs to two creatures, every material sells for exactly what the
rule says for its creature's level and its tier, its icon is its own picture or else the box drawn in its tier's colour, every piece of gear is worth what a brand-new piece is
(CraftingMath.NewPieceWorth), and NOTHING BLIGHTED LIVES NEAR A TOWN: no Blighted version (a normal's x10 copy) is kept
on a world patch whose middle lies within the spawn builder's ring (RING in tools/world-spawns/build_world_spawns.py)
of any town in data/world-layout.json. The tier openings and the price multiples are read out of CreatureMaterials.cs, the
level-to-grade ladder out of CreatureLoot.cs and the new-piece worth out of CraftingMath.cs, so the rule has one owner
and this only proves the saved files still agree with it. The editor writes these numbers itself on every save
(DerivedItemValues); nobody types them. Design: docs/3-world/creature-materials.md, docs/3-world/what-a-kill-pays.md.

USAGE:   tools/check-creature-materials.py [--root <repo>]
EXIT:    0 = every creature material agrees with the rule; 1 = something does not (each fault is named).
"""
import glob
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TIER = {1: "Common", 2: "Uncommon", 3: "Rare", 4: "Epic", 5: "Legendary"}
CREATURE_KIND = "6"
ETC_TAB = "3"
WEIGHT = "10"
# THE GROUND ROUND A TOWN IS NEVER BLIGHTED. The world spawn builder lays every patch and keeps Blighted (x10) creatures
# out of its ring round each town; the ring is read out of the builder and the towns out of the world layout, so the
# rule has one owner and this only proves the saved spawn list still agrees with it. A patch's saved middle is the
# square nearest the middle the builder measured from, so the ring is held one layout square short of RING.
SPAWN_BUILDER = os.path.join("tools", "world-spawns", "build_world_spawns.py")
WORLD_LAYOUT = os.path.join("data", "world-layout.json")
WORLD_MAP = "world"
BLIGHTED_FROM = 8      # the builder's own split: a version of x8 or more is Blighted, below it Tainted


def town_ring(root):
    """(ring, towns as (name, scene x, scene z)) or None: the builder's RING less one layout square, and every town."""
    try:
        src = open(os.path.join(root, SPAWN_BUILDER), encoding="utf-8").read()
        layout = json.load(open(os.path.join(root, WORLD_LAYOUT), encoding="utf-8"))
    except (OSError, ValueError):
        return None
    ring = re.search(r"\bRING = (\d+(?:\.\d+)?)", src)
    if ring is None or not layout.get("towns"):
        return None
    # Scene coordinates from the layout's: x = layout x - 640, z = 640 - layout y (the layout's own "unity" note).
    towns = [(t["name"], t["x"] - 640, 640 - t["y"]) for t in layout["towns"]]
    return float(ring.group(1)) - float(layout.get("cell", 0)), towns


def field(text, key):
    m = re.search(r"^  %s: *(.*)$" % re.escape(key), text, re.M)
    return m.group(1).strip() if m else ""


def guid_of(ref):
    m = re.search(r"guid: (\w+)", ref)
    return m.group(1) if m else ""


def multiples(root):
    """Gold per creature level for each tier, read from the one function that decides it."""
    path = os.path.join(root, "unity", "Assets", "Scripts", "CreatureMaterials.cs")
    src = open(path, encoding="utf-8").read()
    body = re.search(r"static int SellValue\(int level, Rarity tier\).*?\n    \}", src, re.S)
    gold = re.search(r"const int GoldPerLevel = (\d+);", src)
    if body is None or gold is None:
        return None
    out = {1: 2 * int(gold.group(1))}
    for name, value in re.findall(r"case Rarity\.(\w+):\s*perLevel = (\d+);", body.group(0)):
        for step, tier in TIER.items():
            if tier == name:
                out[step] = int(value)
    return out if len(out) == 5 else None


def tier_openings(root):
    """The multiplier each tier, Common .. Legendary, opens from, read from the game."""
    src = open(os.path.join(root, "unity", "Assets", "Scripts", "CreatureMaterials.cs"), encoding="utf-8").read()
    m = re.search(r"TierFromMultiplier\s*=\s*\{([^}]*)\}", src)
    if m is None:
        return None
    row = [float(v.strip().rstrip("f")) for v in m.group(1).split(",")]
    return row if len(row) == 5 else None


def box_icons(root):
    """{step: guid} of the box icon each tier's material shows, from the path DerivedItemValues writes them from."""
    src = open(os.path.join(root, "unity", "Assets", "Scripts", "Editor", "DerivedItemValues.cs"), encoding="utf-8").read()
    m = re.search(r'const string BoxIconPath = "([^"]+)";', src)
    if m is None:
        return None
    out = {}
    for step, tier in TIER.items():
        meta = os.path.join(root, "unity", m.group(1).replace("{0}", tier) + ".meta")
        if not os.path.exists(meta):
            return None
        out[step] = guid_of(open(meta, encoding="utf-8").read())
    return out


def gear_rule(root):
    """(grades, ladder top, least recycled dust), read from the functions that decide them. CraftingMath.cs is read
    forgivingly: only a number is searched for in it, and a stray badly-encoded character elsewhere must not hide it."""
    scripts = os.path.join(root, "unity", "Assets", "Scripts")
    craft = open(os.path.join(scripts, "CraftingMath.cs"), encoding="utf-8", errors="replace").read()
    loot = open(os.path.join(scripts, "CreatureLoot.cs"), encoding="utf-8").read()
    least = re.search(r"const int RecycleLeastDust = (\d+);", craft)
    grades = re.search(r"const int Grades = (\d+);", loot)
    top = re.search(r"const int LadderTop = (\d+);", loot)
    if least is None or grades is None or top is None:
        return None
    return int(grades.group(1)), int(top.group(1)), int(least.group(1))


def worn_grade(level, grades, top):
    """CreatureLoot.WornGrade: 0 = Raw .. grades - 1 = A."""
    return min(grades, max(1, -(-max(1, level) * grades // top))) - 1


def main():
    root = os.path.dirname(HERE)
    if "--root" in sys.argv:
        root = sys.argv[sys.argv.index("--root") + 1]
    res = os.path.join(root, "unity", "Assets", "3_Data", "Resources")
    per_level = multiples(root)
    if per_level is None:
        print("could not read the five price multiples out of CreatureMaterials.cs, so nothing can be checked")
        return 1
    rule = gear_rule(root)
    if rule is None:
        print("could not read least recycled dust out of CraftingMath.cs, or the grade ladder out of CreatureLoot.cs, "
              "so nothing can be checked")
        return 1
    grade_count, ladder_top, least_dust = rule
    openings = tier_openings(root)
    if openings is None:
        print("could not read the five tier openings out of CreatureMaterials.cs, so nothing can be checked")
        return 1
    boxes = box_icons(root)
    if boxes is None:
        print("could not read the box icon path out of DerivedItemValues.cs, or a tier's box icon is missing, "
              "so nothing can be checked")
        return 1

    items = {}
    for path in glob.glob(os.path.join(res, "Items", "*.asset")):
        text = open(path, encoding="utf-8").read()
        meta = open(path + ".meta", encoding="utf-8").read() if os.path.exists(path + ".meta") else ""
        items[guid_of(meta)] = (os.path.basename(path)[:-6], text)
    creature_items = {g: v for g, v in items.items() if field(v[1], "materialKind") == CREATURE_KIND}

    faults = []
    creature_rows = {}
    owner = {}
    creatures = 0
    for path in sorted(glob.glob(os.path.join(res, "Monsters", "*.asset"))):
        text = open(path, encoding="utf-8").read()
        name = field(text, "displayName") or os.path.basename(path)
        kind, level = field(text, "kind"), int(field(text, "level") or 0)
        own = guid_of(field(text, "material"))
        if re.search(r"^  rareMaterial:", text, re.M):
            faults.append("%s still names a second material; a creature has one" % name)
        # No creature carries a measured number of its own: what it pays is read off its typed multiplier.
        if re.search(r"^  toughnessPercent:", text, re.M):
            faults.append("%s still carries a saved toughness, which nothing reads" % name)
        if re.search(r"^  gearQuality:", text, re.M):
            faults.append("%s still carries a typed gear grade; the grade it wears comes from its level" % name)
        try:
            multiplier = float(field(text, "multiplier"))
        except ValueError:
            faults.append("%s has no multiplier" % name)
            multiplier = 1.0
        grade = worn_grade(level, grade_count, ladder_top)
        creature_rows[field(text, "id")] = (name, kind, grade, multiplier)
        creatures += 1
        want_tier = max([t for t, at in enumerate(openings) if max(1.0, multiplier) >= at] or [0])
        if not own:
            faults.append("%s has no material of its own" % name)
            continue
        if own not in creature_items:
            faults.append("%s names a material that is not a creature material item" % name)
            continue
        item_name, item = creature_items[own]
        if own in owner:
            faults.append("%s is named by both %s and %s; a material belongs to one creature" % (item_name, owner[own], name))
            continue
        owner[own] = name
        step = int(field(item, "materialStep") or 0)
        if step not in TIER:
            faults.append("%s has no rarity tier (step %d)" % (item_name, step))
            continue
        if step != want_tier + 1:
            faults.append("%s is %s's material but is %s; its multiplier x%g gives %s" % (
                item_name, name, TIER[step], multiplier, TIER[want_tier + 1]))
        sells = per_level[step] * max(1, level)
        # The rule names what a shop PAYS; the item keeps the value that pays exactly that once the Tax is taken
        # (ShopMath.ValueForSellPrice: the price and half of it again, rounded up).
        want = 0 if sells <= 0 else sells + (sells + 1) // 2
        if field(item, "value") != str(want):
            faults.append("%s is worth %s but the rule says %d, to sell for %d (%d x level %d of %s)"
                          % (item_name, field(item, "value"), want, sells, per_level[step], level, name))
        icon = guid_of(field(item, "icon"))
        # Its own painted picture, or -- when it has none -- the box of its own tier.
        if not icon:
            faults.append("%s has no icon" % item_name)
        elif icon in boxes.values() and icon != boxes[step]:
            faults.append("%s is %s but its icon is another tier's box" % (item_name, TIER[step]))
        if field(item, "category") != ETC_TAB:
            faults.append("%s is not in the Etc tab" % item_name)
        if field(item, "weight") != WEIGHT:
            faults.append("%s weighs %s, not %s" % (item_name, field(item, "weight"), WEIGHT))
    spawns = open(os.path.join(res, "WorldSpawns.asset"), encoding="utf-8").read()
    ringed = town_ring(root)
    if ringed is None:
        print("could not read RING out of %s or the towns out of %s, so nothing can be checked" % (SPAWN_BUILDER, WORLD_LAYOUT))
        return 1
    ring, towns = ringed
    rows_part, _, areas_part = spawns.partition("\n  areas:\n")
    middles = [(float(m.group(1)), float(m.group(2))) for m in
               re.finditer(r"^  - center: \{x: ([-\d.e]+), y: [-\d.e]+, z: ([-\d.e]+)\}", areas_part, re.M)]
    for block in re.split(r"^  - (?=map: )", rows_part, flags=re.M)[1:]:
        mm, cr, ar = re.match(r"map: (\S+)", block), re.search(r"^    creature: (\S+)", block, re.M), \
            re.search(r"^    area: (-?\d+)", block, re.M)
        if not (mm and cr and ar) or mm.group(1) != WORLD_MAP:
            continue
        row, area = creature_rows.get(cr.group(1)), int(ar.group(1))
        if row is None or not 0 <= area < len(middles):
            continue
        name, kind, grade, multiplier = row
        if kind != "0" or multiplier < BLIGHTED_FROM:
            continue
        x, z = middles[area]
        town, x0, z0 = min(towns, key=lambda t: (t[1] - x) ** 2 + (t[2] - z) ** 2)
        dist = ((x0 - x) ** 2 + (z0 - z) ** 2) ** 0.5
        if dist < ring:
            faults.append("%s is Blighted (x%g) but lives on world patch %d, %.0f units from %s; nothing Blighted lies "
                          "within %.0f of a town" % (name, multiplier, area, dist, town, ring))
    for guid, (item_name, _) in sorted(creature_items.items(), key=lambda kv: kv[1][0]):
        if guid not in owner:
            faults.append("%s is a creature material that no creature drops" % item_name)

    # EVERY PIECE OF GEAR IS WORTH WHAT A BRAND-NEW PIECE IS: Raw +0 recycles to the least dust and nothing else.
    dust = [v[1] for v in items.values() if field(v[1], "materialKind") == "7"]
    if len(dust) != 1:
        faults.append("there is not exactly one Temper Dust item, so a new piece's worth cannot be worked out")
    else:
        new_piece = least_dust * int(field(dust[0], "value") or 0)
        for item_name, text in sorted(items.values()):
            if field(text, "slot") not in ("", "0") and field(text, "value") != str(new_piece):
                faults.append("%s is gear worth %s but a new piece is worth %d" % (item_name, field(text, "value"), new_piece))

    if faults:
        print("%d creature material fault(s):" % len(faults))
        for f in faults:
            print("  " + f)
        return 1
    print("creature materials: %d creatures, %d materials, every price agrees with the rule, every piece of gear is worth a new piece, and nothing Blighted lives within %.0f of a town" % (creatures, len(owner), ring))
    return 0


if __name__ == "__main__":
    sys.exit(main())
