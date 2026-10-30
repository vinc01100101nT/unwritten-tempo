#!/usr/bin/env python3
"""UserPromptSubmit: when this session's context passes a limit, tell the session to remind the user to /compact.

WHY THIS EXISTS. The session cannot see its own context size and cannot run /compact itself; only the user can.
The session's own transcript records the context size after every reply (an assistant message's usage: fresh
input + cache written + cache read), so this reads the latest one and, past the limit, asks the reply to open
with a reminder. Ruled by the user on 2026-10-28: first at 150,000 tokens, then once per further 25,000.

The step already reminded is remembered per session, so the reminder fires once per step, not on every message.
A /compact drops the size below the step, which lowers the remembered step and re-arms it.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import guard_check   # the one implementation of the heartbeat every guard here stamps

FIRST = 150_000
EVERY = 25_000
TAIL_BYTES = 256 * 1024
STATE = os.path.join(HERE, "context-watch.json")
SESSIONS_KEPT = 10


def context_size(transcript):
    """The context size after the latest main-session reply, or None when the transcript has none yet."""
    try:
        with open(transcript, "rb") as f:
            f.seek(0, os.SEEK_END)
            f.seek(max(0, f.tell() - TAIL_BYTES))
            lines = f.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return None
    for line in reversed(lines):
        if '"usage"' not in line:
            continue
        try:
            entry = json.loads(line)
        except ValueError:
            continue
        if entry.get("isSidechain"):
            continue
        usage = (entry.get("message") or {}).get("usage") or {}
        if "input_tokens" in usage:
            return (usage.get("input_tokens", 0) + usage.get("cache_creation_input_tokens", 0)
                    + usage.get("cache_read_input_tokens", 0))
    return None


def step_for(size):
    """0 below the first limit, then 1 at 150k, 2 at 175k, …"""
    return 0 if size < FIRST else 1 + (size - FIRST) // EVERY


def load_state():
    try:
        with open(STATE, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return []


def save_state(state):
    try:
        with open(STATE, "w", encoding="utf-8") as f:
            json.dump(state[-SESSIONS_KEPT:], f)
    except OSError:
        pass


def main():
    try:
        payload = json.load(sys.stdin) if not sys.stdin.isatty() else {}
    except (json.JSONDecodeError, ValueError):
        payload = {}

    session = payload.get("session_id", "")
    guard_check.beat(os.path.basename(__file__), session)

    size = context_size(payload.get("transcript_path", "") or "")
    if size is None:
        print(json.dumps({"suppressOutput": True}))
        return 0

    step = step_for(size)
    state = load_state()
    reminded = next((s.get("step", 0) for s in state if s.get("session") == session), 0)
    save_state([s for s in state if s.get("session") != session] + [{"session": session, "step": step}])

    if step <= reminded:
        print(json.dumps({"suppressOutput": True}))
        return 0
    note = ("This session's context is at %s tokens (reminder limit %s, then every %s). Open your reply with one "
            "line telling the user: \"Context at %s tokens, time to /compact.\" Then answer the message as usual."
            % (f"{size:,}", f"{FIRST:,}", f"{EVERY:,}", f"{size:,}"))
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": note}}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
