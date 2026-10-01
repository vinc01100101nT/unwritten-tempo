#!/usr/bin/env python3
"""Prove the write-scope gate REFUSES what it is supposed to refuse — and, just as important,
ALLOWS what it must allow.

WHY THIS EXISTS: this gate is the only thing standing between a role that says "I only read"
and a role that actually cannot write. A guard that quietly stops refusing looks identical to
a guard that had nothing to refuse — both are silent. And the other half matters just as much:
the first time this gate was tested it refused EVERY write including the allowed ones, because
it worked out the repo's location one folder short. A fence that blocks everything is not a
safe failure, it is a role that can do nothing at all.

USAGE:   tools/prove-write-scope-gate.py
EXIT:    0 = every case behaved, 1 = the gate got one wrong.
"""
import json
import os
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
GATE = os.path.join(REPO, "tools", "hook-analytics", "write_scope_gate.py")

RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

ALLOWED, REFUSED = "allowed", "refused"

# (what the role may write, what the call is, what must happen, a plain description)
CASES = [
    # --- a role that may write NOTHING -----------------------------------------------------
    ([], ("Edit", {"file_path": "unity/Assets/Scripts/Editor/MobRoster.cs"}), REFUSED,
     "read-only role editing code"),
    ([], ("Write", {"file_path": "docs/README.md"}), REFUSED,
     "read-only role writing a document"),
    ([], ("Bash", {"command": "echo hi > docs/README.md"}), REFUSED,
     "read-only role writing through a shell redirect"),
    ([], ("Bash", {"command": "sed -i s/a/b/ docs/README.md"}), REFUSED,
     "read-only role rewriting a file in place"),
    ([], ("Bash", {"command": "unity cmd eval --code return 1;"}), REFUSED,
     "read-only role running code inside the editor"),
    ([], ("Bash", {"command": "cat docs/README.md"}), ALLOWED,
     "read-only role reading a file"),
    ([], ("Bash", {"command": "grep -rn Rarity docs"}), ALLOWED,
     "read-only role searching"),
    ([], ("Bash", {"command": "unity status"}), ALLOWED,
     "read-only role asking the editor whether it is up"),
    ([], ("Write", {"file_path": "/tmp/throwaway-probe.py"}), ALLOWED,
     "read-only role writing a throwaway script to the scratchpad"),
    ([], ("Write", {"file_path": os.path.join(tempfile.gettempdir(), "throwaway-probe.py")}), ALLOWED,
     "read-only role writing to the system's own temp folder (the only real one on Windows)"),

    # --- a role that may write GAME DATA ONLY ----------------------------------------------
    (["unity/Assets/3_Data"], ("Write", {"file_path": "unity/Assets/3_Data/Resources/Monsters/X.asset"}), ALLOWED,
     "data author writing a creature"),
    (["unity/Assets/3_Data"], ("Edit", {"file_path": "unity/Assets/3_Data/Resources/Items/X.asset"}), ALLOWED,
     "data author editing an item"),
    (["unity/Assets/3_Data"], ("Edit", {"file_path": "unity/Assets/Scripts/Editor/MobRoster.cs"}), REFUSED,
     "data author reaching into code"),
    (["unity/Assets/3_Data"], ("Edit", {"file_path": "docs/README.md"}), REFUSED,
     "data author reaching into documents"),
    (["unity/Assets/3_Data"], ("Write", {"file_path": "unity/Assets/3_Data/../Scripts/Editor/Sneak.cs"}), REFUSED,
     "data author escaping its folder with .."),

    # --- a role that may write DOCUMENTS AND THE WIKI ONLY ---------------------------------
    (["docs", "wiki"], ("Edit", {"file_path": "docs/1-planning/roadmap-to-launch.md"}), ALLOWED,
     "doc keeper revising a document"),
    (["docs", "wiki"], ("Write", {"file_path": "wiki/pages/anything.json"}), ALLOWED,
     "doc keeper writing a wiki page"),
    (["docs", "wiki"], ("Write", {"file_path": "unity/Assets/3_Data/Resources/Items/X.asset"}), REFUSED,
     "doc keeper reaching into game data"),
]


def run(allowed, call):
    tool_name, tool_input = call
    payload = json.dumps({
        "session_id": "prove-write-scope-gate",
        "tool_name": tool_name,
        "tool_input": tool_input,
        "cwd": REPO,
    })
    proc = subprocess.run([sys.executable, GATE] + list(allowed),
                          input=payload, capture_output=True, text=True)
    return REFUSED if proc.returncode == 2 else ALLOWED


def main():
    if not os.path.isfile(GATE):
        print(RED + "the gate is missing at " + GATE + OFF)
        return 1
    bad = 0
    for allowed, call, expected, description in CASES:
        got = run(allowed, call)
        if got == expected:
            print(GRN + "  ok  " + OFF + description + "  (" + got + ")")
        else:
            bad += 1
            print(RED + " WRONG" + OFF + " " + description +
                  "  (expected " + expected + ", got " + got + ")")
    print()
    if bad:
        print(RED + str(bad) + " of " + str(len(CASES)) + " cases wrong — the fence is not safe to rely on." + OFF)
        return 1
    print(GRN + "all " + str(len(CASES)) + " cases behaved — the fence refuses and allows "
          "correctly." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
