#!/usr/bin/env python3
"""Keeps the repeat watcher honest. Run it after ANY edit to repeat_watch.py.

WHY THIS EXISTS. The watcher decides whether a command was RUN or merely MENTIONED, and it gets that
wrong silently: a wrong answer produces no error, just an end-of-turn reminder about a repeat that
never happened. It fired three times in one session on 2026-09-07 without a single real repeat behind
it — every one of them was a `grep`, `sed` or `cp` that happened to name a script — and a guard that
cries wolf is one that gets ignored, which is the opposite of what it is for.

Every case below is a REAL command line from that session, with what it should have meant. Same
contract as tools/prove-skill-data-check.py: the checker is only trustworthy while its prover passes.

    python3 tools/hook-analytics/prove_repeat_watch.py     # exit 0 = the watcher still tells them apart
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import repeat_watch

# (command line, the key it should be filed under — None means "this is not an attempt at anything")
CASES = [
    # --- genuinely running something heavy -------------------------------------------------------
    # The same shell shapes the three offline checkers used to cover — a bare script, a `cd &&`, a
    # `python3 …`, a pipe — kept here on commands that ARE heavy, so dropping those three from the
    # list costs no coverage of run-versus-mention.
    ("tools/build-game.sh 2>&1 | tail -5",                                   "build-game.sh"),
    ("cd /repo && tools/build-game.sh",                                      "build-game.sh"),
    # A heavy entry that is TWO WORDS. No single argument can ever contain one, so these matched
    # nothing at all until the run line was read whole — a dead entry that reported no error.
    ("npm run build",                                                        "npm run build"),
    ("cd web && npm run dev",                                                "npm run dev"),
    ("npm start 2>&1 | tail -5",                                             "npm start"),
    ("tools/build-game.sh both",                                             "build-game.sh both"),
    ("tools/build-game.sh server --linux",                                   "build-game.sh server"),
    ("tools/build-game.sh client",                                           "build-game.sh client"),
    ("CHAR_SERVER_URL=http://x SERVER_SECRET=y "
     "builds/server/unwritten-server.app/unwritten -batchmode",              "unwritten-server.app"),

    # --- merely TALKING about it: reading, searching, copying, syntax-checking --------------------
    ("grep -n 'logFile' tools/build-game.sh | head -20",                     None),
    ("sed -n '140,165p' tools/build-game.sh",                                None),
    ("head -60 tools/build-game.sh",                                         None),
    ("wc -l tools/hook-analytics/repeat_watch.py",                           None),
    ("bash -n tools/build-game.sh && echo ok",                               None),
    ("cp tools/build-game.sh /tmp/backup/",                                  None),
    ("grep -rln 'unwritten-server' . | head",                                None),
    ("ps aux | grep -iE 'unwritten-server|CharServer' | grep -v grep",       None),
    ("pkill -f 'unwritten-server.app/unwritten'",                            None),
    ("python3 - <<'EOF'\nio.open('tools/build-game.sh')\nEOF",               None),

    # --- polling a STATUS is a read, not an attempt ----------------------------------------------
    ("unity cmd recompile_status 2>&1",                                      None),

    # --- one line that both runs something and greps for something else --------------------------
    ("tools/build-game.sh | tail -3; grep -n x tools/check-unity-code.sh",   "build-game.sh"),

    # --- WORK THE PROJECT'S RULES DEMAND IS NOT AN ATTEMPT, however many times it runs ------------
    # The rule is to run the three checkers after EVERY edit, and to push each edit into the editor
    # yourself instead of handing over a click — so a repeat of any of these is compliance, not a
    # retry. These cases exist so that putting one back on the heavy list fails loudly instead of
    # quietly restoring a guard that fired on correct behaviour in 13 of 16 sessions (the checkers)
    # and 10 of 17 (recompile). See repeat_watch.HEAVY.
    ("tools/check-unity-code.sh 2>&1 | tail -5",                             None),
    ("cd /repo && tools/check-unity-code.sh",                                None),
    ("python3 tools/check-perf-hotpaths.py 2>&1 | head -25",                 None),
    ("python3 tools/check-skill-data.py 2>&1 | tail -2",                     None),
    ("unity cmd recompile 2>&1 | tail -2",                                   None),
    ("unity cmd recompile",                                                  None),
]


def main():
    bad = 0
    for command, want in CASES:
        got = repeat_watch.key_for(command)
        if got != want:
            bad += 1
            one_line = command.replace("\n", "\\n")
            print("FAIL  %s\n        got %r, expected %r" % (one_line, got, want))
    if bad:
        print("\n%d of %d wrong — the watcher can no longer tell running from mentioning." % (bad, len(CASES)))
        return 1
    print("All %d cases right: the watcher still tells running from mentioning." % len(CASES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
