#!/usr/bin/env python3
"""Every creature's own material is whole, and its grade is one its kind may wear: each normal creature and elite
names exactly ONE material, whose tier is the one its gear grade gives (CreatureMaterials.TierOfGrade: a normal Raw..C
gives Common..Legendary, an elite only D/C/B, giving Rare/Epic/Legendary), no material belongs to two creatures, every
material sells for exactly what the rule says for its creature's level, its tier and the grade and folds of the
creature's gear (folds fixed by its kind, CreatureLoot.FileFolds), every piece of gear is worth what a brand-new piece
is (CraftingMath.NewPieceWorth), and the NEWBIE MAPS hold only Raw +0 normal creatures (elites, which roam every map,
excepted). The tier tables and the price
multiples are read out of CreatureMaterials.cs, the two gear percents out of CreaturePay.cs, the folds out of
CreatureLoot.cs and the new-piece worth out of CraftingMath.cs, so the rule has one owner and this only proves the saved
files still agree with it. The editor writes these numbers itself on every save (DerivedItemValues); nobody types them.
Design: docs/3-world/creature-materials.md, docs/3-world/what-a-kill-pays.md.

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
# The maps a brand-new player starts on: the creatures that live there are Raw +0 normals, so a level 1 player in the
# starter armour survives at least 5 blows from them (docs/3-world/creature-gear.md, decided 2026-10-16). Elites roam
# every map whatever its level and are not held to it (decided 2026-10-18).
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


def tier_tables(root):
    """(normal, elite): the material tier each grade gives, -1 where that kind may not wear it, read from the game."""
    src = open(os.path.join(root, "unity", "Assets", "Scripts", "CreatureMaterials.cs"), encoding="utf-8").read()
    out = []
    for name in ("NormalTierByGrade", "EliteTierByGrade"):
        m = re.search(name + r"\s*=\s*\{([^}]*)\}", src)
        if m is None:
            return None
        row = [int(v) for v in m.group(1).split(",")]
        if len(row) != 7:
            return None
        out.append(row)
    return tuple(out)


def gear_rule(root):
    """(percent per grade, percent per fold, fold cap per grade, folds by kind "0"/"1"/"2", least recycled dust), read
    from the functions that decide them."""
    scripts = os.path.join(root, "unity", "Assets", "Scripts")
    pay = open(os.path.join(scripts, "CreaturePay.cs"), encoding="utf-8").read()
    craft = open(os.path.join(scripts, "CraftingMath.cs"), encoding="utf-8").read()
    loot = open(os.path.join(scripts, "CreatureLoot.cs"), encoding="utf-8").read()
    grade = re.search(r"const int PercentPerGrade = (\d+);", pay)
    fold = re.search(r"const int PercentPerFold = (\d+);", pay)
    caps = re.search(r"int\[\] FoldCaps\s*=\s*\{([^}]*)\}", craft)
    least = re.search(r"const int RecycleLeastDust = (\d+);", craft)
    kinds = re.search(r"FileFolds\(CreatureKind kind\) =>\s*kind == CreatureKind\.Shadow \? (\d+) : "
                      r"kind == CreatureKind\.Elite \? (\d+) : (\d+);", loot)
    if grade is None or fold is None or caps is None or least is None or kinds is None:
        return None
    caps = [int(c) for c in caps.group(1).split(",")]
    by_kind = {"2": int(kinds.group(1)), "1": int(kinds.group(2)), "0": int(kinds.group(3))}
    return (int(grade.group(1)), int(fold.group(1)), caps, by_kind, int(least.group(1))) if len(caps) == 7 else None


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
        print("could not read the gear percents out of CreaturePay.cs, the fold caps or least recycled dust out of "
              "CraftingMath.cs, or the folds by kind out of CreatureLoot.cs, so nothing can be checked")
        return 1
    per_grade, per_fold, fold_caps, folds_by_kind, least_dust = rule
    tables = tier_tables(root)
    if tables is None:
        print("could not read the two grade-to-tier tables out of CreatureMaterials.cs, so nothing can be checked")
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
        # No creature carries a measured number of its own: what its gear pays is read off its grade and folds.
        if re.search(r"^  toughnessPercent:", text, re.M):
            faults.append("%s still carries a saved toughness, which nothing reads" % name)
        grade = field(text, "gearQuality")
        if not re.fullmatch(r"[0-6]", grade):
            faults.append("%s has no gear grade" % name)
            grade = "0"
        grade, folds = int(grade), folds_by_kind.get(kind, 0)
        grade_percent = 100 + per_grade * grade
        fold_percent = 100 + per_fold * min(folds, fold_caps[grade])
        creature_rows[field(text, "id")] = (name, kind, grade, folds)
        if kind == "2":
            if own:
                faults.append("%s is a shadow and names a material of its own; shadows have none" % name)
            continue
        creatures += 1
        want_tier = (tables[0] if kind == "0" else tables[1])[grade]
        if want_tier < 0:
            faults.append("%s is %s and wears grade %s, which %s never wears" % (
                name, "an elite" if kind == "1" else "a normal creature", grades[grade],
                "an elite (never below D, never A)" if kind == "1" else "a normal creature (never B or A)"))
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
        if want_tier >= 0 and step != want_tier + 1:
            faults.append("%s is %s's material but is %s; its grade %s gives %s" % (
                item_name, name, TIER[step], grades[grade], TIER[want_tier + 1]))
        # The game's own rounding: whole numbers, half up (CreaturePay.Scaled).
        sells = (per_level[step] * max(1, level) * grade_percent * fold_percent + 5000) // 10000
        # The rule names what a shop PAYS; the item keeps the value that pays exactly that once the Tax is taken
        # (ShopMath.ValueForSellPrice: the price and half of it again, rounded up).
        want = 0 if sells <= 0 else sells + (sells + 1) // 2
        if field(item, "value") != str(want):
            faults.append("%s is worth %s but the rule says %d, to sell for %d (%d x level %d of %s, x %d%% for its grade, x %d%% for its folds)"
                          % (item_name, field(item, "value"), want, sells, per_level[step], level, name, grade_percent, fold_percent))
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
        name, kind, grade, folds = row
        # ELITES ROAM EVERY MAP, whatever its level (decided 2026-10-18), so the rule is for the creatures that live there.
        if kind == "1":
            continue
        if kind != "0" or grade != 0 or folds != 0:
            faults.append("%s spawns on newbie map %s but is %s %s +%d; a newbie map's own creatures are Raw +0 normals"
                          % (name, m.group(1), {"0": "a normal", "1": "an elite", "2": "a shadow"}.get(kind, "?"),
                             grades[grade], folds))
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
