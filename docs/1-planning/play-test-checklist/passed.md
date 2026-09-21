---
status: open
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# Play-Test Checklist — Passed

Steps that were watched working and have not been re-opened since. Every step keeps the section and number it had in the single checklist, so a step referred to elsewhere as "step 2 of section 10" is still that step, and a step moves between these files as its verdict changes. The four files are [passed](passed.md), [failed](failed.md), [on-going](on-going.md) and [not done yet](not-done-yet.md).

## Without Play mode

Nothing needs rebuilding or restarting for these.

1. **Passed** — **The old live-tuning panel is gone** *(batch 2)*. Search the game's code for a key binding on **P**, and the saved scenes and prefabs for a key setting stored as P.
   *Should:* nothing is bound to P.
   *FAIL:* anything bound to P opens a panel.
2. **Passed** — **No accessory place** *(batch 3, ST-3-K)*. Search the game's code, its interface files and its saved prefabs for "Accessory".
   *Should:* nothing the game draws on screen names an accessory.
   *FAIL:* a label, a slot or a window piece named Accessory that the game still draws.

## In Play mode

Before running any of these, do what [not-done-yet.md ▸ Before you start](not-done-yet.md#before-you-start--what-to-rebuild-and-restart) says must be rebuilt and restarted.

### 1. The bag's columns

2. **Passed** — Type `/weapon sword` and `/item item.minor-healing-potion 3`.
   *Should:* the sword's row reads **1** under Held; the potion row reads **×3**.
   *FAIL:* the sword shows a blank or a dash under Held.
4. **Passed** — Type `sword` in the **FIND** box, then clear it and type the name of one skill the sword rolled.
   *Should:* both searches find the sword.
   *FAIL:* searching a skill name finds nothing.

### 2. Your machine works out no game number

1. **Passed** — Hit a creature ten times with basic attacks.
   *Should:* every hit draws exactly one number (or **MISS**), and the creature's health bar drops on each hit that shows a number.
   *FAIL:* a hit draws two numbers, or a number appears while the bar does not move.
2. **Passed** — Kill a creature with a multi-hit sword skill part-way through its hits. Blade Dance is the wrong skill for this: after a kill it aims its remaining hits at other creatures, never at the body, so use a skill that strikes one target several times.
   *Should:* the hits after the kill still draw numbers on the body, and nothing else happens to it.
   *FAIL:* those hits apply a status, a buff or a kill a second time.
3. **Passed** — Take some damage, then use the potion from step 1.2 (double-click its row).
   *Should:* green **+N** digits appear over you once, and your health bar rises by that amount.
   *FAIL:* no green digits, digits twice, or the bar does not move.
4. **Passed** — Stand still after taking damage.
   *Should:* health refills steadily with no jumps backwards.
   *FAIL:* the bar climbs and then snaps back down.
5. **Passed** — Press a skill on your hotbar, then press it again straight away, then wait for its timer.
   *Should:* the second press is refused; the skill becomes usable exactly when its timer runs out.
   *FAIL:* the second press fires, or the skill stays unusable after its timer ends.
6. **Passed** — Press **C**, type `/statpts 5`, spend one point.
   *Should:* every number on the sheet that depends on that stat updates within about half a second.
   *FAIL:* the sheet keeps the old numbers until you close and reopen it.

### 3. The gear shape (`MT-3`)

1. **Passed** — Press **C**. *(ST-3-P)*
   *Should:* the sheet shows **MELEE ATK**, **RANGED ATK** and **M.ATK** on their own lines, and beside each main stat a **next +N at X**.
   *FAIL:* a single ATK line, a missing "next" note, or clipped text.
2. **Passed** — Sign in as **Bravo** — a new character, every main stat at 1, 48 points to spend. Raise **STR** one point at a time, watching MELEE ATK, until STR goes from 9 to 10. *(ST-3-O)*
   *Should:* each ordinary point adds 1; the point that reaches 10 adds **5**.
   *FAIL:* the step to 10 adds 1 or 2.
3. **Passed** — Holding the sword, raise **DEX** by two points. *(ST-3-N)*
   *Should:* RANGED ATK rises 1 per point; the **high end** of MELEE ATK rises 1 in total across the two points. (The low end rises on every DEX point by design — DEX narrows the range from below.)
   *FAIL:* the high end of MELEE ATK rises on every DEX point.
4. **Passed** — Type `/item item.bow`, double-click the bow to wear it, hit a creature, raise DEX, hit it again. *(ST-3-N)*
   *Should:* the hits grow with DEX.
   *FAIL:* only STR makes the bow's hits grow.
5. **Passed** — Hover the sword, then the bow.
   *Should:* the sword's card says **MELEE attack**; the bow's says **RANGED attack**.
   *FAIL:* the line is missing.
7. **Passed** — Fight a creature for about a minute, taking hits.
   *Should:* now and then a much bigger hit lands on you, drawn in the same red as an ordinary hit — a crit on you is not drawn differently, by design.
   *FAIL:* every hit falls in one narrow band.
8. **Passed** — Hit a creature with basic attacks.
   *Should:* each hit's effect plays once.
   *FAIL:* the effect plays twice.

### 4. The skill and server plumbing (`MT-1`)

Put a learned skill on your bar by dragging it from the Skills window (**K**); a learned passive works on its own. `/bind <skillId>` binds from chat.

**Passives on a landed hit and on a kill:**

1. **Passed** — **B1 — a passive on a landed basic attack.** Tick **Momentum** in J. Land basic attacks on a creature.
   *Should:* a **Mo** tile appears on your buff bar and its count climbs with each landed hit.
   *FAIL:* nothing appears.
2. **Passed** — **B2 — a passive on a kill.** Tick **Encore** in J, put **Blade Dance** on your bar, kill a creature with Blade Dance.
   *Should:* Encore's effect (read its card in J) fires on the kill.
   *FAIL:* nothing happens on the kill.

**Then the rest:**

4. **Passed** — **A6** — double-click a potion in the bag. *Should:* it is used once and the stack drops by one. *FAIL:* nothing happens, or two go.
5. **Passed** — **A8** — press skills until your mana is below the next skill's cost, then press it. *Should:* refused, no mana spent. *FAIL:* it fires.
6. **Passed** — **B3** — put **Parry** on your bar, tick **Composure** and **Opening** in J. Press Parry just before a creature hits you. *Should:* the hit is erased, and Composure fires. *FAIL:* the hit lands, or Composure never fires.
7. **Passed** — **B4** — tick **Last Stand**, type `/heal`, then let a creature bring you below 30% health. *Should:* Last Stand fires once as you cross the line. *FAIL:* it never fires, or fires on every hit below the line.
8. **Passed** — **B5** — mid-fight, tick a passive you have not used this session and trigger it. *Should:* it fires the first time. *FAIL:* it needs a relog to work.
9. **Passed** — **B6** — untick it and trigger the same moment. *Should:* it no longer fires. *FAIL:* it still fires.
10. **Passed** — **C2** — put **War Banner** or **Bulwark** on your bar from J and press it. Neither has a two-letter tag. *Should:* its buff tile shows the skill's full name. *FAIL:* a blank tile.
11. **Passed** — **C4** — press **Brace**, and while its tile is up press another skill that grants a buff, let that one run out, then hover Brace's tile. *Should:* Brace's hover text is still complete. *FAIL:* the text is gone or blank.
12. **Passed** — **C5** — let a buff expire, then press the same skill again. *Should:* the tile comes back with its timer. *FAIL:* no tile the second time.
13. **Passed** — **C6** — press **Brace** and take hits while it is up. *Should:* the red tile that follows carries the same letters, **Br**. *FAIL:* different letters, or blank.
14. **Passed** — **C7** — press **Parry**, press it again straight away, then let a creature hit you. *Should:* the second press is refused — Parry's cooldown (5 s) outlasts its window (3 s), by design — and the hit is erased. *FAIL:* the second press adds a charge, or the hit lands.
15. **Passed** — **D2** — press a skill, swing, and double-click a potion within about a second. *Should:* all three happen. *FAIL:* one is silently dropped.
17. **Passed** — **E1** — walk up to a hostile creature and walk away. *Should:* it chases you. *FAIL:* it stands still.
18. **Passed** — **E2** — walk through a portal to another map. *Should:* the new map has creatures in it. *FAIL:* the map is empty.
19. **Passed** — **E4** — let a creature kill you. *Should:* it stops attacking once you are dead. *FAIL:* it keeps swinging at your body.

**Needs two players at once** — the tester plays through the editor, which is one player. Start a second copy of the game with the command in [Starting the Network ▸ 5. The players](../../2-systems/networking/starting-the-network.md), signed in on a second account:

20. **Passed** — Both stand in the same map. *Should:* each sees the other walking and swinging. *FAIL:* either is invisible to the other.
21. **Passed** — Stand in different maps. *Should:* neither sees the other. *FAIL:* a player from another map appears.
22. **Passed** — Both attack one creature. *Should:* it picks one of you and hits that person; both of you see its health drop. *FAIL:* one player's screen shows its health frozen.

### 5. Fixes from the 2026-09-14 play-test

1. **Passed** — Type `/summon 5 normal`, then kill each one with a single blow.
   *Should:* most of them pay gold — each has an 85% chance.
   *FAIL:* none of the five pays anything.
2. **Passed** — Type `/tp field_15` with no spawn name after it.
   *Should:* you land standing on the ground.
   *FAIL:* you fall through the world.
3. **Passed** — Hit a creature with basic attacks.
   *Should:* one hit sound per hit.
   *FAIL:* two sounds per hit, or none.
4. **Passed** — Type `/summon 1 normal`, kill it, and wait for it to come back — a summoned creature comes back where it died about a second later. A creature a map spawned does not: the map replaces it with a new random creature somewhere else.
   *Should:* it looks exactly as it did before it died.
   *FAIL:* it comes back faded or tinted.
5. **Passed** — Type `/tp field_14` from another map, wait 3 seconds, `/summon 3 normal`, and stand beside them with invincibility off for 30 seconds.
   *Should:* they attack you.
   *FAIL:* nothing attacks you.
7. **Passed** — Press **Brace** and take several hits while it is up, then wait for it to end.
   *Should:* the held hits show in pale blue, and the bill that lands when Brace ends is drawn violet — not the red of an ordinary hit.
   *FAIL:* the bill is drawn red.
8. **Passed** — Sign in and press **C** straight away.
   *Should:* the sheet never shows "0-0" or "next +0 at 0" — it stays empty for a moment, then fills with real numbers.
   *FAIL:* zeros appear, even briefly.

### 14. Bosses drawn whole, with the normal health bar

1. **Passed** — Type `/summon 5 boss` and look at each boss while it stands still.
   *Status:* 2026-09-18 08:46, watched in the editor — SquidRed, TenguRed, TenguBlue, GiantRacoon, GiantRacoonGold, GiantSpirit, GiantFlam and DemonCyclop2 all drawn whole, feet on the ground. A boss summoned on the far side of the tavern roof from the camera is hidden by the roof, like any creature standing there.
   *Should:* each shows its whole creature, feet on the ground, playing a short standing animation.
   *FAIL:* only a small square corner of a picture shows, the picture is cut off at an edge, or the creature floats above or sinks into the ground.
3. **Passed** — Type `/summon 1 normal` beside a boss, and hit both once.
   *Status:* 2026-09-18 08:46, watched in the editor — every boss bar the same width as the Bamboo's and the Slime3's, shown before any hit.
   *Should:* the boss's health bar is the same size as the normal creature's; the boss still shows its name under the bar, and its bar shows before you hit it.
   *FAIL:* the boss's bar is wider or taller than the normal creature's.

### 15. GMCP — an AI plays the built game as text (`MT-38`)

1. **Passed** — With the four services up, run `tools/agent-play/game_link.py launch`, then `login alpha@test.local testpass123`, `play Tessa`, and `say hello` while you stand at the town spawn in the editor.
   *Should:* Tessa appears beside you and her line shows in your chat; her `look` lists you under **players** with the right distance.
   *FAIL:* she never appears, her line never shows, or her players list is empty while you stand next to her.
   *Status:* 2026-09-17 — three players (Alpha in the editor, Tessa on port 7790, Tester on port 7791) met at the town spawn and read each other's chat.
2. **Passed** — Type `/tp field_11`, then `/tp town_0`, and try to walk.
   *Should:* you land on the town's Start spawn spot at (-1.6, -1.8) and can walk away.
   *FAIL:* you land near the map's centre point, sunk into the ground, and every step is refused.
   *Status:* 2026-09-18 — fixed and re-tested on a fresh client build: two full field_11 ↔ town_0 round trips, every arrival logged "placed in 'town_0' at spawn 'Start' (-1.58, 0.00, -1.81)" and walking worked each time. Cause: the map being left is still loaded for the frame the new map arrives in, so the arrival search saw BOTH maps' "Start" spots and sometimes chose the field's (0,0) inside the town. Arrival now only considers the new map's spots and ground.
4. **Passed** — In the text client, `cast` Committed Strike at any creature.
   *Should:* the skill lands and the client answers with the screen.
   *FAIL:* the client process dies.
   *Status:* 2026-09-18 13:00 — on the rebuilt client that skips all drawing, a tester cast it (`cast Committed Strike`, `m3 119`, the server logged the kill) and the client was still listening 5 s later with a clean log. Earlier that morning the old client, which still tried to draw with no window, died 4 times out of 4 within a second of the cast, in its drawing thread.

### 16. GMCP reworked for fewer round trips and tokens

1. **Passed** — With the four services up, launch a text client (`AGENT_PORT=7793 tools/agent-play/game_link.py launch`), then send one line: `login alpha@test.local testpass123; play Tessa`, then `look` four times.
   *Should:* the one answer carries the sign-in's character list, a `play Tessa: in the world` line, and the screen once; the door ids (`d1`, `d2`) read the same on every look.
   *FAIL:* a door changes its number between two looks, or the batched line answers with two screens.
   *Status:* 2026-09-18 12:49 — tester: one answer, one screen; m1/m2/m3/d1/d2 kept their numbers across four looks while their order changed. Before the rework they changed once ~0.3 s after entering.
2. **Passed** — After `play`, read the first screen.
   *Should:* every creature within 25 m is on it (the `play` waits until nothing new has arrived for a second).
   *FAIL:* a creature that hits you a moment later was missing from the first screen.
   *Status:* 2026-09-18 12:25 — both town creatures listed on the first screen; before the rework the first screen said "mobs none within 25" with a creature 1.6 m away.
3. **Passed** — Stand two text clients on the same spot (Tessa on one port, Bravo on another) and let a creature hit one of them; read the other's `since last`.
   *Should:* the hit shows as `p1 113` (the other player's id), never as `me`.
   *FAIL:* one client reports the other's damage as `me`.
   *Status:* 2026-09-18 14:50 — **both directions watched.** Standing together in `field_11` with a summoned creature on them: Bravo's screen read `p1 1`, `me 3`, `p1 1`, `me 4`; Tessa's read `p1 1 x88`, `me 1`, `me Guard`, `me MISS x2`. Each names the other as `p1` and itself as `me`, and 88 identical blows arrived as one counted line. Before the rework the same blows read `me 115`, `me 111`.
4. **Passed** — Send `wait 1`, then `wait 1; stats; bag`, standing still.
   *Should:* the second answer ends with `unchanged: hotbar, npcs, doors` and those three sections are absent; `look` brings them back in full.
   *FAIL:* a section that did not change is printed again, or `look` leaves one out.
   *Status:* 2026-09-18 12:25 — as expected.
5. **Passed** — Fight a creature for 30 s (`attack m1`) while another nibbles you for 1 HP a second.
   *Should:* the answer says `still fighting after 30s`; `since last` counts repeats (`me 1 x3`) and says `(+N earlier not shown)` when more than 25 happened; a walk ordered while nibbled (`move 6 6`) does NOT end on the nibble (only on arrival, a new map, HP under 30%, death, or 30 s).
   *FAIL:* a walk answers `hit` at once, or repeated identical lines are printed one per line.
   *Status:* 2026-09-18 12:55 — tester: `still fighting after 30s`, `me 1 x3`, `(+12 earlier not shown)`; the walk did not answer `hit` and moved 0.8 m in 30 s — the client log said `MOVEMENT REFUSED … Nearest solid: Deciduous_tree2 at 0`: field_10's Start spot also has a tree on it, on top of the known stagger rooting.
6. **Passed** — Read the text client's own log file after two minutes in the world.
   *Should:* a few dozen lines, none of them a drawing error.
   *FAIL:* `RenderTexture.Create failed` or `RenderPass` lines.
   *Status:* 2026-09-18 13:07 — tester: 70 and 84 lines after 18 minutes on two clients, 0 drawing errors, 0 error lines; before the rework, 22,476 lines in 117 s. A plain `look` answers in 0.04–0.07 s.
