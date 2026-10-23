---
name: staff-orb-system
description: "The Staff's orb pool — 4 orbs + Release + 20 combinations sorted by job on the element wheel, the four element marks (which also set armour element), and the rules the user decided. Redesigned 2026-10-22, not play-tested."
metadata:
  node_type: memory
  type: project
  originSessionId: 3e07a2c1-f1c3-4187-9533-9618ee70753e
  modified: 2026-10-22T13:47:41.371Z
---

Built 2026-10-22, then redesigned with the user the same day (second build not play-tested yet).

- **Kashira:** Kaelen, the Turning World; style Endless Cycle (docs/canon NOT yet updated — no doc edits until final).
- **Orbs:** `skill.staff.orb_{fire,earth,wind,water}` — the ONLY things learned with Staff points; free level 1,
  cap 35, +1.32%/level (`OrbMath.LevelLift`). 3 slots, oldest drops — each orb stores its PLACE in line 1–3, never a
  running count (an ever-growing press counter that reset on login was the bug of 2026-10-22). Held stat only with a
  Staff in hand: Fire MAtk, Earth Def, Wind cast speed, Water max MP, cap 15% per element.
- **Release** casts the combination the orbs make; combos have `orbCombo`, maxLevel 0; pure combos open at orb 20.
  Every part (effect) has an `orb` that grows it; the Skills tooltip lists per orb what it grows, with the server's
  numbers.
- **Jobs, by how the third orb relates to the pair on the wheel (Fire > Earth > Wind > Water > Fire):** pair beats
  third = Offensive cash-in (Meteor, Sandstorm, Windshear, Deluge); third beats pair = Defensive, around the caster,
  turns marks into protection (Scald, Ground Strike, Obsidian, Hoarfrost); neutral = Setter, adds ONE mark only
  (Brushfire, Sirocco, Permafrost, Stalactite); three of one = Ultimate, leaves NO mark, grows per mark its element
  beats without spending it; one of each = Utility (Eruption lengthens marks, Geyser escape, Erosion MP drain, Weathering
  resets the last combination incl. ultimates — 55s cooldown, 40 MP, locked by the user).
- **Renamed (ids kept):** rime → Erosion, dry_lightning → Ground Strike, icicle → Hoarfrost.
- **Marks:** Burn, Chill (slow per stack set by the skill, 3 = freeze for 10s — far longer than any stun), Static
  (attack-speed cut per stack set by the skill, cap 5), Stone (Staff weakening charges, cap 5, ≤30%). **A marked unit
  is hit as the mark's element at level 1** (newest mark wins), so its casher deals 150–175%, and an element on its
  own mark only 25% — why ultimates leave no marks.
- **Any blow that costs health breaks a freeze; DoT ticks don't.** Burn and Chill never coexist.
- Rejected by the user as useless: "make a mark and spend it in one cast" and "Weathering flips your orbs" (same output
  every cast).

Related: [[weapon-roster-12-identities]], [[self-audit-designs-before-presenting]].
