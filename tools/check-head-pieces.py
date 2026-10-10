#!/usr/bin/env python3
"""Check hair and headgear — what every character wears on its head — against the files the game loads.

WHY THIS EXISTS: a hairstyle or a hat is named by a short text (a hairstyle id, an item id) typed into a townsperson
row, a creature entry or a Head item, and a name that matches nothing is silently drawn as no hair, or no hat. A body
address left pointing at the model file instead of its bare body draws the model's own hair on top of the chosen one.
Nothing shows any of this until somebody looks at the person in game. This catches all of it before then.

Reads the files on disk (YAML), so what it checks is what the game loads. Checks:
  - every character body address (Characters/<model> in the Characters Addressable group) points at that model's BARE
    body, 2_Generated/Characters/Bodies/<model>.prefab (HeadPieceBaker), never at the model file
  - the hairstyle table (3_Data/Resources/HeadPieces.asset) lists every body in the roster, each body's own hair is
    "none" or a listed hairstyle, and every hairstyle's piece is a prefab carrying a HeadPiece
  - every Head item (slot 1) that names a model names a prefab carrying a HeadPiece
  - every townsperson's hairstyle is empty, "none" or listed, its hair colour empty or "#RRGGBB", and its headgear
    empty or the id of a Head item

USAGE:   tools/check-head-pieces.py [--root <repo>]
EXIT:    0 = clean, 1 = problems found, 2 = setup problem.
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if "--root" in sys.argv:
    REPO = os.path.abspath(sys.argv[sys.argv.index("--root") + 1])

ASSETS = os.path.join(REPO, "unity", "Assets")
RES = os.path.join(ASSETS, "3_Data", "Resources")
TABLE = os.path.join(RES, "HeadPieces.asset")
ITEMS = os.path.join(RES, "Items")
TOWNSFOLK = os.path.join(RES, "WorldTownsfolk.asset")
GROUP = os.path.join(ASSETS, "AddressableAssetsData", "AssetGroups", "Characters.asset")
BODIES = os.path.join(ASSETS, "2_Generated", "Characters", "Bodies")
HEAD_PIECE_SCRIPT = os.path.join(ASSETS, "Scripts", "HeadPiece.cs.meta")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"
HEX = re.compile(r"^#[0-9A-Fa-f]{6}$")


def read(path):
    with open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def guid_of_meta(path):
    m = re.search(r"^guid: (\w+)", read(path), re.M)
    return m.group(1) if m else None


def setup_fail(msg):
    print(RED + "setup problem: " + msg + OFF)
    sys.exit(2)


def prefabs_by_guid():
    """Prefab GUID -> path, for every prefab under unity/Assets."""
    out = {}
    for dirpath, _dirs, files in os.walk(ASSETS):
        for f in files:
            if f.endswith(".prefab.meta"):
                g = guid_of_meta(os.path.join(dirpath, f))
                if g:
                    out[g] = os.path.join(dirpath, f[:-len(".meta")])
    return out


def carries_head_piece(prefab_path, script_guid):
    return prefab_path is not None and os.path.exists(prefab_path) and \
        ("guid: %s" % script_guid) in read(prefab_path)


def field(chunk, name, indent):
    m = re.search(r"^%s%s: ?(.*)$" % (" " * indent, name), chunk, re.M)
    return None if m is None else m.group(1).strip().strip("'\"")


def main():
    for p in (TABLE, ITEMS, TOWNSFOLK, GROUP, BODIES, HEAD_PIECE_SCRIPT):
        if not os.path.exists(p):
            setup_fail("missing " + p)
    script = guid_of_meta(HEAD_PIECE_SCRIPT)
    prefabs = prefabs_by_guid()
    problems = []

    # ---- the body addresses point at the bare bodies
    bare = {}
    for f in os.listdir(BODIES):
        if f.endswith(".prefab.meta"):
            bare[guid_of_meta(os.path.join(BODIES, f))] = f[:-len(".prefab.meta")]
    roster = []
    for guid, model in re.findall(r"- m_GUID: (\w+)\n    m_Address: Characters/(\S+)", read(GROUP)):
        roster.append(model)
        if bare.get(guid) != model:
            problems.append("body address Characters/%s does not point at its bare body (Bodies/%s.prefab) - the "
                            "model's own hair would be drawn under the hair the character chose." % (model, model))

    # ---- the hairstyle table
    table = read(TABLE)
    styles = {}
    for sid, guid in re.findall(r"^  - id: (\S+)\n    piece: \{fileID: -?\d+, guid: (\w+)", table, re.M):
        styles[sid] = guid
    if not styles:
        problems.append("the hairstyle table lists no hairstyle.")
    for sid, guid in styles.items():
        if not carries_head_piece(prefabs.get(guid), script):
            problems.append("hairstyle '%s' has no head piece prefab behind it - it is drawn as no hair." % sid)
    bodies = dict(re.findall(r"^  - model: (\S+)\n    hair: (\S+)", table, re.M))
    for model in roster:
        if model not in bodies:
            problems.append("body '%s' has no row in the hairstyle table - a character made before hairstyles who "
                            "wears it is drawn bald." % model)
    for model, hair in bodies.items():
        if hair != "none" and hair not in styles:
            problems.append("body '%s' was made with hairstyle '%s', which is not in the hairstyle list." % (model, hair))

    # ---- Head items
    head_items = set()
    for f in sorted(os.listdir(ITEMS)):
        if not f.endswith(".asset"):
            continue
        text = read(os.path.join(ITEMS, f))
        if field(text, "slot", 2) != "1":
            continue
        head_items.add(field(text, "id", 2))
        m = re.search(r"^  equipModel: \{fileID: (-?\d+)(?:, guid: (\w+))?", text, re.M)
        if m and m.group(1) != "0" and not carries_head_piece(prefabs.get(m.group(2)), script):
            problems.append("Head item %s names a model that is not a head piece - nothing is drawn on the head." % f)

    # ---- townsfolk
    for chunk in re.split(r"\n  - id: ", read(TOWNSFOLK))[1:]:
        who = field(chunk, "name", 4) or "?"
        hair = field(chunk, "hairStyle", 4) or ""
        colour = field(chunk, "hairColour", 4) or ""
        hat = field(chunk, "headgear", 4) or ""
        if hair and hair != "none" and hair not in styles:
            problems.append("townsperson %s wears hairstyle '%s', which is not in the hairstyle list." % (who, hair))
        if colour and not HEX.match(colour):
            problems.append("townsperson %s has hair colour '%s', which is not #RRGGBB." % (who, colour))
        if hat and hat not in head_items:
            problems.append("townsperson %s wears '%s', which is not a Head item." % (who, hat))

    if problems:
        print(RED + "%d problem(s) with hair and headgear:" % len(problems) + OFF)
        for p in problems:
            print("  " + p)
        return 1
    print(GRN + "hair and headgear clean - %d bodies, %d hairstyles, %d Head items." % (len(roster), len(styles),
                                                                                     len(head_items)) + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
