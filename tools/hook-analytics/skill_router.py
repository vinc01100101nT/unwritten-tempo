#!/usr/bin/env python3
"""UserPromptSubmit: when a message is about a job a project skill owns, tell the session to load that skill.

WHY THIS EXISTS. A skill is only used when the session judges that the request matches the skill's description,
and that judgement misses on different wording ("swap the axe", one model instead of a pack) or on a job that
looks too small to need instructions. The user ruled on 2026-10-08 that the weapon-model skill must be used every
time, single replacements included. This puts the instruction in front of the session the moment a matching
message arrives, so using the skill no longer depends on that judgement.

A route fires when the message contains a word from EVERY one of its word lists. Add a route by adding one row.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import guard_check   # the one implementation of the heartbeat every guard here stamps

ROUTES = [
    {
        "skill": "import-weapon-models",
        "job": "a weapon's 3D model — importing a pack, adding one model, or replacing or re-fitting a single weapon",
        "all_of": [
            r"weapons?|swords?|greatswords?|longswords?|axes?|warhammers?|hammers?|maces?|daggers?|katars?|spears?"
            r"|staffs?|staves|wands?|scythes?|fists?|knuckles?|shurikens?|snipers?|firearms?|guns?|rifles?|bows?",
            r"models?|3d|mesh(es)?|prefabs?|fbx|glb|gltf|unitypackage|asset ?store|packs?|grips?|in (the |their |his |her )?hands?"
            r"|holds?|holding|held",
        ],
    },
]


def matches(route, text):
    return all(re.search(r"\b(?:%s)\b" % words, text, re.IGNORECASE) for words in route["all_of"])


def routes_for(text):
    return [r for r in ROUTES if matches(r, text)]


def main():
    try:
        payload = json.load(sys.stdin) if not sys.stdin.isatty() else {}
    except (json.JSONDecodeError, ValueError):
        payload = {}

    guard_check.beat(os.path.basename(__file__), payload.get("session_id", ""))

    hits = routes_for(payload.get("prompt", "") or "")
    if not hits:
        print(json.dumps({"suppressOutput": True}))
        return 0
    lines = ["If this message asks for work on %s, invoke the `%s` skill with the Skill tool BEFORE doing any of "
             "that work, and follow it. This is a standing rule set by the user: the skill is used every time, a "
             "single weapon included. If the message is not about that, ignore this note." % (r["job"], r["skill"])
             for r in hits]
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "UserPromptSubmit",
                                             "additionalContext": "\n".join(lines)}}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
