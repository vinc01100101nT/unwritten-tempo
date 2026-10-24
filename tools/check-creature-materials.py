#!/usr/bin/env python3
"""Every creature's own material is whole: each normal creature and elite has its multiplier written and names exactly
ONE material, whose tier is the one its multiplier gives (CreatureMaterials.TierOfMultiplier: x1 Common, x3 Uncommon,
x10 Rare, x30 Epic, x90 Legendary), no material belongs to two creatures, every material sells for exactly what the
rule says for its creature's level and its tier, its icon is its own picture or else the box drawn in its tier's colour, every piece of gear is worth what a brand-new piece is
(CraftingMath.NewPieceWorth), and the NEWBIE MAPS hold only x1 normal creatures of a level that wears Raw (elites,
which roam every map, excepted). The tier openings and the price multiples are read out of CreatureMaterials.cs, the
level-to-grade ladder out of CreatureLoot.cs and the new-piece worth out of CraftingMath.cs, so the rule has one owner
and this only proves the saved files still agree with it. The editor writes these numbers itself on every save
(DerivedItemValues); nobody types them. Design: docs/3-world/creature-materials.md, docs/3-world/what-a-kill-pays.md.

USAGE:   tools/check-creature-materials.py [--root <repo>]
EXIT:    0 = every creature material agrees with the rule; 1 = something does not (each fault is named).
"""
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TIER = {1: "Common", 2: "Uncommon", 3: "Rare", 4: "Epic", 5: "Legendary"}
CREATURE_KIND = "6"
ETC_TAB = "3"
WEIGHT = "10"
# The maps a brand-new player starts on: the creatures that live there are x1 normals of a level that wears Raw, so a
# newbie map is all easy (decided 2026-10-20). Elites roam every map whatever its level and are not held to it
# (decided 2026-10-18).
NEWBIE_MAPS = ("lst_fild01", "prl_fild01")


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
    grades = "Raw F E D C B A".split()

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
    for m in re.finditer(r"^  - map: (\S+)\n    creature: (\S+)$", spawns, re.M):
        if m.group(1) not in NEWBIE_MAPS:
            continue
        row = creature_rows.get(m.group(2))
        if row is None:
            continue
        name, kind, grade, multiplier = row
        # ELITES ROAM EVERY MAP, whatever its level (decided 2026-10-18), so the rule is for the creatures that live there.
        if kind == "1":
            continue
        if kind != "0" or grade != 0 or multiplier != 1.0:
            faults.append("%s spawns on newbie map %s but is %s x%g wearing %s; a newbie map's own creatures are x1 "
                          "normals wearing Raw" % (name, m.group(1), {"0": "a normal", "1": "an elite", "2": "a shadow"}.get(kind, "?"),
                                                   multiplier, grades[grade]))
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
    print("creature materials: %d creatures, %d materials, every price agrees with the rule, and every piece of gear is worth a new piece" % (creatures, len(owner)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
