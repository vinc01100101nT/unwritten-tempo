#!/usr/bin/env python3
"""Check the saved ASSETS against the rules in unity/Assets/ASSETS.md — the ones the 2026-09-20 audit found
broken, turned into tests so they cannot come back.

WHY THIS EXISTS: an import setting, a prefab flag or a project setting is a number in a text file, and a wrong
one is silent. Nine ground tiles shipped uncompressed at 85 MB of a 307 MB build; 4,608 blades of grass were
solid, shadow-casting physics meshes; all 84 creatures loaded at level 1 because one struct was not marked
serializable and the baker silently dropped their stats; an AI package nothing referenced shipped 10 MB. None of
that shows in the editor, in the console, or in play until somebody measures. This measures, every turn.

Every rule below is written in ASSETS.md in plain words; this file is the mechanical half. A rule you have to
remember is a rule you can drop.

USAGE:   tools/check-asset-rules.py [--root DIR] [--all]
           --root  check a different copy of the project (the prover uses a scratch copy)
           --all   also print WARN lines (debt that does not block a turn)
EXIT:    0 = clean, 1 = a FAIL line printed, 2 = could not run (says why)

Every fault line names the file, in the form the end-of-turn gate reads:  FAIL <path>: <what>
"""
import argparse
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)

# ---- the numbers pinned by ASSETS.md — change the doc and this together -------------------------------------
MAX_TEXTURE_SIZE = 2048
GROUND_TILE_SIZE = 1024     # every ground tile imports at exactly this, no alpha, from a .png — ASSETS.md
MAX_MAIN_SHADOWMAP = 1024
MAX_ADDITIONAL_SHADOWMAP = 1024
BATCHING_STATIC = 4          # StaticEditorFlags.BatchingStatic, read from Unity's own library on 2026-09-20

TEXTURE_EXT = (".png", ".tga", ".jpg", ".jpeg", ".psd", ".tif", ".tiff", ".exr")
COLLIDER_DOCS = ("!u!64 ", "!u!65 ", "!u!135 ", "!u!136 ")   # Mesh, Box, Sphere, Capsule colliders
PROP_FOLDERS = ("Outdoor", "Trees", "Plants")                  # the placeable palette: nothing in it moves


def rel(repo, path):
    return os.path.relpath(path, repo).replace(os.sep, "/")


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def default_block(meta):
    """The DefaultTexturePlatform block of a texture .meta — the settings that actually apply unless a
    platform block says `overridden: 1`."""
    m = re.search(r"buildTarget: DefaultTexturePlatform\n((?:    .*\n)+)", meta)
    return m.group(1) if m else ""


def value(block, key):
    m = re.search(r"^\s*%s: (\S+)" % re.escape(key), block, re.M)
    return m.group(1) if m else None


# ---- A1–A3  textures ------------------------------------------------------------------------------------------
def png_fits_one_block(path):
    """True when `path` is a PNG at most 4 pixels on each side — one block-compression block."""
    if not path.lower().endswith(".png"):
        return False
    try:
        with open(path, "rb") as fh:
            head = fh.read(24)
    except OSError:
        return False
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
        return False
    w, h = int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")
    return 0 < w <= 4 and 0 < h <= 4


