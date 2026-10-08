#!/usr/bin/env python3
"""Prove the skill router still fires on the messages it exists for, and stays quiet on the rest.

Re-run after ANY edit to skill_router.py. A router gone deaf prints exactly what a quiet message prints, so the
only proof it works is a list of messages it must catch.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import skill_router

MUST_FIRE = {
    "import-weapon-models": [
        "done importing the weapon pack, can you put them in the hands?",
        "swap the axe model for this one",
        "the bow model faces the wrong way",
        "replace the dagger mesh with the new fbx",
        "I downloaded a sword pack from the asset store",
        "the spear sits wrong in the hand",
        "make the character hold the new staff prefab",
        "can you do the 3d weapon model mapping to the hands yourself?",
    ],
}
MUST_STAY_QUIET = [
    "make the sword skill cost less mana",
    "the axe does too much damage",
    "what does the bow's attack range do?",
    "import the new ground textures",
    "the player model's hair is the wrong colour",
]


def main():
    bad = []
    for skill, messages in MUST_FIRE.items():
        for m in messages:
            if skill not in [r["skill"] for r in skill_router.routes_for(m)]:
                bad.append("did NOT route to %s: %r" % (skill, m))
    for m in MUST_STAY_QUIET:
        hits = skill_router.routes_for(m)
        if hits:
            bad.append("routed %r to %s" % (m, ", ".join(r["skill"] for r in hits)))
    for line in bad:
        print("FAIL " + line)
    total = sum(len(v) for v in MUST_FIRE.values()) + len(MUST_STAY_QUIET)
    print("%d of %d cases right" % (total - len(bad), total))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
