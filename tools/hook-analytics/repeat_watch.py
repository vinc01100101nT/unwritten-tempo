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
HEAVY = (
    "build-game.sh",
    "check-unity-code.sh",
    "check-skill-data.py",
    "check-perf-hotpaths.py",
    "unwritten-server.app",
    "npm start",
    "npm run",
    "editor_play",
    "recompile",
)

_WS = re.compile(r"\s+")


def key_for(command):
    """A stable name for 'the same attempt again', ignoring the parts that differ run to run.

    Paths, log destinations and flags all change between two goes at the same thing, so the key is
    the heavy word plus the first plain argument after it — 'build-game.sh server' stays distinct
    from 'build-game.sh client', which are genuinely different attempts."""
    if not command:
        return None
    flat = _WS.sub(" ", command.strip())
    for word in HEAVY:
        at = flat.find(word)
        if at < 0:
            continue
        rest = flat[at + len(word):].strip().split(" ")
        arg = ""
        for token in rest:
            if token and not token.startswith("-"):
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
