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


def main():
    bad = 0
    cases = [
        ("a creature with no multiplier", "Monsters/Plopling.asset", "  multiplier: 1\n", "", "plopling"),
        ("a multiplier below 1", "Monsters/Shadow_Riven.asset", "multiplier: 90", "multiplier: 0.5", "riven"),
        ("a C elite with a second spawn row", "WorldSpawns.asset", "    creature: mob.oculord\n",
         "    creature: mob.oculord\n    biome: 0\n  - map: olk_fild02\n    creature: mob.oculord\n", "oculord"),
        ("a summon-only boss with no recipe", "SummonRecipes.asset", "bossId: mob.blood-ronin", "bossId: mob.nobody", "blood-ronin"),
        ("an original back on a dungeon floor", "WorldSpawns.asset", "creature: mob.tainted-grumbear", "creature: mob.grumbear",
         "holds the original"),
        ("a Blighted creature in a field", "WorldSpawns.asset", "map: trn_fild01\n    creature: mob.tainted-chomper",
         "map: trn_fild01\n    creature: mob.blighted-chomper", "a field holds nothing of x10"),
        ("a Tainted creature on a deepest floor", "WorldSpawns.asset", "creature: mob.blighted-willo", "creature: mob.tainted-willo",
         "deepest floor's creatures are x10"),
        ("a x2 creature on a newbie map", "WorldSpawns.asset", "map: lst_fild01\n    creature: mob.plopling",
         "map: lst_fild01\n    creature: mob.rascal", "newbie map"),
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
