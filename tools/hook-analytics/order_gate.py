#!/usr/bin/env python3
"""Run by perf_gate.py on every PreToolUse of Bash and of the editor's build tool, so it costs no Python start of
its own: a heavy or outward action runs ONLY when the user's latest message literally orders it.

WHY THIS EXISTS. The rule "a question gets an answer, never an action" was written down three times and broken
anyway, because whether a message is an order is a judgement, and the judgement is made in the same breath as the
urge to be useful. On 2026-10-16 "how to publish all" was answered with a build plan, and "i only want a publish"
started a 15-minute build of both games that had to be killed half-way. So the judgement is taken away: before a
build, a publish, a push, a commit, a deploy or a server start/restart, this reads the user's LATEST message and
refuses unless that message says the action's own verb and is not a "how to" question.

The verb is taken literally: "publish" does not order a build, "commit" does not order a push. Only what a command
RUNS counts — a grep, an echo or a sed that merely names `git push` is not a push. The tables below are the whole
policy; add an action by adding one row and one case to prove_order_gate.py.
"""
import json
import os
import re

# action → (what RUNS it — matched against the program a shell segment runs, never against text it merely names —
# and the verbs the user's message must contain: EVERY group, one word from each)
ACTIONS = [
    ("build and publish", r"build-and-publish\.sh",                       [r"build|rebuild", r"publish"]),
    ("build",             r"build-game\.sh|build-launcher\.sh",          [r"build|rebuild"]),
    ("publish",           r"publish-update\.py|publish\.sh",             [r"publish"]),
    ("push",              r"git push",                                   [r"push"]),
    ("commit",            r"git commit",                                 [r"commit"]),
    ("deploy",            r"rsync|scp",                                  [r"deploy|upload|rsync|copy"]),
    ("server start",      r"(?:re)?start-servers\.sh|run-map-server\.sh", [r"start|restart|run|launch"]),
]
# The editor's own build tool, called directly instead of through Bash.
TOOL_ACTIONS = {"mcp__unity-editor-mcp__build": ("build", [r"build|rebuild"])}

# Programs that run the script named after them, so `python tools/publish-update.py` runs the publish.
EXECUTORS = ("python", "python3", "py", "sh", "bash", '"$PY"', "$PY")
_SEGMENT = re.compile(r"&&|\|\||[;|\n]")
_ASSIGNMENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")

# A message asking HOW orders nothing, whatever verbs it contains.
QUESTION = re.compile(r"\bhow\s+(to|do|does|can|should|would)\b|\bpaano\b|\bpano\b|\bwhat\s+(is|does)\s+the\s+command\b",
                      re.IGNORECASE)


def latest_user_text(transcript_path):
    """The text the user typed most recently — a typed message, or one sent while a turn was running. Tool results,
    system reminders and interruption notices are not something the user said."""
    if not transcript_path or not os.path.isfile(transcript_path):
        return None
    last = None
    try:
        with open(transcript_path, encoding="utf-8", errors="replace") as f:
            for line in f:
                if '"user"' not in line:
                    continue
                try:
                    entry = json.loads(line)
                except ValueError:
                    continue
                if entry.get("type") != "user":
                    continue
                content = (entry.get("message") or {}).get("content")
                texts = []
                if isinstance(content, str):
                    texts.append(content)
                elif isinstance(content, list):
                    for block in content:
                        if isinstance(block, dict) and block.get("type") == "text":
                            texts.append(block.get("text") or "")
                for t in texts:
                    t = re.sub(r"<system-reminder>.*?</system-reminder>", "", t, flags=re.S).strip()
                    if not t or t.startswith("[Request interrupted"):
                        continue
                    last = t
    except OSError:
        return None
    return last


def ordered(message, verb_groups):
    if message is None:
        return False
    if QUESTION.search(message):
        return False
    return all(re.search(r"\b(?:%s)\b" % g, message, re.IGNORECASE) for g in verb_groups)


def _what_runs(segment):
    """The program a shell segment runs, plus its first argument — `git push`, `tools/publish.sh`, or for
    `python x.py` / `bash x.sh` the script itself. Text an echo, grep or sed merely NAMES is not run, and neither is
    a syntax check (`bash -n`) or a script fed on stdin (`python -`)."""
    tokens = [x for x in segment.strip().split() if x]
    while tokens and _ASSIGNMENT.match(tokens[0]):
        tokens.pop(0)
    if not tokens:
        return ""
    base = tokens[0].rsplit("/", 1)[-1]
    if base in EXECUTORS or tokens[0] in EXECUTORS:
        rest = [x for x in tokens[1:] if not x.startswith("-") or x == "-"]
        if "-n" in tokens[1:] or not rest or rest[0] == "-":
            return ""
        return rest[0]
    return tokens[0] + (" " + tokens[1] if len(tokens) > 1 else "")


def action_for(tool, tool_input):
    if tool in TOOL_ACTIONS:
        return TOOL_ACTIONS[tool]
    if tool != "Bash":
        return None
    cmd = (tool_input or {}).get("command", "") or ""
    runs = [_what_runs(s) for s in _SEGMENT.split(cmd)]
    for name, mark, verbs in ACTIONS:
        pat = re.compile(r"(?:^|/)(?:%s)\b" % mark)
        if any(r and pat.search(r) for r in runs):
            return name, verbs
    return None


def decide(tool, tool_input, message):
    """None = let it run; otherwise the reason it is refused."""
    hit = action_for(tool, tool_input)
    if hit is None:
        return None
    name, verbs = hit
    if ordered(message, verbs):
        return None
    return ("ORDER GATE: this is a %s, and the user's latest message does not order one. A question (\"how to …\") "
            "gets the command as an ANSWER, never a run, and the user's verb is literal: publish is not build, commit "
            "is not push. Answer what they asked and stop. Their latest message: %r"
            % (name, (message or "")[:200]))
