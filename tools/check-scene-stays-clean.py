#!/usr/bin/env python3
"""Check that nothing saved into a scene can mark that scene modified on its own — the cause of every build
stopping at "Modified Scenes must be saved to continue".

WHY THIS EXISTS: our own code is already held to RULE NOEDITORWRITES by tools/check-editor-writes.py. This covers
the other half — Unity's own components that rearrange things while the game is NOT playing. Measured 2026-10-09:
a row layout baked onto the hotbar in the saved HUD made Unity reset the four hotbar cells after every code
recompile, which marked the Systems scene modified with not one saved byte changed. Every code edit therefore
re-armed the build prompt. A layout component works only by moving the things inside it, so none of the family is
allowed in saved data; the code that builds a window adds its layout at runtime instead.

WHAT IS CHECKED: every scene under unity/Assets/Scenes, and every prefab those scenes place (followed through
prefabs placed inside prefabs), because a placed prefab's contents load into the scene.

  S1  No layout component (row, column, grid, size fitter, aspect fitter, scroll view) saved in either.
  S2  No text saved without a font. A fontless text is given the default font the moment it switches on in the
      editor, which writes a change into the scene (measured: the Systems scene carried exactly that change).

USAGE:   tools/check-scene-stays-clean.py [--root DIR]
EXIT:    0 = clean, 1 = a FAIL line printed, 2 = could not run (says why)

Every fault line names the file, in the form the end-of-turn gate reads:  FAIL <path>: <what>
"""
import argparse
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)

# Script ids read from the UI package's own .cs.meta files (com.unity.ugui, 2026-10-09).
LAYOUT_SCRIPTS = {
    "30649d3a9faa99c48a7b1166b86bf2a0": "a row layout (HorizontalLayoutGroup)",
    "59f8146938fff824cb5fd77236b75775": "a column layout (VerticalLayoutGroup)",
    "8a8695521f0d02e499659fee002a26c2": "a grid layout (GridLayoutGroup)",
    "3245ec927659c4140ac4f8d17403cc18": "a size fitter (ContentSizeFitter)",
    "86710e43de46f6f4bac7c8e50813a599": "an aspect fitter (AspectRatioFitter)",
    "1aa08ab6e0800fa44ae55d278d1423e3": "a scroll view (ScrollRect)",
}
TEXT_SCRIPTS = {"f4688fdb7df04437aeb418b961361dc5", "9541d86e2fd84c1d9990edf0852d74ab"}   # TMP UI text, TMP 3D text


def rel(repo, path):
    return os.path.relpath(path, repo).replace(os.sep, "/")


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def object_names(text):
    """fileID -> GameObject name, so a fault line says WHICH object, not only which file."""
    names = {}
    for m in re.finditer(r"^--- !u!1 &(\d+).*\n((?:(?!^--- ).*\n)*)", text, re.M):
        n = re.search(r"^  m_Name: (.*)$", m.group(2), re.M)
        names[m.group(1)] = n.group(1).strip() if n else "?"
    return names


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=DEFAULT_REPO)
    a = ap.parse_args()
    repo = os.path.abspath(a.root)
    assets = os.path.join(repo, "unity", "Assets")
    scenes_dir = os.path.join(assets, "Scenes")
    if not os.path.isdir(scenes_dir):
        print("cannot run: %s has no unity/Assets/Scenes" % repo)
        return 2

    prefab_by_guid = {}
    for dirpath, _d, files in os.walk(assets):
        for f in files:
            if f.endswith(".prefab.meta"):
                m = re.search(r"^guid: (\w+)", read(os.path.join(dirpath, f)), re.M)
                if m:
                    prefab_by_guid[m.group(1)] = os.path.join(dirpath, f[:-5])

    todo = [os.path.join(d, f) for d, _x, fs in os.walk(scenes_dir) for f in fs if f.endswith(".unity")]
    if not todo:
        print("cannot run: no scenes under %s" % rel(repo, scenes_dir))
        return 2
    seen, fails = set(), []
    while todo:
        path = todo.pop()
        if path in seen or not os.path.isfile(path):
            continue
        seen.add(path)
        text = read(path)
        for g in set(re.findall(r"guid: (\w{32}), type: 3\}", text)):
            if g in prefab_by_guid:
                todo.append(prefab_by_guid[g])
        names = object_names(text)
        for doc in re.split(r"^--- !u!\d+ &\d+.*\n", text, flags=re.M)[1:]:
            if not doc.startswith("MonoBehaviour:"):
                continue
            s = re.search(r"m_Script: \{fileID: \d+, guid: (\w+)", doc)
            if not s:
                continue
            go = re.search(r"m_GameObject: \{fileID: (\d+)", doc)
            who = names.get(go.group(1), "?") if go else "?"
            if s.group(1) in LAYOUT_SCRIPTS:
                fails.append("FAIL %s: '%s' has %s saved on it — it rearranges what is inside it in the editor too, "
                             "which marks the scene modified and stops every build; add it at runtime instead"
                             % (rel(repo, path), who, LAYOUT_SCRIPTS[s.group(1)]))
            if s.group(1) in TEXT_SCRIPTS and re.search(r"^  m_fontAsset: \{fileID: 0\}", doc, re.M):
                fails.append("FAIL %s: text '%s' is saved with no font — the editor gives it one the moment it "
                             "switches on, which marks the scene modified; save it with a font"
                             % (rel(repo, path), who))

    for line in sorted(set(fails)):
        print(line)
    if fails:
        print("\n%d thing(s) saved into a scene can mark it modified on their own — see the header of "
              "tools/check-scene-stays-clean.py." % len(set(fails)))
        return 1
    print("scenes stay clean — %d scene(s) and %d placed prefab(s) hold nothing that rewrites itself in the editor."
          % (sum(1 for p in seen if p.endswith(".unity")), sum(1 for p in seen if p.endswith(".prefab"))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
