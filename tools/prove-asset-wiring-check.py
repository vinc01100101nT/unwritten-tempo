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
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SWEEP = os.path.join(HERE, "check-asset-wiring.py")
MONSTERS_REL = os.path.join("unity", "Assets", "3_Data", "Resources", "Monsters")
MONSTERS = os.path.join(REPO, MONSTERS_REL)

sys.path.insert(0, HERE)
from prove_mirror import mirror_with_change, discard

RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=REPO):
    """(exit code, the lines it printed) — from `root`: the real repo, or a throwaway mirror of it."""
    done = subprocess.run([sys.executable, os.path.join(root, "tools", "check-asset-wiring.py")],
                          cwd=root, capture_output=True, text=True)
    return done.returncode, done.stdout


def case(description, rel, old, new, must_name):
    """Plant one break in a MIRROR of the project, confirm the sweep names it, and confirm the real
    project's own answer is unchanged.

    THE REAL ASSET IS NEVER WRITTEN. It used to be edited in place and put back in a finally, which
    does not survive a kill — and whose leftover backup could be copied over on the next run, baking
    the planted break in for good. Nothing outside the temporary mirror is opened for writing now.
    """
    before_code, before_out = run()

    root, hits = mirror_with_change(REPO, rel, old, new)
    if root is None:
        print(RED + " WRONG" + OFF + " " + description +
              " — the anchor is in %s %d times, not once, so this proves nothing" % (rel, hits))
        return 1
    try:
        after_code, after_out = run(root)
    finally:
        discard(root)

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
    if not quiet_again: print("         the real project's own answer changed, so this proves nothing")
    return 1


def first_creature_with_art():
    """(file, its m_Name, its artFolder guid, the guid of a picture it uses) for the first creature that
    has art of its own, or None. The picture is its faceset, when it has one — a real file that is not
    a folder, for the case that needs a reference that resolves to the wrong kind of thing."""
    for f in sorted(os.listdir(MONSTERS)):
        if not f.endswith(".asset"):
            continue
        with open(os.path.join(MONSTERS, f), encoding="utf-8", errors="ignore") as fh:
            text = fh.read()
        art = re.search(r"^  artFolder: ([0-9a-f]{32})$", text, re.M)
        name = re.search(r"^  m_Name: (\S+)$", text, re.M)
        face = re.search(r"^  faceset: \{fileID: \d+, guid: ([0-9a-f]{32})", text, re.M)
        if art and name:
            return f, name.group(1), art.group(1), (face.group(1) if face else None)
    return None


def main():
    if not os.path.isdir(MONSTERS):
        print(RED + "no creature assets at %s" % MONSTERS + OFF)
        return 1
    # The test creature is FOUND, never named: a hardcoded one broke this whole proof the day the
    # roster was renamed and it no longer existed.
    found = first_creature_with_art()
    if not found:
        print(RED + "no creature in %s has an art folder to break" % MONSTERS + OFF)
        return 1
    file_name, name, art, own_texture = found
    victim = os.path.join(MONSTERS_REL, file_name)
    bad = 0
    # A creature whose art folder reference points at nothing. This is the shape of the fault that
    # left every boss wearing another creature's sprite.
    bad += case("a creature whose art folder reference points at nothing",
                victim, "artFolder: " + art, "artFolder: " + "0" * 32, file_name)

    # A creature whose art reference resolves — but to a single picture, not a creature art folder.
    if own_texture:
        bad += case("a creature whose art reference points at something that is not an art folder",
                    victim, "artFolder: " + art, "artFolder: " + own_texture, file_name)

    # A reference to a file that is not in the project or in any installed package.
    bad += case("a reference pointing at a file that exists nowhere",
                victim, "m_Name: " + name, "m_Name: " + name + "\n  probeRef: {fileID: 2800000, guid: "
                        "deadbeefdeadbeefdeadbeefdeadbeef, type: 3}", file_name)
    print()
    if bad:
        print(RED + "%d case(s) wrong — the sweep's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the sweep sees a planted break and is quiet once it is repaired." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
