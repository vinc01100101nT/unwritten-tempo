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
  - a Have / Folded To / Wear step names an item id the game has; Folded To asks for at least one fold
  - a Kill step names a creature the monster catalog has, which the world spawn list really places (by its own id,
    with at least one alive at a time — in the step's map when it names one), and asks for at least one
  - a quest is given automatically OR offered by somebody, never both; its giver is listed and keeps no station
    and no shop (those open a window when F is pressed, never a conversation)
  - every reward item exists, and a rewarded quest has no step judged on holding something (one lent item would
    finish it, and pay it, for everybody)
  - an offered quest has offer text
  - every hint's spot is a real guide spot, and a station spot is only on a step whose item is made at a station
  - a hint has text

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
MONSTERS = os.path.join(ASSETS, "3_Data", "Resources", "Monsters")
SPAWNS = os.path.join(ASSETS, "3_Data", "Resources", "WorldSpawns.asset")
QUEST_DEF = os.path.join(ASSETS, "Scripts", "QuestDef.cs")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def enum_values(cs, name):
    m = re.search(r"enum\s+%s\s*\{([^}]*)\}" % name, cs)
    if not m:
        return None
    out = {}
    for part in m.group(1).split(","):
        part = re.sub(r"//.*", "", part).strip()
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
    if os.path.isfile(TOWNSFOLK):
        for block in re.split(r"^  - (?=id: )", read(TOWNSFOLK), flags=re.M)[1:]:
            pid = re.match(r"id: (.+)", block).group(1).strip()
            people.add(pid)
            station = re.search(r"^\s*station: (\d+)", block, re.M)
            shop = re.search(r"^\s*shop: (.*)$", block, re.M)
            if (station and station.group(1) != "0") or (shop and shop.group(1).strip()):
                keepers.add(pid)
    placed = set()   # (creature, map) pairs the world spawn list really places
    if os.path.isfile(SPAWNS):
        for block in re.split(r"^  - (?=map: )", read(SPAWNS), flags=re.M)[1:]:
            mm = re.match(r"map: (.+)", block)
            cr = re.search(r"^\s*creature: (.*)$", block, re.M)
            pop = re.search(r"^\s*population: (\d+)", block, re.M)
            if mm and cr and cr.group(1).strip() and pop and int(pop.group(1)) > 0:
                placed.add((cr.group(1).strip(), mm.group(1).strip()))
    creatures = set()
    if os.path.isdir(MONSTERS):
        for f in os.listdir(MONSTERS):
            if f.endswith(".asset"):
                m = re.search(r"^\s*id: (.+)$", read(os.path.join(MONSTERS, f)), re.M)
                if m:
                    creatures.add(m.group(1).strip())

    faults = []
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
        auto = str(q.get("givenAutomatically", "0")).strip() == "1"
        giver = (q.get("offeredBy") or "").strip()
        if auto and giver:
            faults.append("%s: is both given automatically and offered by '%s'" % (where, giver))
        if giver and giver not in people:
            faults.append("%s: is offered by '%s', whom the townsfolk list does not have" % (where, giver))
        elif giver in keepers:
            faults.append("%s: is offered by '%s', who keeps a station or a shop and so never holds a conversation"
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
            else:
                if item not in items:
                    faults.append("%s: names item '%s', which the game does not have" % (sw, item))
                if kind == "FoldedTo" and int(o.get("folds", "0") or 0) < 1:
                    faults.append("%s: asks for fewer than one fold" % sw)
            hints = s.get("hints") or []
            for h in hints if isinstance(hints, list) else []:
                try:
                    spot = int(h.get("spot", "0"))
                except ValueError:
                    spot = -1
                if spot not in spots:
                    faults.append("%s: a hint points at spot %r, which is not a real one" % (sw, h.get("spot")))
                    continue
                if spot in station_spots and (kind in ("TalkTo", "Kill") or items.get(item, (0, 0))[0] == 0):
                    faults.append("%s: a hint points at a station's %s, but this step's item is not made at a station"
                                  % (sw, spots[spot]))
                if not (h.get("text") or "").strip():
                    faults.append("%s: a hint has no text" % sw)

    if faults:
        print(RED + "%d problem(s) in the quest data:" % len(faults) + OFF)
        for x in faults:
            print("  " + x)
        return 1
    print(GRN + "quest data clean: %d quest(s)." % len(files) + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
