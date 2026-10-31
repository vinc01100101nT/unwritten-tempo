#!/usr/bin/env python3
"""Prove tools/check-creature-materials.py can still SEE: plant one fault at a time into a throwaway mirror of the
repo (tools/prove_mirror.py — the real files are never opened for writing), run the checker against the mirror, and
demand it fail AND name the fault. One planted change that IS valid must pass, so the checker cannot pass this by
failing everything. Then demand the real project still passes. Every anchor is read out of the real files at run time.

USAGE:   tools/prove-creature-materials-check.py
EXIT:    0 = every planted fault caught, the valid plant passes and the real project passes; 1 = blind or crying wolf.
"""
import glob
import json
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

    # A x1 normal creature of a level that wears Raw, a x1 normal of a higher level, and an elite (x30, so a rarer
    # tier) — found, never named here.
    raw = graded = elite = None
    for path in sorted(glob.glob(os.path.join(REPO, RES, "Monsters", "*.asset"))):
        rel = os.path.relpath(path, REPO)
        text = read(rel)
        kind, level = line(text, "kind")[-1], int(line(text, "level").split(":")[1])
        # Only originals: a Tainted or Blighted version is kind 0 too, but its tier comes from a higher multiplier.
        if re.search(r"(?m)^  versionOf: \{fileID: [1-9]", text):
            continue
        if kind == "0" and level <= 14 and raw is None:
            raw = (rel, text)
        elif kind == "0" and level >= 15 and graded is None:
            graded = (rel, text)
        elif kind == "1" and elite is None:
            elite = (rel, text)
    if not (raw and graded and elite):
        print(RED + "no x1 normal creature wearing Raw, x1 normal of a higher level or elite to plant into" + OFF)
        return 1

    def item_of(creature_text, key):
        guid = re.search(r"guid: (\w+)", line(creature_text, key)).group(1)
        for meta in glob.glob(os.path.join(REPO, RES, "Items", "*.asset.meta")):
            if ("guid: " + guid) in open(meta, encoding="utf-8").read():
                rel = os.path.relpath(meta[:-5], REPO)
                return rel, read(rel)
        return None, None

    ord_rel, ord_text = item_of(raw[1], "material")
    rare_rel, rare_text = item_of(elite[1], "material")
    spawns_rel = os.path.join(RES, "WorldSpawns.asset")
    spawns = read(spawns_rel)
    # The world patch whose middle lies nearest a town, and one creature kept on it — found, never named here.
    rows_part, _, areas_part = spawns.partition("\n  areas:\n")
    middles = [(float(m.group(1)), float(m.group(2))) for m in
               re.finditer(r"(?m)^  - center: \{x: ([-\d.e]+), y: [-\d.e]+, z: ([-\d.e]+)\}", areas_part)]
    layout = json.load(open(os.path.join(REPO, "data", "world-layout.json"), encoding="utf-8"))
    towns = [(t["x"] - 640, 640 - t["y"]) for t in layout["towns"]]
    near = None
    for m in re.finditer(r"(?m)^  - map: world\r?\n    creature: (\S+)\r?\n(?:    .*\r?\n)*?    area: (\d+)", rows_part):
        a = int(m.group(2))
        if a >= len(middles):
            continue
        d = min(((tx - middles[a][0]) ** 2 + (tz - middles[a][1]) ** 2) ** 0.5 for tx, tz in towns)
        if near is None or d < near[0]:
            near = (d, m.group(1))
    # A Blighted version (x10 copy of a normal) and a Tainted one (below x8) — found, never named here.
    blighted = tainted = None
    for path in sorted(glob.glob(os.path.join(REPO, RES, "Monsters", "*.asset"))):
        text = read(os.path.relpath(path, REPO))
        if line(text, "kind")[-1] != "0" or not re.search(r"(?m)^  versionOf: \{fileID: [1-9]", text):
            continue
        mult = float(line(text, "multiplier").split(":")[1])
        cid = line(text, "id").split(":", 1)[1].strip()
        if mult >= 8 and blighted is None:
            blighted = cid
        elif mult < 8 and tainted is None:
            tainted = cid
    if near is None or blighted is None or tainted is None:
        print(RED + "no world patch near a town, Blighted creature or Tainted creature to plant" + OFF)
        return 1
    near_anchor = "  - map: world\n    creature: " + near[1] + "\n"

    bad = 0
    bad += case("a material given another description passes", ord_rel, line(ord_text, "description"),
                "  description: Something else entirely.", None, must_pass=True)
    bad += case("a material whose saved price is not the rule's", ord_rel, line(ord_text, "value"), "  value: 7",
                "but the rule says")
    bad += case("a creature with its material taken away", raw[0], line(raw[1], "material"), "  material: {fileID: 0}",
                "has no material of its own")
    bad += case("an original's Common material given a Rare tier", ord_rel, line(ord_text, "materialStep"), "  materialStep: 3",
                "gives Common")
    bad += case("an elite's material made Common", rare_rel, line(rare_text, "materialStep"), "  materialStep: 1",
                "but is Common; its multiplier")
    bad += case("a material with no tier at all", rare_rel, line(rare_text, "materialStep"), "  materialStep: 0",
                "has no rarity tier")
    bad += case("two creatures naming one material", elite[0], line(elite[1], "material"), line(raw[1], "material"),
                "a material belongs to one creature")
    bad += case("a creature naming a second material again", raw[0], line(raw[1], "material"),
                line(raw[1], "material") + "\n  rareMaterial: {fileID: 0}", "still names a second material")
    bad += case("a creature's multiplier raised with its material left behind", raw[0], line(raw[1], "multiplier"),
                "  multiplier: 3", "its multiplier x3 gives Uncommon")
    bad += case("a creature with a typed gear grade again", raw[0], line(raw[1], "multiplier"),
                line(raw[1], "multiplier") + "\n  gearQuality: 2", "still carries a typed gear grade")
    bad += case("a Tainted creature on the patch nearest a town passes", spawns_rel, near_anchor,
                "  - map: world\n    creature: " + tainted + "\n", None, must_pass=True)
    bad += case("a Blighted creature on the patch nearest a town", spawns_rel, near_anchor,
                "  - map: world\n    creature: " + blighted + "\n", "nothing Blighted lies within")
    geared = None
    for path in sorted(glob.glob(os.path.join(REPO, RES, "Monsters", "*.asset"))):
        rel = os.path.relpath(path, REPO)
        text = read(rel)
        # A shadow: it has gear, and its own material like every creature.
        if line(text, "kind")[-1] == "2":
            geared = (rel, text)
            break
    if geared is None:
        print(RED + "no shadow to plant other folds into, so that rule is not proved" + OFF)
        return 1
    level_now = int(line(graded[1], "level").split(":")[1])
    bad += case("a creature whose level changed and whose prices did not follow", graded[0], line(graded[1], "level"),
                "  level: %d" % (level_now + 1), "but the rule says")
    bad += case("a creature with its multiplier removed", graded[0], line(graded[1], "multiplier"),
                "  multiplierGone: 1", "has no multiplier")
    bad += case("a creature carrying a saved toughness again", geared[0], line(geared[1], "level"),
                line(geared[1], "level") + "\n  toughnessPercent: 235", "still carries a saved toughness")
    bad += case("a shadow with its material removed", geared[0], line(geared[1], "material"),
                "  material: {fileID: 0}", "has no material of its own")
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
    bad += case("a material with no icon", ord_rel, line(ord_text, "icon"), "  icon: {fileID: 0}", "has no icon")
    rare_box = re.search(r"guid: (\w+)", read("unity/Assets/1_Authored/Art/Icons/Items/Box Rare.png.meta")).group(1)
    bad += case("a Common material showing the Rare box", ord_rel, line(ord_text, "icon"),
                "  icon: {fileID: 21300000, guid: %s, type: 3}" % rare_box, "its icon is another tier's box")

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
