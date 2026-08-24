#!/usr/bin/env python3
"""Print WHO each authored effect actually lands on — the one thing the skill-data checker cannot tell you.

Every effect names its own side outright — Caster or Target, nothing else. (There used to be an "Auto" meaning
"this KIND's traditional side"; sub-stat changes were traditionally SELF, so an authored "cut THEIR attack
speed" left on Auto cut the player's own and passed every check. Auto was removed 2026-08-24.)

Reading the side off the asset is still worth doing, because no checker can flag the wrong one: every side is
legal for every kind, and a self-inflicted debuff is a real design (Overclock). So this prints, never judges.
It reads both enums out of the C# at run time, so it cannot drift from the game.

    .claude/skills/compose-skill/audit-effect-sides.py            # every skill
    .claude/skills/compose-skill/audit-effect-sides.py sword      # only ids containing "sword"
"""
import re, sys, glob, os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
SRC = os.path.join(ROOT, "unity/Assets/Scripts/SkillEffect.cs")
SKILLS = os.path.join(ROOT, "unity/Assets/3_Data/Resources/Skills")


def enum_members(src, name):
    """Every member of `name`, in declaration order — the order Unity serializes them by int."""
    body = src.split(f"public enum {name}", 1)[1].split("{", 1)[1]
    out = []
    for line in body.splitlines():
        if line.strip() == "}":
            break
        for m in re.finditer(r"\b([A-Z][A-Za-z]*)\s*(?:,|$)", line.split("//")[0]):
            out.append(m.group(1))
    return out


def main():
    src = open(SRC).read()
    kinds = enum_members(src, "SkillEffectKind")
    sides = enum_members(src, "EffectSide")
    want = sys.argv[1] if len(sys.argv) > 1 else ""

    for path in sorted(glob.glob(os.path.join(SKILLS, "*.asset"))):
        sid = os.path.basename(path)[:-6]
        if want and want not in sid:
            continue
        txt = open(path).read()
        ks = [int(x) for x in re.findall(r"^    - kind: (\d+)", txt, re.M)]
        sd = re.findall(r"^      side: (\d+)", txt, re.M)
        if not ks:
            continue
        rows = []
        for k, s in zip(ks, sd):
            kn = kinds[k] if k < len(kinds) else f"?{k}"
            lands = sides[int(s)].upper() if int(s) < len(sides) else f"?{s}"
            rows.append(f"    {kn:24s} lands on {lands}")
        print(sid)
        print("\n".join(rows))


if __name__ == "__main__":
    main()
