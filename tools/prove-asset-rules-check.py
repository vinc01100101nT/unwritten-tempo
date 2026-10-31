#!/usr/bin/env python3
"""Prove tools/check-asset-rules.py still SEES every fault it exists for — and is quiet about a sound project.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. Both are silence,
and only one of them is good news. This builds a small COPY of the real project in a scratch folder (nothing
in the repo is ever touched), plants one known fault at a time into that copy, confirms the checker names the
file, then confirms the untouched copy passes. The baked-component case is the one that matters most: it
re-creates the exact shape that had every creature loading at level 1 — a component whose field is missing
from the saved prefab — and demands the checker say so.

USAGE:   tools/prove-asset-rules-check.py
EXIT:    0 = every planted fault caught and the sound copy is quiet, 1 = blind or crying wolf.
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CHECK = os.path.join(HERE, "check-asset-rules.py")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

# The smallest slice of the real project that exercises every rule. Paths are relative to the repo.
FIXTURE = [
    "unity/Assets/Settings/UniversalRP.asset",
    "unity/ProjectSettings/ProjectSettings.asset",
    "unity/ProjectSettings/QualitySettings.asset",
    "unity/Packages/manifest.json",
    "tools/packages-in-use.txt",
    "unity/Assets/Shaders/FoliageWind.shader.meta",
    "unity/Assets/1_Authored/Prefabs/Props/Materials/Plant (wind).mat",
    "unity/Assets/1_Authored/Prefabs/Props/Materials/Plant (wind).mat.meta",
    "unity/Assets/1_Authored/Art/GroundTiles/BASE_OR_PATCH_OR_PATH_soil.png.meta",
    "unity/Assets/1_Authored/Prefabs/Weapons/Arrow/tex_blue.png.meta",
    "unity/Assets/1_Authored/Prefabs/Props/Plants/Grass1.prefab",
    "unity/Assets/1_Authored/Prefabs/Props/Outdoor/Stone1_LOD0.prefab",
    "unity/Assets/1_Authored/Prefabs/Props/Outdoor/Stone1_LOD1.prefab",
    "unity/Assets/1_Authored/Prefabs/Props/Outdoor/Rock1.prefab",
    "unity/Assets/Scripts/UnitStatBlock.cs",
    "unity/Assets/Scripts/UnitStatBlock.cs.meta",
    "unity/Assets/2_Generated/MobPrefabs/Mobs/Adder.prefab",
    "unity/Assets/3_Data/Resources/CharacterDyeParts.asset",
]

# A character model the saved outfit-parts table DOES list. Only its name is read, so the copy is an empty file.
LISTED_MODEL = "unity/Assets/1_Authored/Art/Characters/Models/Chef_Female.fbx"


def build_copy(root):
    for relp in FIXTURE:
        src = os.path.join(REPO, relp)
        if not os.path.isfile(src):
            print(RED + "fixture file missing from the real project: " + relp + OFF)
            sys.exit(1)
        dst = os.path.join(root, relp)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)
    listed = os.path.join(root, LISTED_MODEL)
    os.makedirs(os.path.dirname(listed), exist_ok=True)
    open(listed, "wb").close()


def run(root):
    done = subprocess.run([sys.executable, CHECK, "--root", root], capture_output=True, text=True)
    return done.returncode, done.stdout


def edit(root, relp, fn):
    p = os.path.join(root, relp)
    with open(p, encoding="utf-8") as f:
        text = f.read()
    new = fn(text)
    assert new != text, "the plant changed nothing in " + relp
    with open(p, "w", encoding="utf-8") as f:
        f.write(new)


def main():
    failures = 0
    base = tempfile.mkdtemp(prefix="asset-rules-prove-")
    try:
        build_copy(base)
        code, out = run(base)
        if code != 0:
            print(RED + "the sound copy is NOT quiet — the checker cries wolf:\n" + out + OFF)
            return 1
        print(GRN + "sound copy: quiet" + OFF)

        cases = [
            ("uncompressed texture",
             "unity/Assets/1_Authored/Art/GroundTiles/BASE_OR_PATCH_OR_PATH_soil.png.meta",
             lambda t: re.sub(r"(buildTarget: DefaultTexturePlatform\n(?:.*\n){3}    textureCompression: )1", r"\g<1>0", t, 1)),
            ("oversized texture",
             "unity/Assets/1_Authored/Art/GroundTiles/BASE_OR_PATCH_OR_PATH_soil.png.meta",
             lambda t: re.sub(r"(buildTarget: DefaultTexturePlatform\n    maxTextureSize: )\d+", r"\g<1>4096", t, 1)),
            ("ground tile not at the one tile size",
             "unity/Assets/1_Authored/Art/GroundTiles/BASE_OR_PATCH_OR_PATH_soil.png.meta",
             lambda t: re.sub(r"(buildTarget: DefaultTexturePlatform\n    maxTextureSize: )\d+", r"\g<1>2048", t, 1)),
            ("ground tile carrying alpha",
             "unity/Assets/1_Authored/Art/GroundTiles/BASE_OR_PATCH_OR_PATH_soil.png.meta",
             lambda t: t.replace("  alphaUsage: 0\n", "  alphaUsage: 1\n", 1)),
            ("uncompressed texture outside the authored folder",
             "unity/Assets/1_Authored/Prefabs/Weapons/Arrow/tex_blue.png.meta",
             lambda t: re.sub(r"(buildTarget: DefaultTexturePlatform\n(?:.*\n){3}    textureCompression: )1", r"\g<1>0", t, 1)),
            ("readable texture",
             "unity/Assets/1_Authored/Art/GroundTiles/BASE_OR_PATCH_OR_PATH_soil.png.meta",
             lambda t: t.replace("  isReadable: 0\n", "  isReadable: 1\n", 1)),
            ("plant casting shadows",
             "unity/Assets/1_Authored/Prefabs/Props/Plants/Grass1.prefab",
             lambda t: t.replace("  m_CastShadows: 0\n", "  m_CastShadows: 1\n", 1)),
            ("plant with a collider",
             "unity/Assets/1_Authored/Prefabs/Props/Plants/Grass1.prefab",
             lambda t: t + "--- !u!64 &999\nMeshCollider:\n  m_GameObject: {fileID: 0}\n"),
            ("prop with no static flags",
             "unity/Assets/1_Authored/Prefabs/Props/Outdoor/Rock1.prefab",
             lambda t: t.replace("  m_StaticEditorFlags: 22\n", "  m_StaticEditorFlags: 0\n", 1)),
            ("batching-static foliage",
             "unity/Assets/1_Authored/Prefabs/Props/Plants/Grass1.prefab",
             lambda t: t.replace("  m_StaticEditorFlags: 16\n", "  m_StaticEditorFlags: 22\n", 1)),
            ("LOD group missing a level that exists beside it",
             "unity/Assets/1_Authored/Prefabs/Props/Outdoor/Stone1_LOD0.prefab",
             # Stone1 is wired (its LODGroup names LOD1 and LOD2), so the fault is a level dropped from that group.
             lambda t: t.replace("  m_Name: LOD1\n", "  m_Name: LODX\n", 1)),
            ("shadow map above the pin",
             "unity/Assets/Settings/UniversalRP.asset",
             lambda t: t.replace("  m_MainLightShadowmapResolution: 1024\n", "  m_MainLightShadowmapResolution: 4096\n", 1)),
            ("GPU skinning off",
             "unity/ProjectSettings/ProjectSettings.asset",
             lambda t: t.replace("  gpuSkinning: 1\n", "  gpuSkinning: 0\n", 1)),
            ("unlimited skin weights",
             "unity/ProjectSettings/QualitySettings.asset",
             lambda t: t.replace("    skinWeights: 4\n", "    skinWeights: 255\n", 1)),
            ("unclaimed package",
             "unity/Packages/manifest.json",
             lambda t: t.replace('"dependencies": {', '"dependencies": {\n    "com.example.nobody-uses-this": "1.0.0",', 1)),
            ("baked component missing a declared field (the level-1 creatures)",
             "unity/Assets/2_Generated/MobPrefabs/Mobs/Adder.prefab",
             lambda t: re.sub(r"^  inputs:\n(?:    .*\n)+", "", t, count=1, flags=re.M)),
        ]
        for name, relp, fn in cases:
            root = tempfile.mkdtemp(prefix="asset-rules-case-")
            try:
                shutil.copytree(base, root, dirs_exist_ok=True)
                edit(root, relp, fn)
                code, out = run(root)
                named = code == 1 and os.path.basename(relp) in out
                print((GRN + "caught  " if named else RED + "MISSED  ") + name + OFF)
                if not named:
                    failures += 1
                    print(out)
            finally:
                shutil.rmtree(root, ignore_errors=True)

        # A stray file in the ground tile folder (the leftover material that sat beside the plaza tile, BASE_OR_SIDEWALK_plaza).
        root = tempfile.mkdtemp(prefix="asset-rules-case-")
        try:
            shutil.copytree(base, root, dirs_exist_ok=True)
            stray = os.path.join(root, "unity/Assets/1_Authored/Art/GroundTiles/stray_tile.mat")
            with open(stray, "w", encoding="utf-8") as f:
                f.write("%YAML 1.1\n")
            code, out = run(root)
            named = code == 1 and "stray_tile.mat" in out
            print((GRN + "caught  " if named else RED + "MISSED  ") + "a non-tile file in the ground tile folder" + OFF)
            if not named:
                failures += 1
                print(out)
        finally:
            shutil.rmtree(root, ignore_errors=True)

        # A character model added with no saved outfit parts: nobody wearing it could change its colours.
        root = tempfile.mkdtemp(prefix="asset-rules-case-")
        try:
            shutil.copytree(base, root, dirs_exist_ok=True)
            open(os.path.join(root, os.path.dirname(LISTED_MODEL), "Unbaked_Model.fbx"), "wb").close()
            code, out = run(root)
            named = code == 1 and "Unbaked_Model.fbx" in out and "Chef_Female.fbx" not in out
            print((GRN + "caught  " if named else RED + "MISSED  ") + "a character model with no saved outfit parts" + OFF)
            if not named:
                failures += 1
                print(out)
        finally:
            shutil.rmtree(root, ignore_errors=True)

        # A character clip that leaves the body's movement as root motion: in game the body stays put and a foot slides.
        root = tempfile.mkdtemp(prefix="asset-rules-case-")
        try:
            shutil.copytree(base, root, dirs_exist_ok=True)
            clips = os.path.join(root, "unity/Assets/1_Authored/Art/Characters/Animations")
            os.makedirs(clips, exist_ok=True)
            def clip(name, flag):
                with open(os.path.join(clips, name), "w", encoding="utf-8") as f:
                    f.write("AnimationClip:\n  m_EditorCurves:\n  - attribute: RootT.x\n  m_AnimationClipSettings:\n"
                            "    m_LoopBlendOrientation: %d\n    m_LoopBlendPositionY: %d\n    m_LoopBlendPositionXZ: %d\n" % (flag, flag, flag))
            clip("Unbaked_Attack.anim", 0)
            clip("Baked_Attack.anim", 1)
            code, out = run(root)
            named = code == 1 and "Unbaked_Attack.anim" in out and "Baked_Attack.anim" not in out.replace("Unbaked_Attack.anim", "")
            print((GRN + "caught  " if named else RED + "MISSED  ") + "a character clip that leaves the body's movement as root motion" + OFF)
            if not named:
                failures += 1
                print(out)
        finally:
            shutil.rmtree(root, ignore_errors=True)

        # The one rule that must only WARN: an unwired family is debt, not a red turn.
        code, out = run(base)
        lod_quiet = code == 0
        print((GRN + "quiet   " if lod_quiet else RED + "WRONG   ") + "an unwired LOD family warns without failing" + OFF)
        if not lod_quiet:
            failures += 1
    finally:
        shutil.rmtree(base, ignore_errors=True)

    total = len(cases) + 5   # every planted fault, the stray tile-folder file, the unbaked model, the unbaked clip, the sound copy, the warn-only LOD case
    if failures:
        print(RED + "%d of %d case(s) failed — the asset-rules checker is blind somewhere." % (failures, total) + OFF)
        return 1
    print(GRN + "All %d cases right: the asset-rules checker sees every planted fault and is quiet on a sound copy." % total + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
