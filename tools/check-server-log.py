#!/usr/bin/env python3
"""Reads the game server's log after a play-test and flags things that must never happen.

    tools/check-server-log.py                         # logs/server.log
    tools/check-server-log.py logs/server-prev.log    # the run before

Exit 0 = hit lines were read and nothing was flagged, 1 = something flagged (each printed with its line),
2 = no log file, 3 = the log has no hit lines at all (switch 'hit' on and play again), 4 = the log HAS hit lines but in
a shape these checks cannot read — it was written by an older build of the server. Neither 3 nor 4 is a clean result.

The checks:
  1. A KILL THAT PAID NOBODY — a unit died "killed by nobody" although a blow had cost it health in that life, the
     killing blow included.
  2. A PLAYER'S KILL WITH NO LOOT LINE — a creature killed by a player wrote no loot line in that life. Every kill a
     player earns writes one, "earned nothing" included, so silence means the payout never ran. Needs 'loot' on.
  3. HURT WHILE INVINCIBLE — a blow dealt damage to somebody whose invincibility was on.
  4. A UNIT WITH NO SPAWN_ID — a hit, miss or death line names a unit without a "#number".
  5. A SPAWN_ID HANDED OUT TWICE — two units appeared or entered with the same map and number.
  6. A ZERO WITH NO REASON — a blow that dealt nothing says "reason not recorded": a way of turning a blow away that
     does not yet say why.

THE ORDER OF LINES AROUND A DEATH. A unit dies inside the blow that kills it, so its "died" line (and its loot and
on-kill passive lines) are written BEFORE the hit line of that killing blow. So the first hit on a unit after its death
line that is not marked "(after it died)" is the killing blow, and it counts towards that life.

Full guide: docs/0-process/reading-the-game-log.md
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from server_log_lines import (HIT, MISS, DEATH, LOOT, EXPERIENCE, APPEARED, CAME_BACK, GOT_UP,  # noqa: E402
                              INVINCIBLE, ANY_HIT, NUMBERED, NOT_A_UNIT, REASON_NOT_RECORDED)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


class Life:
    """One life of one unit: health lost so far, whether it paid loot, and its death if it has died."""

    def __init__(self):
        self.hurt = 0
        self.looted = False
        self.death = None          # (lineno, line, killer) once "died" is read
        self.killing_blow_seen = False


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "logs", "server.log")
    if not os.path.exists(path):
        print(f"no log at {path}", file=sys.stderr)
        return 2

    flags = []
    lives = {}                # "map label" -> Life
    character_of = {}         # "map label" -> player id, from the line they entered on
    invincible = {}           # player id (or "map label" when the id is unknown) -> True/False
    handed_out = {}           # (map, number) -> the line it first appeared on
    hit_lines = 0
    raw_hit_lines = 0
    loot_lines = 0

    def unnumbered(name, lineno, line):
        if name not in NOT_A_UNIT and not NUMBERED.match(name):
            flags.append((lineno, "a unit with no spawn_id", line))

    def close(key):
        life = lives.pop(key, None)
        if not life or not life.death:
            return
        lineno, line, killer = life.death
        if killer == "nobody" and life.hurt > 0:
            flags.append((lineno, "a kill that paid nobody", line))
        victim_is_player = key in character_of
        killer_is_player = f'{key.split(" ", 1)[0]} {killer}' in character_of
        if killer_is_player and not victim_is_player and not life.looted and loot_lines > 0:
            flags.append((lineno, "a player's kill with no loot line", line))

    with open(path, encoding="utf-8", errors="replace") as f:
        for lineno, raw in enumerate(f, 1):
            line = raw.rstrip("\n")
            if ANY_HIT.search(line):
                raw_hit_lines += 1

            m = APPEARED.search(line)
            if m:
                key = f'{m["map"]} {m["who"]}'
                number = (m["map"], m["n"])
                if number in handed_out:
                    flags.append((lineno, f"a spawn_id handed out twice (first on line {handed_out[number]})", line))
                handed_out.setdefault(number, lineno)
                close(key)
                lives[key] = Life()
                if m["how"] == "entered":
                    character_of[key] = m["id"]
                continue

            m = CAME_BACK.search(line) or GOT_UP.search(line)
            if m:
                key = f'{m["map"]} {m["who"]}'
                close(key)
                lives[key] = Life()
                continue

            if EXPERIENCE.search(line):
                continue

            m = LOOT.search(line)
            if m:
                loot_lines += 1
                lives.setdefault(f'{m["map"]} {m["victim"]}', Life()).looted = True
                continue

            m = INVINCIBLE.search(line)
            if m:
                key = f'{m["map"]} {m["who"]}'
                invincible[character_of.get(key, key)] = m["state"] == "on"
                continue

            m = HIT.search(line)
            if m:
                hit_lines += 1
                unnumbered(m["who"], lineno, line)
                unnumbered(m["whom"], lineno, line)
                if m["why"] == REASON_NOT_RECORDED:
                    flags.append((lineno, "a zero with no reason", line))
                key = f'{m["map"]} {m["whom"]}'
                if m["late"]:
                    continue        # shown on screen, took no health
                amount = int(m["dealt"])
                life = lives.setdefault(key, Life())
                if life.death is None or not life.killing_blow_seen:
                    life.hurt += amount
                    if life.death is not None:
                        life.killing_blow_seen = True
                if amount > 0 and invincible.get(character_of.get(key, key)):
                    flags.append((lineno, "hurt while invincible", line))
                continue

            m = MISS.search(line)
            if m:
                hit_lines += 1
                unnumbered(m["who"], lineno, line)
                unnumbered(m["whom"], lineno, line)
                continue

            m = DEATH.search(line)
            if m:
                hit_lines += 1
                unnumbered(m["who"], lineno, line)
                if m["killer"] != "nobody":
                    unnumbered(m["killer"], lineno, line)
                key = f'{m["map"]} {m["who"]}'
                life = lives.setdefault(key, Life())
                if life.death is not None:
                    close(key)
                    life = lives.setdefault(key, Life())
                life.death = (lineno, line, m["killer"])

    for key in list(lives):
        close(key)

    if hit_lines == 0 and raw_hit_lines > 0:
        print(f"read {path}: {raw_hit_lines} hit line(s), but NONE in a shape these checks can read — this log was "
              f"written by an older build of the server. Rebuild and restart the server, then play again. This is not "
              f"a clean result.")
        return 4
    if hit_lines == 0:
        print(f"read {path}: NO hit lines at all, so there was nothing to check — this is not a clean result. Switch "
              f"the 'hit' category on ('/log hit on', or LOG_CATEGORIES=hit on the server's start command) and play again.")
        return 3
    if loot_lines == 0:
        print("note: no loot lines in this log, so check 2 (a player's kill with no loot line) could not run — "
              "switch the 'loot' category on to include it.")

    flags.sort(key=lambda f: f[0])
    for lineno, what, line in flags:
        print(f"FLAG line {lineno}: {what}\n    {line}")
    if not flags:
        print(f"read {path}: {hit_lines} hit/miss/death line(s), nothing flagged.")
    return 1 if flags else 0


if __name__ == "__main__":
    sys.exit(main())
