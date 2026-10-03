---
name: skill-tree
description: Use when laying out or reviewing a weapon's skill tree — which skill needs which, the mastery level each opens at, how many levels each skill can take, and how mastery points are budgeted. Covers the one rule for requirements (a requirement is only what a skill literally uses), the 1/5/10 max-level rule, the points budget check, and the timeline layout of the Skills window.
---

# Laying out a weapon's skill tree

Decided with the user on 2026-10-03 while laying out the Sword. Every weapon's tree follows the same rules, so a
player who learned one tree can read the next.

## 1. A requirement is ONLY what the skill literally uses

A skill may name another skill it needs first **only when it cannot do its job without that skill** — it spends
that skill's stacks, adds to that skill's pile, fires when that skill is cast, or cashes what that skill banked.
"It pairs well with", "it makes sense after", "it is the same theme", "it finishes the family" are **never** reasons.
The user calls this one of the game's strengths: a requirement tells the player something true about how two skills
work together.

**Prove every requirement from the skill file, never from the name or the description.** Search the dependent
skill's asset for the parent's id anywhere other than its own `needsSkillId` line:

```bash
cd unity/Assets/3_Data/Resources/Skills
for f in skill.<weapon>.*.asset; do
  p=$(grep -m1 "needsSkillId:" "$f" | awk '{print $2}'); [ -z "$p" ] && continue
  echo "$f needs $p: $(grep -n "$p" "$f" | grep -v needsSkillId | tr '\n' ' ')"
done
```

A hit on `triggerSkillId`, `requireSkillId` or `modifierSkillId` is a real use. **No hit means the link is a "makes
sense" link — remove it.** One exception needs reading rather than grepping: a skill that fires on a MOMENT another
skill is the source of (an OnParry passive needs a way to parry). Name the moment and the skill that supplies it
before keeping such a link.

*(Found 2026-10-03: Thousand Petals required Squall, and nothing in Thousand Petals used Squall — it only benefits
from the attack speed Squall gives. The link was removed.)*

## 2. A requirement is always level 1 of the parent

The child works the same whether the parent is level 1 or 10, so asking for more is a points tax with no meaning in
a fight. It also cannot apply evenly: a parent that stops at level 1 could never be asked for more.

## 3. The mastery level is the only pacing

`masteryLevel` says how far into the weapon a skill opens. It is the timeline's horizontal position and the minimum
mastery to BUY the skill; it never learns anything on its own. Order skills so a child opens after its parent.

## 4. Max level: 10, 5 or 1 — decided by WHAT GROWS

Every level after the first adds the same global percentage to the numbers the fight already raises
(`EffectScaling.Boostable`). The only per-skill knob is how far a skill can go:

- **10** — what grows is mostly damage.
- **5** — what grows is a defensive or utility percentage (damage reduction, an attack-speed swing, a damage bonus or
  cut) or a whole-number charge count. At +45% these get extreme: 50% damage reduction becomes 72.5%.
- **1** — nothing on the skill grows (a parry, a taunt, an immunity window). Any level past 1 would be a point that
  does nothing.

A skill that mixes damage with a percentage debuff takes the lower cap. Read the effects in the asset to decide —
the kind list is positional, so read the real enum first (see the `compose-skill` skill).

## 5. Budget check — the points must not buy everything

A weapon gives one point per mastery level, and mastery tops out at 70. Add up every skill's max level: a fully
mastered player should afford about **half** of it. The Sword's split is 7 × 10 + 11 × 5 + 4 × 1 = 129 levels against
70 points (54%). Far above half and the tree decides nothing; far below and a maxed player still feels starved.

## 6. Layout — a timeline of mastery

The Skills window draws one weapon as a horizontal timeline, mastery 0 to 70 left to right:

- each skill's left edge sits on the mastery level that opens it, so no "Lv" label is needed on the skill;
- a family (a skill and everything that needs it) keeps its own lanes, joined by wires; skills that need nothing and
  that nothing needs share packed lanes;
- three looks: out of reach (mastery too low) is faint, in reach with no point is solid grey, bought wears the
  weapon's colour;
- the hover card shows the requirement(s) met or not, `level / max`, and every growing number now and at the next
  level.

The clickable reference is `prototypes/skills-mastery-points.html`.
