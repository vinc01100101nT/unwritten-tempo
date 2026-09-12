#!/usr/bin/env python3
"""Does everything the game loads still point at something that exists?

WHY THIS EXISTS: a reference that has gone stale is SILENT. Unity drops it, the code falls back to
a default, and the game runs — wearing the wrong art, or missing a piece, with nothing written
anywhere. The one that prompted this had all 20 bosses standing in the world wearing an ordinary
creature's sprite, because the baker searched one art folder and their art was in the other. It
had been that way for weeks. Nothing reported it, because falling back is silent by design.

This is the general form of that question, asked of everything at once rather than of the case
that happened to be noticed.

USAGE:   tools/check-asset-wiring.py
EXIT:    0 = everything resolves, 1 = something points at nothing, 2 = setup problem.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_assets as ua

RED, YEL, GRN, OFF = "\033[31m", "\033[33m", "\033[32m", "\033[0m"

# Only OUR folders are reported on. A vendor pack's own example scenes carry their own dangling
# references, and drowning a real fault in three hundred of somebody else's is how a checker gets
# switched off.
OURS = ("1_Authored", "2_Generated", "3_Data", "Scenes", "UI", "Settings")
READABLE = (".asset", ".mat", ".prefab", ".unity", ".controller")

MONSTERS = os.path.join(ua.ASSETS, "3_Data", "Resources", "Monsters")
CREATURE_ART = [os.path.join(ua.ASSETS, "1_Authored", "Art", "Billboards", "Actor", "Monster"),
                os.path.join(ua.ASSETS, "1_Authored", "Art", "Billboards", "Actor", "Boss")]

_SHEET = re.compile(r"^\s*sheet:\s*(.*)$", re.M)


def broken_references(known):
    """Files of ours naming a guid that resolves to nothing, anywhere Unity looks."""
    faults = []
    for base, dirs, files in os.walk(ua.ASSETS):
        dirs[:] = [d for d in dirs if d not in ua.SKIP_DIRS]
        top = os.path.relpath(base, ua.ASSETS).split(os.sep)[0]
        if top not in OURS:
            continue
        for f in files:
            if not f.endswith(READABLE):
                continue
            path = os.path.join(base, f)
            for g in sorted(ua.references(path)):
                if g in known or ua.is_builtin(g):
                    continue
                faults.append((path, "points at a file that is not in the project or any package (%s)" % g))
    return faults


def creature_art():
    """Every creature's sprite folder, in EITHER art folder — ordinary creatures and bosses alike.
    The asset records only a folder name, never which of the two it came from, so both are searched.
    Searching one is the bug this whole checker was written after."""
    faults = []
    if not os.path.isdir(MONSTERS):
        return faults
    for f in sorted(os.listdir(MONSTERS)):
        if not f.endswith(".asset"):
            continue
        path = os.path.join(MONSTERS, f)
        try:
            with open(path, encoding="utf-8", errors="ignore") as fh:
                m = _SHEET.search(fh.read())
        except OSError:
            continue
        name = (m.group(1).strip() if m else "")
        if not name:
            continue
        if not any(os.path.isdir(os.path.join(d, name)) for d in CREATURE_ART):
            faults.append((path, "names the art folder '%s', which is in neither creature art folder" % name))
    return faults


def main():
    if not os.path.isdir(ua.ASSETS):
        print(RED + "no Unity project at %s" % ua.ASSETS + OFF)
        return 2
    known = ua.guid_map()
    # An empty or tiny map means the walk found nothing, and EVERY reference would then look broken.
    # Reporting that as hundreds of faults would be the instrument's failure wearing the project's
    # clothes, so it is a setup problem and says so.
    if len(known) < 1000:
        print(RED + "only %d guids found — the project was not read properly, so nothing can be "
                    "concluded about what is broken" % len(known) + OFF)
        return 2

    faults = creature_art() + broken_references(known)
    if not faults:
        print(GRN + "asset wiring clean" + OFF +
              " — every reference in our own files resolves, and every creature's art folder exists "
              "(%d guids known)" % len(known))
        return 0

    print(RED + "%d thing(s) point at something that is not there:" % len(faults) + OFF)
    for path, why in faults:
        # Said as a repo-relative path followed by a colon, on ONE line, because that is the shape
        # the end-of-turn gate matches to tell THIS session's faults from everybody else's. Printed
        # any other way, every one of these rides along on every turn and the real one is lost in
        # the pile of pre-existing ones.
        print("  %s: %s" % (os.path.relpath(path, ua.REPO), why))
    return 1


if __name__ == "__main__":
    sys.exit(main())
