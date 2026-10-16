#!/usr/bin/env python3
"""Prove the order gate still refuses what it must and lets through what it must — a gate gone blind looks exactly
like a gate with nothing to stop. Every case is a real message from 2026-10-16 or its plain opposite."""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import order_gate as g

BASH = "Bash"
CASES = [
    # (message, command, should run?)
    ("how to publish all, windows, mac, website, patch", "tools/build-and-publish.sh", False),
    ("no, i only want a publish. publish lahat, website patch game", "tools/build-and-publish.sh", False),
    ("no, i only want a publish. publish lahat, website patch game", "tools/publish.sh", True),
    ("how to publish all", "tools/publish.sh", False),
    ("paano mag publish", "python tools/publish-update.py --site-only", False),
    ("build and publish everything", "tools/build-and-publish.sh --server", True),
    ("commit everything and push to main", "git commit -q -F -", True),
    ("commit everything and push to main", "git push origin main", True),
    ("commit everything", "git push origin main", False),
    ("push, including the new patch notes", "git push origin main", True),
    ("rebuild the server", "tools/build-game.sh server", True),
    ("publish it", "tools/build-game.sh client", False),
    ("anything at all", "git status --porcelain", True),
    ("anything at all", "tools/check-unity-code.sh", True),
    # NAMED IS NOT RUN — reading, testing or syntax-checking these must never be refused.
    ("anything at all", 'grep -n "git push" docs/x.md', True),
    ("anything at all", 'echo \'{"command":"git push origin main"}\' | python tools/hook-analytics/perf_gate.py', True),
    ("anything at all", "bash -n tools/publish.sh", True),
    ("anything at all", "sed -n 1,40p tools/publish-update.py", True),
    ("anything at all", "cd /repo && git push origin main", False),
    ("anything at all", 'PY=python3 "$PY" tools/publish-update.py --site-only', False),
    ("anything at all", "python3 tools/publish-update.py --windows builds/client", False),
    (None, "git push origin main", False),
]


def main():
    bad = 0
    for msg, cmd, want in CASES:
        got = g.decide(BASH, {"command": cmd}, msg) is None
        if got != want:
            bad += 1
            print("WRONG: %r + %r -> %s, should %s" % (msg, cmd, "runs" if got else "refused", "run" if want else "be refused"))
    tool_case = g.decide("mcp__unity-editor-mcp__build", {}, "how do i build") is None
    if tool_case:
        bad += 1
        print("WRONG: the editor's build tool ran on a how-to question")
    if bad:
        print("order gate: %d case(s) wrong" % bad)
        return 1
    print("order gate proved: %d cases" % (len(CASES) + 1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