def check_textures(repo, out):
    # Every texture the game can use: all of Assets except the untouched vendor packs in 0_Raw (never placed, never
    # shipped as they are) and Mirror's own bundled samples.
    root = os.path.join(repo, "unity", "Assets")
    for dirpath, dirs, files in os.walk(root):
        if dirpath == root:
            dirs[:] = [d for d in dirs if d not in ("0_Raw", "Mirror")]
        for f in files:
            if not f.endswith(".meta") or not f[:-5].lower().endswith(TEXTURE_EXT):
                continue
            path = os.path.join(dirpath, f)
            p = rel(repo, path)
            # The pixel-art sprites under /Art/ are UNCOMPRESSED BY DESIGN (block compression bleeds on a pixel
            # grid) and the import rule owns them. Ground tiles are the one /Art/ folder that is not pixel art. The
            # weapons made from sprite sheets carry one texel per block face, so they are pixel art too. So are the
            # head pieces' colour strips (HeadPieceBaker): a few exact colours a hair's chosen colour is matched
            # against, which block compression would shift off the colour the piece names.
            pixel_art = ("/Art/" in p and "/GroundTiles/" not in p) or "/2_Generated/Weapons/" in p                 or "/2_Generated/Characters/HeadPieces/" in p
            cursor = "/KenneyCursorPack/" in p
            # A colour palette no bigger than one 4×4 compression block (a low-poly model's swatch) gains nothing from
            # block compression, and compressing it blends its few exact colours together.
            palette = png_fits_one_block(path[:-5])
            meta = read(path)
            if "TextureImporter:" not in meta:
                continue
            block = default_block(meta)
            size = value(block, "maxTextureSize")
            comp = value(block, "textureCompression")
            readable = value(meta, "isReadable")
            if size and int(size) > MAX_TEXTURE_SIZE:
                out("FAIL", p, "maxTextureSize %s is above %d — a 2.5D camera never shows a prop that large"
                    % (size, MAX_TEXTURE_SIZE))
            if not pixel_art and not cursor and not palette and comp == "0":
                out("FAIL", p, "textureCompression: 0 (None) — every authored texture that is not pixel art "
                    "ships compressed; this one costs four to eight times its compressed size")
            if readable == "1" and not cursor:
                out("FAIL", p, "isReadable: 1 (Read/Write Enabled) doubles this texture's memory; only the "
                    "hardware cursor needs it")
            if "/GroundTiles/" in p:
                if not f[:-5].lower().endswith(".png"):
                    out("FAIL", p, "a ground tile must be a .png — every tile ships from the same format")
                if size and int(size) != GROUND_TILE_SIZE:
                    out("FAIL", p, "maxTextureSize %s — every ground tile imports at exactly %d so blended tiles "
                        "match in sharpness; reimport the folder so the import rule re-applies it"
                        % (size, GROUND_TILE_SIZE))
                if value(meta, "alphaUsage") not in (None, "0"):
                    out("FAIL", p, "alphaUsage %s — ground tiles import with no alpha so every tile compresses "
                        "to the same format" % value(meta, "alphaUsage"))


# ---- A1b  the ground tile folder holds tile pictures and nothing else ---------------------------------------
def check_ground_tile_folder(repo, out):
    folder = os.path.join(repo, "unity", "Assets", "1_Authored", "Art", "GroundTiles")
    if not os.path.isdir(folder):
        return
    for f in sorted(os.listdir(folder)):
        name = f[:-5] if f.endswith(".meta") else f
        if name.startswith("."):
            continue
        if not name.lower().endswith(".png") or os.path.isdir(os.path.join(folder, name)):
            out("FAIL", rel(repo, os.path.join(folder, f)), "the ground tile folder holds only .png tile pictures — "
                "a tile is used by name, so anything else here is either a second way to reach a tile or dead weight")


# ---- A4–A5  the prop palette: plants, static flags, foliage batching -----------------------------------------
def foliage_material_guids(repo):
    """Materials on the wind shader. That shader reads each object's own pivot, which static batching erases
    (batched objects share one identity matrix), so a batch-static plant sways wrong."""
    shader_meta = os.path.join(repo, "unity", "Assets", "Shaders", "FoliageWind.shader.meta")
    if not os.path.isfile(shader_meta):
        return set()
    sg = value(read(shader_meta), "guid")
    guids = set()
    for dirpath, _d, files in os.walk(os.path.join(repo, "unity", "Assets")):
        for f in files:
            if f.endswith(".mat"):
                mp = os.path.join(dirpath, f)
                if ("m_Shader: {fileID: 4800000, guid: %s" % sg) in read(mp):
                    g = value(read(mp + ".meta"), "guid") if os.path.isfile(mp + ".meta") else None
                    if g:
                        guids.add(g)
    return guids


