#!/usr/bin/env python3
"""Find editor code that can WRITE saved data with nobody having asked — the project's highest-priority rule,
mechanically.

WHY THIS EXISTS: the rule "nothing changes a scene, prefab or asset unless the user just clicked" has been in
the always-loaded instructions for weeks, and the 2026-09-20 audit still found three scripts breaking it: one
re-measured 107 effects and saved the table on every editor load, one moved portals on every scene save with no
undo and no Play guard, one rewrote 1,800 texture settings on any reimport. A rule that lives only in prose gets
broken by code that was written in good faith. This reads the code.

THE RULE, as a test:
  E1  In unity/Assets/Scripts/Editor, a file that has an AUTOMATIC TRIGGER — something that runs with no click:
        [InitializeOnLoad], [InitializeOnLoadMethod], an AssetPostprocessor, EditorSceneManager.sceneSaving /
        sceneOpened, EditorApplication.update / delayCall / playModeStateChanged, Selection.selectionChanged,
        hierarchyChanged, projectChanged
      may not contain a WRITE —
        SetDirty, SaveAssets, SaveAssetIfDirty, SaveAsPrefabAsset, SavePrefabAsset, SaveScene, MarkSceneDirty,
        MarkAllScenesDirty, CreateAsset, MoveAsset, DeleteAsset, AssetDatabase.Refresh, DestroyImmediate,
        PlayerSettings.Set*, or an assignment to .position / .localPosition / .rotation / .localScale
      unless that write's line (or the line above it) says WHY it is a user's action:
          // editor-write-ok: <why>
      The marker is the whole contract, exactly as `// perf-ok:` is for PERFORMANCE.md: the reason sits beside
      the write, where the next reader can argue with it.
  E2  A runtime script marked [ExecuteAlways] or [ExecuteInEditMode] must test Application.isPlaying somewhere,
      because everything it writes outside Play is a saved byte.

USAGE:   tools/check-editor-writes.py [--root DIR]
EXIT:    0 = clean, 1 = a FAIL printed, 2 = could not run
Fault lines name the file and line:  FAIL <path>(<line>): <what>
"""
import argparse
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)

TRIGGERS = [
    (r"\[InitializeOnLoad\]", "[InitializeOnLoad]"),
    (r"\[InitializeOnLoadMethod\]", "[InitializeOnLoadMethod]"),
    (r":\s*AssetPostprocessor\b", "an AssetPostprocessor"),
    (r"EditorSceneManager\.sceneSaving\s*\+=", "sceneSaving"),
    (r"EditorSceneManager\.sceneOpened\s*\+=", "sceneOpened"),
    (r"EditorApplication\.update\s*\+=", "EditorApplication.update"),
    (r"EditorApplication\.delayCall\s*\+=", "EditorApplication.delayCall"),
    (r"EditorApplication\.playModeStateChanged\s*\+=", "playModeStateChanged"),
    (r"Selection\.selectionChanged\s*\+=", "selectionChanged"),
    (r"EditorApplication\.hierarchyChanged\s*\+=", "hierarchyChanged"),
    (r"EditorApplication\.projectChanged\s*\+=", "projectChanged"),
]
WRITES = [
    r"EditorUtility\.SetDirty\(", r"AssetDatabase\.SaveAssets\(", r"SaveAssetIfDirty\(", r"SaveAsPrefabAsset\(",
    r"SavePrefabAsset\(", r"EditorSceneManager\.SaveScene\(", r"MarkSceneDirty\(", r"MarkAllScenesDirty\(",
    r"AssetDatabase\.CreateAsset\(", r"AssetDatabase\.MoveAsset\(", r"AssetDatabase\.DeleteAsset\(",
    r"AssetDatabase\.Refresh\(", r"DestroyImmediate\(", r"PlayerSettings\.Set\w+\(",
    r"\.(?:position|localPosition|rotation|localRotation|localScale)\s*=[^=]",
]
MARKER = "editor-write-ok:"
WRITE_RE = re.compile("|".join(WRITES))


sys.path.insert(0, HERE)
from cs_methods import strip_comments, methods_of, reachable, METHOD_HEAD, CALL   # the one C# reader

TRIGGER_EVENTS = r"(?:EditorSceneManager\.sceneSaving|EditorSceneManager\.sceneOpened|EditorApplication\.update|" \
                 r"EditorApplication\.delayCall|EditorApplication\.playModeStateChanged|Selection\.selectionChanged|" \
                 r"EditorApplication\.hierarchyChanged|EditorApplication\.projectChanged)\s*\+=\s*"


