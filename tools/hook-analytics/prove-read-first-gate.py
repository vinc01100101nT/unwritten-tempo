#!/usr/bin/env python3
"""Prove the read-first gate (perf_gate.py) still refuses an edit whose rulebook has not been read this session,
for each of its three file classes — and still waves through an edit whose rulebook has been read, and any file
it does not govern.

WHY THIS EXISTS: the gate is the thing that makes the rulebooks READ rather than merely written. A gate that has
gone blind waves everything through and looks exactly like a session that read every rulebook. This feeds it
fake edit requests with a fake transcript and reads its verdict.

USAGE:   tools/hook-analytics/prove-read-first-gate.py
EXIT:    0 = every class refused unread and allowed read, 1 = the gate is blind or refuses what it should not.
"""
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
GATE = os.path.join(HERE, "perf_gate.py")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

CASES = [
    ("runtime C#", os.path.join(REPO, "unity", "Assets", "Scripts", "Character.cs"),
     os.path.join(REPO, "unity", "Assets", "Scripts", "PERFORMANCE.md")),
    ("a texture import setting", os.path.join(REPO, "unity", "Assets", "1_Authored", "Art", "GroundTiles", "BASE_OR_PATCH_OR_PATH_soil.png.meta"),
     os.path.join(REPO, "unity", "Assets", "ASSETS.md")),
    ("a prefab", os.path.join(REPO, "unity", "Assets", "1_Authored", "Prefabs", "Props", "Plants", "Grass1.prefab"),
     os.path.join(REPO, "unity", "Assets", "ASSETS.md")),
    ("the package manifest", os.path.join(REPO, "unity", "Packages", "manifest.json"),
     os.path.join(REPO, "unity", "Assets", "ASSETS.md")),
    ("a project setting", os.path.join(REPO, "unity", "ProjectSettings", "QualitySettings.asset"),
     os.path.join(REPO, "unity", "Assets", "ASSETS.md")),
    ("the sign-in service", os.path.join(REPO, "accounts", "src", "server.ts"),
     os.path.join(REPO, "SERVER.md")),
    ("the character server", os.path.join(REPO, "charserver", "Characters.cs"),
     os.path.join(REPO, "SERVER.md")),
    ("a migration", os.path.join(REPO, "accounts", "sql", "006_leaderboard_index.sql"),
     os.path.join(REPO, "SERVER.md")),
]
UNGATED = [
    ("a document", os.path.join(REPO, "docs", "README.md")),
    ("a script's own .meta", os.path.join(REPO, "unity", "Assets", "Scripts", "Character.cs.meta")),
    ("an editor tool", os.path.join(REPO, "unity", "Assets", "Scripts", "Editor", "TOOLS.md")),
]


def verdict(file_path, transcript_text):
    with tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False, encoding="utf-8") as t:
        t.write(json.dumps({"type": "user", "text": transcript_text}, ensure_ascii=False) + "\n")
        transcript = t.name
    try:
        payload = {"session_id": "prove-gate", "tool_name": "Edit", "cwd": REPO, "transcript_path": transcript,
                   "tool_input": {"file_path": file_path, "old_string": "a", "new_string": "b"}}
        done = subprocess.run([sys.executable, GATE], input=json.dumps(payload), capture_output=True, text=True)
        return "denied" if done.returncode == 2 else "allowed"
    finally:
        os.unlink(transcript)


def main():
    failures = 0
    for name, path, doc in CASES:
        with open(doc, encoding="utf-8") as f:
            text = f.read()
        unread = verdict(path, "nothing relevant was read")
        read = verdict(path, text)
        ok = unread == "denied" and read == "allowed"
        print((GRN + "right   " if ok else RED + "WRONG   ") + "%-28s unread → %s, read → %s (%s)" % (name, unread, read, os.path.basename(doc)) + OFF)
        if not ok:
            failures += 1
    for name, path in UNGATED:
        v = verdict(path, "nothing relevant was read")
        ok = v == "allowed"
        print((GRN + "right   " if ok else RED + "WRONG   ") + "%-28s ungated → %s" % (name, v) + OFF)
        if not ok:
            failures += 1
    total = len(CASES) + len(UNGATED)
    if failures:
        print(RED + "%d of %d case(s) wrong — the read-first gate is not doing its job." % (failures, total) + OFF)
        return 1
    print(GRN + "All %d cases right: each file class is refused until its rulebook is read, and nothing else is touched." % total + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
