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
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CHECK = os.path.join(HERE, "check-game-math.sh")
SCRIPTS_REL = os.path.join("unity", "Assets", "Scripts")

sys.path.insert(0, HERE)
from prove_mirror import mirror_with_change, discard

RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=REPO):
    """Run the check inside `root` — the real repo, or a throwaway mirror of it."""
    done = subprocess.run(["sh", os.path.join(root, "tools", "check-game-math.sh")],
                          cwd=root, capture_output=True, text=True)
    return done.returncode, done.stdout + done.stderr


def case(description, filename, old, new, must_name):
    """Confirm the check catches one moved number.

    THE REAL GAME FILE IS NEVER WRITTEN. The number is moved inside a throwaway mirror of the repo
    (see prove_mirror), so no crash, kill or timeout can leave the project carrying a planted fault —
    which is exactly what the old plant-and-restore-in-a-finally version did on 2026-09-22.
    """
    rel = os.path.join(SCRIPTS_REL, filename)
    if not os.path.isfile(os.path.join(REPO, rel)):
        print(RED + " WRONG" + OFF + " " + description + " — no such file: " + filename)
        return 1

    root, hits = mirror_with_change(REPO, rel, old, new)
    if root is None:
        print(RED + " WRONG" + OFF + " " + description +
              " — the anchor is in " + filename + " %d times, not once, so this proves nothing" % hits)
        return 1
    try:
        code, out = run(root)
    finally:
        discard(root)

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
              " — the real project does not pass the check, so this proves nothing")
        bad = 1
    else:
        print(GRN + "  ok  " + OFF + description)
    return bad


def main():
    if not os.path.isfile(CHECK):
        print(RED + "the check is missing at " + CHECK + OFF)
        return 1
    bad = 0
    # The experience curve's growth, nudged by a thousandth. Every level's pinned cost moves with it, so a
    # change to the curve that nobody meant cannot pass as "the numbers are what they were".
    bad += case("a moved experience curve is caught",
                "UnitStatMath.cs",
                "System.Math.Exp(0.098 * L + 0.021 * wall * wall)",
                "System.Math.Exp(0.099 * L + 0.021 * wall * wall)",
                "UnitStatMath.cs")
    # A different file, so one blind spot cannot pass the whole prover. The accuracy ceiling is a
    # promise; raising it past 100% lets a chance to hit exceed certainty, and nothing on screen would say so.
    bad += case("a raised accuracy ceiling is caught",
                "Combat.cs",
                "public const float MaxHitChance = 1f;",
                "public const float MaxHitChance = 1.05f;",
                "Combat.cs")
    # The bug that made Momentum's +3% a stack run as +103%, put back: a plain buff's number read as the whole
    # multiplier instead of as a bonus.
    bad += case("a buff number read as a whole multiplier again is caught",
                "UnitStates.cs",
                "public float Mult => 1f + magnitude;",
                "public float Mult => stacking ? 1f + magnitude : magnitude;",
                "UnitStates.cs")
    # Regen paid per tick instead of per second, put back — the 0.8s tick that healed 25% more than its label.
    bad += case("regen paid per tick again is caught",
                "StatusController.cs",
                "accum += ratePerSecond * UnitStates.RegenInterval;",
                "accum += ratePerSecond;",
                "StatusController.cs")
    print()
    if bad:
        print(RED + "%d case(s) wrong - the check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the check sees a moved number, names its file, and goes quiet once it is put back." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
