---
name: rarity-lifts-skill-output
description: "Rarity: 0/3/9/18/30 POINTS on a weapon's skill output and its own attack; on rolled armour the same ladder on defence plus +0/1/2/3/5 per rolled main stat. NOT kit size, the ultimate, refine or card slots."
metadata:
  node_type: memory
  type: project
---

**WHAT RARITY DRIVES (2026-09-13).** On a weapon: the skill output lift below AND the weapon's own attack / magic attack
(`Rarities.Lifted`, same 0/3/9/18/30% ladder). On a rolled piece of armour (Head/Armor/Garment/Shield — `ArmourRoll`,
rolled per copy as it enters the world): its defence and magic defence by the same ladder, and +0/1/2/3/5 flat on each
rolled main stat (`Rarities.StatExtra`). Plus the name/border colour. Nothing else — in particular NOT (2026-09-10, the
user's instruction) the kit size, the ultimate chance, refine or card slots — every one of those couplings was cut, and the
kit roll no longer even takes a rarity argument so it cannot quietly come back.

**What a drop rolls, identically at every tier:** 3–4 actives, 3–4 passives, and a **25% chance** of an
ultimate. Exact odds for the total: 6 skills 18.75%, 7 skills 43.75%, 8 skills 31.25%, 9 skills 6.25%.
`ActiveMax` is load-bearing — the hotbar is built with exactly that many numbered slots plus the ult slot, so
lowering it leaves a slot nothing can fill.

**⚠️ ONLY THE SWORD CAN ROLL A KIT.** Measured 2026-09-10 across every weapon type: Sword has 3 ultimates, 10
actives and 9 passives published; **all eleven others are EMPTY**. A dropped Bow, Axe, Spear or Staff grants no
skills at all. If someone reports "my weapon has no skills", this is why — it is not a bug in the roll.

**SAY "+30 POINTS", NEVER "+30% BIGGER".** The lift is added to the ring's multiplier, not to the finished
number, so +30% is true only for a character whose ring contributes nothing to that skill; at double the ring
multiplier the same Legendary is worth +15%. That wrong phrasing shipped into three documents before another
session caught it.

**Built 2026-09-09.** A weapon's rarity makes the skills it granted hit harder. `Rarities.OutputLift(Rarity)`
in `Rarity.cs` is the single source of the ladder: **Common +0%, Uncommon +3%, Rare +9%, Epic +18%,
Legendary +30%** — deliberately accelerating so each tier is a bigger step than the last.

**It is ADDED to the ring's term, never multiplied onto the finished number.** `SkillEffectApplier.OutputMult`
returns `AffinityMult(e.affinity, caster) + rarityLift`. Multiplying on the outside would pay out more the more
ring a player had already built (a maxed ring would gain 0.36 from a Legendary where a new character gains 0.20);
adding gives everyone the same flat +0.30. Measured live: at 50 Atk + 40 Attack Speed ranks with 80/20 weights,
ring alone is ×1.4356 and a Legendary is ×1.7356.

**No ring weights are required.** An empty affinity list makes `AffinityMult` return exactly 1, and the lift is
added to that — so a weightless skill on a Legendary is still ×1.30. Verified in the editor.

**Which channels take it (7):** damage, execute power, heal amount, shield pool, damage-per-tick, sub-stat
buff/debuff amount, and what one outgoing-damage charge is worth. The last two were deliberately FLAT before
this change (the code said "a support's value is uptime, not a bigger number") and the user chose to reverse
that on 2026-09-09 — the comments were rewritten to match.

**Which never take it, and why:** any duration (a Legendary must not hold a longer stun), proc chance (clamped to
100%, so it saturates then does nothing), hit count and cleanse count (whole numbers — dead until they jump a
whole unit), a shove's speed (movement, not power), and the charges a modifier hands ANOTHER skill (that skill
gets its own lift; feeding it twice is double-dipping).

**Where the lift comes from:** `Character.SkillRarityLift(skillId)` — the equipped weapon's ladder value when
that weapon's rolled kit contains the skill, else 0. The kit is the test, not "is a weapon worn", so a Grimoire
skill can never borrow whatever is in hand. Resolved ONCE per cast in `ApplyBurst` and restored on the way out,
because a passive can cast inside a burst.

**Gotchas:**
- `SkillTooltip` had to be taught the lift too (`LiveMult`), or the card promises one number and the fight deals
  another. Heal and shield were ALREADY mismatched before this change — the card showed the raw authored value
  while the ring scaled it.
- The lift lookup reads the **Weapon slot only**. The weapon roster lists Shield as one of the twelve identities
  with its own skill pool; the day a shield grants skills, its kit gets NO lift until that lookup widens.
- Since 2026-09-13 the lift, the ring affinity and the damage roll are all asked of the unit USING the skill
  (`ICombatUnit.SkillRarityLift` / `RingBonus` / `RollSkillDamage`, one rule in `Rarities.LiftFor`) — the applier no
  longer falls back to `Character.Instance`, so a creature's cast would read its own weapon, ring and attack.
