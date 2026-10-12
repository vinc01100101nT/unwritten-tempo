#!/usr/bin/env python3
"""Check that every shader the GAME looks up by name at runtime is guaranteed to be in a built game.

WHY THIS EXISTS: a built game carries only the shaders some saved material uses, plus the ones on Project Settings >
Graphics > Always Included Shaders. Shader.Find of any other shader works in the editor (which holds every shader)
and answers null in a build. On 2026-10-12 the afterimage looked up URP's Unlit this way; in the built game the null
shader threw inside the server's "glide now" message, and the network library answers a throw in a message by
dropping the connection — every Thousand Petals dash disconnected the dasher. Nothing in the editor ever showed it.

WHAT IT CHECKS: every Shader.Find("...") in unity/Assets/Scripts outside Editor/. The name is matched to a .shader
file in unity/Assets (by its `Shader "Name"` line) or to a Unity built-in below, and that shader must be on the
Always Included list. A Shader.Find whose argument is not one plain string cannot be checked, so it is flagged too,
unless its line says why it is safe: `// shader-ok: <why>`.

USAGE:   tools/check-runtime-shaders.py [--root <unity project folder>]
EXIT:    0 = every runtime shader is in the build, 1 = at least one is not (each printed with file:line).
"""
import argparse
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MARKER = "shader-ok:"

# Unity's own shaders, which have no file in the project: name -> the fileID they carry on the Always Included list.
BUILT_IN = {
    "Sprites/Default": "10753",
    "UI/Default": "10770",
}

FIND = re.compile(r'Shader\.Find\s*\(')
LITERAL = re.compile(r'Shader\.Find\s*\(\s*"([^"]+)"\s*\)')


def strip_comment(line):
    i = line.find("//")
    return line if i < 0 else line[:i]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=os.path.join(HERE, "..", "unity"))
    root = os.path.abspath(ap.parse_args().root)
    assets = os.path.join(root, "Assets")
    settings = os.path.join(root, "ProjectSettings", "GraphicsSettings.asset")

    text = open(settings, encoding="utf-8").read()
    block = text.split("m_AlwaysIncludedShaders:")[1]
    block = block.split("\n  m_", 1)[0]
    included_guids = set(re.findall(r"guid: ([0-9a-f]{32}), type: 3", block))
    included_builtin = set(re.findall(r"fileID: (\d+), guid: 0{16}f0{15}", block))

    by_name = {}
    for f in glob.glob(os.path.join(assets, "**", "*.shader"), recursive=True):
        m = re.search(r'Shader\s+"([^"]+)"', open(f, encoding="utf-8", errors="ignore").read())
        meta = f + ".meta"
        if m and os.path.exists(meta):
            g = re.search(r"guid: ([0-9a-f]{32})", open(meta, encoding="utf-8").read())
            if g:
                by_name[m.group(1)] = (g.group(1), os.path.relpath(f, root))

    faults = []
    scripts = os.path.join(assets, "Scripts")
    for f in sorted(glob.glob(os.path.join(scripts, "**", "*.cs"), recursive=True)):
        rel = os.path.relpath(f, scripts).replace(os.sep, "/")
        if rel.startswith("Editor/") or "/Editor/" in rel:
            continue
        for n, line in enumerate(open(f, encoding="utf-8", errors="ignore"), 1):
            code = strip_comment(line)
            if not FIND.search(code):
                continue
            where = "Assets/Scripts/" + rel + ":" + str(n)
            names = LITERAL.findall(code)
            if not names:
                if MARKER not in line:
                    faults.append(where + " — Shader.Find with a name that is not one plain string cannot be "
                                  "checked. Use a literal name, or say on the line why it is safe: // " + MARKER + " <why>")
                continue
            for name in names:
                if name in BUILT_IN:
                    if BUILT_IN[name] not in included_builtin:
                        faults.append(where + ' — "' + name + '" (a Unity built-in) is not on Always Included Shaders')
                elif name in by_name:
                    guid, path = by_name[name]
                    if guid not in included_guids:
                        faults.append(where + ' — "' + name + '" (' + path + ") is not on Always Included Shaders")
                else:
                    faults.append(where + ' — "' + name + '" is no shader file in Assets and no known built-in, so '
                                  "nothing proves a build carries it (a package shader such as URP's is only in a build "
                                  "when a saved material uses it). Use a shader on Always Included Shaders.")

    if faults:
        print("\033[31mA shader the game looks up by name would be missing from a built game:\033[0m")
        for x in faults:
            print("  " + x)
        return 1
    print("runtime shaders clean — every shader the game looks up by name is on Always Included Shaders.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
