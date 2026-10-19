#!/usr/bin/env python3
"""Is every guard this machine is configured to run ACTUALLY running?

WHY THIS EXISTS: a hook that cannot start fails SILENTLY. Claude Code runs the command,
the command is not there, and the turn carries on exactly as if the guard had approved the
work — no error, no warning, nothing in the reply. The performance gate died that way when
this project moved from Windows to a Mac (it was configured to start with `python`, which
does not exist here) and it guarded NOTHING for five weeks while its own log sat there
looking finished. Nobody was told, because nothing was watching the watchers.

So this asks two different questions about every configured hook, and they are not the same
question:
  1. CAN it start — the program exists, the script it points at still exists, the settings
     file it lives in still parses.
  2. Has it actually RUN — each of our own hooks stamps a heartbeat every time it is invoked,
     so a hook that is configured, runnable, and never firing (a matcher that matches nothing,
     a settings file the app never loaded) shows up as a hook nobody has seen in weeks.

Runnable is NOT the same as running. Question 1 alone would have missed a wrong matcher;
question 2 alone would have missed a fresh install. Both, every session.

Run directly, it answers as a SessionStart hook. Imported, `problems()` is the same answer
for anything else that wants it — one implementation, several callers.
"""
import datetime
import io
import json
import os
import shlex
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
HEARTBEAT = os.path.join(HERE, "hook-heartbeat.json")

# A hook of ours that has not been seen in this long is worth a question — not an accusation.
# (My choice of number, not a measured one: two weeks is longer than any quiet stretch this
# project has had, and short enough that a dead guard is caught in the same month.)
UNSEEN_DAYS = 14

SETTINGS_FILES = [
    os.path.join(os.path.expanduser("~"), ".claude", "settings.json"),
    os.path.join(os.path.expanduser("~"), ".claude", "settings.local.json"),
    os.path.join(REPO, ".claude", "settings.json"),
    os.path.join(REPO, ".claude", "settings.local.json"),
]

# Where fenced agent ROLES live. A module-level name rather than a path built inside the
# function, for the same reason SETTINGS_FILES is one: the prover replaces both to test this
# checker against planted fixtures, and anything it cannot replace leaks the real machine into
# every case. (Measured: built inline, the five real role gates turned all three healthy
# fixtures into false alarms.)
AGENTS_DIR = os.path.join(REPO, ".claude", "agents")

SCRIPT_SUFFIXES = (".py", ".sh", ".js", ".ts", ".mjs")
SHELL_CHARS = "|;&<>()`"
LAUNCHER_NAME = "run-hook.sh"
PYTHONS = ("python3", "python", "py")

# The rules only the user's own machines can follow, and the git-ignored file that loads them there.
LOCAL_RULES = "docs/0-process/local-machine-rules.md"
LOCAL_RULES_IMPORT = "@" + LOCAL_RULES
CLAUDE_LOCAL = os.path.join(REPO, "CLAUDE.local.md")


def in_cloud():
    """True in a claude.ai cloud container. Claude Code sets CLAUDE_CODE_REMOTE=true there and never on the user's
    own machines; the shell twin is `in_cloud` in tools/platform.sh, reading the same variable. Read at call time,
    so a prover can try both answers in one run."""
    return os.environ.get("CLAUDE_CODE_REMOTE") == "true"


def ensure_local_rules():
    """On the user's own machine, make sure CLAUDE.local.md imports the local-machine rules. True when the import
    had to be added just now — Claude Code read the memory files before this hook ran, so the caller then hands
    this session the rules itself. Never in the cloud, and never rewrites what the file already says."""
    if in_cloud() or not os.path.isfile(os.path.join(REPO, LOCAL_RULES)):
        return False
    try:
        with io.open(CLAUDE_LOCAL, encoding="utf-8") as f:
            text = f.read()
    except FileNotFoundError:
        text = None
    except OSError:
        return False
    if text is not None and any(line.strip() == LOCAL_RULES_IMPORT for line in text.splitlines()):
        return False
    try:
        with io.open(CLAUDE_LOCAL, "a", encoding="utf-8") as f:
            if text and not text.endswith("\n"):
                f.write("\n")
            if not text:
                f.write("# This machine only (git-ignored). The local-machine rules, which a cloud session never loads.\n")
            f.write(LOCAL_RULES_IMPORT + "\n")
    except OSError:
        return False
    return True


