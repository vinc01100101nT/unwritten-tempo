#!/usr/bin/env python3
"""Prove tools/check-head-pieces.py can still SEE: plant one fault at a time into a throwaway mirror of the repo
(tools/prove_mirror.py — the real files are never opened for writing), run the checker against the mirror, and demand
it fail AND name the fault. One planted change that IS valid must pass, so the checker cannot pass this by failing
everything. Then demand the real project still passes. Every anchor is read out of the real files at run time.

USAGE:   tools/prove-head-pieces-check.py
EXIT:    0 = every planted fault caught, the valid plant passes and the real project passes; 1 = blind or crying wolf.
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from prove_mirror import mirror_with_change, discard  # noqa: E402

CHECK = os.path.join(HERE, "check-head-pieces.py")
RES = os.path.join("unity", "Assets", "3_Data", "Resources")
TABLE = os.path.join(RES, "HeadPieces.asset")
TOWNSFOLK = os.path.join(RES, "WorldTownsfolk.asset")
GROUP = os.path.join("unity", "Assets", "AddressableAssetsData", "AssetGroups", "Characters.asset")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=None):
    args = [sys.executable, CHECK] + (["--root", root] if root else [])
    p = subprocess.run(args, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def case(description, rel, old, new, must_say, must_pass=False):
    root, hits = mirror_with_change(REPO, rel, old, new)
    if root is None:
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
    with open(os.path.join(REPO, rel), encoding="utf-8") as fh:
        return fh.read()


def main():
    code, out = run()
    if code != 0:
        print(RED + "the real project does not pass the check, so no planted fault can be told apart:" + OFF)
        print(out)
        return 1

    town = read(TOWNSFOLK)
    row = re.search(r"\n    hairStyle: (\S+)\n    hairColour: (.*)\n    headgear: (.*)\n", town)
    if row is None:
        print(RED + "no townsperson row with hair fields to plant into, so this proves nothing" + OFF)
        return 1
    old = row.group(0)
    table = read(TABLE)
    style = re.search(r"^  - id: (\S+)$", table, re.M).group(1)
    body = re.search(r"^  - model: (\S+)\n    hair: (\S+)", table, re.M)
    group = read(GROUP)
    entry = re.search(r"- m_GUID: (\w+)\n    m_Address: Characters/(\S+)", group)

    bad = 0
    bad += case("a townsperson given another real hairstyle and colour passes", TOWNSFOLK, old,
                "\n    hairStyle: %s\n    hairColour: '#123456'\n    headgear: %s\n" % (style, row.group(3)),
                None, must_pass=True)
    bad += case("a townsperson wearing a hairstyle that does not exist", TOWNSFOLK, old,
                "\n    hairStyle: hair99\n    hairColour: %s\n    headgear: %s\n" % (row.group(2), row.group(3)),
                "is not in the hairstyle list")
    bad += case("a townsperson hair colour that is not #RRGGBB", TOWNSFOLK, old,
                "\n    hairStyle: %s\n    hairColour: blonde\n    headgear: %s\n" % (row.group(1), row.group(3)),
                "is not #RRGGBB")
    bad += case("a townsperson wearing an item that is not a Head item", TOWNSFOLK, old,
                "\n    hairStyle: %s\n    hairColour: %s\n    headgear: item.nope\n" % (row.group(1), row.group(2)),
                "which is not a Head item")
    bad += case("a body made with a hairstyle the list does not have", TABLE, body.group(0),
                "  - model: %s\n    hair: hair99" % body.group(1), "which is not in the hairstyle list")
    bad += case("a roster body with no row in the hairstyle table", TABLE, body.group(0),
                "  - model: %s_Gone\n    hair: %s" % (body.group(1), body.group(2)), "has no row in the hairstyle table")
    bad += case("a body address pointing at something other than its bare body", GROUP, entry.group(0),
                "- m_GUID: 00000000000000000000000000000000\n    m_Address: Characters/%s" % entry.group(2),
                "does not point at its bare body")
    bad += case("a hairstyle whose piece is not a head piece", TABLE,
                re.search(r"^  - id: %s\n    piece: \{fileID: -?\d+, guid: \w+" % re.escape(style), table, re.M).group(0),
                "  - id: %s\n    piece: {fileID: 1, guid: 00000000000000000000000000000000" % style,
                "has no head piece prefab behind it")

    code_after, out_after = run()
    if code_after != 0 or out_after != out:
        print(RED + " WRONG" + OFF + " the real project's own answer changed while proving, so this proves nothing")
        bad += 1
    print()
    if bad:
        print(RED + "%d case(s) wrong - the hair and headgear check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the hair and headgear check sees every planted fault and the real project passes." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
