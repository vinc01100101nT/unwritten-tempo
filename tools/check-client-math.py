#!/usr/bin/env python3
"""Find runtime code that works out a GAME NUMBER on a path the player's machine can run — RULE NOCLIENTMATH,
mechanically.

WHY THIS EXISTS: "the player's machine draws what the server sends and sends what the player pressed" has been
the project's highest rule for a week, and the 2026-09-20 audit still found the whole level-up (remainder, new
level, stat points, full heal) running on the player's machine from a raw amount, a tooltip recomputing nine
character-sheet stats, a draft rolling its own wildcard. Every one was written in good faith by somebody who
had read the rule. So the rule reads the code.

WHAT COUNTS:
  a line of runtime C# (unity/Assets/Scripts, never Editor) that does ARITHMETIC — `+= -= *= /= ++ --` or an
  assignment whose right side has + - * / — on a GAME NUMBER: experience, level, points, health, mana, damage,
  attack, defence, crit, heal, regen, cooldown, gold, a drop, a roll, a rank, a percentage bonus or multiplier.
  Movement is the one exception the rule itself grants, so transform / Vector / Quaternion lines are skipped.

WHAT MAKES IT FINE (any one):
  · the file is a `static class` — the plain-function math layer the rule says arithmetic belongs in
  · the enclosing method opens with a server guard: `if (!NetworkServer.active`, `if (!isServer`,
    `if (Told)`, `if (Watching)`, `if (TheWorldIsElsewhere)`, `if (GameNetwork.TheWorldIsElsewhere)` — or carries
    [Server] / [ServerCallback] / [Command]
  · the line carries `// server-math: <why>` — the reason sits beside the sum, as `// perf-ok:` does
  · the line is in tools/client-math-baseline.txt — the debt known on the day this was written, listed so it
    can only shrink. New sums are refused; old ones are named with --all.

USAGE:   tools/check-client-math.py [--root DIR] [--all] [--write-baseline]
EXIT:    0 = no NEW client-side sum, 1 = one or more, 2 = could not run
Fault lines:  FAIL <path>(<line>): <what>
"""
import argparse
import hashlib
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from cs_methods import strip_comments, strip_strings, code_only, methods_of   # the one C# reader

BASELINE = os.path.join(HERE, "client-math-baseline.txt")
MARKER = "server-math:"

GAME_NUMBER = re.compile(
    r"\b(?:XP\w*|xp\w*|[Ll]evel|StatPoints|LifetimeXp|Current(?:HP|MP)|Max(?:HP|MP|Health|Mana)|"
    r"[Dd]amage\w*|[Aa]ttack\w*|[Dd]efen[cs]e\w*|[Cc]rit\w*|[Hh]eal(?:ing|Amount|s)?|[Rr]egen\w*|[Cc]ooldown\w*|"
    r"[Mm]ana\w*|[Gg]old\w*|[Dd]rop(?:Chance|s|Rate)|[Rr]oll\w*|XPToNext|BankedPoints|[Rr]anks?|"
    r"\w*(?:Crit|Damage|Attack|Cast|Speed)Mult\w*|[Pp]ercent\w*|[Rr]each|\bHit\b|[Hh]itChance|\bhits\b|[Ff]lee|"
    r"[Ee]vasion|[Aa]ccuracy|[Ll]eech\w*|[Rr]eflect\w*|[Ss]hield\w*|[Aa]bsorb\w*|[Pp]rice)\b")
ARITH = re.compile(r"(?<![+\-*/=!<>])(?:[+\-*/]=(?!=)|\+\+|--|=\s*[^=;]*?(?<![=!<>])[+\-*/](?![=/*]))")
MOVEMENT = re.compile(r"\b(?:transform|Vector[234]|Quaternion|localScale|localPosition|position|rotation|deltaTime|"
                      r"Lerp|MoveTowards|Slerp|sortingOrder|color|alpha|pixelsPerUnit|sizeDelta|anchoredPosition|"
                      r"fillAmount|GUILayout|Rect|width|height|padding|spacing|volume|pitch|Physics|Matrix|"
                      r"worldToCameraMatrix|Dirty|Gait\w*|[Dd]ash\w*|Glide\w*)\b")
STRING_BUILD = re.compile(r'""|\bstring\b|\$"')     # a blanked literal or a string target: `+` is concatenation, not a sum
EVENT_SUBSCRIBE = re.compile(r"^\s*\w[\w.]*\s*[+-]=\s*[\w.]+\s*;\s*$")
GUARD = re.compile(r"if\s*\(\s*(?:!NetworkServer\.active|!isServer|Told|Watching|TheWorldIsElsewhere|"
                   r"GameNetwork\.TheWorldIsElsewhere|CasterGone|!NetworkServer\.active\b)")
