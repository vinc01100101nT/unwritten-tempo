---
name: mastery-points-and-free-hotbar-design
description: Agreed 2026-10-03; the SKILL half is BUILT (unplayed), the hotbar half is not — mastery points buy skill levels (global +5%/level, max 1/5/10), a requirement is ONLY what a skill literally uses, 27-slot free hotbar, all ults share one 40s cooldown.
metadata:
  type: project
---

Agreed with the user on 2026-10-03. The skill half (points, levels, max levels, grade cap, timeline Skills window, card
section, /resetskills) was BUILT the same day and is unplayed; the hotbar half is still only a plan. Clickable
prototype: `prototypes/skills-mastery-points.html`.

- **Hotbar:** 3 rows × 9 slots, any kind in any slot — active, passive, ult or item. One bar per character, not per
  weapon (storage measured: ~1 KB/character as one row). **Every ultimate, of any weapon, shares ONE 40-second
  cooldown.**
- **Mastery points replace automatic learning:** each weapon mastery level banks a point for that weapon (copy the
  stat ring's bank-and-spend). A skill's `masteryLevel` becomes the minimum to BUY it.
- **Skill levels replace use-based skill mastery** (and the passive/creature "ride weapon mastery" lift): one channel.
  Global **+5% per level** to every number the fight already raises (`EffectScaling.Boostable`); level 1 = as authored.
- **Max level per skill = the only per-skill knob:** 10 if what grows is mostly damage, 5 if it is a defensive/utility
  percentage or a charge count, 1 if nothing on it grows. Sword split totals 129 levels vs 70 points (~54%).
- **Layout:** horizontal timeline of weapon mastery 0→70; skill's left edge at its opening level; families in lanes
  joined by wires; out-of-reach skills faint, reached-but-unbought solid grey, bought in weapon colour.

**THE PRINCIPLE (the user called it one of the game's strengths): a skill requirement is literally what the skill
uses** — it consumes the parent's stacks, triggers off the parent's cast, adds to the parent's pile. Never "because
it makes sense". Requirement is always parent level 1 (higher would be a points tax with no combat meaning).
Thousand Petals' old Squall requirement broke this and was removed on 2026-10-03. The full set of tree rules is the
`skill-tree` project skill.

**Decided at build time (2026-10-03):** old characters reset and refunded (save v17 drops the old learned list); grade
caps counted levels by the old skill-cap table scaled to each max (10-level: 2/3/4/6/7/9/10); creatures keep their
mastery-gated kit but every skill sits at level 1 until per-creature builds (user: "we will allocate it later per mob");
no player refund yet — `/resetskills` is the only way back.

**Still parked for the user** (hotbar plan): passive cap moot now; keys for rows 2–3 (proposed Ctrl+1–9, row 3 click
only); shared 40s ult lock on top of each ult's own cooldown (proposed); gear on the bar needs a per-copy id; refund
cost; Squall's self-drawback also grows with level under the current Boostable rule.
