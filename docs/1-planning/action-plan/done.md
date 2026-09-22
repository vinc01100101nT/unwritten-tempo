---
status:
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# unwritten — Action Plan — Done

Tasks whose status is `[DONE]`. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

## Week 1 — Prove the ground is solid

**Goal:** nothing changes for a player. Everything after this is built on a game we know works rather than one we assume does.

- [DONE] `MT-1` **`[E]` Finish the play-test open since 3 September.** About half its checklist was never run. Start with its one inconclusive item: whether a passive fires when a normal attack lands, and again when something dies. — **2–3 days**
  *Done when:* every line of that checklist reads passed or failed with none unrun, and the passive question has a watched answer either way.

- [DONE] `MT-38` **`[E]` GMCP — let an AI play the built game as text.** The game's MCP: the client runs with no window and answers one text command per line — the screen as text out, the same orders a click gives in (walk, attack, cast, chat, pick up, equip, spend a point, respawn) — behind three plain MCP tools. A tester or debugger can now stand in the world as a second or third player beside you, which the editor alone never could. Doc: `docs/0-process/gmcp-playing-the-game-as-text.md`. — **1 day**
  *Done when:* three players — a person in the editor and two AI clients with no window — stand in one town and read each other's chat lines. Watched 2026-09-17.

- [DONE] `MT-2` **`[E]` Kill the last descriptions of the deleted equipment window.** The editor-tools catalogue says one tool drops an Inventory/Equipment/Character trio toggled with I, E and C. The window code's own comments still call them a trio and still mention dragging onto a paperdoll. Those stale comments are what misled the first draft of this plan. — **0.5 days**
  *Done when:* no document and no code comment describes an equipment window.

- [DONE] `MT-3` **`[D]` Decide the gear shape.** Six places on the body can hold something — head, armour, garment, weapon, shield and the ring. Only the weapon has any item authored. *Recommendation: fill head, armour, garment and shield — four places — with a low, a middle and a high piece each. Twelve pieces.* — **0.5 days**
  *Done when:* you have written down which places get gear, how many pieces each, and roughly what each does.

  - [DONE] `ST-3-A` **`[D]` Armour gives stats, not skills.** The weapon stays the only item that grants skills.

  - [DONE] `ST-3-B` **`[D]` The two accessory places go.** The ring already fills that role. That leaves head, armour, garment, weapon, shield and the ring.

  - [DONE] `ST-3-C` **`[D]` Any piece can carry any of the six main stats — STR, AGI, VIT, INT, DEX, LUK — in any place.** *Widened by `MT-39`:* a piece also carries SUB-stats now. One Ink writes one thing onto it — twelve sub-stats or, far more rarely, one of the six primaries — so the decision that any stat may sit in any place still stands; what changed is that the ring is no longer the only source of a sub-stat.

  - [DONE] `ST-3-D` **`[D]` Main-stat bonuses are flat, never scaled to level.** +10 STR is +10 at level 1 and at level 99. It stays worth chasing late because the last points a player buys into a stat cost the most, and gear adds on top of the 99 that buying stops at.

  - [DONE] `ST-3-E` **`[D]` A piece's defence or attack comes from its folds and its quality grade, never from a number set on the copy and never scaled to the wearer's level.** Better gear still comes from harder creatures: a piece folds only with the material band of its own grade, and the strongest creatures drop the highest bands. Decided 2026-09-20 with Gameplay System V4 (`MT-39`).

  - [DONE] `ST-3-F` **`[D]` Each place's main job.** Armour — defence, the largest share; head — defence and magic defence, smaller; garment — magic defence; shield — defence plus block chance, which already works in combat. The main job is the one exception to `ST-3-C`'s "no sub-stats". It does not overlap the ring: the ring multiplies the defence a character already has and adds none of its own.

  - [DONE] `ST-3-G` **`[D]` Gear has no level needed to wear it.** Any character can wear any piece.

  - [DONE] `ST-3-H` **`[D]` There is no rarity.** A piece's quality grade — Raw, then F to A, moved one rung by quenching — is what lifts it. No labels, no colours, no drop weights. Decided 2026-09-20 with `MT-39`.

  - [DONE] `ST-3-I` **`[D]` Nothing is rolled when a piece is made.** Gear is forged, not dropped, and its stats are written onto it afterwards with Ink — two copies of one design differ only by what was done to each. Decided 2026-09-20 with `MT-39`.

  - [DONE] `ST-3-J` **`[D]` How the jumps in main stats work.** Each main stat has one headline jump every 10 points, gear included: STR → melee attack, INT → magic attack, VIT → defence, AGI → evasion, DEX → ranged attack, LUK → crit damage. Every jump uses the same shape: (the stat ÷ 10, rounded down) squared, plus 3 for every 10 — so crossing 10 adds 4 and crossing 100 adds 22, on top of what each point already gives. The stat screen shows what the next jump will give.

  - [DONE] `ST-3-M` **`[D]` Physical damage splits into melee attack and ranged attack.** Melee attack gets +1 per STR plus STR's jump, and +1 per 2 DEX. Ranged attack gets +1 per DEX plus DEX's jump, and +1 per 2 STR. Level, LUK and gear attack feed both. The weapon held decides which one a normal attack and a physical skill use: a Bow or Firearm uses ranged attack, anything else uses melee attack. Magic damage is untouched — a skill whose damage is magic rolls magic attack whatever is held. The ring's attack node and attack buffs raise whichever of the two is in use, creatures follow the same rule, and the stat screen shows both.

  - [DONE] `ST-3-Q` **`[D]` A staff's and a wand's normal attack rolls melee attack.** It is a physical melee swing, as a staff's is today; magic damage comes only from skills.

  - [DONE] `ST-3-R` **`[D]` LUK's jump counts in crit damage percent.** The jump numbers are percentages: crossing 10 LUK adds 4% crit damage, crossing 100 adds 22%, and by 100 LUK the jumps add up to +130% — on top of the 0.2% each point already gives and the 140% every crit starts at.

- [DONE] `MT-4` **`[D]` Gear comes from neither kills nor a shop.** A player forges it at a station from materials, and a kill pays materials, consumables and gold. Every item still carries a sell value, so selling works the moment a shop exists; nothing carries a buy price. Decided 2026-09-20 with `MT-39`. — **0.25 days**

---

## Week 2 — Nothing can rot silently

**Goal:** still nothing for a player, but from here on a mistake in the loot data gets caught by a machine instead of by you, six weeks later.

- [DONE] `MT-6` **`[D]` The ring is not an item.** It is part of the character — no ring item, no ring slot, nothing to author; the work that made it so is `ST-39-A`. Decided 2026-09-20 with `MT-39`. — **0 days**

- [DONE] `MT-8` **`[D]` How hard gear is to get.** Creatures give up no equipment and there is no rarity, so there is no drop chance to set: how hard a piece is to get is which creatures drop which material band, which is `MT-11`. Decided 2026-09-20 with `MT-39`. — **0.25 days**

---

## Week 3 — A full wardrobe exists

**Goal:** every place on the body has something you can make, and a reason to keep working on the one you made.

- [DONE] `MT-11` **`[D]` Decide which creatures drop which material band.** Decided 2026-09-22: a creature drops the
  band its level falls in — the seven bands split levels 1–99 evenly, and a shadow drops the top band — and its kind
  decides how much and which Inks and Salts. The table is in [Mob Roster](../../3-world/mob-roster.md). — **0.5 days**

---
