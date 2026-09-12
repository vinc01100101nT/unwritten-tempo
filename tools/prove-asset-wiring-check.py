#!/usr/bin/env python3
"""Prove the wiring sweep still SEES a broken reference — and is still quiet about a sound one.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. Both are
silence, and only one of them is good news. So the sweep gets checked: this plants one known break
at a time in a COPY of a real file, confirms the sweep names it, then puts the file back and
confirms the sweep stops naming it.

It counts the DIFFERENCE the plant makes rather than expecting a clean project, because the project
has real faults today and a prover that only works on a clean project is a prover that stops working
the moment it is needed.

USAGE:   tools/prove-asset-wiring-check.py
EXIT:    0 = the sweep sees what it should, 1 = it is blind, or it cries wolf.
"""
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SWEEP = os.path.join(HERE, "check-asset-wiring.py")
MONSTERS = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "Monsters")

RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run():
    """(exit code, the lines it printed)."""
    done = subprocess.run([sys.executable, SWEEP], cwd=REPO, capture_output=True, text=True)
    return done.returncode, done.stdout


def plant(path, old, new):
    """Swap one string inside a real file, keeping an exact copy to put back. The copy is taken
    BEFORE the edit and restored in a finally, so an interrupted run cannot leave the project
    changed."""
    backup = path + ".prove-backup"
    shutil.copy2(path, backup)
    with open(path, encoding="utf-8") as f:
        text = f.read()
    assert old in text, "the anchor '%s' is not in %s" % (old, path)
    with open(path, "w", encoding="utf-8") as f:
        f.write(text.replace(old, new, 1))
    return backup


def restore(path, backup):
    shutil.move(backup, path)


def case(description, path, old, new, must_name):
    """Plant one fault, confirm the sweep names the file, then confirm it goes quiet again."""
    before_code, before_out = run()
    backup = plant(path, old, new)
    try:
        after_code, after_out = run()
    finally:
        restore(path, backup)
    clean_code, clean_out = run()

    named = must_name in after_out and must_name not in before_out
    louder = after_out.count("\n") > before_out.count("\n")
    quiet_again = clean_out == before_out

    if named and louder and after_code == 1 and quiet_again:
        print(GRN + "  ok  " + OFF + description)
        return 0
    print(RED + " WRONG" + OFF + " " + description)
    if not named:       print("         it never named %s after the break was planted" % must_name)
    if not louder:      print("         it reported no more faults than before the break")
    if after_code != 1: print("         it exited %d, not 1, with a known break in place" % after_code)
    if not quiet_again: print("         it did NOT return to its previous answer after the file was put back")
    return 1


def main():
    if not os.path.isdir(MONSTERS):
        print(RED + "no creature assets at %s" % MONSTERS + OFF)
        return 1
    bad = 0
    # A creature whose art folder name is misspelt by one letter. This is the exact shape of the
    # fault that left every boss wearing another creature's sprite.
    victim = os.path.join(MONSTERS, "Bear.asset")
    bad += case("a creature naming an art folder that does not exist",
                victim, "sheet: Bear", "sheet: BearZZ", "Bear.asset")

    # A reference to a file that is not in the project or in any installed package.
    bad += case("a reference pointing at a file that exists nowhere",
                victim, "m_Name: Bear", "m_Name: Bear\n  probeRef: {fileID: 2800000, guid: "
                        "deadbeefdeadbeefdeadbeefdeadbeef, type: 3}", "Bear.asset")
    print()
    if bad:
        print(RED + "%d case(s) wrong — the sweep's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the sweep sees a planted break and is quiet once it is repaired." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
