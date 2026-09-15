#!/usr/bin/env python3
"""Totals from the game server's log: for every unit that swung, how many blows it threw, missed, landed, crit,
how much damage it dealt against how much it rolled, which skills dealt it, and why its zero-damage blows got
nowhere. Then what every unit took from blows and from everything else, what it was healed, what its states did
(granted, spent, ran out), what its passives did (fired, skipped and why), experience earned, and who killed what.

Each section needs its category on while playing: hit, heal, buff, passive, loot. Units are named by map and
spawn_id ("field_15 Snake #3"), so two creatures of the same kind are counted separately.

    tools/server-log-tally.py                                  # logs/server.log
    tools/server-log-tally.py logs/server-prev.log             # the run before
    tools/server-log-tally.py --map field_15                   # one map only
    tools/server-log-tally.py --since "2026-09-15 23:27:00"    # only lines at or after this moment
    tools/server-log-tally.py --since 23:27:00                 # the same, on the date of the log's first game line

Full guide: docs/0-process/reading-the-game-log.md
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from server_log_lines import (HIT, MISS, DEATH, TOOK, HEAL, EXPERIENCE, STATE_ADDED, STATE_LOST,  # noqa: E402
                              PASSIVE_FIRED, PASSIVE_SKIPPED, ANY_HIT)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHAPES = (HIT, MISS, DEATH, TOOK, HEAL, EXPERIENCE, STATE_ADDED, STATE_LOST, PASSIVE_FIRED, PASSIVE_SKIPPED)


def main():
    ap = argparse.ArgumentParser(description="Totals per spawn_id from the game server's log.")
    ap.add_argument("log", nargs="?", default=os.path.join(ROOT, "logs", "server.log"))
    ap.add_argument("--map", help="only count lines from this map")
    ap.add_argument("--since", help='only count lines at or after this moment: "YYYY-MM-DD HH:MM:SS" or HH:MM:SS')
    args = ap.parse_args()

    if not os.path.exists(args.log):
        print(f"no log at {args.log}", file=sys.stderr)
        return 2

    rows = collections.defaultdict(collections.Counter)
    by_skill = collections.defaultdict(collections.Counter)
    reasons = collections.defaultdict(collections.Counter)
    taken = collections.Counter()
    other = collections.defaultdict(collections.Counter)
    healed = collections.defaultdict(collections.Counter)
    states = collections.defaultdict(collections.Counter)      # "map unit — state from skill" -> granted/spent/...
    passives = collections.defaultdict(collections.Counter)    # "map unit — skill on trigger" -> fired / skipped: why
    experience = collections.Counter()
    kills = collections.Counter()
    deaths = 0
    hit_lines = 0
    raw_hit_lines = 0
    since = args.since

    with open(args.log, encoding="utf-8", errors="replace") as f:
        for raw in f:
            line = raw.rstrip("\n")
            if ANY_HIT.search(line):
                raw_hit_lines += 1
            m = next((found for found in (shape.search(line) for shape in SHAPES) if found), None)
            if not m:
                continue
            if since and len(since) == 8:
                since = m["t"][:10] + " " + since      # a time alone means the date of the first game line
            if since and m["t"][:len(since)] < since:
                continue
            if args.map and m["map"] != args.map:
                continue
            key = f'{m["map"]} {m["who"]}'

            if m.re is HEAL:
                healed[key][m["by"] or "no skill named"] += int(m["amount"])
                continue
            if m.re is EXPERIENCE:
                experience[key] += int(m["xp"])
                continue
            if m.re is STATE_ADDED:
                states[f'{key} — {m["state"]} from {m["skill"]}']["granted"] += 1
                continue
            if m.re is STATE_LOST:
                states[f'{key} — {m["state"]} from {m["skill"]}'][m["how"]] += int(m["n"])
                continue
            if m.re is PASSIVE_FIRED:
                passives[f'{key} — {m["skill"]} on {m["trigger"]}']["fired"] += 1
                continue
            if m.re is PASSIVE_SKIPPED:
                passives[f'{key} — {m["skill"]} on {m["trigger"]}'][f'skipped: {m["why"]}'] += 1
                continue

            hit_lines += 1
            if m.re is TOOK:
                cause = m["cause"] + (f' by {m["by"]}' if m["by"] else "")
                other[key][cause] += int(m["dealt"])
                continue
            if m.re is DEATH:
                deaths += 1
                kills[f'{m["map"]} {m["killer"]}'] += 1
                continue

            row = rows[key]
            row["swings"] += 1
            if m.re is MISS:
                row["misses"] += 1
                continue
            if m["late"]:
                # Reached a unit already dead: the number was shown, but no health was taken.
                row["after death"] += 1
                continue
            amount = int(m["dealt"])
            row["hits" if amount > 0 else "zero"] += 1
            row["dealt"] += amount
            row["rolled"] += int(m["rolled"])
            if m["crit"]:
                row["crits"] += 1
            if m["why"]:
                reasons[key][m["why"]] += 1
            by_skill[key][m["skill"]] += amount
            taken[f'{m["map"]} {m["whom"]}'] += amount

    if hit_lines == 0 and raw_hit_lines > 0:
        print(f"{raw_hit_lines} hit line(s) in a shape this script cannot read — the log was written by an older build "
              f"of the server. Rebuild and restart it, then play again.")
    elif hit_lines == 0:
        print("no hit lines found — was the 'hit' category switched on? ('/log hit on', or LOG_CATEGORIES=hit)")

    if rows:
        # swings = every hit/miss line; hits = blows that took health; 0 dmg = landed but took nothing (reasons below);
        # dead = blows that reached a unit already dead, left out of every other column.
        print(f"{'attacker':32} {'swings':>6} {'miss':>5} {'miss%':>6} {'hits':>5} {'0 dmg':>5} {'crits':>5} "
              f"{'dealt':>8} {'rolled':>8} {'dead':>5}")
        for key in sorted(rows):
            r = rows[key]
            miss_pct = 100.0 * r["misses"] / r["swings"] if r["swings"] else 0.0
            print(f"{key:32} {r['swings']:6} {r['misses']:5} {miss_pct:5.0f}% {r['hits']:5} {r['zero']:5} "
                  f"{r['crits']:5} {r['dealt']:8} {r['rolled']:8} {r['after death']:5}")
            for skill, n in by_skill[key].most_common():
                print(f"{'':34}{n} dealt with {skill}")
            for why, n in reasons[key].most_common():
                print(f"{'':34}{n} × nothing got through: {why}")

        print(f"\n{'damage taken from blows':32} {'total':>8}")
        for key, n in sorted(taken.items(), key=lambda kv: -kv[1]):
            print(f"{key:32} {n:8}")

    if other:
        print("\ndamage taken from everything else")
        for key in sorted(other):
            for cause, n in other[key].most_common():
                print(f"  {key}: {n} from {cause}")

    if healed:
        print("\nhealed")
        for key in sorted(healed):
            for by, n in healed[key].most_common():
                print(f"  {key}: {n} ({by})")

    if states:
        print("\nstates (granted = times applied; every other count is stacks)")
        for key in sorted(states):
            print(f"  {key}: " + ", ".join(f"{n} {what}" for what, n in states[key].most_common()))

    if passives:
        print("\npassives")
        for key in sorted(passives):
            print(f"  {key}: " + ", ".join(f"{n} {what}" for what, n in passives[key].most_common()))

    if experience:
        print("\nexperience earned")
        for key, n in experience.most_common():
            print(f"  {key}: {n}")

    if hit_lines:
        print(f"\n{deaths} death(s)")
        for key, n in kills.most_common():
            print(f"  {n} killed by {key.split(' ', 1)[1]} in {key.split(' ', 1)[0]}")

    nothing = not (rows or other or healed or states or passives or experience)
    return 1 if nothing else 0


if __name__ == "__main__":
    sys.exit(main())
