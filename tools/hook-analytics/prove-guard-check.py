#!/usr/bin/env python3
"""Prove the guard check can still SEE a broken guard.

A checker that has gone blind reports silence, and silence reads exactly like good news —
which is the whole failure this project just paid five weeks for. So the guard check gets a
guard of its own: seven planted faults it must catch, and two healthy setups it must NOT
complain about. Edit the guard check and this re-runs; if it ever stops catching one of
these, a clean run from it means nothing.
"""
import datetime
import importlib.util
import io
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("guard_check", os.path.join(HERE, "guard_check.py"))
gc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gc)

WORK = tempfile.mkdtemp(prefix="prove-guards-")


def setup(settings, beats=None, raw=None):
    path = os.path.join(WORK, "settings.json")
    with io.open(path, "w", encoding="utf-8") as f:
        f.write(raw if raw is not None else json.dumps(settings))
    gc.SETTINGS_FILES = [path]
    hb = os.path.join(WORK, "heartbeat.json")
    with io.open(hb, "w", encoding="utf-8") as f:
        json.dump(beats or {}, f)
    gc.HEARTBEAT = hb


def hooks(*commands):
    return {"hooks": {"Stop": [{"hooks": [{"type": "command", "command": c} for c in commands]}]}}


def ago(days):
    when = datetime.datetime.now().astimezone() - datetime.timedelta(days=days)
    return {"ts": when.isoformat(timespec="seconds"), "session": "x"}


OURS = 'python3 "$CLAUDE_PROJECT_DIR/tools/check-before-done.py"'
LAUNCHED = 'sh "$CLAUDE_PROJECT_DIR/tools/hooks/run-hook.sh" %s'
FRESH = {"check-before-done.py": ago(0)}

CASES = [
    # (name, settings-or-raw, heartbeats, must_be_caught)
    ("a program that does not exist on this machine",
     hooks('python "$CLAUDE_PROJECT_DIR/tools/check-before-done.py"'), FRESH, True),
    ("a script the command points at is gone",
     hooks('python3 "$CLAUDE_PROJECT_DIR/tools/no-such-file.py"'), FRESH, True),
    ("an absolute path that is not executable",
     hooks('/etc/hosts --run'), FRESH, True),
    ("a settings file that is not valid JSON", None, FRESH, True),
    ("a hook of ours that has never reported running", hooks(OURS), {}, True),
    ("a hook of ours that stopped running weeks ago",
     hooks(OURS), {"check-before-done.py": ago(gc.UNSEEN_DAYS + 3)}, True),
    ("a hook of ours seen only just outside the window",
     hooks(OURS), {"check-before-done.py": ago(gc.UNSEEN_DAYS)}, True),
    # …and the two it must stay QUIET about, or it cries wolf every session
    ("a healthy hook seen today", hooks(OURS), FRESH, False),
    ("an inline shell command that names no program",
     hooks("TS=\"$(date '+%H:%M')\"; printf '%s' \"$TS\""), FRESH, False),
    # The launcher exists so no interpreter name is written into the settings at all. The check
    # has to see THROUGH it to the hook it starts, or every hook would look like the launcher
    # and none could be matched to its own heartbeat.
    ("the launcher pointing at a hook file that is gone",
     hooks(LAUNCHED % "tools/no-such-hook.py"), FRESH, True),
    ("a launched hook that has never reported running",
     hooks(LAUNCHED % "tools/check-before-done.py"), {}, True),
    ("a launched hook that stopped running weeks ago",
     hooks(LAUNCHED % "tools/check-before-done.py"),
     {"check-before-done.py": ago(gc.UNSEEN_DAYS + 3)}, True),
    ("a launched hook seen today", hooks(LAUNCHED % "tools/check-before-done.py"), FRESH, False),
]

failures = []
for name, settings, beats, must_catch in CASES:
    setup(settings, beats, raw="{ not json" if settings is None else None)
    found = gc.problems()
    if must_catch and not found:
        failures.append("MISSED: %s" % name)
    if not must_catch and found:
        failures.append("FALSE ALARM on %s -> %s" % (name, found))

if failures:
    print("The guard check is no longer trustworthy:")
    for f in failures:
        print("  ·", f)
    sys.exit(1)
print("guard check proven: catches %d planted faults, quiet on %d healthy setups"
      % (sum(1 for c in CASES if c[3]), sum(1 for c in CASES if not c[3])))