def trigger_roots(code, methods):
    """The methods (and inline lambda bodies) that run with NO click: what each automatic trigger points at."""
    roots, why = [], []
    for m in re.finditer(r"\[InitializeOnLoadMethod\]", code):
        nxt = METHOD_HEAD.search(code, m.end())
        if nxt:
            roots.append(nxt.group(1)); why.append("[InitializeOnLoadMethod]")
    if re.search(r"\[InitializeOnLoad\]", code):
        cls = re.search(r"class\s+(\w+)", code)
        if cls and cls.group(1) in methods:
            roots.append(cls.group(1)); why.append("[InitializeOnLoad] (static constructor)")
    if re.search(r":\s*AssetPostprocessor\b", code):
        for name in methods:
            if re.match(r"On(?:Pre|Post)process\w*", name):
                roots.append(name); why.append("AssetPostprocessor." + name)
    for m in re.finditer(TRIGGER_EVENTS + r"([^;]+);", code):
        target = m.group(1).strip()
        ev = m.group(0).split("+=")[0].strip()
        if re.match(r"^\w+$", target):
            roots.append(target); why.append(ev)
        else:                                          # a lambda: every method it calls is a root
            for c in CALL.findall(target):
                if c in methods:
                    roots.append(c); why.append(ev + " (lambda)")
    return roots, why


def check_editor_folder(repo, out):
    root = os.path.join(repo, "unity", "Assets", "Scripts", "Editor")
    if not os.path.isdir(root):
        return
    for dirpath, _d, files in os.walk(root):
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            path = os.path.join(dirpath, f)
            rel = os.path.relpath(path, repo).replace(os.sep, "/")
            with open(path, encoding="utf-8", errors="replace") as fh:
                lines = fh.read().splitlines()
            code = "\n".join(strip_comments(l) for l in lines)
            methods = methods_of(code)
            roots, why = trigger_roots(code, methods)
            if not roots:
                continue
            hot = reachable(methods, roots)
            for name in sorted(hot):
                for start, body_start, body, _head in methods[name]:
                    for k, line in enumerate(body.split("\n")):
                        if not WRITE_RE.search(line):
                            continue
                        ln = body_start + k
                        src = lines[ln - 1] if 0 < ln <= len(lines) else ""
                        marked = MARKER in src or (ln > 1 and MARKER in lines[ln - 2])
                        if not marked:
                            out("FAIL", "%s(%d)" % (rel, ln),
                                "writes saved data on a path that runs with no click (%s reaches %s) — put "
                                "`// %s <why this write is the user's own action>` on this line, or move the "
                                "write behind a menu item" % (" / ".join(sorted(set(why))), name, MARKER))


def check_execute_always(repo, out):
    root = os.path.join(repo, "unity", "Assets", "Scripts")
    if not os.path.isdir(root):
        return
    for dirpath, _d, files in os.walk(root):
        if os.sep + "Editor" in dirpath:
            continue
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            path = os.path.join(dirpath, f)
            with open(path, encoding="utf-8", errors="replace") as fh:
                text = "\n".join(strip_comments(l) for l in fh.read().splitlines())
            m = re.search(r"\[(ExecuteAlways|ExecuteInEditMode)\]", text)
            if not m:
                continue
            rel = os.path.relpath(path, repo).replace(os.sep, "/")
            line = text[:m.start()].count("\n") + 1
            if "Application.isPlaying" not in text:
                out("FAIL", "%s(%d)" % (rel, line),
                    "[%s] runs in the editor and never tests Application.isPlaying — every write it makes "
                    "outside Play is a saved byte the user did not ask for" % m.group(1))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=DEFAULT_REPO)
    a = ap.parse_args()
    repo = os.path.abspath(a.root)
    if not os.path.isdir(os.path.join(repo, "unity", "Assets", "Scripts")):
        print("cannot run: %s has no unity/Assets/Scripts" % repo)
        return 2
    fails = []

    def out(level, where, what):
        fails.append("%s %s: %s" % (level, where, what))

    check_editor_folder(repo, out)
    check_execute_always(repo, out)
    for line in fails:
        print(line)
    if fails:
        print("\n%d editor write(s) that nobody clicked for — the rule is RULE NOEDITORWRITES in CLAUDE.md." % len(fails))
        return 1
    print("editor writes clean — every write in an auto-running editor file says why it is the user's own action.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
