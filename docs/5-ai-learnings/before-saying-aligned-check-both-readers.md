---
status:
owner: vince
last-updated: 2026-09-21
tags:
  - ai-learnings
---

# 🪞 "Are We Aligned?" — the Screen and the Code Can Each Have Their Own Answer

## Reach for this when

Someone shows you an editor screen, a settings page or a dashboard and asks whether it matches what the program
actually does — "is this the channel the bonus lifts?", "does this toggle control that?" — and you are about to
say yes.

## The rule

**Find every place that decides the thing, before answering.** A screen that shows a rule and the code that
enforces it are often written separately, each with its own list, and they agree only until somebody changes one.
If you checked only the code, you know what the code does — not what the screen tells people it does.

"Yes, aligned" is a claim about BOTH. Answer it only after you have read both, and if they disagree, the answer is
"no — and here are the two lists", never yes.

The permanent fix, when they do disagree, is not to update the second list: it is to delete it. Move the decision
into one function, have the screen and the code both call it, and prove no behaviour moved by replaying the real
saved data under the old rule and the new.

## Why

2026-09-21, this project. The user showed the skill editor's "boosts DAMAGE / HITS COUNT / DURATION" rows and asked
whether those were the channels a new weapon bonus and Skill Mastery would raise. I had read the combat code, so I
said yes. The editor, it turned out, kept its **own** list of which numbers can be raised, and it called four kinds
of number fixed — sub-stat buffs and debuffs and outgoing-damage boosts and cuts — while the combat code raised all
four by Skill Mastery. The user designed two rounds of trademark weapons on my "yes", found Opening's editor page
showing no damage row, and asked the question that exposed it. Their words: "we want single source of truth."

Cost: two design rounds built on a false premise, and the user's trust in every "aligned" that came before it.
The fix took one hour: one shared rule, three readers (fight, tooltip, editor), 57 saved effects replayed with zero
numbers changed.

## How to apply

- Search for the DECISION, not the feature — every switch or list over the same enum is a candidate reader:
  ```bash
  grep -rn "case SkillEffectKind\.\(SubStatBuff\|Damage\)" unity/Assets/Scripts/*.cs unity/Assets/Scripts/Editor/*.cs
  ```
  Two files with the same switch is two answers.
- Read the editor/screen side as seriously as the runtime side. For a code-built editor, its "which rows appear"
  function IS its rule.
- When you unify, replay the real data under old and new before claiming "nothing changed" — then say the count.