def check_props(repo, out):
    props = os.path.join(repo, "unity", "Assets", "1_Authored", "Prefabs", "Props")
    if not os.path.isdir(props):
        return
    foliage = foliage_material_guids(repo)
    for folder in PROP_FOLDERS:
        d = os.path.join(props, folder)
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            if not f.endswith(".prefab"):
                continue
            path = os.path.join(d, f)
            p = rel(repo, path)
            text = read(path)
            flags = value(text, "m_StaticEditorFlags")
            if flags == "0":
                out("FAIL", p, "m_StaticEditorFlags: 0 — a palette prop never moves; without static flags it "
                    "can be neither batched nor hidden behind a wall (22 for a hard prop, 16 for foliage)")
            uses_wind = any(("guid: %s" % g) in text for g in foliage)
            if uses_wind and flags and flags.isdigit() and int(flags) & BATCHING_STATIC:
                out("FAIL", p, "batching-static on a wind-shader prop — the shader reads the object's own "
                    "pivot, which batching erases; use occludee only (16)")
            if folder == "Plants":
                if any(("--- " + c) in text for c in COLLIDER_DOCS):
                    out("FAIL", p, "a plant carries a collider — 4,608 placed plants were solid physics "
                        "meshes; plants are walked through")
                if "m_CastShadows: 1" in text:
                    out("FAIL", p, "a plant casts shadows — every blade is drawn a second time into the "
                        "shadow map; plants receive shadows but never cast them")


# ---- A6  level-of-detail families ---------------------------------------------------------------------------
def check_lod_families(repo, out):
    d = os.path.join(repo, "unity", "Assets", "1_Authored", "Prefabs", "Props", "Outdoor")
    if not os.path.isdir(d):
        return
    families = {}
    for f in os.listdir(d):
        m = re.match(r"(.+)_LOD(\d+)\.prefab$", f)
        if m:
            families.setdefault(m.group(1), {})[int(m.group(2))] = os.path.join(d, f)
    for fam, levels in sorted(families.items()):
        if 0 not in levels or len(levels) < 2:
            continue
        text = read(levels[0])
        p = rel(repo, levels[0])
        if "LODGroup:" not in text:
            out("WARN", p, "a simplified copy exists (%s) but no LODGroup uses it — run "
                "LodGroupWirer.WireAllCore() through the live editor" % ", ".join("LOD%d" % k for k in sorted(levels) if k))
            continue
        for k in sorted(levels):
            if k and ('m_Name: LOD%d\n' % k) not in text:
                out("FAIL", p, "its LODGroup is missing level LOD%d that exists beside it" % k)


# ---- A7  project settings pinned ----------------------------------------------------------------------------
def check_settings(repo, out):
    urp = os.path.join(repo, "unity", "Assets", "Settings", "UniversalRP.asset")
    if os.path.isfile(urp):
        t = read(urp)
        p = rel(repo, urp)
        for key, cap in (("m_MainLightShadowmapResolution", MAX_MAIN_SHADOWMAP),
                         ("m_AdditionalLightsShadowmapResolution", MAX_ADDITIONAL_SHADOWMAP)):
            v = value(t, key)
            if v and int(v) > cap:
                out("FAIL", p, "%s: %s is above %d — at 18 units of shadow distance that is memory and fill "
                    "for nothing" % (key, v, cap))
    ps = os.path.join(repo, "unity", "ProjectSettings", "ProjectSettings.asset")
    if os.path.isfile(ps):
        t = read(ps)
        if value(t, "gpuSkinning") == "0":
            out("FAIL", rel(repo, ps), "gpuSkinning: 0 — every animated character is skinned on the CPU")
    qs = os.path.join(repo, "unity", "ProjectSettings", "QualitySettings.asset")
    if os.path.isfile(qs):
        t = read(qs)
        if re.search(r"^\s*skinWeights: 255$", t, re.M):
            out("FAIL", rel(repo, qs), "skinWeights: 255 (Unlimited) on a quality level — 4 bones is the ceiling")


