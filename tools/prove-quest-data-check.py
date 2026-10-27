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
    # A fold step is found by its kind, never by the number of folds it happens to ask for today.
    bad += planted("a tutorial fold step asking for more folds than a Raw piece takes",
                   r"^      kind: 2\n(?:^      .*\n)*?^      folds: \d+$",
                   lambda m: m.group(0) + "\n", lambda m: m.group(0).rsplit(":", 1)[0] + ": 99\n",
                   "more than a Raw piece takes")
    bad += planted("a tutorial with no unlock condition", r"^  unlockCondition: [1-9]\d*$",
                   lambda m: m.group(0) + "\n", lambda m: "  unlockCondition: 0\n", "with no unlock condition")
    bad += planted("a buy step for an item no shop sells", r"^      kind: 6\n(?:^      .*\n)*?^      itemId: [\w.-]+$",
                   lambda m: m.group(0) + "\n", lambda m: m.group(0).rsplit(":", 1)[0] + ": item.sword\n",
                   "which no shop sells")
    bad += planted("a shop's item row lit on a step that buys nothing", r"^    - spot: 7$",
                   lambda m: "    - spot: 7\n", lambda m: "    - spot: 10\n", "not to buy anything")
    side = next((rel for rel, text in texts.items() if re.search(r"^  category: 2$", text, re.M)
                 and "    hints: []\n" in text), None)
    if side is None:
        print(RED + " WRONG" + OFF + " a side quest with a hint — no side quest to plant it in, so this proves nothing")
        bad += 1
    else:
        bad += case("a side quest with a hint", side, "    hints: []\n",
                    "    hints:\n    - spot: 0\n      text: planted\n", "only a tutorial's guide shows them")
        bad += case("a side quest needing a level", side, "  steps:\n",
                    "  levelNeeded: 5\n  steps:\n", "only a tutorial has Play")
    bad += planted("a quest with no id", r"^  id: ", lambda m: "  id: ", lambda m: "  id_gone: ", "has no quest id")

    # ---- the Keepers: anchors read out of today's townsfolk list, like every anchor above. Every town is now a named
    # area of the seamless world, so the cases are planted on a Keeper who stands in it.
    folk_rel = os.path.join("unity", "Assets", "3_Data", "Resources", "WorldTownsfolk.asset")
    with open(os.path.join(REPO, folk_rel), encoding="utf-8") as fh:
        folk = fh.read()
    in_world = [b for b in re.split(r"^(?=  - id: )", folk, flags=re.M)
                if re.search(r"^    keeper: 1$", b, re.M) and re.search(r"^    map: world$", b, re.M)]
    if not in_world:
        print(RED + " WRONG" + OFF + " the Keeper cases — no Keeper stands in the seamless world, so this proves nothing")
        bad += 1
    else:
        w = in_world[0]
        twin = re.sub(r"^  - id: (.+)$", r"  - id: \1-twin", w, count=1, flags=re.M)
        bad += case("a town with no Keeper", folk_rel, w, w.replace("    keeper: 1\n", "    keeper: 0\n"), "has no Keeper")
        bad += case("a town with two Keepers", folk_rel, w, w + twin, "has more than one Keeper")
        bad += case("a Keeper standing outside every town", folk_rel, w,
                    re.sub(r"^    home: .*$", "    home: {x: 900, y: 0, z: 900}", w, count=1, flags=re.M),
                    "which is not a town")
        bad += case("a Keeper who also keeps a shop", folk_rel, w,
                    re.sub(r"^    shop: .*$", "    shop: tool-dealer", w, count=1, flags=re.M),
                    "also keeps a shop or works a station")
        bad += case("a Keeper who strolls", folk_rel, w,
                    re.sub(r"^    wanderRadius: .*$", "    wanderRadius: 4", w, count=1, flags=re.M),
                    "a Keeper stands still")

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
