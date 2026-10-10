#!/usr/bin/env python3
"""Prove tools/check-scene-stays-clean.py still SEES every fault it exists for — and is quiet about a sound project.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This copies the real
Systems scene and the HUD prefab it places into a scratch folder (nothing in the repo is touched), plants one known
fault at a time, and demands the checker name it: the exact row layout that stopped every build on 2026-10-09, a
layout saved straight into a scene, one inside a prefab placed by a placed prefab, and a text saved with no font.

USAGE:   tools/prove-scene-stays-clean-check.py
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
CHECK = os.path.join(HERE, "check-scene-stays-clean.py")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

SCENE = "unity/Assets/Scenes/Systems.unity"
HUD = "unity/Assets/3_Data/Resources/GameUICanvas.prefab"
FIXTURE = [SCENE, HUD, HUD + ".meta"]
ROW_LAYOUT = "30649d3a9faa99c48a7b1166b86bf2a0"
TMP_TEXT = "f4688fdb7df04437aeb418b961361dc5"


def layout_doc(file_id, go_id):
    return ("--- !u!114 &%s\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_GameObject: {fileID: %s}\n  m_Enabled: 1\n"
            "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n  m_Spacing: 6\n" % (file_id, go_id, ROW_LAYOUT))


def run(root):
    p = subprocess.run([sys.executable, CHECK, "--root", root], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    return p.returncode, p.stdout + p.stderr


def edit(root, relp, fn):
    path = os.path.join(root, relp)
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(fn(text))


def first_go(text):
    return re.search(r"^--- !u!1 &(\d+)", text, re.M).group(1)


def main():
    cases = []

    def plant_hud_row(t):
        return t + layout_doc("990000000000000001", first_go(t))

    def plant_scene_row(t):
        return t + "--- !u!1 &990000000000000010\nGameObject:\n  m_Name: Planted\n" + layout_doc("990000000000000011", "990000000000000010")

    def plant_fontless(t):
        return t + ("--- !u!114 &990000000000000002\nMonoBehaviour:\n  m_GameObject: {fileID: %s}\n"
                    "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n  m_fontAsset: {fileID: 0}\n" % (first_go(t), TMP_TEXT))

    cases.append(("the row layout that stopped every build, in the HUD the scene places", HUD, plant_hud_row,
                  "GameUICanvas.prefab"))
    cases.append(("a layout saved straight into a scene", SCENE, plant_scene_row, "Systems.unity"))
    cases.append(("a text saved with no font", HUD, plant_fontless, "GameUICanvas.prefab"))

    failed = 0
    for what, relp, plant, must_name in cases:
        with tempfile.TemporaryDirectory() as root:
            for f in FIXTURE:
                dst = os.path.join(root, f)
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy2(os.path.join(REPO, f), dst)
            edit(root, relp, plant)
            code, out = run(root)
            if code == 1 and must_name in out and "FAIL" in out:
                print(GRN + "caught: " + what + OFF)
            else:
                print(RED + "BLIND to: " + what + OFF + "\n" + out)
                failed += 1

    # A prefab placed only by another placed prefab: the checker must follow the chain, not stop at the scene.
    with tempfile.TemporaryDirectory() as root:
        for f in FIXTURE:
            dst = os.path.join(root, f)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(os.path.join(REPO, f), dst)
        inner = "unity/Assets/Planted/Inner.prefab"
        os.makedirs(os.path.join(root, "unity/Assets/Planted"), exist_ok=True)
        with open(os.path.join(root, inner), "w", encoding="utf-8") as f:
            f.write("%YAML 1.1\n--- !u!1 &990000000000000020\nGameObject:\n  m_Name: Inner\n"
                    + layout_doc("990000000000000021", "990000000000000020"))
        with open(os.path.join(root, inner + ".meta"), "w", encoding="utf-8") as f:
            f.write("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n")
        edit(root, HUD, lambda t: t + "--- !u!1001 &990000000000000030\nPrefabInstance:\n  m_SourcePrefab: "
                                      "{fileID: 100100000, guid: 0123456789abcdef0123456789abcdef, type: 3}\n")
        code, out = run(root)
        if code == 1 and "Inner.prefab" in out:
            print(GRN + "caught: a layout inside a prefab placed by a placed prefab" + OFF)
        else:
            print(RED + "BLIND to: a layout inside a prefab placed by a placed prefab" + OFF + "\n" + out)
            failed += 1

    with tempfile.TemporaryDirectory() as root:
        for f in FIXTURE:
            dst = os.path.join(root, f)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(os.path.join(REPO, f), dst)
        code, out = run(root)
        if code == 0:
            print(GRN + "quiet on the sound copy" + OFF)
        else:
            print(RED + "CRIES WOLF on the sound copy:" + OFF + "\n" + out)
            failed += 1

    if failed:
        print(RED + "\nthe scene-stays-clean checker cannot be trusted until this passes" + OFF)
        return 1
    print(GRN + "\nthe scene-stays-clean checker sees every planted fault" + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
