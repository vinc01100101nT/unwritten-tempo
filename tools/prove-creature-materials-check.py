#!/usr/bin/env python3
"""Prove tools/check-creature-materials.py can still SEE: plant one fault at a time into a throwaway mirror of the
repo (tools/prove_mirror.py — the real files are never opened for writing), run the checker against the mirror, and
demand it fail AND name the fault. One planted change that IS valid must pass, so the checker cannot pass this by
failing everything. Then demand the real project still passes. Every anchor is read out of the real files at run time.

USAGE:   tools/prove-creature-materials-check.py
EXIT:    0 = every planted fault caught, the valid plant passes and the real project passes; 1 = blind or crying wolf.
"""
import glob
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from prove_mirror import mirror_with_change, discard  # noqa: E402

CHECK = os.path.join(HERE, "check-creature-materials.py")
RES = os.path.join("unity", "Assets", "3_Data", "Resources")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=None):
    args = [sys.executable, CHECK] + (["--root", root] if root else [])
    p = subprocess.run(args, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def case(description, rel, old, new, must_say, must_pass=False):
    root, hits = mirror_with_change(REPO, rel, old, new)
    if root is None or not hits:
        if root is not None:
            discard(root)
        print(RED + " WRONG" + OFF + " %s - the anchor was not found in %s, so this proves nothing" % (description, rel))
        return 1
    try:
        code, out = run(root)
    finally:
        discard(root)
    if must_pass:
        if code == 0:
            print(GRN + "  ok  " + OFF + description)
            return 0
        print(RED + " WRONG" + OFF + " %s (exit %d, expected to pass)" % (description, code))
    else:
        if code == 1 and must_say in out:
            print(GRN + "  ok  " + OFF + description)
            return 0
        print(RED + " WRONG" + OFF + " %s (exit %d, expected to say %r)" % (description, code, must_say))
    print("\n".join("         " + l for l in out.splitlines()[:6]))
    return 1


def read(rel):
    with open(os.path.join(REPO, rel), encoding="utf-8", newline="") as fh:
        return fh.read()


def line(text, key):
    return re.search(r"(?m)^  %s: .*$" % re.escape(key), text).group(0).rstrip("\r")


def main():
    code, out = run()
    if code != 0:
        print(RED + "the real project does not pass the check, so no planted fault can be told apart:" + OFF)
        print(out)
        return 1

    # A normal creature with both materials, another normal creature, and an elite — found, never named here.
    two = other = elite = None
    for path in sorted(glob.glob(os.path.join(REPO, RES, "Monsters", "*.asset"))):
        rel = os.path.relpath(path, REPO)
        text = read(rel)
        kind = line(text, "kind")[-1]
        has_rare = "guid:" in line(text, "rareMaterial")
        if kind == "0" and has_rare and two is None:
            two = (rel, text)
        elif kind == "0" and other is None:
            other = (rel, text)
        elif kind == "1" and has_rare and elite is None:
            elite = (rel, text)
    if not (two and other and elite):
        print(RED + "no normal creature with two materials, second normal creature or elite to plant into" + OFF)
        return 1

    def item_of(creature_text, key):
        guid = re.search(r"guid: (\w+)", line(creature_text, key)).group(1)
        for meta in glob.glob(os.path.join(REPO, RES, "Items", "*.asset.meta")):
            if ("guid: " + guid) in open(meta, encoding="utf-8").read():
                rel = os.path.relpath(meta[:-5], REPO)
                return rel, read(rel)
        return None, None

    ord_rel, ord_text = item_of(two[1], "material")
    rare_rel, rare_text = item_of(two[1], "rareMaterial")

    bad = 0
    bad += case("a material given another description passes", ord_rel, line(ord_text, "description"),
                "  description: Something else entirely.", None, must_pass=True)
    bad += case("a material whose saved price is not the rule's", ord_rel, line(ord_text, "zeny"), "  zeny: 7",
                "but the rule says")
    bad += case("a creature with its material taken away", two[0], line(two[1], "material"), "  material: {fileID: 0}",
                "has no material of its own")
    bad += case("an ordinary material given a rare tier", ord_rel, line(ord_text, "materialStep"), "  materialStep: 3",
                "ordinary material but is Rare")
    bad += case("a rare material made Common", rare_rel, line(rare_text, "materialStep"), "  materialStep: 1",
                "rare material but is Common")
    bad += case("a material with no tier at all", rare_rel, line(rare_text, "materialStep"), "  materialStep: 0",
                "has no rarity tier")
    bad += case("two creatures naming one material", other[0], line(other[1], "material"), line(two[1], "material"),
                "a material belongs to one creature")
    bad += case("an elite with no rare material", elite[0], line(elite[1], "rareMaterial"), "  rareMaterial: {fileID: 0}",
                "is an elite and has no rare material")
    geared = None
    for path in sorted(glob.glob(os.path.join(REPO, RES, "Monsters", "*.asset"))):
        rel = os.path.relpath(path, REPO)
        text = read(rel)
        # A shadow: it has gear and no material of its own, so its folds can move without a price to follow.
        if line(text, "kind")[-1] == "2":
            geared = (rel, text)
            break
    if geared is None:
        print(RED + "no shadow to plant other folds into, so that rule is not proved" + OFF)
        return 1
    folds_now = int(line(two[1], "gearFolds").split(":")[1])
    grade_now = int(line(two[1], "gearQuality").split(":")[1])
    bad += case("a creature whose folds changed and whose prices did not follow", two[0], line(two[1], "gearFolds"),
                "  gearFolds: %d" % (folds_now - 1 if folds_now > 0 else 1), "but the rule says")
    bad += case("a creature whose grade changed and whose prices did not follow", two[0], line(two[1], "gearQuality"),
                "  gearQuality: %d" % (grade_now + 1 if grade_now < 6 else 5), "but the rule says")
    bad += case("a creature with its fold count removed", two[0], line(two[1], "gearFolds"),
                "  gearFoldsGone: 0", "has no gear grade or no fold count")
    bad += case("a creature folded past its grade's cap", two[0], line(two[1], "gearFolds"),
                "  gearFolds: 99", "past its grade's cap")
    bad += case("a creature carrying a saved toughness again", geared[0], line(geared[1], "gearFolds"),
                line(geared[1], "gearFolds") + "\n  toughnessPercent: 235", "still carries a saved toughness")
    shadow_folds = int(line(geared[1], "gearFolds").split(":")[1])
    bad += case("a creature with no material given other folds passes", geared[0], line(geared[1], "gearFolds"),
                "  gearFolds: %d" % (shadow_folds - 1 if shadow_folds > 0 else 1), None, must_pass=True)
    bad += case("a material moved out of the Etc tab", ord_rel, line(ord_text, "category"), "  category: 2",
                "is not in the Etc tab")
    bad += case("a material with another weight", ord_rel, line(ord_text, "weight"), "  weight: 1", "weighs 1")

    code_after, out_after = run()
    if code_after != 0 or out_after != out:
        print(RED + " WRONG" + OFF + " the real project's own answer changed while proving, so this proves nothing")
        bad += 1
    print()
    if bad:
        print(RED + "%d case(s) wrong - the creature material check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the creature material check sees every planted fault and the real project passes." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