# ---- A8  packages: every installed package is claimed by a line saying why --------------------------------
def check_packages(repo, out):
    manifest = os.path.join(repo, "unity", "Packages", "manifest.json")
    claims = os.path.join(repo, "tools", "packages-in-use.txt")
    if not os.path.isfile(manifest):
        return
    import json
    deps = json.load(open(manifest, encoding="utf-8")).get("dependencies", {})
    claimed = set()
    if os.path.isfile(claims):
        for line in read(claims).splitlines():
            line = line.strip()
            if line and not line.startswith("#"):
                claimed.add(line.split()[0])
    p = rel(repo, manifest)
    for name in sorted(deps):
        if name.startswith("com.unity.modules."):
            continue
        if name not in claimed:
            out("FAIL", p, "package %s is installed but tools/packages-in-use.txt does not say what uses it — "
                "an unclaimed package ships code and bloats the editor for nothing (eight did, on 2026-09-20)" % name)
    for name in sorted(claimed):
        if name not in deps and not name.startswith("com.unity.modules."):
            out("WARN", rel(repo, claims), "claims %s, which is no longer installed — delete the line" % name)


# ---- A9  every baked component carries every field its script declares ------------------------------------
NEVER_SERIALIZED_TYPES = ("Action", "Func", "Dictionary", "HashSet", "Queue", "Stack", "IEnumerator",
                          "Coroutine", "object", "delegate", "Task", "CancellationToken", "Thread")
FIELD = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*public\s+(?!static|const|readonly|event|class|struct|enum|interface|"
                   r"delegate|override|virtual|abstract|void)([\w<>\[\],\s\.]+?)\s+(\w+)\s*(?:=[^;]*)?;", re.M)


def class_body(cs_text, class_name):
    """Only the text at the component class's OWN brace depth — never a nested type's fields (they serialize
    nested, under their own key) and never another top-level class that shares the file."""
    m = re.search(r"\b(?:class|struct)\s+%s\b[^{]*\{" % re.escape(class_name), cs_text)
    if not m:
        return ""
    depth, out, i = 1, [], m.end()
    while i < len(cs_text) and depth > 0:
        ch = cs_text[i]
        if ch == "{":
            depth += 1
            if depth == 2:
                out.append("{")     # keep the brace itself, so a property or method still LOOKS like one
        elif ch == "}":
            depth -= 1
            if depth == 1:
                out.append("}")
        elif depth == 1:
            out.append(ch)
        i += 1
    return "".join(out)


def script_fields(cs_text, class_name):
    """The names Unity will serialize for this class: public instance fields not marked NonSerialized, of a
    type Unity can serialize. Conservative — anything doubtful is skipped rather than reported."""
    names = []
    for m in FIELD.finditer(class_body(cs_text, class_name)):
        whole = m.group(0)
        typ, name = m.group(1).strip(), m.group(2)
        if "NonSerialized" in whole or "HideInInspector" in whole and "SerializeField" not in whole:
            continue
        if "=>" in whole or "{" in whole:
            continue
        head = re.split(r"[<\[]", typ)[0].strip()
        if head in NEVER_SERIALIZED_TYPES or re.match(r"^I[A-Z]\w*$", head):
            continue
        names.append(name)
    return names


def check_baked_components(repo, out):
    scripts = os.path.join(repo, "unity", "Assets", "Scripts")
    # Both the hand-placed palette AND the bakers' output: baked prefabs are precisely where a dropped field
    # hides, because nobody opens them.
    prefab_roots = [os.path.join(repo, "unity", "Assets", "1_Authored", "Prefabs"),
                    os.path.join(repo, "unity", "Assets", "2_Generated")]
    if not os.path.isdir(scripts):
        return
    by_guid = {}
    for dirpath, _d, files in os.walk(scripts):
        if os.sep + "Editor" + os.sep in dirpath + os.sep:
            continue
        for f in files:
            if f.endswith(".cs.meta"):
                g = value(read(os.path.join(dirpath, f)), "guid")
                if g:
                    by_guid[g] = os.path.join(dirpath, f[:-5])
    fields_cache = {}
    for dirpath, _d, files in (w for r in prefab_roots if os.path.isdir(r) for w in os.walk(r)):
        for f in files:
            if not f.endswith(".prefab"):
                continue
            path = os.path.join(dirpath, f)
            text = read(path)
            for doc in re.split(r"^--- !u!\d+ &\d+\n", text, flags=re.M)[1:]:
                if not doc.startswith("MonoBehaviour:"):
                    continue
                m = re.search(r"m_Script: \{fileID: \d+, guid: (\w+)", doc)
                if not m or m.group(1) not in by_guid:
                    continue
                cs = by_guid[m.group(1)]
                if cs not in fields_cache:
                    fields_cache[cs] = script_fields(read(cs), os.path.basename(cs)[:-3])
                present = set(re.findall(r"^  (\w+):", doc, re.M))
                for name in fields_cache[cs]:
                    if name not in present:
                        out("FAIL", rel(repo, path), "component %s declares public field '%s' but the baked "
                            "prefab does not carry it — the field's type is not serializable, so the baker "
                            "silently dropped it (this is how every creature loaded at level 1)"
                            % (os.path.basename(cs)[:-3], name))
                        break   # one line per component is enough to send somebody to the script


