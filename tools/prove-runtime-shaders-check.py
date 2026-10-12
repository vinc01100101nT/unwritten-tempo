#!/usr/bin/env python3
"""Prove tools/check-runtime-shaders.py still SEES a runtime shader a build would not carry — and is quiet about
the sound ones.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This builds a tiny scratch
project — one included shader, one shader left off the list, a Unity built-in on and off the list — and plants game
scripts in the exact shapes that matter: the 2026-10-12 afterimage (URP's Unlit, a package shader), a project shader
left off the list, a name built from a variable, and the same lines in Editor/ code, which never ships.

USAGE:   tools/prove-runtime-shaders-check.py
EXIT:    0 = every planted fault named and every sound line quiet, 1 = blind or crying wolf.
"""
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CHECK = os.path.join(HERE, "check-runtime-shaders.py")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

ON_GUID = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
OFF_GUID = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"

SETTINGS = """%YAML 1.1
GraphicsSettings:
  m_AlwaysIncludedShaders:
  - {fileID: 10753, guid: 0000000000000000f000000000000000, type: 0}
  - {fileID: 4800000, guid: """ + ON_GUID + """, type: 3}
  m_PreloadedShaders: []
"""

# file -> (source, the substring the checker must report for it, or None when it must stay quiet)
PLANTS = {
    "Ghost.cs": ('class Ghost { void A() { var s = UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"); } }',
                 "Ghost.cs:1"),
    "OffList.cs": ('class OffList { void A() { var s = UnityEngine.Shader.Find("Test/Off"); } }', "OffList.cs:1"),
    "Built.cs": ('class Built { void A(string n) { var s = UnityEngine.Shader.Find(n + " Overlay"); } }', "Built.cs:1"),
    "UiOff.cs": ('class UiOff { void A() { var s = UnityEngine.Shader.Find("UI/Default"); } }', "UiOff.cs:1"),
    "Fine.cs": ('class Fine { void A() { var a = UnityEngine.Shader.Find("Test/On"); var b = UnityEngine.Shader.Find("Sprites/Default"); } }', None),
    "Marked.cs": ('class Marked { void A(string n) { var s = UnityEngine.Shader.Find(n); }   // shader-ok: proven elsewhere\n}', None),
    "Commented.cs": ('class Commented { // was Shader.Find("Universal Render Pipeline/Unlit")\n}', None),
    "Editor/Tool.cs": ('class Tool { void A() { var s = UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"); } }', None),
}


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def main():
    tmp = tempfile.mkdtemp(prefix="prove-shaders-")
    try:
        write(os.path.join(tmp, "ProjectSettings", "GraphicsSettings.asset"), SETTINGS)
        for name, guid in (("On", ON_GUID), ("Off", OFF_GUID)):
            p = os.path.join(tmp, "Assets", "Shaders", name + ".shader")
            write(p, 'Shader "Test/' + name + '" { SubShader { Pass { } } }\n')
            write(p + ".meta", "fileFormatVersion: 2\nguid: " + guid + "\n")
        for name, (src, _) in PLANTS.items():
            write(os.path.join(tmp, "Assets", "Scripts", name), src + "\n")

        done = subprocess.run([sys.executable, CHECK, "--root", tmp], capture_output=True, text=True)
        out = done.stdout + done.stderr
        bad = []
        if done.returncode != 1:
            bad.append("exit code " + str(done.returncode) + ", expected 1")
        for name, (_, must) in PLANTS.items():
            short = name.split("/")[-1]
            if must and must not in out:
                bad.append("did not name " + name)
            if not must and (short + ":") in out:
                bad.append("cried wolf on " + name)
        if bad:
            print(RED + "check-runtime-shaders is BLIND or noisy:" + OFF)
            for b in bad:
                print("  " + b)
            print(out)
            return 1
        print(GRN + "check-runtime-shaders sees every planted missing shader and stays quiet on the sound ones." + OFF)
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