def beat(name, session=""):
    """Called BY a hook, at the top of its own run, before any early-out: 'I ran.' Written at
    the top on purpose — a hook that runs and decides to say nothing has still run, and that
    is exactly the case a heartbeat has to tell apart from a hook that never fired at all."""
    # ONE WRITER AT A TIME. Every stamp is read-add-write, and hooks fire back to back, so two at
    # once each read the diary, add themselves, and the slower one writes back a copy missing the
    # other. (Measured 2026-10-04: the diary held three guards; the session-start guard and the
    # write-scope gate had both been rubbed out, and this guard was then reported as never having
    # run — while running fine by hand.)
    lock = _take_diary_lock()
    if lock is None:
        return                          # could not get it in time: skip one stamp, never clobber
    tmp = HEARTBEAT + ".%d.tmp" % os.getpid()
    try:
        seen = _read_diary()
        if seen is None:
            return                      # there but unreadable: writing now would erase every other guard
        # Keep the RECENT sessions, not just the last one. A single "who ran me last" slot is
        # overwritten by any test run of the hook, and the next end-of-turn check then compares the
        # test's fake id against the real one and reports a working guard as dead. (Measured: the
        # gate was run five times with the id "try" while proving it; both false alarms followed.)
        # RANKED BY THE LAST STAMP, NOT THE FIRST. A session that stamps again moves to the newest end, so
        # one still working is never the one dropped. Ranked by first appearance, a long session was pushed
        # out by eight shorter ones started after it while it kept stamping, and the end-of-turn check then
        # called a gate dead that had run 46 times that session. (Measured 2026-10-05.)
        recent = list((seen.get(name) or {}).get("sessions") or [])
        if session:
            if session in recent:
                recent.remove(session)
            recent.append(session)
        seen[name] = {"ts": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
                      "session": session,
                      "sessions": recent[-8:]}
        # Written beside the real file and moved into place in one step. Hooks fire back to back at
        # the end of a turn, and a plain "open for writing" empties the file first — a guard reading
        # it in that instant sees nothing and reports every other guard as dead. (Measured: exactly
        # that, at 10:31:22, on a gate that was running perfectly.)
        with io.open(tmp, "w", encoding="utf-8") as f:
            json.dump(seen, f, indent=2, sort_keys=True)
        _retry(lambda: os.replace(tmp, HEARTBEAT))
    except OSError:
        pass                            # a guard must never fail because its diary is unwritable
    finally:
        # A move that failed leaves its copy behind; one was found sitting there from the night before.
        try:
            if os.path.exists(tmp):
                os.remove(tmp)
        except OSError:
            pass
        _release_diary_lock(lock)


# How long a stamp waits for another to finish, and when a lock is old enough to be a leftover from
# a killed hook rather than a stamp in progress. (My numbers, not measured ones: a stamp takes a few
# milliseconds, and every hook here has a timeout of ten seconds or more.)
LOCK_WAIT_SECONDS = 2.0
LOCK_STALE_SECONDS = 10.0


def _retry(action, tries=20, pause=0.05):
    """Windows refuses to read or replace a file another process has open for that instant, so a
    refusal is retried briefly before it is believed."""
    import time
    for i in range(tries):
        try:
            return action()
        except PermissionError:
            if i == tries - 1:
                raise
            time.sleep(pause)


