#!/usr/bin/env python3
"""Prove tools/check-shadow-looks.py can still SEE: plant one fault at a time into a throwaway mirror of the repo
(tools/prove_mirror.py — the real files are never opened for writing), run the checker against the mirror, and demand
it fail AND name the fault. One planted look that IS valid must pass, so the checker cannot pass this by failing
everything. Then demand the real project still passes. Every anchor is read out of the real files at run time, so a
fault always lands on today's data.

USAGE:   tools/prove-shadow-looks-check.py
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

CHECK = os.path.join(HERE, "check-shadow-looks.py")
MONSTERS = os.path.join("unity", "Assets", "3_Data", "Resources", "Monsters")
DYE_PARTS = os.path.join("unity", "Assets", "3_Data", "Resources", "CharacterDyeParts.asset")
GROUPS = os.path.join("unity", "Assets", "AddressableAssetsData", "AssetGroups")
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


def hex_of(r, g, b):
    return "#%02X%02X%02X" % tuple(int(round(max(0.0, min(1.0, float(v))) * 255)) for v in (r, g, b))


def main():
    code, out = run()
    if code != 0:
        print(RED + "the real project does not pass the check, so no planted fault can be told apart:" + OFF)
        print(out)
        return 1

    # A shadow whose look is a body with no colour changes yet — the cleanest place to plant into.
    folder = os.path.join(REPO, MONSTERS)
    target = None
    for f in sorted(os.listdir(folder)):
        if not f.endswith(".asset"):
            continue
        with open(os.path.join(folder, f), encoding="utf-8") as fh:
            text = fh.read()
        m = re.search(r"^  modelName: (\S+)\n  dyeSources: \[\]\n  dyeTargets: \[\]\n", text, re.M)
        if m:
            target = (os.path.join(MONSTERS, f), m.group(1))
            break
    if target is None:
        print(RED + "no shadow with a body and no colour changes to plant into, so this proves nothing" + OFF)
        return 1
    rel, body = target
    look = "  modelName: %s\n  dyeSources: []\n  dyeTargets: []\n" % body

    with open(os.path.join(REPO, DYE_PARTS), encoding="utf-8") as fh:
        parts_text = fh.read()
    row = re.search(r"\n  - modelFile: %s\n    textures: (.*)\n    parts:\n    - \{r: ([-\d.eE]+), g: ([-\d.eE]+), "
                    r"b: ([-\d.eE]+)" % re.escape(body), parts_text)
    if row is None:
        print(RED + "body %s has no parts row to read a real part from, so this proves nothing" % body + OFF)
        return 1
    part = hex_of(row.group(2), row.group(3), row.group(4))

    bad = 0
    bad += case("a valid look (a real part of the body, recoloured) passes", rel, look,
                "  modelName: %s\n  dyeSources:\n  - '%s'\n  dyeTargets:\n  - '#FF0000'\n" % (body, part),
                None, must_pass=True)
    bad += case("a shadow with no body", rel, look, "  modelName: \n  dyeSources: []\n  dyeTargets: []\n",
                "no body picked")
    bad += case("a shadow wearing a body the roster does not have", rel, look,
                "  modelName: %s_Gone\n  dyeSources: []\n  dyeTargets: []\n" % body, "is not in the character roster")
    bad += case("a colour stored against a part the body does not have", rel, look,
                "  modelName: %s\n  dyeSources:\n  - '#010203'\n  dyeTargets:\n  - '#FF0000'\n" % body,
                "is not a part of body")
    bad += case("source and target lists of different lengths", rel, look,
                "  modelName: %s\n  dyeSources:\n  - '%s'\n  dyeTargets: []\n" % (body, part), "source colours but")
    bad += case("a colour that is not #RRGGBB", rel, look,
                "  modelName: %s\n  dyeSources:\n  - '%s'\n  dyeTargets:\n  - 'red'\n" % (body, part),
                "is not #RRGGBB")
    bad += case("one part listed twice", rel, look,
                "  modelName: %s\n  dyeSources:\n  - '%s'\n  - '%s'\n  dyeTargets:\n  - '#FF0000'\n  - '#00FF00'\n"
                % (body, part, part), "is listed twice")
    bad += case("a body whose parts row is keyed by other textures", DYE_PARTS,
                "\n  - modelFile: %s\n    textures: %s\n" % (body, row.group(1)),
                "\n  - modelFile: %s\n    textures: %s_Other\n" % (body, row.group(1)), "never matches it")
    bad += case("a body with no parts row at all", DYE_PARTS,
                "\n  - modelFile: %s\n" % body, "\n  - modelFile: %s_Gone\n" % body, "no row in the outfit-parts table")

    # The roster itself: take the label off the body's Addressable entry, and it must be named as not in the roster.
    group_rel = None
    for f in sorted(os.listdir(os.path.join(REPO, GROUPS))):
        if not f.endswith(".asset"):
            continue
        with open(os.path.join(REPO, GROUPS, f), encoding="utf-8") as fh:
            gtext = fh.read()
        gm = re.search(r"    m_Address: (Characters/%s)\n((?:    .*\n)*?)    m_SerializedLabels:\n    - (\w+)\n"
                       % re.escape(body), gtext)
        if gm:
            group_rel = (os.path.join(GROUPS, f), gm.group(0), gm.group(0).replace("    - %s\n" % gm.group(3),
                                                                                  "    - NotACharacter\n"))
            break
    if group_rel is None:
        print(RED + " WRONG" + OFF + " a body dropped from the roster - no roster entry for %s to plant into" % body)
        bad += 1
    else:
        bad += case("a body dropped from the roster", group_rel[0], group_rel[1], group_rel[2],
                    "is not in the character roster")

    code_after, out_after = run()
    if code_after != 0 or out_after != out:
        print(RED + " WRONG" + OFF + " the real project's own answer changed while proving, so this proves nothing")
        bad += 1
    print()
    if bad:
        print(RED + "%d case(s) wrong - the shadow-look check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the shadow-look check sees every planted fault and the real project passes." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
