#!/usr/bin/env python3
"""Prove tools/check-client-math.py still SEES a new client-side sum on a game number — and is quiet about the
shapes the rule allows.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This writes small
runtime scripts into a scratch tree with an EMPTY baseline, so nothing is "known": a sum on a game number in a
MonoBehaviour must be named; the same sum behind a server guard, in a static math class, with the marker, on a
movement line, or building a string must not.

USAGE:   tools/prove-client-math-check.py
EXIT:    0 = every planted sum caught and every allowed shape quiet, 1 = blind or crying wolf.
"""
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CHECK = os.path.join(HERE, "check-client-math.py")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

BAD = {
    # the level-up, as it was: a raw amount turned into a level on whatever machine runs this
    "LevelsUpAnywhere.cs": """using UnityEngine;
public class LevelsUpAnywhere : MonoBehaviour
{
    int level; public int XP;
    public void AddXP(int amount) { XP += amount; while (XP >= 10) { XP -= 10; level++; } }
}
""",
    # a tooltip multiplying an authored figure into a live damage number
    "TooltipMath.cs": """using UnityEngine;
public class TooltipMath : MonoBehaviour
{
    public float damage; public float bonus;
    void Show() { float shown = damage * (1f + bonus); Debug.Log(shown); }
}
""",
    # a plain (non-static) class rolling gold on a kill
    "RollsGold.cs": """public class RollsGold
{
    public int goldMin, goldMax;
    public int Roll() { int amount = UnityEngine.Random.Range(goldMin, goldMax + 1); return amount; }
}
""",
}
OK = {
    "GuardedLevelUp.cs": """using UnityEngine;
public class GuardedLevelUp : MonoBehaviour
{
    int level; public int XP;
    public void AddXP(int amount) { if (GameNetwork.TheWorldIsElsewhere) return; XP += amount; level++; }
}
""",
    "ServerAttribute.cs": """using UnityEngine;
public class ServerAttribute : MonoBehaviour
{
    public int CurrentHP;
    [Server] void Bleed(int damage) { CurrentHP -= damage; }
}
""",
    "PureMath.cs": """public static class PureMath
{
    public static int XpToNext(int level) => 5 + 5 * level;
    public static int Mitigate(int damage, int defense) => damage - defense;
}
""",
    "Explained.cs": """using UnityEngine;
public class Explained : MonoBehaviour
{
    public int CurrentHP, MaxHP;
    // server-math: proven fixture — this method is only ever reached from a [Server] caller two files away
    void Refill() { CurrentHP = MaxHP - 0; }
}
""",
    "Movement.cs": """using UnityEngine;
public class Movement : MonoBehaviour
{
    public float attackReach;
    void Update() { transform.position += Vector3.forward * Time.deltaTime * attackReach; }
}
""",
    "Labels.cs": """using UnityEngine;
public class Labels : MonoBehaviour
{
    public int level; public int XP;
    string Line() { return "Lv " + level + "  " + XP + " / " + 10; }
}
""",
}


def write_tree(root, scripts):
    d = os.path.join(root, "unity", "Assets", "Scripts")
    os.makedirs(d, exist_ok=True)
    for name, text in scripts.items():
        with open(os.path.join(d, name), "w", encoding="utf-8") as f:
            f.write(text)
    empty = os.path.join(root, "empty-baseline.txt")
    open(empty, "w").close()
    return empty


def run(root, baseline):
    done = subprocess.run([sys.executable, CHECK, "--root", root, "--baseline", baseline], capture_output=True, text=True)
    return done.returncode, done.stdout


def main():
    failures = 0
    root = tempfile.mkdtemp(prefix="client-math-prove-")
    try:
        baseline = write_tree(root, OK)
        code, out = run(root, baseline)
        quiet = code == 0
        print((GRN + "quiet   " if quiet else RED + "WOLF    ") + "guarded, [Server], static-math, explained, movement and string-building scripts" + OFF)
        if not quiet:
            failures += 1
            print(out)
        for name, text in BAD.items():
            case = tempfile.mkdtemp(prefix="client-math-case-")
            try:
                b = write_tree(case, OK | {name: text})
                code, out = run(case, b)
                named = code == 1 and name in out
                print((GRN + "caught  " if named else RED + "MISSED  ") + name + OFF)
                if not named:
                    failures += 1
                    print(out)
            finally:
                shutil.rmtree(case, ignore_errors=True)
        # the baseline is honoured: the same bad script, once recorded, is known and no longer fails
        case = tempfile.mkdtemp(prefix="client-math-case-")
        try:
            b = write_tree(case, OK | {"LevelsUpAnywhere.cs": BAD["LevelsUpAnywhere.cs"]})
            subprocess.run([sys.executable, CHECK, "--root", case, "--baseline", b, "--write-baseline"], capture_output=True, text=True)
            code, out = run(case, b)
            ok = code == 0 and "1 known sum" in out
            print((GRN + "quiet   " if ok else RED + "WRONG   ") + "a sum recorded in the baseline is known debt, not a new fault" + OFF)
            if not ok:
                failures += 1
                print(out)
        finally:
            shutil.rmtree(case, ignore_errors=True)
    finally:
        shutil.rmtree(root, ignore_errors=True)
    total = 1 + len(BAD) + 1
    if failures:
        print(RED + "%d of %d case(s) failed — the client-math checker is blind somewhere." % (failures, total) + OFF)
        return 1
    print(GRN + "All %d cases right: the client-math checker sees every new client-side sum and lets the allowed shapes through." % total + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
