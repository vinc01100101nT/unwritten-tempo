#!/usr/bin/env python3
"""Prove tools/check-editor-writes.py still SEES an editor script that writes with no click — and is quiet
about the marked, legitimate ones.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This writes small
editor scripts into a scratch copy of the tree — each the exact shape of a real 2026-09-20 finding — and demands
the checker name them; then confirms the same scripts pass once their write carries the marker with a reason,
and that a click-only script (a menu item that saves) is never touched.

USAGE:   tools/prove-editor-writes-check.py
EXIT:    0 = every planted fault caught and the sound scripts are quiet, 1 = blind or crying wolf.
"""
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CHECK = os.path.join(HERE, "check-editor-writes.py")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

PLANTS = {
    # the effect-table baker: editor load -> delayCall -> a method that saves an asset
    "SavesOnLoad.cs": """using UnityEditor;
public static class SavesOnLoad
{
    [InitializeOnLoadMethod]
    static void Go() { EditorApplication.delayCall += () => Bake(); }
    static void Bake() { var o = Resources.Load<UnityEngine.Object>("x"); EditorUtility.SetDirty(o); AssetDatabase.SaveAssetIfDirty(o); }
}
""",
    # the portal guard: a scene-save hook that moves a transform
    "MovesOnSave.cs": """using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine; using UnityEngine.SceneManagement;
[InitializeOnLoad]
static class MovesOnSave
{
    static MovesOnSave() { EditorSceneManager.sceneSaving += OnSaving; }
    static void OnSaving(Scene s, string p) { foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) t.position = Vector3.zero; }
}
""",
    # an import hook that moves assets and refreshes
    "MovesOnImport.cs": """using UnityEditor;
class MovesOnImport : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] a, string[] b, string[] c, string[] d) { Tidy(); }
    static void Tidy() { AssetDatabase.MoveAsset("Assets/a", "Assets/b"); AssetDatabase.Refresh(); }
}
""",
}
CLEAN = {
    # a menu item that saves — a click, so never a finding
    "SavesOnClick.cs": """using UnityEditor;
public static class SavesOnClick
{
    [MenuItem("Tools/unwritten/Prove/Save")]
    static void Save() { var o = Resources.Load<UnityEngine.Object>("x"); EditorUtility.SetDirty(o); AssetDatabase.SaveAssets(); }
}
""",
    # the same load-time shape, with its write explained
    "MarkedOnLoad.cs": """using UnityEditor;
public static class MarkedOnLoad
{
    [InitializeOnLoadMethod]
    static void Go() { EditorApplication.delayCall += () => Bake(); }
    static void Bake() { var o = Resources.Load<UnityEngine.Object>("x");
        // editor-write-ok: proven fixture — the write is behind a flag only the menu item sets
        EditorUtility.SetDirty(o); }
}
""",
    # a window that ticks a preview on update AND saves on a button: the button is a click
    "TicksAndSaves.cs": """using UnityEditor; using UnityEngine;
public class TicksAndSaves : EditorWindow
{
    void OnEnable() { EditorApplication.update += Tick; }
    void Tick() { Repaint(); }
    void OnGUI() { if (GUILayout.Button("Save")) { var o = Resources.Load<UnityEngine.Object>("x"); EditorUtility.SetDirty(o); AssetDatabase.SaveAssets(); } }
}
""",
}
RUNTIME_BAD = {
    "AlwaysWrites.cs": """using UnityEngine;
[ExecuteAlways]
public class AlwaysWrites : MonoBehaviour { void LateUpdate() { transform.position = Vector3.zero; } }
""",
}
RUNTIME_OK = {
    "AlwaysGuarded.cs": """using UnityEngine;
[ExecuteAlways]
public class AlwaysGuarded : MonoBehaviour { void LateUpdate() { if (!Application.isPlaying) return; transform.position = Vector3.zero; } }
""",
    # the words in a comment on a plain static class are not an attribute
    "MentionsInComment.cs": """/// so [ExecuteAlways] sorters can read this
public static class MentionsInComment { public static int Frame; }
""",
}


def write_tree(root, editor, runtime):
    ed = os.path.join(root, "unity", "Assets", "Scripts", "Editor")
    rt = os.path.join(root, "unity", "Assets", "Scripts")
    os.makedirs(ed, exist_ok=True)
    for name, text in editor.items():
        with open(os.path.join(ed, name), "w", encoding="utf-8") as f:
            f.write(text)
    for name, text in runtime.items():
        with open(os.path.join(rt, name), "w", encoding="utf-8") as f:
            f.write(text)


def run(root):
    done = subprocess.run([sys.executable, CHECK, "--root", root], capture_output=True, text=True)
    return done.returncode, done.stdout


def main():
    failures = 0
    root = tempfile.mkdtemp(prefix="editor-writes-prove-")
    try:
        write_tree(root, CLEAN, RUNTIME_OK)
        code, out = run(root)
        quiet = code == 0
        print((GRN + "quiet   " if quiet else RED + "WOLF    ") + "click-only, marked, and guarded scripts" + OFF)
        if not quiet:
            failures += 1
            print(out)
        for name, text in list(PLANTS.items()) + list(RUNTIME_BAD.items()):
            case = tempfile.mkdtemp(prefix="editor-writes-case-")
            try:
                is_editor = name in PLANTS
                write_tree(case, CLEAN | ({name: text} if is_editor else {}), RUNTIME_OK | ({} if is_editor else {name: text}))
                code, out = run(case)
                named = code == 1 and name in out
                print((GRN + "caught  " if named else RED + "MISSED  ") + name + OFF)
                if not named:
                    failures += 1
                    print(out)
            finally:
                shutil.rmtree(case, ignore_errors=True)
    finally:
        shutil.rmtree(root, ignore_errors=True)
    total = 1 + len(PLANTS) + len(RUNTIME_BAD)
    if failures:
        print(RED + "%d of %d case(s) failed — the editor-writes checker is blind somewhere." % (failures, total) + OFF)
        return 1
    print(GRN + "All %d cases right: the editor-writes checker sees every unclicked write and lets the explained ones through." % total + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
