#!/usr/bin/env python3
"""Prove the guard check can still SEE a broken guard.

A checker that has gone blind reports silence, and silence reads exactly like good news —
which is the whole failure this project just paid five weeks for. So the guard check gets a
guard of its own: planted faults it must catch, and healthy setups it must NOT
complain about. Edit the guard check and this re-runs; if it ever stops catching one of
these, a clean run from it means nothing.
"""
import datetime
import importlib.util
import io
import json
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("guard_check", os.path.join(HERE, "guard_check.py"))
gc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gc)

WORK = tempfile.mkdtemp(prefix="prove-guards-")

# Every case below is about the user's own machine unless it says otherwise, so a run inside a cloud container
# must not quietly turn them all into cloud cases.
os.environ.pop("CLAUDE_CODE_REMOTE", None)

# A fenced role's file, cut to the only part this checker reads: the gate in its frontmatter.
ROLE_FILE = """---
name: a-fenced-role
description: a role used only by this prover
tools: Read, Bash
hooks:
  PreToolUse:
    - matcher: "Edit|Write|Bash"
      hooks:
        - type: command
          command: "%s"
---

The body is never read by the guard check.
"""


def setup(settings, beats=None, raw=None, agents=None):
    path = os.path.join(WORK, "settings.json")
    with io.open(path, "w", encoding="utf-8") as f:
        f.write(raw if raw is not None else json.dumps(settings))
    gc.SETTINGS_FILES = [path]
    hb = os.path.join(WORK, "heartbeat.json")
    with io.open(hb, "w", encoding="utf-8") as f:
        json.dump(beats or {}, f)
    gc.HEARTBEAT = hb
    # The agent roles get redirected too, or every fixture silently inherits whatever roles
    # this machine really has — which is exactly how five real gates turned all three healthy
    # cases into false alarms the first time roles were added.
    folder = os.path.join(WORK, "agents")
    if os.path.isdir(folder):
        shutil.rmtree(folder)
    os.makedirs(folder)
    for name, command in (agents or {}).items():
        with io.open(os.path.join(folder, name), "w", encoding="utf-8") as f:
            f.write(ROLE_FILE % command)
    gc.AGENTS_DIR = folder


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
    # A name that exists on NO machine. It used to be `python`, which is missing on the Mac but present
    # on Windows — so on Windows this planted fault was a healthy hook and the case always failed.
    ("a program that does not exist on this machine",
     hooks('no-such-program-anywhere "$CLAUDE_PROJECT_DIR/tools/check-before-done.py"'), FRESH, True),
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

    # A fenced agent ROLE carries its gate in its own file rather than in the settings, and the
    # two questions are NOT the same for it. Whether it can start still matters — a gate whose
    # script is gone is as dead as any other. Whether it has reported running does not: it
    # stamps nothing until somebody spawns that role, so on a fresh machine "never reported"
    # is the ordinary state. Asked anyway it complains every single session, and an alarm that
    # is usually wrong trains everyone to skim past the one time it is right.
    ("a role whose gate points at a script that is gone",
     hooks(OURS), FRESH, True, {"broken.md": LAUNCHED % "tools/no-such-gate.py"}),
    ("a role whose gate has never reported running",
     hooks(OURS), FRESH, False, {"fresh.md": LAUNCHED % "tools/check-before-done.py"}),
    ("a role whose gate has not been used in weeks",
     hooks(OURS), {"check-before-done.py": ago(0), "write_scope_gate.py": ago(gc.UNSEEN_DAYS + 9)},
     False, {"stale.md": LAUNCHED % "tools/hook-analytics/write_scope_gate.py"}),
]

# The END-OF-TURN question is a different function from the session-start one above, and the
# role exclusion had to be made in BOTH. It was made in only one the first time, which is why
# this is checked here rather than trusted: "I fixed the other one too" is not evidence.
END_OF_TURN = [
    # (name, heartbeats, agents, must_be_caught)
    # The heartbeat file must be NON-EMPTY and simply not mention this guard. An empty one
    # means "no diary to read", which the check deliberately treats as cannot-tell rather than
    # never-ran — so an empty fixture here would pass while proving nothing.
    ("a session-wide guard that really slept through a turn that wrote files",
     {"check-before-done.py": ago(0)},
     {"fresh.md": LAUNCHED % "tools/hook-analytics/write_scope_gate.py"}, True),
    ("a role's gate, in a turn that never spawned that role",
     {"perf_gate.py": ago(0)}, {"fresh.md": LAUNCHED % "tools/hook-analytics/write_scope_gate.py"},
     False),
]

