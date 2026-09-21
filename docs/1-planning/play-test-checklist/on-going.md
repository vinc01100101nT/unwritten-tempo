---
status: open
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# Play-Test Checklist — On-going

Steps that were touched but have no final verdict: passed once and re-opened by a later batch, run but not settled, or blocked (**Cannot be done now** / **Cannot be done by tester**). Every step keeps the section and number it had in the single checklist, so a step referred to elsewhere as "step 2 of section 10" is still that step, and a step moves between these files as its verdict changes. The four files are [passed](passed.md), [failed](failed.md), [on-going](on-going.md) and [not done yet](not-done-yet.md).

## In Play mode

Before running any of these, do what [not-done-yet.md ▸ Before you start](not-done-yet.md#before-you-start--what-to-rebuild-and-restart) says must be rebuilt and restarted.

### 1. The bag's columns

1. **Passed** — Press **B**. Click every tab along the top of the bag in turn.
   *Should:* every tab shows the same three columns — **Name**, **Grade**, **Held**.
   *FAIL:* a tab shows Slot, Skills, Atk/Def, Out or **Tier**, or any tab is missing Held.
   *Status:* re-opened by batch 21 — the Tier column is now Grade, and it reads a piece's quality and folds.
3. **Passed** — Click **Name**, then **Name** again, then a third time. Do the same for **Grade** and **Held**.
   *Status:* re-opened by batch 21 — the column it sorts by is now Grade.
   *Should:* first click sorts ascending (▲), second descending (▼), third returns the bag to your own order.
   *FAIL:* a header does nothing, or the third click leaves the list sorted.

### 3. The gear shape (`MT-3`)

6. **Cannot be done now** — was `/weapon sword common|epic|legendary`. Batch 21 removed rarity: the command is now
   `/weapon sword` and hands over a Raw, unfolded base. Replaced by the forging steps in batch 21.
   *Old step:* Type `/weapon sword common`, `/weapon sword epic`, `/weapon sword legendary`. Hover each.
   *Should:* ATK reads **3**, **4** and **4**. (A Rare sword also reads 3 — a 9% lift on 3 still rounds to 3.)
   *FAIL:* the Epic or Legendary sword reads 3.

**Cannot be watched in play today, and why:**

- **Cannot be done now** — **ST-3-L (creatures count their gear's main stats)** — the only creature wearing gear is the Axolot, and its Sword carries no main stats, so there is nothing to see move.

### 4. The skill and server plumbing (`MT-1`)

**Then the rest:**

3. **Cannot be done now** — **A5** — a weapon no longer carries a kit (batch 21). Its replacement is step 10 of
   batch 21: learn a skill by mastery, bind it, press it.
   *Old step:* type `/weapon sword rare`, wear it, press a skill from its kit.
16. **D3** — kill creatures until one drops something. *Should:* the item lies on the ground where the creature fell, and standing beside it and pressing **F** puts it in your bag. *FAIL:* the item goes straight into your bag, or **F** picks nothing up. A kill with no drop is not a fail. *Status:* re-opened 2026-09-15 — kill loot now lands on the ground instead of in your bag (batch 7).

### 7. Creature loot on the ground and random creature gear

1. Type `/summon 5 normal`, then CLICK each creature in turn to pick a fight with it.
   *Should:* in the panel at the top CENTRE — the one for whoever you are fighting — below the creature's name and states, one line per piece it wears: its grade and folds, then the item's name. Some creatures wear nothing.
   *Status:* re-opened by batch 21 — worn gear no longer drops, so the drop chance is gone from the line and the rarity colour with it.
   *FAIL:* a chance that does not match the colour of the name beside it, or no creature out of the five wears anything after three summons.
3. **Cannot be done by tester** — with a second player signed in beside you, kill a creature that drops something, and have the second player press **F** on it straight away, then again after 10 seconds.
   *Should:* the first press takes nothing; after 10 seconds the second player can take it.
   *FAIL:* the second player takes it within 10 seconds.

### 9. Creatures cast their weapon's skills

Before starting, type `/log cast on` and `/log passive on` in chat. Only the Sword has skills written for it, so only a creature holding a **Sword** casts any of them. **Batch 21 changed where those skills come from:** a creature now knows every skill of the weapon in its hands up to its own Weapon Mastery, which is authored as its level — so a level-20 creature with a sword knows the sword skills that open at 20 and below, instead of a kit of three or four rolled onto the weapon. Every creature also carries one extra skill (batch 13), so any creature may cast that one.

1. Type `/summon 5 normal` and hover over each creature until one's panel lists **Sword** — summon again if none does. Attack it and stay in its reach with invincibility off.
   *Should:* within a minute its skill's name floats in a bubble over its head, the skill's effect plays and lands on you, and the MP bar in the top-centre panel drops. `logs/server.log` has a line naming that creature followed by `casts skill.sword.` and the skill.
   *FAIL:* after 60 seconds at full MP it has only swung, or its name bubble appears and its MP bar never moves.
   *Status:* 2026-09-16 play-test — no name bubble over any creature. A flat sprite creature had no head for a bubble to ride, so the bubble was silently skipped; it now sits above the sprite. Cast logging was off, so that run did not record whether any creature cast at all. Second run, about an hour: only Lunge and Parry were ever seen. Replayed offline with the real costs, a level 3–5 creature holds 17–23 MP and refills 1.5 a second, so its two 8-MP skills take almost every cast (about 5 a fight), each 12-MP skill gets about 1, Committed Strike about 0.3, and no 40-MP ultimate can ever be afforded below level 11. **Settled 2026-09-17:** a creature takes its ultimate first whenever that is off cooldown and affordable, and otherwise one of its ready ordinary actives at random — it never saves up. So seeing only the cheap skills on a low-level creature is correct, not a failure.
10. Type `/summon 1 normal shadow`, kill the 3D creature, and wait for it to come back.
   *Should:* when it is back it stands, walks, runs and swings with its animations, exactly as before it died.
   *FAIL:* it stands static with no pose after coming back.
   *Status:* 2026-09-16 play-test — static after coming back. A body that came back had its animations switched off by a distance check that looked for players in the wrong place; fixed, needs re-test.
11. **Cannot be done by tester** — with a second player signed in and watching you fight the sword creature.
   *Should:* they see the same name bubble, cast bar and skill effect on it that you see.
   *FAIL:* the creature casts on your screen and stands still or only swings on theirs.
13. **Cannot be done by tester** — with a second player signed in, have them press **Onslaught** on a creature while you watch them.
   *Should:* on your screen their sword swings as fast as their damage numbers land, the same as on their own screen.
   *FAIL:* on your screen they swing at normal speed while their numbers land fast.

### 10. Creatures get every skill moment, the arrow, and other people's weapons

Before starting, type `/log cast on` and `/log passive on` in chat.

8. **Cannot be done by tester** — with a second player signed in, stand where you can see each other and have them type `/weapon sword`.
   *Should:* a sword appears in their hand on your screen within a second, and changing or removing it changes what you see.
   *FAIL:* their hands stay empty whatever they equip.
9. **Cannot be done by tester** — with a second player signed in, have them shoot a creature with a Bow while you watch.
   *Should:* you see the same line fly from them to the creature.
   *FAIL:* you see the creature take damage with nothing flying at it.

### 11. Dash trails and the stop previous on replay setting

3. Equip **Lunge** and **Lock Blades**, press Lunge on a creature, and keep attacking so Lock Blades sends you lunging again.
   *Should:* any trails that overlap in time sit on top of each other along the path you actually travelled — never two copies side by side.
   *FAIL:* two trails running side by side a short distance apart.
   *Status:* 2026-09-17 — seen by the user: two trails side by side. Measured: 8 of 78 trails started up to 1 unit to the side of the body they follow, because the dash had already moved the body by the time the trail started, and the trail kept that gap for its whole life. Fixed so a trail keeps only the height difference; not yet watched in play.

### 15. GMCP — an AI plays the built game as text (`MT-38`)

5. In the text client, enter `town_0` with creatures standing near the spawn and `look` a dozen times over a minute.
   *Should:* every creature within 25 m is listed with its name, health and distance for as long as it stands there.
   *FAIL:* a creature that is hitting you is missing from the list.
   *Status:* 2026-09-18 — not settled. In one run a DemonCyclop hitting Tessa from melee range was never listed, a freshly summoned KappaGreen was never listed, and in `field_11` the list drained from 33 creatures to none in 2.4 seconds with no map change; in three later runs the lists stayed full. A creature dropped from the list still hits you and its damage numbers still draw.
   *Status, 2026-09-18 14:10:* **not a fault in the list — the player is falling out of the world.** A debugger reproduced it and measured 22,326 units down in 123 s at a frozen x/z; the list only shows what is within 25 m, and 25 m down takes about three seconds, which is exactly when it empties. It is the player's own machine that falls, it happens when you WALK to a field's centre as well as when you teleport there, and it hits `field_11`, `field_12` and `field_0`. See the new step 11 below — the list itself is working.

### 16. GMCP reworked for fewer round trips and tokens

7. Send `cast 3 m2` (Lunge) at a creature within reach, then `pickup g1` on anything it drops, `go d2` through a door, `talk n1` to a villager, `use 1` on a bag item, `equip`/`unequip`, `stat STR`, `revive` after a death.
   *Should:* each answers with one line saying how it ended (`cast Lunge`, `picked up …`, `new map field_11`, the villager's lines, `used …`, `equipped …`, `spent…`/the stats line, `revived`).
   *FAIL:* a command answers with no reason line, or a wrong one.
   *Status:* 2026-09-18 15:00 — tester: `cast Lunge` / `m2 68` with XP and gold counted and the creature gone; `used Grilled Fish` (state `Grilled Fish 118s`); `unequipped Weapon`, `equipped Sword`; `stat STR` STR 19→20 (it cost 3 points, as the character sheet's rising cost says); `go d2` → `new map field_11` with ids restarted; `talk n1` returned the villager's two lines; `revived` / `not dead`; `wait 45` while dead took 45.09 s; `unknown id 'd9'`, `no hotbar skill '9'`. **Pickups took two goes to get right:** the tester found `picked up Bow` answered for a Bow that had vanished on its own (a kill's drops expire after 60 s); the first fix then went the other way and said `still on the floor: Mana Draught` while the bag had already gained it. Both settled by asking the bag first: watched on the final build, `picked up Grilled Fish` and `picked up Padded Vest`, both in the bag afterwards. Talking to a villager now answers like every other action (a line, then the screen) instead of being the only command with no screen — built, and watched only refusing an id in a map with no villagers.
9. Send `say /tp field_10` on its own.
   *Should:* the answer waits for the hang-up and re-dial and says `said; new map field_10` with the new map's screen (a command that moves nobody, such as `/log all on`, answers `said` after two seconds).
   *FAIL:* the answer shows the old map's screen half a second after the say.
   *Status:* 2026-09-18 14:40 — watched: `say /tp field_11 w0_from_town_0` answered `said; new map field_11` with the new map's screen. A command that moves nobody answers `said` after two seconds.
10. `cast 3 d1` (a door as the target).
   *Should:* `a skill is aimed at a creature (m#), a player (p#), me, or a spot (x z) — not 'd1'`.
   *FAIL:* the character walks toward the door for 15 s.
   *Status:* 2026-09-18 14:40 — watched: `cast 3 d1` answers `a skill is aimed at a creature (m#), a player (p#), me, or a spot (x z) — not 'd1'` and nobody moves.

### 20. Repository audit fixes

9. **Cannot be done by tester** — the door players arrive through now logs a warning and stays open when it hiccups, instead of shutting for good in silence. There is no way to provoke that hiccup from inside the game.
