#!/usr/bin/env python3
"""Prove the game-math check still CATCHES a moved number.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a healthy project prints. Both
are silence and only one is good news. So the checker gets checked: this changes one real number in
the real game code, confirms the check fails AND names the file it is about, then puts the code back
and confirms it passes again.

Naming the file matters as much as failing. The end-of-turn guard keeps only fault lines naming a
file this session edited, so a failure that names only itself is filtered into silence and reported
as good news — which is worse than no check at all.

USAGE:   tools/prove-game-math-check.py
EXIT:    0 = the check sees a moved number, 1 = it is blind, or it cries wolf.
"""
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CHECK = os.path.join(HERE, "check-game-math.sh")
SCRIPTS = os.path.join(REPO, "unity", "Assets", "Scripts")

RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run():
    done = subprocess.run(["sh", CHECK], cwd=REPO, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


def case(description, filename, old, new, must_name):
    """Change one number in a real game file, confirm the check catches it, then put it back."""
    path = os.path.join(SCRIPTS, filename)
    if not os.path.isfile(path):
        print(RED + " WRONG" + OFF + " " + description + " — no such file: " + filename)
        return 1
    backup = path + ".prove-backup"
    shutil.copy2(path, backup)
    try:
        with open(path, encoding="utf-8") as f:
            text = f.read()
        if text.count(old) != 1:
            print(RED + " WRONG" + OFF + " " + description +
                  " — the anchor is not in " + filename + " exactly once, so this proves nothing")
            return 1
        with open(path, "w", encoding="utf-8") as f:
            f.write(text.replace(old, new, 1))
        code, out = run()
    finally:
        shutil.move(backup, path)

    after_code, after_out = run()

    bad = 0
    if code != 1:
        print(RED + " WRONG" + OFF + " " + description +
              " — with the number changed it exited %d, not 1" % code)
        bad = 1
    elif must_name not in out:
        print(RED + " WRONG" + OFF + " " + description +
              " — it failed but never named " + must_name + ", so the guard would filter it into silence")
        bad = 1
    elif after_code != 0:
        print(RED + " WRONG" + OFF + " " + description +
              " — it did NOT go quiet again after the code was put back")
        bad = 1
    else:
        print(GRN + "  ok  " + OFF + description)
    return bad


def main():
    if not os.path.isfile(CHECK):
        print(RED + "the check is missing at " + CHECK + OFF)
        return 1
    bad = 0
    # The per-level experience cost, moved by one. The running total is worked out a second way, so the
    # two stop agreeing — which is the whole reason both are pinned.
    bad += case("a moved experience cost is caught",
                "UnitStatMath.cs",
                "public static int XpToNextFrom(int level) => 5 + 5 * Mathf.Max(1, level);",
                "public static int XpToNextFrom(int level) => 6 + 5 * Mathf.Max(1, level);",
                "UnitStatMath.cs")
    # A different file, so one blind spot cannot pass the whole prover. The accuracy ceiling is a
    # promise; raising it means a target can never dodge, and nothing on screen would say so.
    bad += case("a raised accuracy ceiling is caught",
                "Combat.cs",
                "public const float MaxHitChance = 0.95f;",
                "public const float MaxHitChance = 1.05f;",
                "Combat.cs")
    print()
    if bad:
        print(RED + "%d case(s) wrong - the check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the check sees a moved number, names its file, and goes quiet once it is put back." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
