#!/usr/bin/env python3
"""Every creature's own material is whole: each normal creature and elite names one ordinary material, a rare one
is a real rarer tier, no material belongs to two creatures, and every material sells for exactly what the rule says
for its creature's level, its tier and the grade and folds of the creature's gear. The price multiples are read out
of CreatureMaterials.cs, the two gear percents out of CreaturePay.cs and the fold caps out of CraftingMath.cs, so the
rule has one owner and this only proves the saved files still agree with it.
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
    body = re.search(r"static int SellValue\(.*?\n    \}", src, re.S)
    gold = re.search(r"const int GoldPerLevel = (\d+);", src)
    if body is None or gold is None:
        return None
    out = {1: 2 * int(gold.group(1))}
    for name, value in re.findall(r"case Rarity\.(\w+):\s*perLevel = (\d+);", body.group(0)):
        for step, tier in TIER.items():
            if tier == name:
                out[step] = int(value)
    return out if len(out) == 5 else None


def gear_rule(root):
    """(percent per grade, percent per fold, fold cap per grade), read from the functions that decide them."""
    scripts = os.path.join(root, "unity", "Assets", "Scripts")
    pay = open(os.path.join(scripts, "CreaturePay.cs"), encoding="utf-8").read()
    craft = open(os.path.join(scripts, "CraftingMath.cs"), encoding="utf-8").read()
    grade = re.search(r"const int PercentPerGrade = (\d+);", pay)
    fold = re.search(r"const int PercentPerFold = (\d+);", pay)
    caps = re.search(r"int\[\] FoldCaps\s*=\s*\{([^}]*)\}", craft)
    if grade is None or fold is None or caps is None:
        return None
    caps = [int(c) for c in caps.group(1).split(",")]
    return (int(grade.group(1)), int(fold.group(1)), caps) if len(caps) == 7 else None


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
        print("could not read the gear percents out of CreaturePay.cs or the fold caps out of CraftingMath.cs, "
              "so nothing can be checked")
        return 1
    per_grade, per_fold, fold_caps = rule

    items = {}
    for path in glob.glob(os.path.join(res, "Items", "*.asset")):
        text = open(path, encoding="utf-8").read()
        meta = open(path + ".meta", encoding="utf-8").read() if os.path.exists(path + ".meta") else ""
        items[guid_of(meta)] = (os.path.basename(path)[:-6], text)
    creature_items = {g: v for g, v in items.items() if field(v[1], "materialKind") == CREATURE_KIND}

    faults = []
    owner = {}
    creatures = 0
    for path in sorted(glob.glob(os.path.join(res, "Monsters", "*.asset"))):
        text = open(path, encoding="utf-8").read()
        name = field(text, "displayName") or os.path.basename(path)
        kind, level = field(text, "kind"), int(field(text, "level") or 0)
        ordinary, rare = guid_of(field(text, "material")), guid_of(field(text, "rareMaterial"))
        # No creature carries a measured number of its own: what its gear pays is read off its grade and folds.
        if re.search(r"^  toughnessPercent:", text, re.M):
            faults.append("%s still carries a saved toughness, which nothing reads" % name)
        grade, folds = field(text, "gearQuality"), field(text, "gearFolds")
        if not re.fullmatch(r"[0-6]", grade) or not re.fullmatch(r"\d+", folds):
            faults.append("%s has no gear grade or no fold count" % name)
            grade, folds = "0", "0"
        grade, folds = int(grade), int(folds)
        if folds > fold_caps[grade]:
            faults.append("%s has %d folds, past its grade's cap of %d" % (name, folds, fold_caps[grade]))
        grade_percent = 100 + per_grade * grade
        fold_percent = 100 + per_fold * min(folds, fold_caps[grade])
        if kind == "2":
            if ordinary or rare:
                faults.append("%s is a shadow and names a material of its own; shadows have none" % name)
            continue
        creatures += 1
        if not ordinary:
            faults.append("%s has no material of its own" % name)
        if kind == "1" and not rare:
            faults.append("%s is an elite and has no rare material" % name)
        for guid, is_rare in ((ordinary, False), (rare, True)):
            if not guid:
                continue
            if guid not in creature_items:
                faults.append("%s names a material that is not a creature material item" % name)
                continue
            item_name, item = creature_items[guid]
            if guid in owner:
                faults.append("%s is named by both %s and %s; a material belongs to one creature"
                              % (item_name, owner[guid], name))
                continue
            owner[guid] = name
            step = int(field(item, "materialStep") or 0)
            if step not in TIER:
                faults.append("%s has no rarity tier (step %d)" % (item_name, step))
                continue
            if not is_rare and step != 1:
                faults.append("%s is %s's ordinary material but is %s, not Common" % (item_name, name, TIER[step]))
            if is_rare and step == 1:
                faults.append("%s is %s's rare material but is Common" % (item_name, name))
            if is_rare and kind == "1" and step == 2:
                faults.append("%s is an elite's rare material but is Uncommon, which an elite never drops" % item_name)
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
    for guid, (item_name, _) in sorted(creature_items.items(), key=lambda kv: kv[1][0]):
        if guid not in owner:
            faults.append("%s is a creature material that no creature drops" % item_name)

    if faults:
        print("%d creature material fault(s):" % len(faults))
        for f in faults:
            print("  " + f)
        return 1
    print("creature materials: %d creatures, %d materials, every price agrees with the rule" % (creatures, len(owner)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
