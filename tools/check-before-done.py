#!/usr/bin/env python3
"""Run the project's checkers the moment a turn tries to END, and refuse to end it while
something is broken.

WHY THIS EXISTS: the checkers only helped when someone REMEMBERED to run them, so the one
time they were forgotten is exactly the time the reply said "done" over code that doesn't
compile or data that silently does nothing. A rule you have to remember is a rule you can
drop; this can't be dropped. It is the same shape as the read-before-you-edit gate that
already guards edits, moved to the other end of the turn.

Two checkers, each run ONLY when its own files actually changed since the last clean pass:
  · the game's C#            -> tools/check-unity-code.sh   (never touches the Unity editor)
  · the saved skill data     -> tools/check-skill-data.py

Cheap by design: unchanged files mean it does nothing at all and adds no waiting. It never
blocks twice in a row for the same turn (the payload says when it has already fired), and
it never blocks when a checker could not run at all — a missing tool is not a red build.
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
TOOLS = os.path.join(REPO, "tools")
SCRIPTS = os.path.join(REPO, "unity", "Assets", "Scripts")
SKILLS = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "Skills")

ANSI = re.compile(r"\033\[[0-9;]*m")

STATE = os.path.join(tempfile.gettempdir(),
                     "check-before-done-%s.json" % hashlib.sha1(REPO.encode()).hexdigest()[:12])

GROUPS = {
    "code": {
        "roots": [(SCRIPTS, ".cs")],
        "run": ["bash", os.path.join(TOOLS, "check-unity-code.sh")],
        "headline": "The game's C# does not compile. Fix these before saying anything is done:",
    },
    "skills": {
        "roots": [(SKILLS, ".asset")],
        "run": [sys.executable, os.path.join(TOOLS, "check-skill-data.py")],
        "headline": "A saved skill is wired in a way that silently does nothing in the running game:",
    },
    # The checker that guards the data is itself guarded: edit it and we re-prove it still
    # SEES faults. Without this, a checker could quietly go blind and its silence would read
    # exactly like good news.
    "skill-checker": {
        "files": [os.path.join(TOOLS, "check-skill-data.py")],
        "run": [sys.executable, os.path.join(TOOLS, "prove-skill-data-check.py")],
        "headline": "The skill-data checker no longer catches every fault it is supposed to, "
                    "so a clean run from it proves nothing until this passes:",
    },
}


def watched(group):
    """Every file this group cares about — whole trees by extension, plus named files."""
    out = list(group.get("files", []))
    for root, ext in group.get("roots", []):
        for dirpath, _dirs, files in os.walk(root):
            out.extend(os.path.join(dirpath, f) for f in files if f.endswith(ext))
    return out


def fingerprint(paths):
    """What the watched files look like right now — path, size and modification time. Cheap
    (no reading of contents) and enough to notice any edit, by us or by the editor."""
    marks = []
    for p in paths:
        try:
            st = os.stat(p)
        except OSError:
            continue
        marks.append("%s|%d|%d" % (p, st.st_size, st.st_mtime_ns))
    marks.sort()
    return hashlib.sha1("\n".join(marks).encode()).hexdigest()


def load_state():
    try:
        with open(STATE, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save_state(state):
    try:
        with open(STATE, "w", encoding="utf-8") as f:
            json.dump(state, f)
    except OSError:
        pass                       # the gate must still work with nowhere to remember


def main():
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        payload = {}

    # Already blocked once for this turn — say nothing, or the turn can never finish.
    if payload.get("stop_hook_active"):
        return 0

    state = load_state()
    findings, checked, unavailable, already = [], [], [], []

    for name, group in GROUPS.items():
        if not all(os.path.isdir(r) for r, _ in group.get("roots", [])):
            continue
        if not all(os.path.isfile(f) for f in group.get("files", [])):
            continue
        now = fingerprint(watched(group))
        if state.get(name) == now:
            continue                                    # nothing changed since it last passed
        try:
            done = subprocess.run(group["run"], cwd=REPO, capture_output=True, text=True, timeout=180)
        except (OSError, subprocess.TimeoutExpired) as e:
            unavailable.append("%s (%s)" % (name, e.__class__.__name__))
            continue
        if done.returncode == 0:
            state[name] = now                           # clean — don't check it again until it moves
            state.pop(name + ":told", None)
            checked.append(name)
        elif done.returncode == 1:
            body = ANSI.sub("", (done.stdout or "") + (done.stderr or "")).strip()
            # Say a given fault ONCE. Having been told, the turn is free to end — the same rule
            # the in-game gate uses when it reports an unauthored pile: report per fault, not
            # per attempt. Anything NEW, or the same fault after the files moved, speaks again.
            mark = hashlib.sha1(("%s|%s" % (now, body)).encode()).hexdigest()
            if state.get(name + ":told") == mark:
                already.append(name)
            else:
                state[name + ":told"] = mark
                findings.append(group["headline"] + "\n" + body)
        else:
            unavailable.append("%s: %s" % (name, ((done.stderr or done.stdout).strip().splitlines() or [""])[0]))

    save_state(state)

    if findings:
        reason = ("\n\n".join(findings) +
                  "\n\nFix these, then finish the turn. If any of it was already broken before "
                  "this turn's work, say so plainly instead of quietly repairing it.")
        print(json.dumps({"decision": "block", "reason": reason}))
        sys.stderr.write(reason + "\n")
        return 2

    notes = []
    if unavailable:
        notes.append("could not run: " + "; ".join(unavailable))
    if already:
        notes.append("still unfixed (already reported): " + ", ".join(already))
    if checked:
        notes.append("checked and clean: " + ", ".join(checked))
    if notes:
        print(json.dumps({"suppressOutput": True, "systemMessage": " · ".join(notes)}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
