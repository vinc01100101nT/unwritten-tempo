#!/usr/bin/env python3
"""Picks the cheapest model a helper job can safely run on, by asking Jev to size the job.

Usage: tools/size-helper-job.py <role> "<one-line job description>"
Prints exactly one of haiku / sonnet / opus — pass it as the model when starting the helper. Anything that goes
wrong (no key, no network, a strange reply) prints opus, because a hard job on a small model costs a whole round.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import jev_client

SIZED_ROLES = ("data-author", "doc-keeper", "analyzer")
CRITERIA = {
    "haiku": "A short mechanical job: follow an exact instruction and edit one or two known places. No judgement "
             "about design, code behaviour or wording.",
    "sonnet": "A medium job: many edits or several files following a clear, already-agreed pattern, with some "
              "judgement but no hard reasoning.",
    "opus": "A hard job: designing, diagnosing a cause, balancing numbers, rewriting meaning, or following many "
            "interlocking rules where one wrong judgement breaks the game or a document.",
}
# Step down only when Jev puts less than this chance on the job needing anything bigger.
MAX_RISK_OF_NEEDING_MORE = 0.2


def pick(probabilities):
    p = {m: probabilities.get(m, 0.0) for m in CRITERIA}
    if p["sonnet"] + p["opus"] < MAX_RISK_OF_NEEDING_MORE:
        return "haiku"
    if p["opus"] < MAX_RISK_OF_NEEDING_MORE:
        return "sonnet"
    return "opus"


def size(role, job):
    reply = jev_client.ask(
        {"project": "an online role-playing game in Unity; a helper AI is about to be given one job",
         "job": "%s helper, job: %s" % (role, job)},
        {"model": {"type": "choice",
                   "instructions": "Which model should do this job? Pick the cheapest that will not get it wrong.",
                   "criteria": CRITERIA}})
    return pick(reply["answers"]["model"]["probabilities"])


def main():
    if len(sys.argv) != 3:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    role, job = sys.argv[1], sys.argv[2]
    if role not in SIZED_ROLES:
        print("opus")
        print("%s is not sized; it always runs on its own model" % role, file=sys.stderr)
        return 0
    try:
        print(size(role, job))
    except Exception as e:  # any failure falls back to the safe model
        print("opus")
        print("Jev sizing failed, using opus: %s" % e, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