def _read_diary():
    """The diary as a dict, {} when there is none yet, or None when it exists but cannot be read —
    and None must never be written back as though it were an empty diary."""
    def read():
        with io.open(HEARTBEAT, encoding="utf-8") as f:
            return json.load(f)
    try:
        seen = _retry(read)
    except FileNotFoundError:
        return {}
    except (OSError, ValueError):
        return None
    return seen if isinstance(seen, dict) else None


def _take_diary_lock():
    import time
    path = HEARTBEAT + ".lock"
    deadline = time.time() + LOCK_WAIT_SECONDS
    while True:
        try:
            os.close(os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY))
            return path
        except FileExistsError:
            try:
                if time.time() - os.path.getmtime(path) > LOCK_STALE_SECONDS:
                    os.remove(path)
                    continue
            except OSError:
                pass
            if time.time() > deadline:
                return None
            time.sleep(0.02)
        except OSError:
            return None


def _release_diary_lock(path):
    try:
        os.remove(path)
    except OSError:
        pass


def _heartbeats():
    try:
        with io.open(HEARTBEAT, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def _agent_hooks():
    """Hooks configured inside an AGENT's own file rather than in a settings file. A fenced
    role carries its gate in its frontmatter, so a gate that cannot start would be invisible
    to a check that reads only the settings files — which is precisely the silent death this
    whole script exists to catch.

    The frontmatter is read with a plain search for its command lines rather than a YAML
    parser: no third-party library is guaranteed to exist on this machine, and a guard that
    cannot import what it needs fails exactly as quietly as the hooks it is meant to watch.
    Every command line is enough, because all this check does with one is ask whether the
    file it names is really there."""
    out = []
    folder = AGENTS_DIR
    if not os.path.isdir(folder):
        return out
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".md"):
            continue
        path = os.path.join(folder, name)
        try:
            with io.open(path, encoding="utf-8") as f:
                text = f.read()
        except OSError:
            continue
        if not text.startswith("---"):
            continue
        # Everything up to the CLOSING delimiter is the frontmatter; the body comes after it.
        front = text[3:].split("\n---", 1)[0]
        for line in front.splitlines():
            stripped = line.strip()
            if not stripped.startswith("command:"):
                continue
            command = stripped[len("command:"):].strip().strip('"').strip("'")
            command = command.replace('\\"', '"')
            if command:
                # scope "agent" is what keeps this out of the never-ran-this-session check
                # below. A gate on a role fires only while that role is running, so a session
                # that never spawned the role is the ORDINARY case, not a dead guard — and a
                # warning that fires on the ordinary case teaches everyone to ignore it.
                out.append({"event": "PreToolUse", "matcher": name[:-3], "scope": "agent",
                            "command": command, "source": path})
    return out


def configured():
    """Every hook configured for this machine, from every settings file that can hold one —
    the user's own and the project's — plus the gates that fenced agent roles carry in their
    own files. A guard in the global file is just as able to die quietly as a guard in this
    repo, and one in an agent's frontmatter just as able as either, so all three are read."""
    found, unreadable = [], []
    for path in SETTINGS_FILES:
        if not os.path.isfile(path):
            continue
        try:
            with io.open(path, encoding="utf-8") as f:
                cfg = json.load(f)
        except ValueError:
            unreadable.append(path)
            continue
        except OSError:
            continue
        for event, entries in (cfg.get("hooks") or {}).items():
            for entry in entries or []:
                for hook in entry.get("hooks") or []:
                    if hook.get("type") != "command" or not (hook.get("command") or "").strip():
                        continue
                    found.append({
                        "event": event,
                        "matcher": entry.get("matcher") or hook.get("matcher") or "*",
                        "scope": "session",
                        "command": hook["command"].strip(),
                        "source": path,
                    })
    found.extend(_agent_hooks())
    return found, unreadable


def _scripts_in(command):
    """Every script file a command names, absolute. Repo-relative paths are resolved against
    the repo, which is how the launcher is given its target."""
    try:
        parts = shlex.split(command.replace("$CLAUDE_PROJECT_DIR", REPO))
    except ValueError:
        return []
    out = []
    for arg in parts[1:]:
        if arg.endswith(SCRIPT_SUFFIXES):
            out.append(arg if os.path.isabs(arg) else os.path.join(REPO, arg))
    return out


