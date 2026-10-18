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

    # A Raw normal creature, a normal creature above Raw, and an elite — found, never named here.
    raw = graded = elite = None
    for path in sorted(glob.glob(os.path.join(REPO, RES, "Monsters", "*.asset"))):
        rel = os.path.relpath(path, REPO)
        text = read(rel)
        kind, grade = line(text, "kind")[-1], line(text, "gearQuality")[-1]
        if kind == "0" and grade == "0" and raw is None:
            raw = (rel, text)
        elif kind == "0" and grade != "0" and graded is None:
            graded = (rel, text)
        elif kind == "1" and elite is None:
            elite = (rel, text)
    if not (raw and graded and elite):
        print(RED + "no Raw normal creature, graded normal creature or elite to plant into" + OFF)
        return 1

    def item_of(creature_text, key):
        guid = re.search(r"guid: (\w+)", line(creature_text, key)).group(1)
        for meta in glob.glob(os.path.join(REPO, RES, "Items", "*.asset.meta")):
            if ("guid: " + guid) in open(meta, encoding="utf-8").read():
                rel = os.path.relpath(meta[:-5], REPO)
                return rel, read(rel)
        return None, None

    ord_rel, ord_text = item_of(raw[1], "material")
    rare_rel, rare_text = item_of(graded[1], "material")
    spawns_rel = os.path.join(RES, "WorldSpawns.asset")
    spawns = read(spawns_rel)
    newbie = re.search(r"(?m)^  - map: lst_fild01\r?\n    creature: (\S+)", spawns)
    elite_id = line(elite[1], "id").split(":", 1)[1].strip()
    if newbie is None:
        print(RED + "no spawn on the newbie map lst_fild01 to plant into" + OFF)
        return 1

    bad = 0
    bad += case("a material given another description passes", ord_rel, line(ord_text, "description"),
                "  description: Something else entirely.", None, must_pass=True)
    bad += case("a material whose saved price is not the rule's", ord_rel, line(ord_text, "value"), "  value: 7",
                "but the rule says")
    bad += case("a creature with its material taken away", raw[0], line(raw[1], "material"), "  material: {fileID: 0}",
                "has no material of its own")
    bad += case("a Raw creature's material given a Rare tier", ord_rel, line(ord_text, "materialStep"), "  materialStep: 3",
                "its grade Raw gives Common")
    bad += case("a graded creature's material made Common", rare_rel, line(rare_text, "materialStep"), "  materialStep: 1",
                "but is Common; its grade")
    bad += case("a material with no tier at all", rare_rel, line(rare_text, "materialStep"), "  materialStep: 0",
                "has no rarity tier")
    bad += case("two creatures naming one material", graded[0], line(graded[1], "material"), line(raw[1], "material"),
                "a material belongs to one creature")
    bad += case("a creature naming a second material again", raw[0], line(raw[1], "material"),
                line(raw[1], "material") + "\n  rareMaterial: {fileID: 0}", "still names a second material")
    bad += case("an elite below grade D", elite[0], line(elite[1], "gearQuality"), "  gearQuality: 2",
                "never wears")
    bad += case("a normal creature at grade B", graded[0], line(graded[1], "gearQuality"), "  gearQuality: 5",
                "never wears")
    bad += case("an elite spawning on a newbie map passes (elites roam every map)", spawns_rel, newbie.group(0),
                "  - map: lst_fild01\n    creature: " + elite_id, None, must_pass=True)
    graded_id = line(graded[1], "id").split(":", 1)[1].strip()
    bad += case("a graded normal creature spawning on a newbie map", spawns_rel, newbie.group(0),
                "  - map: lst_fild01\n    creature: " + graded_id, "a newbie map's own creatures are Raw +0 normals")
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
    level_now = int(line(graded[1], "level").split(":")[1])
    bad += case("a creature whose level changed and whose prices did not follow", graded[0], line(graded[1], "level"),
                "  level: %d" % (level_now + 1), "but the rule says")
    bad += case("a creature with its gear grade removed", graded[0], line(graded[1], "gearQuality"),
                "  gearQualityGone: 0", "has no gear grade")
    bad += case("a creature carrying a saved toughness again", geared[0], line(geared[1], "level"),
                line(geared[1], "level") + "\n  toughnessPercent: 235", "still carries a saved toughness")
    shadow_level = int(line(geared[1], "level").split(":")[1])
    bad += case("a creature with no material given another level passes", geared[0], line(geared[1], "level"),
                "  level: %d" % (shadow_level + 1), None, must_pass=True)
    gear_rel = None
    for path in sorted(glob.glob(os.path.join(REPO, RES, "Items", "*.asset"))):
        rel = os.path.relpath(path, REPO)
        if re.search(r"(?m)^  slot: [1-9]", read(rel)):
            gear_rel = rel
            break
    if gear_rel is None:
        print(RED + "no piece of gear to plant a typed value into, so that rule is not proved" + OFF)
        return 1
    bad += case("a piece of gear with a typed value", gear_rel, line(read(gear_rel), "value"), "  value: 8",
                "but a new piece is worth")
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
