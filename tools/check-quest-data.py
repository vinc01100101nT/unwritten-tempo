#!/usr/bin/env python3
"""Check the SAVED quest assets for mistakes that would make a quest impossible to finish.

WHY THIS EXISTS: a quest cannot be skipped. One that names an item the game no longer has, a townsperson nobody lists,
or two steps with one id can never be completed — the game suspends such a quest rather than offer it (QuestCatalog),
but a suspended quest is a quest nobody gets. This catches it before it ships.

Reads the assets on disk, so what it checks is what the game loads. The enums (objective kinds, guide spots) are parsed
out of QuestDef.cs at run time, never copied here.

Checks, per quest asset in unity/Assets/3_Data/Resources/Quests:
  - a quest id, unique across every quest
  - at least one step; every step has an id, unique within its quest
  - a Talk To step names a townsperson id listed in WorldTownsfolk.asset
  - a Have / Folded To / Wear / Health step names an item id the game has; Folded To asks for at least one fold
  - a Buy step names an item a shop in WorldShops.asset sells, and asks for at least one
  - a Health step asks for a share of full health between 1 and 100 percent
  - a Kill step names a creature the monster catalog has, which the world spawn list really places (by its own id,
    with at least one alive at a time — in the step's map when it names one), and asks for at least one
  - a quest has an unlock condition OR is offered by somebody, never both; a tutorial always has an unlock condition;
    a giver is listed, keeps no station and no shop, and is no Keeper (those open a window when F is pressed, never a
    conversation)
  - every reward item exists, and a rewarded quest has no step judged on holding something (one lent item would
    finish it, and pay it, for everybody)
  - an offered quest has offer text
  - every hint's spot is a real guide spot, a station spot is only on a step whose item is made at a station, and a
    shop's buy button only on a Buy step
  - a hint has text

And, for the townsfolk list itself (WorldTownsfolk.asset against the towns of WorldAtlas.asset):
  - every map of type town has exactly one Keeper — none, and nobody there can save, store or teleport, and no Keeper
    elsewhere can send anybody to it; two, and which one a trip arrives at is an accident
  - a Keeper lives in a town, stands still (wander radius 0), and keeps no shop and works no station

USAGE:   tools/check-quest-data.py [--root <repo>]
EXIT:    0 = clean, 1 = problems found, 2 = setup problem.
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if "--root" in sys.argv:
    REPO = os.path.abspath(sys.argv[sys.argv.index("--root") + 1])

ASSETS = os.path.join(REPO, "unity", "Assets")
QUESTS = os.path.join(ASSETS, "3_Data", "Resources", "Quests")
ITEMS = os.path.join(ASSETS, "3_Data", "Resources", "Items")
TOWNSFOLK = os.path.join(ASSETS, "3_Data", "Resources", "WorldTownsfolk.asset")
ATLAS = os.path.join(ASSETS, "3_Data", "Resources", "WorldAtlas.asset")
SHOPS = os.path.join(ASSETS, "3_Data", "Resources", "WorldShops.asset")
MONSTERS = os.path.join(ASSETS, "3_Data", "Resources", "Monsters")
SPAWNS = os.path.join(ASSETS, "3_Data", "Resources", "WorldSpawns.asset")
QUEST_DEF = os.path.join(ASSETS, "Scripts", "QuestDef.cs")
CRAFTING_MATH = os.path.join(ASSETS, "Scripts", "CraftingMath.cs")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def enum_values(cs, name):
    m = re.search(r"enum\s+%s\s*\{([^}]*)\}" % name, cs)
    if not m:
        return None
    out = {}
    # Comments go first: a doc comment between two members may hold a comma of its own.
    for part in re.sub(r"//.*", "", m.group(1)).split(","):
        part = part.strip()
        mm = re.match(r"(\w+)\s*=\s*(\d+)", part)
        if mm:
            out[int(mm.group(2))] = mm.group(1)
    return out


def parse_unity_yaml(text):
    """The MonoBehaviour body of a Unity asset as nested dicts and lists - enough of YAML for Unity's own output:
    `key: value`, `key:` with a deeper block under it, `- ` list items (Unity writes them at the key's own
    indentation), `[]`, and a long string wrapped onto deeper lines."""
    lines = [l.rstrip("\r") for l in text.split("\n")]
    start = next((i for i, l in enumerate(lines) if l.startswith("MonoBehaviour:")), None)
    if start is None:
        return {}
    body = [l for l in lines[start + 1:] if l.strip() and not l.startswith("---")]

    def indent(l):
        return len(l) - len(l.lstrip(" "))

    def is_item(l, ind):
        return indent(l) == ind and l.lstrip().startswith("- ")

    def scalar(v):
        v = v.strip()
        if len(v) >= 2 and v[0] == v[-1] and v[0] in "'\"":
            return v[1:-1].replace("''", "'") if v[0] == "'" else v[1:-1]
        return v

    def parse_list(b, i, ind):
        items = []
        while i < len(b) and is_item(b[i], ind):
            first = b[i][ind + 2:]
            sub = [" " * (ind + 2) + first]
            i += 1
            while i < len(b) and indent(b[i]) > ind:
                sub.append(b[i])
                i += 1
            items.append(parse_map(sub, 0, ind + 2)[0] if re.match(r"^\w+:", first) else scalar(first))
        return items, i

    def parse_map(b, i, ind):
        out = {}
        while i < len(b) and indent(b[i]) == ind and not b[i].lstrip().startswith("- "):
            key, _, rest = b[i].strip().partition(":")
            rest = rest.strip()
            i += 1
            if rest == "":
                if i < len(b) and is_item(b[i], ind):
                    out[key], i = parse_list(b, i, ind)
                elif i < len(b) and indent(b[i]) > ind:
                    if b[i].lstrip().startswith("- "):
                        out[key], i = parse_list(b, i, indent(b[i]))
                    else:
                        out[key], i = parse_map(b, i, indent(b[i]))
                else:
                    out[key] = ""
            elif rest == "[]":
                out[key] = []
            else:
                parts = [rest]
                while (i < len(b) and indent(b[i]) > ind and not b[i].lstrip().startswith("- ")
                       and not re.match(r"^\s*\w+:", b[i])):
                    parts.append(b[i].strip())
                    i += 1
                out[key] = scalar(" ".join(parts))
        return out, i

    return parse_map(body, 0, 2)[0]


def main():
    if not os.path.isfile(QUEST_DEF):
        print(RED + "no QuestDef.cs at " + QUEST_DEF + OFF)
        return 2
    cs = read(QUEST_DEF)
    kinds = enum_values(cs, "QuestObjectiveKind")
    spots = enum_values(cs, "GuideSpot")
    if not kinds or not spots:
        print(RED + "could not read the quest enums out of QuestDef.cs — fix this checker, it is now blind" + OFF)
        return 2
    station_spots = {v for v, n in spots.items() if n.startswith("Station")}
    categories = enum_values(cs, "QuestCategory")
    tutorial = next((v for v, n in (categories or {}).items() if n == "Tutorial"), None)
    caps = re.search(r"FoldCaps\s*=\s*\{\s*(\d+)", read(CRAFTING_MATH)) if os.path.isfile(CRAFTING_MATH) else None
    if tutorial is None or not caps:
        print(RED + "could not read the quest categories or the Raw fold cap — fix this checker, "
              "it is now blind" + OFF)
        return 2
    raw_fold_cap = int(caps.group(1))

    items = {}
    for f in os.listdir(ITEMS):
        if f.endswith(".asset"):
            t = read(os.path.join(ITEMS, f))
            m = re.search(r"^\s*id: (.+)$", t, re.M)
            if m:
                made = re.search(r"^\s*madeOf: (\d+)$", t, re.M)
                slot = re.search(r"^\s*slot: (\d+)$", t, re.M)
                items[m.group(1).strip()] = (int(made.group(1)) if made else 0, int(slot.group(1)) if slot else 0)
    people, keepers = set(), set()
    town_keepers = []   # (person id, map, keeps a shop or a station, wander radius) for every row marked Keeper
    if os.path.isfile(TOWNSFOLK):
        for block in re.split(r"^  - (?=id: )", read(TOWNSFOLK), flags=re.M)[1:]:
            pid = re.match(r"id: (.+)", block).group(1).strip()
            people.add(pid)
            station = re.search(r"^\s*station: (\d+)", block, re.M)
            shop = re.search(r"^\s*shop: (.*)$", block, re.M)
            serves = bool((station and station.group(1) != "0") or (shop and shop.group(1).strip()))
            is_keeper = re.search(r"^\s*keeper: 1\s*$", block, re.M) is not None
            if serves or is_keeper:
                keepers.add(pid)
            if is_keeper:
                where_map = re.search(r"^\s*map: (.*)$", block, re.M)
                wander = re.search(r"^\s*wanderRadius: ([-\d.]+)", block, re.M)
                town_keepers.append((pid, where_map.group(1).strip() if where_map else "", serves,
                                     float(wander.group(1)) if wander else 0.0))
    towns = None   # scene names of every map of type town, or None when the atlas cannot be read
    if os.path.isfile(ATLAS):
        towns = [m.group(1) for m in re.finditer(
            r"^  - scene: (\S+)\n(?:^    (?!type:).*\n)*^    type: town\s*$", read(ATLAS), re.M)]
    if not towns:
        print(RED + "could not read any town out of WorldAtlas.asset — fix this checker, it is now blind to Keepers" + OFF)
        return 2
    placed = set()   # (creature, map) pairs the world spawn list really places
    if os.path.isfile(SPAWNS):
        for block in re.split(r"^  - (?=map: )", read(SPAWNS), flags=re.M)[1:]:
            mm = re.match(r"map: (.+)", block)
            cr = re.search(r"^\s*creature: (.*)$", block, re.M)
            pop = re.search(r"^\s*population: (\d+)", block, re.M)
            if mm and cr and cr.group(1).strip() and pop and int(pop.group(1)) > 0:
                placed.add((cr.group(1).strip(), mm.group(1).strip()))
    sold = set()   # item ids some shop stocks
    if os.path.isfile(SHOPS):
        for m in re.finditer(r"^\s*- (item\.[\w.-]+)\s*$", read(SHOPS), re.M):
            sold.add(m.group(1))
    creatures = set()
    if os.path.isdir(MONSTERS):
        for f in os.listdir(MONSTERS):
            if f.endswith(".asset"):
                m = re.search(r"^\s*id: (.+)$", read(os.path.join(MONSTERS, f)), re.M)
                if m:
                    creatures.add(m.group(1).strip())

    faults = []
    # ---- every town has exactly one Keeper --------------------------------------------------------------
    for town in towns:
        here = [pid for pid, where_map, _, _ in town_keepers if where_map == town]
        if not here:
            faults.append("WorldTownsfolk.asset: town '%s' has no Keeper" % town)
        elif len(here) > 1:
            faults.append("WorldTownsfolk.asset: town '%s' has more than one Keeper (%s)" % (town, ", ".join(here)))
    for pid, where_map, serves, wander in town_keepers:
        if where_map not in towns:
            faults.append("WorldTownsfolk.asset: Keeper '%s' lives in '%s', which is not a town" % (pid, where_map))
        if serves:
            faults.append("WorldTownsfolk.asset: Keeper '%s' also keeps a shop or works a station" % pid)
        if wander != 0.0:
            faults.append("WorldTownsfolk.asset: Keeper '%s' strolls (wander radius %s) — a Keeper stands still" % (pid, wander))
    seen_ids = {}
    files = sorted(f for f in os.listdir(QUESTS) if f.endswith(".asset")) if os.path.isdir(QUESTS) else []
    for f in files:
        q = parse_unity_yaml(read(os.path.join(QUESTS, f)))
        qid = (q.get("id") or "").strip()
        where = "%s (%s)" % (f, qid or "no id")
        if not qid:
            faults.append("%s: has no quest id" % where)
        elif qid in seen_ids:
            faults.append("%s: quest id '%s' is also used by %s" % (where, qid, seen_ids[qid]))
        else:
            seen_ids[qid] = f
        unlock = str(q.get("unlockCondition", "0")).strip()
        giver = (q.get("offeredBy") or "").strip()
        if unlock != "0" and giver:
            faults.append("%s: has an unlock condition and is also offered by '%s' — a quest is given one way"
                          % (where, giver))
        if unlock == "0" and str(q.get("category", "0")).strip() == str(tutorial):
            faults.append("%s: is a tutorial with no unlock condition — it would be listed forever and never played"
                          % where)
        if giver and giver not in people:
            faults.append("%s: is offered by '%s', whom the townsfolk list does not have" % (where, giver))
        elif giver in keepers:
            faults.append("%s: is offered by '%s', who keeps a station or a shop, or is a town's Keeper, and so never holds a conversation"
                          % (where, giver))
        reward = q.get("reward") or {}
        for it in (reward.get("items") or []) if isinstance(reward, dict) else []:
            rid = (it.get("itemId") or "").strip() if isinstance(it, dict) else ""
            if rid not in items:
                faults.append("%s: its reward names item '%s', which the game does not have" % (where, rid))
        steps = q.get("steps") or []
        has_reward = isinstance(reward, dict) and (int(reward.get("gold", "0") or 0) > 0
                                                   or int(reward.get("experience", "0") or 0) > 0
                                                   or bool(reward.get("items")))
        if has_reward and isinstance(steps, list):
            for st in steps:
                k = kinds.get(int((st.get("objective") or {}).get("kind", "0") or 0)) if isinstance(st, dict) else None
                if k in ("Have", "FoldedTo", "Wear"):
                    faults.append("%s: pays a reward but step '%s' is judged on holding something — one lent item would "
                                  "finish it, and pay it, for every character it passes through" % (where, st.get("id")))
        is_tutorial = str(q.get("category", "0")).strip() == str(tutorial)
        if int(q.get("levelNeeded", "0") or 0) > 0 and not is_tutorial:
            faults.append("%s: needs a level, but only a tutorial has Play, the one thing that checks it" % where)
        if giver and not (q.get("offerText") or "").strip():
            faults.append("%s: is offered by '%s' but has no offer text" % (where, giver))
        if not isinstance(steps, list) or not steps:
            faults.append("%s: has no steps" % where)
            continue
        step_ids = set()
        for n, s in enumerate(steps):
            if not isinstance(s, dict):
                faults.append("%s: step %d could not be read" % (where, n + 1))
                continue
            sid = (s.get("id") or "").strip()
            sw = "%s step %d (%s)" % (where, n + 1, sid or "no id")
            if not sid:
                faults.append("%s: has no step id" % sw)
            elif sid in step_ids:
                faults.append("%s: step id '%s' is used twice" % (sw, sid))
            step_ids.add(sid)
            o = s.get("objective") or {}
            try:
                kind = kinds.get(int(o.get("kind", "0")))
            except ValueError:
                kind = None
            if kind is None:
                faults.append("%s: objective kind %r is not a real one" % (sw, o.get("kind")))
                continue
            item = (o.get("itemId") or "").strip()
            if kind == "TalkTo":
                if (o.get("townspersonId") or "").strip() not in people:
                    faults.append("%s: talks to '%s', whom the townsfolk list does not have" % (sw, o.get("townspersonId")))
            elif kind == "Kill":
                cid = (o.get("creatureId") or "").strip()
                hunt = (o.get("map") or "").strip()
                if cid not in creatures:
                    faults.append("%s: is to defeat '%s', which the monster catalog does not have" % (sw, cid))
                elif not any(c == cid and (not hunt or m == hunt) for c, m in placed):
                    faults.append("%s: is to defeat '%s', which the world spawn list places nowhere%s — it could never "
                                  "be finished" % (sw, cid, (" in '%s'" % hunt) if hunt else ""))
                if int(o.get("count", "0") or 0) < 1:
                    faults.append("%s: asks to defeat fewer than one" % sw)
            elif kind == "Buy":
                if item not in sold:
                    faults.append("%s: is to buy '%s', which no shop sells" % (sw, item))
                if int(o.get("count", "0") or 0) < 1:
                    faults.append("%s: asks to buy fewer than one" % sw)
            else:
                if item not in items:
                    faults.append("%s: names item '%s', which the game does not have" % (sw, item))
                if kind == "Health" and not 1 <= int(o.get("percent", "0") or 0) <= 100:
                    faults.append("%s: asks for %s%% of full health — it must be between 1 and 100"
                                  % (sw, o.get("percent")))
                if kind == "FoldedTo" and int(o.get("folds", "0") or 0) < 1:
                    faults.append("%s: asks for fewer than one fold" % sw)
                if kind == "FoldedTo" and is_tutorial and int(o.get("folds", "0") or 0) > raw_fold_cap:
                    faults.append("%s: asks for %s folds, more than a Raw piece takes (%d) — its bill, worked out at Raw, "
                                  "would be wrong and the step would need a quench" % (sw, o.get("folds"), raw_fold_cap))
            hints = s.get("hints") or []
            if hints and not is_tutorial:
                faults.append("%s: has hints, but only a tutorial's guide shows them — two quests' hints would collide"
                              % sw)
            for h in hints if isinstance(hints, list) else []:
                try:
                    spot = int(h.get("spot", "0"))
                except ValueError:
                    spot = -1
                if spot not in spots:
                    faults.append("%s: a hint points at spot %r, which is not a real one" % (sw, h.get("spot")))
                    continue
                if spot in station_spots and (kind in ("TalkTo", "Kill", "Buy", "Health")
                                              or items.get(item, (0, 0))[0] == 0):
                    faults.append("%s: a hint points at a station's %s, but this step's item is not made at a station"
                                  % (sw, spots[spot]))
                if spots[spot] == "ShopBuyButton" and kind != "Buy":
                    faults.append("%s: a hint points at a shop's buy button, but this step is not to buy anything" % sw)
                if not (h.get("text") or "").strip():
                    faults.append("%s: a hint has no text" % sw)

    if faults:
        print(RED + "%d problem(s) in the quest data:" % len(faults) + OFF)
        for x in faults:
            print("  " + x)
        return 1
    print(GRN + "quest data clean: %d quest(s); %d town(s), each with one Keeper." % (len(files), len(towns)) + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