def _target_of(command):
    """The hook file that finally RUNS — seen through the launcher, whose whole job is to pick
    an interpreter this machine actually has. Without this, every hook would look like the
    launcher, and no hook could ever be matched to its own heartbeat."""
    scripts = _scripts_in(command)
    if not scripts:
        return None
    if os.path.basename(scripts[0]) == LAUNCHER_NAME and len(scripts) > 1:
        return scripts[1]
    return scripts[0]


def _can_start(command):
    """Empty string when it can start, else the plain reason it cannot.

    A command like `python3 foo.py` names the program it needs, and whether that program exists
    is a fact we can check. A command that is a whole shell line — a variable assignment, a
    pipe, a substitution — names no single program, and guessing at one would invent a fault
    that is not there. So an inline line is only checked for what IS knowable: the files it
    names must exist."""
    expanded = command.replace("$CLAUDE_PROJECT_DIR", REPO)
    try:
        parts = shlex.split(expanded)
    except ValueError:
        parts = []
    inline_shell = (not parts
                    or any(c in command for c in SHELL_CHARS)
                    or "=" in parts[0]
                    or command.lstrip().startswith("$"))
    if not inline_shell:
        prog = parts[0]
        if os.path.sep in prog:
            if not (os.path.isfile(prog) and os.access(prog, os.X_OK)):
                return "there is no runnable %s on this machine" % prog
        elif shutil.which(prog) is None:
            return "there is no %s on this machine" % prog

    scripts = _scripts_in(command)
    for script in scripts:
        if not os.path.isfile(script):
            return "the file it runs is gone: %s" % script

    # The launcher picks the interpreter itself, so the only thing that can defeat it is a
    # machine with no Python at all — which is the one interpreter fact worth checking here.
    if scripts and os.path.basename(scripts[0]) == LAUNCHER_NAME:
        if not any(shutil.which(py) for py in PYTHONS):
            return "this machine has no Python at all (tried %s)" % ", ".join(PYTHONS)
    return ""


def problems():
    """Everything wrong right now, in plain sentences. Empty list = every guard can start and
    every guard of ours has been seen recently."""
    out = []
    hooks, unreadable = configured()
    for path in unreadable:
        out.append("%s is not valid JSON, so EVERY hook in it is switched off" % path)

    seen = _heartbeats()
    now = datetime.datetime.now().astimezone()
    for hook in hooks:
        why = _can_start(hook["command"])
        if why:
            out.append("the %s hook cannot start — %s" % (hook["event"], why))
            continue
        script = _target_of(hook["command"])
        if not script or not script.startswith(REPO):
            continue                    # not ours; it cannot stamp a heartbeat, so we can't ask
        if in_cloud():
            # A cloud container starts from a fresh clone, and the diary is git-ignored, so every hook would read
            # as "never reported". Whether it CAN START, asked above, still holds there.
            continue
        if hook.get("scope") == "agent":
            # Whether it CAN START is asked above, and that is the question that protects it
            # from dying quietly. The heartbeat questions below are not askable of it: a gate
            # on a role stamps nothing until somebody spawns that role, so "never reported"
            # and "not seen in days" are the ORDINARY state, not a fault. Asked anyway, it
            # would complain on a fresh machine forever — and an alarm that is usually wrong
            # trains everyone to skim past the one time it is right.
            continue
        name = os.path.basename(script)
        stamp = (seen.get(name) or {}).get("ts")
        if not stamp:
            out.append("the %s hook (%s) has never once reported running — it may be configured "
                       "but never firing" % (hook["event"], name))
            continue
        try:
            when = datetime.datetime.fromisoformat(stamp)
        except ValueError:
            continue
        days = (now - when).days
        if days >= UNSEEN_DAYS:
            out.append("the %s hook (%s) has not run in %d days — last seen %s"
                       % (hook["event"], name, days, when.strftime("%d %b")))
    return out


