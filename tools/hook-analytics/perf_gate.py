#!/usr/bin/env python3
"""PreToolUse gate: block edits to runtime C# until PERFORMANCE.md has been read
this session, and log every gate decision to hook-analytics.jsonl for the dashboard.

Invoked by Claude Code with the hook payload (session_id, tool_name, tool_input,
transcript_path, ...) on stdin. See tools/hook-analytics/README.md.

How "has it been read?" works WITHOUT a hardcoded phrase
-------------------------------------------------------
We look for a sentinel — a phrase that enters the transcript ONLY when PERFORMANCE.md
is actually Read. Earlier this sentinel was a string literal in THIS file, which meant
reading the gate's own source (to debug it) poisoned the transcript and defeated the
gate for the rest of the session. So we now DERIVE the sentinel at runtime from the
live PERFORMANCE.md: its longest prose line that is NOT also present in the sibling
CLAUDE.md. CLAUDE.md is auto-injected into the transcript whenever you edit a script,
so any line it shares with PERFORMANCE.md would be a false "already read" — we exclude
those. The literal phrase never lives in this file, so reading this file can't trip it.
"""
import sys, os, re, json, datetime

HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, "hook-analytics.jsonl")

_WS = re.compile(r"\s+")
_NONALNUM = re.compile(r"[^a-z0-9]+")


def normalize(text: str) -> str:
    return _WS.sub(" ", text).strip()


def fingerprint(text: str) -> str:
    """Lowercase, then collapse every run of non-alphanumeric chars to a single
    space. The transcript is JSONL, so the Read'd file content lands with its quotes
    backslash-escaped (" -> \\") and is decoded from mixed UTF-8/cp1252 bytes; an exact
    substring of the raw sentinel therefore never matches. Reducing both sides to bare
    [a-z0-9] words is immune to quote-escaping, encoding drift, line-number prefixes,
    and soft-wrapping — it matches on the words alone."""
    return _NONALNUM.sub(" ", text.lower()).strip()


def is_runtime_cs(path: str) -> bool:
    p = path.replace("\\", "/").lower()
    if not p.endswith(".cs"):
        return False
    if "/assets/scripts/" not in p:
        return False
    # Editor-only code isn't a runtime hot path — exclude it.
    if "/assets/scripts/editor/" in p:
        return False
    return True


def scripts_root(file_path: str) -> str:
    """The '.../Assets/Scripts/' prefix of the edited file, in its real casing —
    so PERFORMANCE.md and CLAUDE.md can be located as siblings of any gated file,
    regardless of where the repo lives."""
    fwd = file_path.replace("\\", "/")
    idx = fwd.lower().find("/assets/scripts/")
    return fwd[: idx + len("/assets/scripts/")]


def read_text(path: str) -> str:
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as f:
            return f.read()
    except OSError:
        return ""


def is_prose(line: str) -> bool:
    """A wrapped body line — not a heading, list item, table row, blockquote, rule,
    or checkbox. These are the lines that read as plain sentences in the transcript."""
    s = line.strip()
    if len(s) < 40:
        return False
    if s[0] in "#>|*-[":
        return False
    if s.startswith("---"):
        return False
    if re.match(r"^\d+[.)]", s):
        return False
    if "|" in s:  # table row / cell
        return False
    return True


def derive_sentinel(perf_md_path: str, claude_md_path: str) -> str:
    """Longest normalized prose line in PERFORMANCE.md that does NOT also appear in
    the sibling CLAUDE.md (which the harness auto-injects into the transcript). Empty
    string if PERFORMANCE.md is missing/has no usable line."""
    perf = read_text(perf_md_path)
    if not perf:
        return ""
    exclusion = normalize(read_text(claude_md_path))
    best = ""
    for raw in perf.splitlines():
        if not is_prose(raw):
            continue
        n = normalize(raw)
        if n in exclusion:
            continue
        if len(n) > len(best):
            best = n
    return best


def transcript_has(sentinel: str, transcript_path: str) -> bool:
    if not sentinel or not transcript_path or not os.path.isfile(transcript_path):
        return False
    return fingerprint(sentinel) in fingerprint(read_text(transcript_path))


def log(session_id: str, file_path: str, outcome: str) -> None:
    rec = {
        "ts": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
        "session_id": session_id,
        "file": file_path,
        "outcome": outcome,  # "blocked" | "passed"
        "doc": "PERFORMANCE.md",
    }
    try:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec) + "\n")
    except OSError:
        pass  # analytics must never break the gate


def allow():
    sys.exit(0)


def deny(reason: str):
    # Satisfy BOTH block protocols so this works on any Claude Code build:
    #  - JSON on stdout (modern): permissionDecision "deny"
    #  - exit code 2 (classic): stderr is surfaced to the model and the call is blocked
    # Some builds ONLY honor exit 2, so we must exit 2 (not 0) and put the reason on stderr.
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.stderr.write(reason + "\n")
    sys.exit(2)


def main():
    try:
        data = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        allow()  # malformed payload → never block real work
        return

    tool_input = data.get("tool_input") or {}
    file_path = tool_input.get("file_path") or ""
    if not is_runtime_cs(file_path):
        allow()  # not a gated file → silent, no log
        return

    session_id = data.get("session_id") or ""
    root = scripts_root(file_path)
    sentinel = derive_sentinel(root + "PERFORMANCE.md", root + "CLAUDE.md")

    if transcript_has(sentinel, data.get("transcript_path") or ""):
        log(session_id, file_path, "passed")
        allow()
    else:
        log(session_id, file_path, "blocked")
        deny(
            "Performance is a hard constraint here. Read "
            "unity/Assets/Scripts/PERFORMANCE.md before editing runtime C#, "
            "then retry this edit."
        )


if __name__ == "__main__":
    main()