failures = []
for name, beats, agents, must_catch in END_OF_TURN:
    setup({"hooks": {"PreToolUse": [{"matcher": "Edit|Write|Bash", "hooks": [
        {"type": "command", "command": LAUNCHED % "tools/hook-analytics/perf_gate.py"}]}]}},
        beats, agents=agents)
    complaints = gc.never_ran_this_session("a-session-id-that-never-existed")
    about_roles = [c for c in complaints if "write_scope_gate" in c]
    caught = bool(about_roles) if not must_catch else bool(complaints)
    if must_catch and not caught:
        failures.append("MISSED at end of turn: %s" % name)
    if not must_catch and caught:
        failures.append("FALSE ALARM at end of turn on %s -> %s" % (name, about_roles))

for case in CASES:
    name, settings, beats, must_catch = case[:4]
    agents = case[4] if len(case) > 4 else None
    setup(settings, beats, raw="{ not json" if settings is None else None, agents=agents)
    found = gc.problems()
    if must_catch and not found:
        failures.append("MISSED: %s" % name)
    if not must_catch and found:
        failures.append("FALSE ALARM on %s -> %s" % (name, found))

# In the cloud the diary is git-ignored and every container starts from a fresh clone, so "never reported" is the
# ordinary state there and must stay quiet. Whether a guard can START is still a fault in the cloud.
CLOUD = [
    ("a hook of ours that has never reported running, in the cloud", hooks(OURS), {}, False),
    ("a launched hook that stopped running weeks ago, in the cloud",
     hooks(LAUNCHED % "tools/check-before-done.py"), {"check-before-done.py": ago(gc.UNSEEN_DAYS + 3)}, False),
    ("a script the command points at is gone, in the cloud",
     hooks('python3 "$CLAUDE_PROJECT_DIR/tools/no-such-file.py"'), FRESH, True),
    ("the launcher pointing at a hook file that is gone, in the cloud",
     hooks(LAUNCHED % "tools/no-such-hook.py"), FRESH, True),
]
os.environ["CLAUDE_CODE_REMOTE"] = "true"
for name, settings, beats, must_catch in CLOUD:
    setup(settings, beats)
    found = gc.problems()
    if must_catch and not found:
        failures.append("MISSED: %s" % name)
    if not must_catch and found:
        failures.append("FALSE ALARM on %s -> %s" % (name, found))

# The local-machine rules import: written once on the user's machine, appended without touching what the file
# already says, and never in the cloud.
gc.CLAUDE_LOCAL = os.path.join(WORK, "CLAUDE.local.md")
if gc.ensure_local_rules() or os.path.exists(gc.CLAUDE_LOCAL):
    failures.append("the cloud was given a CLAUDE.local.md")
os.environ.pop("CLAUDE_CODE_REMOTE")
if not gc.ensure_local_rules():
    failures.append("a machine with no CLAUDE.local.md was not given the import")
if gc.ensure_local_rules():
    failures.append("the import was written a second time")
with io.open(gc.CLAUDE_LOCAL, "w", encoding="utf-8") as f:
    f.write("my own note, no newline at the end")
if not gc.ensure_local_rules():
    failures.append("a CLAUDE.local.md of the user's own was not given the import")
with io.open(gc.CLAUDE_LOCAL, encoding="utf-8") as f:
    kept = f.read()
if kept != "my own note, no newline at the end\n" + gc.LOCAL_RULES_IMPORT + "\n":
    failures.append("the user's own CLAUDE.local.md was not kept as it was: %r" % kept)

if failures:
    print("The guard check is no longer trustworthy:")
    for f in failures:
        print("  ·", f)
    sys.exit(1)
print("guard check proven: catches %d planted faults, quiet on %d healthy setups, "
      "%d end-of-turn cases and %d cloud cases behaved, and the local rules import is written once"
      % (sum(1 for c in CASES if c[3]), sum(1 for c in CASES if not c[3]), len(END_OF_TURN), len(CLOUD)))
