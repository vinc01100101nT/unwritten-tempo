#!/usr/bin/env python3
"""Prove tools/check-creature-roster.py still SEES a broken roster.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This copies the creature,
item, spawn, recipe and townsfolk data into a scratch tree, checks the copy is clean, then plants one fault at a time
and demands the checker name it.

USAGE:   tools/prove-creature-roster-check.py
EXIT:    0 = every planted fault caught and the clean copy quiet, 1 = blind or crying wolf.
"""
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CHECK = os.path.join(HERE, "check-creature-roster.py")
RES = os.path.join("unity", "Assets", "3_Data", "Resources")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def copy_tree(dst):
    for sub in ("Items", "Monsters"):
        os.makedirs(os.path.join(dst, RES, sub), exist_ok=True)
        for f in glob.glob(os.path.join(REPO, RES, sub, "*.asset*")):
            shutil.copy(f, os.path.join(dst, RES, sub))
    for name in ("WorldSpawns.asset", "SummonRecipes.asset", "WorldTownsfolk.asset"):
        shutil.copy(os.path.join(REPO, RES, name), os.path.join(dst, RES, name))


def run(root):
    r = subprocess.run([sys.executable, CHECK, "--root", root], capture_output=True, text=True, encoding="utf-8")
    return r.returncode, r.stdout


def edit(root, rel, old, new, count=1):
    p = os.path.join(root, RES, rel)
    s = open(p, encoding="utf-8").read()
    if old not in s:
        return False
    open(p, "w", encoding="utf-8").write(s.replace(old, new, count))
    return True


def elite_area_plant():
    """The first Oculord row's area line, and the same line moved onto the first Gloop King row's area."""
    text = open(os.path.join(REPO, RES, "WorldSpawns.asset"), encoding="utf-8").read()
    def first(creature):
        m = re.search(r"(?ms)^    creature: " + re.escape(creature) + r"\n.*?^    area: (\d+)\n", text)
        return m.group(0) if m else "", m.group(1) if m else ""
    row, _ = first("mob.oculord"); _, gloop = first("mob.gloop-king")
    return row, re.sub(r"area: \d+\n$", "area: " + gloop + "\n", row)


def main():
    bad = 0
    ELITE_AREA = elite_area_plant()
    cases = [
        ("a creature with no multiplier", "Monsters/Plopling.asset", "  multiplier: 1\n", "", "plopling"),
        ("a multiplier below 1", "Monsters/Shadow_Riven.asset", "multiplier: 90", "multiplier: 0.5", "riven"),
        ("a C elite with a third spawn row", "WorldSpawns.asset", "  areas:\n",
         "  - map: world\n    creature: mob.oculord\n    population: 1\n    area: 0\n  areas:\n", "oculord"),
        ("two C elites on one area", "WorldSpawns.asset", ELITE_AREA[0], ELITE_AREA[1], "holds 2 c elites"),
        ("a summon-only boss with no recipe", "SummonRecipes.asset", "bossId: mob.blood-ronin", "bossId: mob.nobody", "blood-ronin"),
        ("a shadow given a spawn row", "WorldSpawns.asset", "  areas:\n",
         "  - map: world\n    creature: mob.shadow.riven\n    population: 1\n    area: 0\n  areas:\n", "summon-only but has a spawn row"),
        ("an original on a dungeon floor", "WorldSpawns.asset", "  areas:\n",
         "  - map: test_dun01\n    creature: mob.plopling\n    population: 3\n    area: -1\n  areas:\n", "holds the original"),
    ]
    with tempfile.TemporaryDirectory() as clean:
        copy_tree(clean)
        code, out = run(clean)
        if code != 0:
            print(f"{RED}WRONG{OFF} the clean copy is not clean:\n{out}")
            return 1
        print(f"{GRN}quiet{OFF}   the clean copy passes")
    for name, rel, old, new, expect in cases:
        with tempfile.TemporaryDirectory() as root:
            copy_tree(root)
            if not edit(root, rel, old, new):
                print(f"{RED}WRONG{OFF} {name}: the anchor is not in {rel}, so this proves nothing")
                bad += 1
                continue
            code, out = run(root)
            if code == 1 and expect in out.lower():
                print(f"{GRN}caught{OFF}  {name}")
            else:
                print(f"{RED}WRONG{OFF} {name}: the checker said\n{out}")
                bad += 1
    if bad:
        print(f"\n{RED}{bad} case(s) wrong - the check's silence proves nothing.{OFF}")
        return 1
    print(f"{GRN}the roster check sees every planted fault and is quiet on the real data.{OFF}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
