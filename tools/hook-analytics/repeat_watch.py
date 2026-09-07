#!/usr/bin/env python3
"""PostToolUse: notice when the SAME heavy command is run more than once in a session.

WHY THIS EXISTS. The project's rule is "open the AI-learnings index before a SECOND attempt at
anything that already failed once". That rule was wired to exactly one moment — the end-of-turn
check BLOCKING on a compile error or bad data — so it only ever fired when the C# was broken. On
2026-08-31 a whole day was lost to a build that came out broken and creatures that did nothing;
the C# compiled clean every single time, nothing ever blocked, and that index was never once put
in front of anybody. The wording was fine. It was listening for the wrong signal.

This listens for the right one. Nobody runs a five-minute build twice for fun: a REPEAT of the
same heavy command IS the second attempt the rule is about, and unlike an exit code it needs no
guessing about what "failed" means for a given tool.

It only counts, and writes nothing to the conversation. The end-of-turn check reads the tally and
does the speaking, because that is the one place in this project already proven able to stop a
turn and be heard.
"""
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import guard_check   # the one implementation of the heartbeat every guard here stamps

TALLY = os.path.join(REPO, "tools", "hook-analytics", "repeat-tally.json")

# Commands worth counting: the slow, world-changing ones. A repeat of any of these is somebody
# trying again, which is the moment the learnings index is for. Everything else — reading files,
# grepping, asking the clock — repeats constantly and means nothing.
#
# ⛔ THE THREE OFFLINE CHECKERS ARE DELIBERATELY NOT HERE, and must never be added:
# check-unity-code.sh, check-skill-data.py and check-perf-hotpaths.py. The project's own hard rule
# is to run them after EVERY edit, mid-task — so running one seven times in a session is seven edits
# each verified as required, not seven attempts at anything. They take about four seconds and touch
# nothing. Counting them made this guard fire on the CORRECT behaviour in 13 of the 16 sessions in
# the tally (measured 2026-09-07: 73 runs in one session, 61 in another, all of them compliance),
# and each firing cost a round — while burying the two that genuinely matter, a five-minute build
# and a server restart, in the same message. A guard that cries wolf is one that gets ignored.
#
# ⛔ `recompile` IS OUT FOR THE SAME REASON, and must never be added back. The project's rule is to
# push an edit into the editor yourself rather than hand the user a click, so one recompile per C# edit
# is the required move — two edits in a turn means two recompiles and neither is a retry. Measured
# 2026-09-07 over the 17 sessions in the tally: a recompile-shaped entry alarmed in 10 of them, and it
# is the single most-counted word in the whole file. `recompile_status` is a READ and was never meant
# to be here at all; it only ever appeared because the matching used to treat it as the same word.
HEAVY = (
    "build-game.sh",
    "unwritten-server.app",
    "npm start",
    "npm run",
    "editor_play",
)

_WS = re.compile(r"\s+")

# Programs that RUN whatever you name them, so a heavy word among their arguments is a real attempt.
# Anything not on this list (grep, sed, cat, head, cp, diff, ls, pkill, ps…) merely MENTIONS its
# arguments, and a mention is not an attempt — see RUNS_IT below for why that distinction matters.
EXECUTORS = ("unity", "python3", "python", "py", "sh", "bash", "node", "npm", "npx", "dotnet")

# Splits one command line into the separate things it runs.
_SEGMENT = re.compile(r"(?:&&|\|\||[;|\n])")

# Leading FOO=bar assignments, which sit in front of the program without being it.
_ASSIGNMENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")

# Redirections, pipes and separators. They follow a command constantly and say nothing about WHICH
# attempt it is, so taking one as the argument split one command into several keys — `recompile` and
# `recompile 2>&1` were counted as two different things and neither ever reached two.
_SHELL_NOISE = re.compile(r"^(?:[0-9]*[<>&|]|&&|\|\||[;&|]|\$)")