# ---- A13  every character model has saved outfit parts ----------------------------------------------------------
CHARACTER_MODELS = ("unity", "Assets", "1_Authored", "Art", "Characters", "Models")
DYE_PARTS_TABLE = ("unity", "Assets", "3_Data", "Resources", "CharacterDyeParts.asset")


def check_dye_parts(repo, out):
    models = os.path.join(repo, *CHARACTER_MODELS)
    if not os.path.isdir(models):
        return
    table = os.path.join(repo, *DYE_PARTS_TABLE)
    saved = set(re.findall(r"^  - modelFile: (.+?)\s*$", read(table), re.M)) if os.path.isfile(table) else set()
    for f in sorted(os.listdir(models)):
        if not f.lower().endswith(".fbx"):
            continue
        if os.path.splitext(f)[0] not in saved:
            out("FAIL", rel(repo, os.path.join(models, f)), "character model has no saved outfit parts, so nobody "
                "wearing it can change its colours — run CharacterDyePartsBaker.BakeAll() in the editor")


CHARACTER_CLIPS = ("unity", "Assets", "1_Authored", "Art", "Characters", "Animations")


def check_clip_root_motion(repo, out):
    """Rule 14: an authored character clip (.anim) bakes the body's turn, height and travel into the pose. The game's
    characters never apply root motion, so whatever a clip leaves as root motion is thrown away in game."""
    folder = os.path.join(repo, *CHARACTER_CLIPS)
    if not os.path.isdir(folder):
        return
    for f in sorted(os.listdir(folder)):
        if not f.endswith(".anim"):
            continue
        path = os.path.join(folder, f)
        text = read(path)
        if "RootT.x" not in text:          # not a humanoid body clip: nothing to bake
            continue
        loose = [k for k in ("m_LoopBlendOrientation", "m_LoopBlendPositionY", "m_LoopBlendPositionXZ")
                 if value(text, k) != "1"]
        if loose:
            out("FAIL", rel(repo, path), "the body's movement is not baked into the pose (%s) — in game the body "
                "stays put and a planted foot slides; bake it in (AnimationUtility.SetAnimationClipSettings)" % ", ".join(loose))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=DEFAULT_REPO)
    ap.add_argument("--all", action="store_true", help="print WARN lines too")
    a = ap.parse_args()
    repo = os.path.abspath(a.root)
    if not os.path.isdir(os.path.join(repo, "unity", "Assets")):
        print("cannot run: %s has no unity/Assets" % repo)
        return 2

    fails, warns = [], []

    def out(level, path, what):
        (fails if level == "FAIL" else warns).append("%s %s: %s" % (level, path, what))

    check_textures(repo, out)
    check_ground_tile_folder(repo, out)
    check_props(repo, out)
    check_lod_families(repo, out)
    check_settings(repo, out)
    check_packages(repo, out)
    check_baked_components(repo, out)
    check_dye_parts(repo, out)
    check_clip_root_motion(repo, out)

    for line in fails:
        print(line)
    if a.all:
        for line in warns:
            print(line)
    if fails:
        print("\n%d asset rule(s) broken — the rules are unity/Assets/ASSETS.md." % len(fails))
        return 1
    print("asset rules clean%s." % (" (%d warning(s) — run with --all)" % len(warns) if warns and not a.all else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
