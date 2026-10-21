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
    "compose-skill": [
        "make the sword skill cost less mana",
        "let's start brainstorming more skills now, let's start with the bow",
        "put numbers on the bow skills, the damage and cd",
        "for all the bow skills without a release animation, give them a bow attack",
        "change the passive so it triggers on dodge",
        "rename downpour skill to jet stream",
        "the ultimate needs a vfx",
    ],
}
# Each message must reach NONE of the routes named beside it.
MUST_STAY_QUIET = {
    "the axe does too much damage": ["import-weapon-models", "compose-skill"],
    "what does the bow's attack range do?": ["import-weapon-models", "compose-skill"],
    "import the new ground textures": ["import-weapon-models", "compose-skill"],
    "the player model's hair is the wrong colour": ["import-weapon-models", "compose-skill"],
    "make the sword skill cost less mana": ["import-weapon-models"],
    "is the cloudburst yellow total right?": ["compose-skill"],
}


def main():
    bad = []
    for skill, messages in MUST_FIRE.items():
        for m in messages:
            if skill not in [r["skill"] for r in skill_router.routes_for(m)]:
                bad.append("did NOT route to %s: %r" % (skill, m))
    for m, never in MUST_STAY_QUIET.items():
        hits = [r["skill"] for r in skill_router.routes_for(m) if r["skill"] in never]
        if hits:
            bad.append("routed %r to %s" % (m, ", ".join(hits)))
    for line in bad:
        print("FAIL " + line)
    total = sum(len(v) for v in MUST_FIRE.values()) + len(MUST_STAY_QUIET)
    print("%d of %d cases right" % (total - len(bad), total))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