def never_ran_this_session(session_id):
    """Our before-a-tool-runs guards that have NOT stamped THIS session, in plain sentences.

    Asked at the end of a turn that actually wrote files: by then every such guard must have been
    consulted at least once, so one that has not reported in is attached to nothing being used and
    is approving everything in silence. The two questions above cannot see this — a guard can be
    perfectly runnable, and stamped minutes ago by some other session, while sitting out this one
    entirely. That is exactly how the performance gate watched a whole session of edits go by.

    ONLY session-wide guards are asked. A guard carried by an agent role fires when that role
    runs and at no other time, so a session that never spawned the role has nothing to explain
    — asking it the same question makes it cry wolf on every ordinary turn, which is worse than
    not watching it at all: a warning everyone learns to scroll past is not a warning. Whether
    that guard CAN start is still checked, above, which is the question that actually protects
    it from dying quietly."""
    if not session_id:
        return []
    seen = _heartbeats()
    if not seen:
        return []                       # no diary to read is "cannot tell", never "never ran"
    out = []
    hooks, _ = configured()
    for hook in hooks:
        if hook["event"] != "PreToolUse" or hook.get("scope") == "agent":
            continue
        script = _target_of(hook["command"])
        if not script or not script.startswith(REPO):
            continue                    # not ours; it cannot stamp a heartbeat, so we can't ask
        name = os.path.basename(script)
        stamp = seen.get(name) or {}
        if session_id not in (stamp.get("sessions") or [stamp.get("session")]):
            out.append("the %s guard (%s) never ran once this session, though files were written "
                       "— it is attached to nothing being used" % (hook["event"], name))
    return out


def summary():
    """One short line for a healthy machine: how many guards, and that they are alive.

    The two kinds are counted apart because only one of them has reported in. A session-wide
    guard is expected to fire in any session that writes; a guard on an agent role fires only
    while that role runs, so claiming they are all "reporting in" would be an overclaim on
    every turn that spawned nobody."""
    hooks, _ = configured()
    on_roles = sum(1 for h in hooks if h.get("scope") == "agent")
    if not on_roles:
        return "guards: %d configured, all able to run and reporting in" % len(hooks)
    return ("guards: %d configured and able to run — %d session-wide and reporting in, "
            "%d waiting on the agent roles they fence"
            % (len(hooks), len(hooks) - on_roles, on_roles))


def main():
    try:
        payload = json.load(sys.stdin) if not sys.stdin.isatty() else {}
    except (json.JSONDecodeError, ValueError):
        payload = {}
    beat(os.path.basename(__file__), payload.get("session_id", ""))

    context = []
    if ensure_local_rules():
        try:
            with io.open(os.path.join(REPO, LOCAL_RULES), encoding="utf-8") as f:
                context.append("CLAUDE.local.md was just made to import %s, so it loads by itself from the next "
                               "session on. For THIS session, here it is — it ranks with CLAUDE.md:\n\n%s"
                               % (LOCAL_RULES, f.read()))
        except OSError:
            pass

    found = problems()
    if not found:
        out = {"suppressOutput": True, "systemMessage": summary()}
        if context:
            out["hookSpecificOutput"] = {"hookEventName": "SessionStart", "additionalContext": "\n\n".join(context)}
        print(json.dumps(out))
        return 0

    text = ("A guard configured for this project is not doing its job:\n  · "
            + "\n  · ".join(found))
    context.insert(0, text + "\n\nTELL THE USER THIS IN YOUR FIRST REPLY, in plain "
                         "words, before anything else they asked for. A guard that "
                         "fails silently is exactly how the performance gate went "
                         "five weeks unnoticed.")
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "SessionStart",
            "additionalContext": "\n\n".join(context),
        },
        "systemMessage": "⚠ " + found[0] + (" (+%d more)" % (len(found) - 1) if len(found) > 1 else ""),
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