SERVER_ATTR = re.compile(r"\[(?:Server|ServerCallback|Command)\]")


def is_static_class(code):
    return re.search(r"\bstatic\s+class\s+\w+", code) is not None and not re.search(r"\bclass\s+\w+\s*:\s*\w*Mono", code)


def line_key(rel, line_text):
    """A stable name for one sum: the file plus a hash of the line's code, so a line can move without leaving
    the baseline but any change to the sum itself is a new sum."""
    norm = re.sub(r"\s+", " ", strip_comments(line_text)).strip()
    return "%s %s" % (rel, hashlib.sha1(norm.encode()).hexdigest()[:12])


def scan(repo):
    """Every client-side sum in the runtime tree: (rel path, line number, line text, method name)."""
    root = os.path.join(repo, "unity", "Assets", "Scripts")
    hits = []
    for dirpath, _d, files in os.walk(root):
        if os.sep + "Editor" in dirpath + os.sep:
            continue
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            path = os.path.join(dirpath, f)
            rel = os.path.relpath(path, repo).replace(os.sep, "/")
            with open(path, encoding="utf-8", errors="replace") as fh:
                raw = fh.read()
            lines = raw.splitlines()
            code = code_only(raw)
            if is_static_class(code):
                continue
            for name, defs in methods_of(code).items():
                for head_line, body_start, body, head in defs:
                    opening = body[:600]
                    guarded = GUARD.search(opening) is not None or SERVER_ATTR.search(head) is not None
                    if guarded:
                        continue
                    for k, bline in enumerate(body.split("\n")):
                        ln = body_start + k
                        src = lines[ln - 1] if 0 < ln <= len(lines) else ""
                        c = strip_strings(strip_comments(src))
                        if not GAME_NUMBER.search(c) or not ARITH.search(c):
                            continue
                        if MOVEMENT.search(c) or EVENT_SUBSCRIBE.match(c) or re.match(r"\s*for\s*\(", c):
                            continue
                        if STRING_BUILD.search(c) or STRING_BUILD.search(src):
                            continue
                        if MARKER in src or (ln > 1 and MARKER in lines[ln - 2]):
                            continue
                        hits.append((rel, ln, src.strip(), name))
    return hits


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=DEFAULT_REPO)
    ap.add_argument("--all", action="store_true", help="also list the sums already in the baseline")
    ap.add_argument("--write-baseline", action="store_true", help="record every current sum as known debt")
    ap.add_argument("--baseline", default=BASELINE)
    a = ap.parse_args()
    repo = os.path.abspath(a.root)
    if not os.path.isdir(os.path.join(repo, "unity", "Assets", "Scripts")):
        print("cannot run: %s has no unity/Assets/Scripts" % repo)
        return 2

    hits = scan(repo)
    keys = {line_key(rel, text): (rel, ln, text, name) for rel, ln, text, name in hits}

    if a.write_baseline:
        with open(a.baseline, "w", encoding="utf-8") as f:
            f.write("# Client-side sums known on the day the check was written — debt, listed so it can only shrink.\n")
            f.write("# One line each: <file> <hash of the line's code>. Fix the sum and delete its line here; never add one.\n")
            for k in sorted(keys):
                f.write(k + "\n")
        print("baseline written: %d known sums" % len(keys))
        return 0

    known = set()
    if os.path.isfile(a.baseline):
        with open(a.baseline, encoding="utf-8") as f:
            known = {l.strip() for l in f if l.strip() and not l.startswith("#")}

    new = [(k, v) for k, v in sorted(keys.items(), key=lambda kv: (kv[1][0], kv[1][1])) if k not in known]
    old = [(k, v) for k, v in sorted(keys.items(), key=lambda kv: (kv[1][0], kv[1][1])) if k in known]
    stale = known - set(keys)

    for _k, (rel, ln, text, name) in new:
        print("FAIL %s(%d): NEW sum on a game number where a player's machine can run it, in %s — `%s`. Move it "
              "to the server (send the finished value), into a static math class, or put `// %s <why>` beside it"
              % (rel, ln, name, text[:90], MARKER))
    if a.all:
        for _k, (rel, ln, text, name) in old:
            print("KNOWN %s(%d): %s — `%s`" % (rel, ln, name, text[:90]))
    if new:
        print("\n%d new client-side sum(s) — the rule is RULE NOCLIENTMATH in CLAUDE.md." % len(new))
        return 1
    note = " (%d known sum(s) in the baseline%s)" % (len(old), "; %d baseline line(s) no longer match — delete them" % len(stale) if stale else "")
    print("client math clean: no new sum on a game number outside the server%s." % note)
    return 0


if __name__ == "__main__":
    sys.exit(main())