def _runs_it(segment, word):
    """Is this segment RUNNING the thing named by `word`, or only talking about it?

    THE WHOLE POINT OF THIS FUNCTION. It used to be a plain substring search over the raw command,
    so `grep -n logFile tools/build-game.sh`, `sed -n 140,165p tools/build-game.sh` and
    `cp tools/build-game.sh backup/` each counted as a five-minute BUILD. Reading a file three times
    while editing it then looked exactly like three failed attempts at building the game, and the
    end-of-turn check said so — a guard that cries wolf is one that gets ignored, which is the
    opposite of what it is for. (Measured 2026-09-07: it fired three times in one session and not
    once on an actual repeat.)"""
    tokens = [t for t in segment.strip().split(" ") if t]
    while tokens and _ASSIGNMENT.match(tokens[0]):
        tokens.pop(0)                       # CHAR_SERVER_URL=… builds/server/… → the build is the program
    if not tokens:
        return False

    program = tokens[0]
    if word in program:
        return True                         # tools/check-unity-code.sh … — run outright

    base = program.rsplit("/", 1)[-1]
    if base not in EXECUTORS:
        return False                        # grep/sed/cp/diff/pkill naming it is not running it

    rest = tokens[1:]
    # `bash -n file` CHECKS a script's syntax and deliberately does not run it.
    if base in ("sh", "bash") and "-n" in rest:
        return False
    # `python3 - <<EOF` runs a script from stdin; a heavy word inside that script is data, not a run.
    for token in rest:
        if token.startswith("-") and token != "-":
            continue
        if token == "-":
            return False
        break

    # THE WHOLE RUN LINE, not one token at a time. Two entries here are two WORDS — `npm run` and
    # `npm start` — and no single token can ever contain either, so asking token by token meant those
    # two matched nothing at all, in any session, ever. (Measured 2026-09-07 against the saved tally:
    # every npm row in it predates the day this function learned to tell running from mentioning; a
    # dead entry reports no error, which is exactly why nobody noticed.) Every negative case — grep,
    # sed, cp, a syntax check, a script fed on stdin — has already been turned away above, so joining
    # the arguments here cannot make one of those match.
    return word in (base + " " + " ".join(rest))


def key_for(command):
    """A stable name for 'the same attempt again', ignoring the parts that differ run to run.

    Paths, log destinations and flags all change between two goes at the same thing, so the key is
    the heavy word plus the first plain argument after it — 'build-game.sh server' stays distinct
    from 'build-game.sh client', which are genuinely different attempts."""
    if not command:
        return None
    flat = _WS.sub(" ", command.strip())
    for word in HEAVY:
        # WHOLE WORD, not any old substring. `recompile` sits inside `recompile_status`, so the two
        # were filed as one thing under the nonsense name "recompile _status" — which is what the
        # end-of-turn check then read out loud. Polling a status is a READ, not an attempt, and with
        # a boundary it stops matching anything in this list, which is the right answer.
        pattern = re.compile(r"\b" + re.escape(word) + r"\b")
        if not pattern.search(flat):
            continue

        # NAMED IS NOT RUN, and each part of the line is asked separately — one line often both runs
        # something and greps for something else. The argument is read from THAT part only: reading on
        # past a pipe picked up the name of whatever the output was fed to, so `… | tail -5` and
        # `… | head -25` filed the same command under two different keys and neither reached two.
        for segment in _SEGMENT.split(flat):
            found = pattern.search(segment)
            if not found or not _runs_it(segment, word):
                continue
            arg = ""
            for token in segment[found.end():].strip().split(" "):
                if not token or token.startswith("-") or _SHELL_NOISE.match(token):
                    continue
                arg = token
                break
            # A path argument says nothing about WHICH attempt this is — drop it.
            if "/" in arg or arg.startswith("$"):
                arg = ""
            return (word + " " + arg).strip()
    return None


def load():
    try:
        with io.open(TALLY, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save(data):
    try:
        tmp = TALLY + ".%d.tmp" % os.getpid()
        with io.open(tmp, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, sort_keys=True)
        os.replace(tmp, TALLY)
    except OSError:
        pass                       # a counter must never fail the tool it is counting


def repeats(session_id):
    """Every heavy command this session has run more than once, and how many times. Read by the
    end-of-turn check; empty for a session that has tried nothing twice."""
    if not session_id:
        return {}
    counts = (load().get(session_id) or {}).get("counts") or {}
    return {k: v for k, v in counts.items() if v >= 2}


def told(session_id, key):
    """Has the reminder for this repeated command already been given? Asked so it is said ONCE
    per thing, not once per attempt — a reminder that repeats becomes wallpaper."""
    return key in ((load().get(session_id) or {}).get("told") or [])


def mark_told(session_id, keys):
    data = load()
    row = data.setdefault(session_id, {})
    seen = set(row.get("told") or [])
    seen.update(keys)
    row["told"] = sorted(seen)
    save(data)


# Sessions kept in the tally. Enough for every session open at once; small enough that the file
# cannot grow forever. (My number, not a measured one.)
SESSIONS_KEPT = 20


def main():
    try:
        payload = json.load(sys.stdin) if not sys.stdin.isatty() else {}
    except (json.JSONDecodeError, ValueError):
        payload = {}

    guard_check.beat(os.path.basename(__file__), payload.get("session_id", ""))

    session = payload.get("session_id", "")
    command = (payload.get("tool_input") or {}).get("command", "")
    key = key_for(command)
    if session and key:
        data = load()
        row = data.setdefault(session, {})
        counts = row.setdefault("counts", {})
        counts[key] = counts.get(key, 0) + 1
        if len(data) > SESSIONS_KEPT:
            for old in list(data)[: len(data) - SESSIONS_KEPT]:
                if old != session:
                    del data[old]
        save(data)

    # Deliberately silent. Counting is this hook's whole job; the speaking happens at the end of
    # the turn, where a message cannot be lost among tool output.
    print(json.dumps({"suppressOutput": True}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
