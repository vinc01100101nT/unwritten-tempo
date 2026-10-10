#!/usr/bin/env python3
"""Check every SHADOW creature's saved look — the body it wears and its outfit colours.

WHY THIS EXISTS: a shadow's body is a name typed into its creature entry (picked in the Mob Table), and nothing in
the game can tell a wrong name from a right one until the server tries to make the creature — which it then refuses
(MobSpawner). Colours are worse: a colour stored against a part the body does not have is silently drawn as nothing,
and the creature just looks plain. This catches both before anyone plays.

Reads the files on disk, so what it checks is what the game loads. The kind that is drawn as a character, the roster
label and the address prefix are all parsed out of the C# at run time, never copied here.

Checks, per creature entry in unity/Assets/3_Data/Resources/Monsters whose kind is drawn as a character:
  - it names a body (modelName is not empty)
  - that body is in the loadable roster: an Addressable entry with the character-model label at
    "<prefix><body>" (CharacterModels.Label / AddressPrefix)
  - that body's model has a row in the saved outfit-parts table (CharacterDyeParts.asset), and the row's texture
    key is the body's name — the game only shows a look stamped with the name of the body it is drawn on
  - the source and target colour lists are the same length, every entry is "#RRGGBB", no part is listed twice
  - every source colour is one of that body's parts (within one step of 8-bit colour, the game's own tolerance)
  - its hairstyle is empty (the hair its body was made with), "none", or a hairstyle in HeadPieces.asset, and its
    hair colour is empty or "#RRGGBB" — a misspelt hairstyle is silently drawn as no hair at all

USAGE:   tools/check-shadow-looks.py [--root <repo>]
EXIT:    0 = clean, 1 = problems found, 2 = setup problem.
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if "--root" in sys.argv:
    REPO = os.path.abspath(sys.argv[sys.argv.index("--root") + 1])

ASSETS = os.path.join(REPO, "unity", "Assets")
SCRIPTS = os.path.join(ASSETS, "Scripts")
MONSTERS = os.path.join(ASSETS, "3_Data", "Resources", "Monsters")
DYE_PARTS = os.path.join(ASSETS, "3_Data", "Resources", "CharacterDyeParts.asset")
HEAD_PIECES = os.path.join(ASSETS, "3_Data", "Resources", "HeadPieces.asset")
BARE_BODIES = os.path.join(ASSETS, "2_Generated", "Characters", "Bodies")
GROUPS = os.path.join(ASSETS, "AddressableAssetsData", "AssetGroups")
CREATURE_KIND = os.path.join(SCRIPTS, "CreatureKind.cs")
CHARACTER_MODELS = os.path.join(SCRIPTS, "CharacterModels.cs")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

# CharacterDye3D's "this is that part" tolerance - one step of 8-bit colour.
SAME = 0.004


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def setup_fail(msg):
    print(RED + "setup: " + msg + OFF)
    sys.exit(2)


def kinds_drawn_as_character():
    """The numeric CreatureKind values Is3D answers yes for, read from CreatureKind.cs."""
    cs = read(CREATURE_KIND)
    m = re.search(r"enum\s+CreatureKind\s*\{(.*?)\}", cs, re.S)
    if not m:
        setup_fail("no CreatureKind enum in " + CREATURE_KIND)
    names, value = {}, -1
    body = re.sub(r"///.*|//.*", "", m.group(1))
    for part in body.split(","):
        part = re.sub(r"\[.*?\]", "", part).strip()
        if not part:
            continue
        mm = re.match(r"(\w+)\s*(?:=\s*(\d+))?$", part)
        if not mm:
            continue
        value = int(mm.group(2)) if mm.group(2) else value + 1
        names[mm.group(1)] = value
    is3d = re.search(r"static\s+bool\s+Is3D\s*\(\s*CreatureKind\s+(\w+)\s*\)\s*=>\s*([^;]+);", cs)
    if not is3d:
        setup_fail("no Is3D(CreatureKind) rule in " + CREATURE_KIND)
    wanted = re.findall(r"CreatureKind\.(\w+)", is3d.group(2))
    if not wanted or any(w not in names for w in wanted):
        setup_fail("could not read which kinds Is3D means from: " + is3d.group(2).strip())
    return {names[w]: w for w in wanted}


def const_string(cs, name):
    m = re.search(r'const\s+string\s+%s\s*=\s*"([^"]*)"' % name, cs)
    if not m:
        setup_fail("no const string %s in %s" % (name, CHARACTER_MODELS))
    return m.group(1)


def roster():
    """Short body name -> asset GUID, for every Addressable entry carrying the character-model label."""
    cs = read(CHARACTER_MODELS)
    label, prefix = const_string(cs, "Label"), const_string(cs, "AddressPrefix")
    out = {}
    if not os.path.isdir(GROUPS):
        setup_fail("no Addressable groups folder at " + GROUPS)
    for f in sorted(os.listdir(GROUPS)):
        if not f.endswith(".asset"):
            continue
        text = read(os.path.join(GROUPS, f))
        for chunk in re.split(r"\n  - m_GUID: ", text)[1:]:
            guid = chunk.split("\n", 1)[0].strip()
            addr = re.search(r"^    m_Address: (.*)$", chunk, re.M)
            labels = re.search(r"^    m_SerializedLabels:\n((?:    - .*\n?)*)", chunk, re.M)
            if not addr or not labels:
                continue
            has_label = any(l.strip()[2:].strip() == label for l in labels.group(1).splitlines())
            address = addr.group(1).strip()
            if has_label and address.startswith(prefix):
                out[address[len(prefix):]] = guid
    return out


def model_files_by_guid():
    """Asset GUID -> model name, for every model under the authored art folder AND every bare body the head-piece
    bake made from one (2_Generated/Characters/Bodies) — the roster points at the bare body once it exists."""
    out = {}
    for root, ending in ((os.path.join(ASSETS, "1_Authored"), ".fbx.meta"), (BARE_BODIES, ".prefab.meta")):
        for dirpath, _dirs, files in os.walk(root):
            for f in files:
                if not f.lower().endswith(ending):
                    continue
                with open(os.path.join(dirpath, f), encoding="utf-8", errors="replace") as fh:
                    for line in fh:
                        if line.startswith("guid:"):
                            out[line.split(":", 1)[1].strip()] = f[:-len(ending)]
                            break
    return out


def hairstyles():
    """Every hairstyle id in the saved table (HeadPieces.asset), or None when there is no table yet."""
    if not os.path.exists(HEAD_PIECES):
        return None
    return set(re.findall(r"^  - id: (\S+)$", read(HEAD_PIECES), re.M))


def dye_parts():
    """modelFile -> (texture key, [(r, g, b)])."""
    text = read(DYE_PARTS)
    out = {}
    for chunk in re.split(r"\n  - modelFile: ", text)[1:]:
        model = chunk.split("\n", 1)[0].strip()
        tex = re.search(r"^    textures: (.*)$", chunk, re.M)
        parts = [(float(a), float(b), float(c)) for a, b, c in
                 re.findall(r"\{r: ([-\d.eE]+), g: ([-\d.eE]+), b: ([-\d.eE]+)", chunk)]
        out[model] = (tex.group(1).strip() if tex else "", parts)
    return out


def string_field(text, name):
    m = re.search(r"^  %s: ?(.*)$" % name, text, re.M)
    if not m:
        return None
    return m.group(1).strip().strip("'\"")


def list_field(text, name):
    m = re.search(r"^  %s:(.*)$" % name, text, re.M)
    if not m:
        return None
    rest = m.group(1).strip()
    if rest == "[]":
        return []
    items = []
    for line in text[m.end():].split("\n")[1:]:
        if not line.startswith("  - "):
            break
        items.append(line[4:].strip().strip("'\""))
    return items


HEX = re.compile(r"^#[0-9A-Fa-f]{6}$")


def colour_of(hexs):
    return tuple(int(hexs[i:i + 2], 16) / 255.0 for i in (1, 3, 5))


def same(a, b):
    return all(abs(x - y) < SAME for x, y in zip(a, b))


def main():
    for p in (MONSTERS, DYE_PARTS, CREATURE_KIND, CHARACTER_MODELS):
        if not os.path.exists(p):
            setup_fail("missing " + p)
    drawn = kinds_drawn_as_character()
    bodies = roster()
    if not bodies:
        setup_fail("the character roster is empty - no Addressable entry carries the character-model label")
    files = model_files_by_guid()
    parts_of = dye_parts()
    styles = hairstyles()

    problems, checked = [], 0
    for f in sorted(os.listdir(MONSTERS)):
        if not f.endswith(".asset"):
            continue
        text = read(os.path.join(MONSTERS, f))
        kind = string_field(text, "kind")
        if kind is None or not kind.isdigit() or int(kind) not in drawn:
            continue
        checked += 1
        who = "%s (%s)" % (f, string_field(text, "id") or "?")
        body = string_field(text, "modelName") or ""
        if not body:
            problems.append("%s: no body picked - the server will not make it. Pick one in the Mob Table." % who)
            continue
        if body not in bodies:
            problems.append("%s: body '%s' is not in the character roster - the server will not make it." % (who, body))
            continue
        model_file = files.get(bodies[body])
        if model_file is None:
            problems.append("%s: body '%s' has a roster entry whose model file cannot be found." % (who, body))
            continue
        if model_file not in parts_of:
            problems.append("%s: body '%s' (model %s) has no row in the outfit-parts table, so its colours can never "
                            "show." % (who, body, model_file))
            continue
        tex_key, parts = parts_of[model_file]
        if tex_key != body:
            problems.append("%s: body '%s' is drawn with textures '%s' - a look stamped '%s' never matches it, so its "
                            "colours can never show." % (who, body, tex_key, body))
        src = list_field(text, "dyeSources") or []
        dst = list_field(text, "dyeTargets") or []
        if len(src) != len(dst):
            problems.append("%s: %d source colours but %d target colours." % (who, len(src), len(dst)))
        seen = []
        for i, s in enumerate(src):
            if not HEX.match(s):
                problems.append("%s: source colour %d '%s' is not #RRGGBB." % (who, i + 1, s))
                continue
            c = colour_of(s)
            if not any(same(c, p) for p in parts):
                problems.append("%s: source colour %s is not a part of body '%s'." % (who, s, body))
            if any(same(c, o) for o in seen):
                problems.append("%s: part %s is listed twice." % (who, s))
            seen.append(c)
        for i, d in enumerate(dst):
            if not HEX.match(d):
                problems.append("%s: target colour %d '%s' is not #RRGGBB." % (who, i + 1, d))

        hair = string_field(text, "hairStyle") or ""
        hair_colour = string_field(text, "hairColour") or ""
        if hair and hair != "none" and (styles is None or hair not in styles):
            problems.append("%s: hairstyle '%s' is not in the hairstyle list - it is drawn as no hair at all." % (who, hair))
        if hair_colour and not HEX.match(hair_colour):
            problems.append("%s: hair colour '%s' is not #RRGGBB." % (who, hair_colour))

    if checked == 0:
        print(RED + "no creature of a kind drawn as a character (%s) was found, so nothing was checked"
              % ", ".join(drawn.values()) + OFF)
        return 1
    if problems:
        print(RED + "%d problem(s) in the shadow looks:" % len(problems) + OFF)
        for p in problems:
            print("  " + p)
        return 1
    print(GRN + "every shadow look holds together (%d creatures, %d bodies in the roster)" % (checked, len(bodies)) + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
