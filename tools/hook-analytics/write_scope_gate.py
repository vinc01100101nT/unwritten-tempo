#!/usr/bin/env python3
"""PreToolUse gate: refuse any call that writes a file OUTSIDE this agent's allowed places.

WHY THIS EXISTS: a role told "you only read" is a promise, not a fence — the agent can edit
anyway the moment it decides that would help. This turns the promise into a refusal. It is the
one gate behind every fenced role, because "may write nothing" and "may write only data" are
the same rule with a different list, and two copies of one rule is how one of them rots.

Usage, from an agent's own hooks block:
    run-hook.sh tools/hook-analytics/write_scope_gate.py [allowed/prefix ...]

No prefixes  -> the agent may write NOTHING. (planner, reviewer, debugger)
With prefixes -> it may write only under those repo-relative folders, and nowhere else.

The scratchpad is always allowed: a measurement that needs a throwaway script must not be
blocked from having one, and nothing there is part of the project.

WHAT IT DOES NOT STOP, said out loud so nobody mistakes it for total: it guards FILES. A call
that changes the running Unity editor is not a file write, so the editor-mutating shell forms
are refused by name below, and a fenced role is not given the editor tools that write.
"""
import sys, os, re, json

import guard_check   # sibling — the heartbeat that proves this gate really ran
import tool_writes   # sibling — the one answer to 'which files does this call write?'

# Taken from the sibling rather than worked out again here: this file counted one folder too
# few the first time and every allowed write was refused, which a second copy of "where is the
# repo" is exactly how you earn.
REPO = tool_writes.REPO

# Running the editor through the command line can change the project without ever naming a
# file, so the file answer above cannot see it. These are refused for every fenced role; the
# read-only forms (status, and reading the console) stay allowed.
_EDITOR_WRITES = re.compile(
    r"""\bunity\s+cmd\s+(?:eval|eval-file|menu|run)\b   # run C# / fire a menu item in the editor
      | \bunity\s+(?:build|test)\b                       # produce or overwrite a build
    """,
    re.VERBOSE,
)


def allow():
    sys.exit(0)


def deny(reason):
    # Satisfy BOTH block protocols so this works on any Claude Code build: JSON on stdout
    # (modern) and exit code 2 with the reason on stderr (classic). Some builds honour only
    # the second, so the exit code must be 2 and the reason must reach stderr.
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.stderr.write(reason + "\n")
    sys.exit(2)


def resolve(path, cwd):
    """Absolute form of a path the write answer gave back. It returns one 'absolute where that
    can be worked out' — so a relative one arrives relative to the CALL's folder, never to
    wherever this gate happens to be running from. Resolving it against the gate's own folder
    is how every allowed write got refused the first time this was tested."""
    if os.path.isabs(path):
        return os.path.normpath(path)
    return os.path.normpath(os.path.join(cwd or REPO, path))


def inside(path_abs, prefix_abs):
    """True when path sits at or under prefix_abs. Compared on normalised absolute paths so
    a '..' cannot walk out of an allowed folder and still look like it is inside one."""
    path_abs = os.path.normpath(path_abs)
    prefix_abs = os.path.normpath(os.path.abspath(prefix_abs))
    return path_abs == prefix_abs or path_abs.startswith(prefix_abs + os.sep)


def scratchpad_roots():
    """Wherever this machine puts a session's throwaway files. Both spellings of the macOS
    temp folder count: it is reached as /tmp and reported as /private/tmp, and a path that
    passes one spelling must not be refused for arriving in the other."""
    return ["/tmp", "/private/tmp", os.environ.get("TMPDIR") or "/tmp"]


def main():
    try:
        data = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        data = {}
    # Stamp BEFORE deciding: a gate that ran and allowed the call has still run, and that is
    # the case a heartbeat exists to tell apart from a gate that never fired at all.
    guard_check.beat(os.path.basename(__file__), data.get("session_id", ""))
    if not data:
        allow()   # malformed payload -> never block real work

    allowed = [os.path.join(REPO, p) for p in sys.argv[1:]] + scratchpad_roots()

    tool_name = data.get("tool_name") or ""
    tool_input = data.get("tool_input") or {}

    command = tool_input.get("command") or "" if tool_name == "Bash" else ""
    if command and _EDITOR_WRITES.search(command):
        deny("You are a read-only role: you may measure the editor but not change it. "
             "Running code or a menu item in the editor, and making a build, are refused. "
             "Report what you found and let whoever spawned you make the change.")

    cwd = data.get("cwd") or REPO
    written = [resolve(f, cwd) for f in tool_writes.files_written(tool_name, tool_input, cwd)]
    blocked = [f for f in written if not any(inside(f, a) for a in allowed)]
    if not blocked:
        allow()

    where = ", ".join(sys.argv[1:]) if len(sys.argv) > 1 else ""
    if where:
        deny("This call writes " + blocked[0] + ", which is outside what your role may change. "
             "You may write only under: " + where + ". Everything else is refused. If the work "
             "genuinely needs a change elsewhere, say so in your report instead of making it.")
    deny("This call writes " + blocked[0] + ", and your role changes nothing at all. "
         "Read, measure, and report what you found — the decision to change anything belongs "
         "to whoever spawned you.")


if __name__ == "__main__":
    main()
