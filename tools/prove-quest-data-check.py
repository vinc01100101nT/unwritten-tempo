#!/usr/bin/env python3
"""Prove tools/check-quest-data.py can still SEE: plant one fault at a time into a throwaway mirror of the repo
(tools/prove_mirror.py — the real files are never opened for writing), run the checker against the mirror, and demand
it fail AND name the fault. Then demand the real project still passes. Every anchor is read out of the real quest asset
at run time, so a fault always lands on today's data.

USAGE:   tools/prove-quest-data-check.py
EXIT:    0 = every planted fault caught and the real project passes, 1 = blind or crying wolf.
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from prove_mirror import mirror_with_change, discard  # noqa: E402

CHECK = os.path.join(HERE, "check-quest-data.py")
QUESTS = os.path.join("unity", "Assets", "3_Data", "Resources", "Quests")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def run(root=None):
    args = [sys.executable, CHECK] + (["--root", root] if root else [])
    p = subprocess.run(args, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def case(description, rel, old, new, must_say):
    root, hits = mirror_with_change(REPO, rel, old, new)
    if root is None:
        print(RED + " WRONG" + OFF + " %s — the anchor was not found in %s, so this proves nothing" % (description, rel))
        return 1
    try:
        code, out = run(root)
    finally:
        discard(root)
    if code == 1 and must_say in out:
        print(GRN + "  ok  " + OFF + description)
        return 0
    print(RED + " WRONG" + OFF + " %s (exit %d, expected to say %r)" % (description, code, must_say))
    print("\n".join("         " + l for l in out.splitlines()[:6]))
    return 1


def main():
    code, out = run()
    if code != 0:
        print(RED + "the real project does not pass the check, so no planted fault can be told apart:" + OFF)
        print(out)
        return 1
    folder = os.path.join(REPO, QUESTS)
    assets = sorted(f for f in os.listdir(folder) if f.endswith(".asset")) if os.path.isdir(folder) else []
    if not assets:
        print(RED + "no quest assets to plant faults in, so this proves nothing" + OFF)
        return 1
    texts = {}
    for f in assets:
        with open(os.path.join(folder, f), encoding="utf-8") as fh:
            texts[os.path.join(QUESTS, f)] = fh.read()

    def where(pattern):
        """The first quest asset whose text matches, and the match — so each fault lands on today's real data."""
        for rel, text in texts.items():
            m = re.search(pattern, text, re.M)
            if m:
                return rel, m
        return None, None

    bad = 0

    def planted(description, pattern, old_of, new_of, must_say):
        rel, m = where(pattern)
        if rel is None:
            print(RED + " WRONG" + OFF + " %s — no quest has anything to plant it in, so this proves nothing" % description)
            return 1
        return case(description, rel, old_of(m), new_of(m), must_say)

    bad += planted("a step naming an item the game does not have", r"itemId: (item\.[\w.-]+)\n\s+folds",
                   lambda m: "itemId: %s\n" % m.group(1), lambda m: "itemId: %s-gone\n" % m.group(1),
                   "which the game does not have")
    bad += planted("a talk step naming a townsperson nobody lists", r"townspersonId: (\w[\w-]*)",
                   lambda m: "townspersonId: %s\n" % m.group(1), lambda m: "townspersonId: %s-gone\n" % m.group(1),
                   "whom the townsfolk list does not have")
    bad += planted("a kill step naming a creature nobody has", r"creatureId: (mob\.[\w.-]+)",
                   lambda m: "creatureId: %s\n" % m.group(1), lambda m: "creatureId: %s-gone\n" % m.group(1),
                   "which the monster catalog does not have")
    bad += planted("a kill step for a creature the world never places in that map", r"^      map: (\w+)$",
                   lambda m: "      map: %s\n" % m.group(1), lambda m: "      map: lastline\n",
                   "places nowhere")
    bad += planted("an offered quest with no offer text", r"^  offerText: ",
                   lambda m: "  offerText: ", lambda m: "  offerText_gone: ", "has no offer text")
    bad += planted("a rewarded quest with a step judged on holding something", r"^      kind: 4$",
                   lambda m: "      kind: 4\n", lambda m: "      kind: 1\n", "judged on holding something")
    bad += planted("a quest offered by somebody nobody lists", r"offeredBy: (\w[\w-]*)",
                   lambda m: "offeredBy: %s\n" % m.group(1), lambda m: "offeredBy: %s-gone\n" % m.group(1),
                   "whom the townsfolk list does not have")
    bad += planted("a quest offered by the smith, who never holds a conversation", r"offeredBy: (\w[\w-]*)",
                   lambda m: "offeredBy: %s\n" % m.group(1), lambda m: "offeredBy: gunnar\n",
                   "keeps a station or a shop")
    bad += planted("two steps sharing one id", r"^  - id: (.+)\n(?:.*\n)*?^  - id: (.+)$",
                   lambda m: "  - id: %s\n" % m.group(2).strip(), lambda m: "  - id: %s\n" % m.group(1).strip(),
                   "is used twice")
    bad += planted("a hint pointing at a spot that does not exist", r"^    - spot: (\d+)$",
                   lambda m: "    - spot: %s\n" % m.group(1), lambda m: "    - spot: 99\n", "not a real one")
    bad += planted("a quest with no id", r"^  id: ", lambda m: "  id: ", lambda m: "  id_gone: ", "has no quest id")

    code_after, out_after = run()
    if code_after != 0 or out_after != out:
        print(RED + " WRONG" + OFF + " the real project's own answer changed while proving, so this proves nothing")
        bad += 1
    print()
    if bad:
        print(RED + "%d case(s) wrong — the quest data check's silence proves nothing." % bad + OFF)
        return 1
    print(GRN + "the quest data check sees every planted fault and the real project passes." + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
