---
status: open
owner: vince
last-updated: 2026-09-18
tags:
  - planning-production
---

# Play-Test Checklist

Everything built but never watched working, in two lists: the checks that can be settled **without Play mode** — by searching the game's code and saved files, with no server and nothing rebuilt — and the checks that need the game running **in Play mode**. Every step says what to do, what should happen, and what counts as a **FAIL**. A step that is blocked by something other than the rebuild below says **Cannot be done now** and why; a step the tester cannot run says **Cannot be done by tester**. The steps come from eighteen batches:

1. **The bag's columns** (2026-09-14).
2. **Your machine works out no game number** (2026-09-14) — the server sends every number and your screen only draws it.
3. **The gear shape** (2026-09-13) — [Action Plan](action-plan.md) `MT-3` and its subtasks `ST-3-K`, `L`, `N`, `O`, `P`, `S`.
4. **The skill and server plumbing** (2026-09-03) — [Action Plan](action-plan.md) `MT-1`, the half of its list that was never run.
5. **Fixes from the 2026-09-14 play-test** — one-hit kills, the endless fall, the removed second sound player and creature hit tint.
6. **The first armour pieces** (2026-09-15) — [Action Plan](action-plan.md) `MT-7`: Leather Cap, Padded Vest, Traveller's Cloak and Wooden Buckler.
7. **Creature loot on the ground and random creature gear** (2026-09-15).
8. **Gear bonuses beside the main stats on the character sheet** (2026-09-15).
9. **Creatures cast their weapon's skills** (2026-09-16) — actives, the ultimate and passives, paying MP; their weapon's reach, the weapon in a 3D creature's hand, and its skill poses.
10. **Creatures get every skill moment, the arrow, and other people's weapons** (2026-09-17) — a creature now answers a parry, a hit it took, dropping to low health, and its own swings; every bow fires a drawn arrow that everybody can see; another person's weapon shows in their hand; a creature casts a long-range skill from as far away as you can; Retribution's card matches what it does; the player's **T** mode is now called the auto-pilot AI; and a multi-hit skill landing on YOU now shows the rising yellow total it has always shown on a creature.
11. **Dash trails and the stop previous on replay setting** (2026-09-17) — a new **stop previous on replay** setting on an effect step in the VFX Composer, left off on the dash effect so Blade Dance keeps its star; and dash trails that were drawn up to a unit to the side of the path you travelled.
12. **The training dummy's health** (2026-09-17) — a dummy made with `/dummy` now starts at full health instead of an empty bar.
13. **One extra skill slot on every creature** (2026-09-17) — apart from its weapon's skills, every creature carries one extra skill drawn at random from all published skills each life, and `/summon` can name that skill instead.
14. **Bosses drawn whole, with the normal health bar** (2026-09-17) — a boss shows its whole picture, playing its Walk animation while moving (Jump for the ones that hop) and its Idle animation while standing; its health bar is the same size as a normal creature's; and the two dragons made of separate body parts are off the roster.
15. **GMCP — an AI plays the built game as text** (2026-09-17) — the client runs with no window and is driven by text commands; three players met and chatted, and two teleport landing spots leave a player stuck.
16. **GMCP reworked for fewer round trips and tokens** (2026-09-18) — several commands per line, one line saying why each ended, ids that never change inside a map, a first screen with everything on it, exact owners on every floating number, unchanged sections left out, counted events, and a client log with no drawing errors.
17. **Every map's centre arrival point removed** (2026-09-18) — the point called Start at each map's middle, which the generator planted among the plaza props and the trees, is deleted from all 41 maps and from both map generators; the arrival points beside the doors are untouched.
18. **Closing the game no longer tries to reconnect** (2026-09-18) — shutting the client down used to be treated as a connection that had died: it counted a lost-world attempt, put "Reconnecting…" on screen and scheduled another dial two seconds later, all while the program was already closing.

---

## Without Play mode

Nothing needs rebuilding or restarting for these.

1. **Passed** — **The old live-tuning panel is gone** *(batch 2)*. Search the game's code for a key binding on **P**, and the saved scenes and prefabs for a key setting stored as P.
   *Should:* nothing is bound to P.
   *FAIL:* anything bound to P opens a panel.
2. **Passed** — **No accessory place** *(batch 3, ST-3-K)*. Search the game's code, its interface files and its saved prefabs for "Accessory".
   *Should:* nothing the game draws on screen names an accessory.
   *FAIL:* a label, a slot or a window piece named Accessory that the game still draws.

---

## In Play mode

GM commands are typed into chat and need a GM account — see [Starting the Network ▸ GM accounts](../2-systems/networking/starting-the-network.md).

### Before you start — what to rebuild and restart

1. **Game server build — REBUILD.** Game code changed in batches 1 to 5, 7 to 10, 12 and 13 (the server decides every creature's extra skill and runs `/summon`), batch 14 removes the two dragons from the roster the server picks a boss from, and batch 10 adds a new message that says an arrow was fired, a new value that carries what a person is wearing, and two new pieces on the message that carries a blow to a person, and batch 6 adds four new items, and a server only runs the code and items it was built with. The message that carries a blow changed shape in batch 5, so an old server and the new game cannot understand each other's blows. In Unity, let the editor finish compiling, then run `tools/build-game.sh server`.
2. **Game server process — RESTART** after the rebuild. Stop it with `pkill -f 'unwritten-server.app/unwritten'`, check `lsof -nP -iUDP:7777` prints nothing, then start it with the command under *The game server ▸ Running it* in [Starting the Network](../2-systems/networking/starting-the-network.md).
3. **Character server — no restart.** Nothing inside it changed, and the one string both servers must agree on did not change.
4. **Account service — no restart.** Nothing inside it changed.
5. **The game you play — REBUILD** if you play the built game. **Batch 17 changed the MAPS, which live in both builds — so the game server must be rebuilt and restarted for it too, not just this one.**: `tools/build-game.sh client`. Batch 16 is entirely inside the windowless client (already rebuilt on 2026-09-18 12:30, nothing on the server side changed shape). Batches 1 and 2 changed what your own screen draws, batch 6 adds items your screen must know to draw them, batch 7 changed the message that describes a creature, batch 8 changed the message that carries your character sheet, batch 9 changed how your screen draws a creature's cast, and batch 10 adds the message that says an arrow was fired, the value that says what a person is wearing, and two new pieces on the message that carries a blow to YOU — an old game and a new server cannot read each other's creatures, sheets, arrows or blows on a person, and an old game draws no arrow, no weapon in anybody else's hand and no rising total over you. Batch 11 changed only what your screen draws, so it needs this rebuild and nothing on the server side. Batch 13 changes nothing your screen draws — only the `/help` line for `/summon`. Batch 14 changes how your screen draws a boss and the saved boss creatures it loads. Playing from the editor needs nothing extra: the game reads the character server's address from its own settings file.

**Never refresh the editor while a play session is running** — its automatic refresh is switched off, so an edit waits for a refresh, and a refresh mid-play reloads the game's code and ends the session.

### 1. The bag's columns

1. **Passed** — Press **B**. Click every tab along the top of the bag in turn.
   *Should:* every tab shows the same three columns — **Name**, **Tier**, **Held**.
   *FAIL:* a tab shows Slot, Skills, Atk/Def or Out, or any tab is missing Held.
2. **Passed** — Type `/weapon sword` and `/item item.minor-healing-potion 3`.
   *Should:* the sword's row reads **1** under Held; the potion row reads **×3**.
   *FAIL:* the sword shows a blank or a dash under Held.
3. **Passed** — Click **Name**, then **Name** again, then a third time. Do the same for **Tier** and **Held**.
   *Should:* first click sorts ascending (▲), second descending (▼), third returns the bag to your own order.
   *FAIL:* a header does nothing, or the third click leaves the list sorted.
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
6. **Passed** — Type `/weapon sword common`, `/weapon sword epic`, `/weapon sword legendary`. Hover each.
   *Should:* ATK reads **3**, **4** and **4**. (A Rare sword also reads 3 — a 9% lift on 3 still rounds to 3.)
   *FAIL:* the Epic or Legendary sword reads 3.
7. **Passed** — Fight a creature for about a minute, taking hits.
   *Should:* now and then a much bigger hit lands on you, drawn in the same red as an ordinary hit — a crit on you is not drawn differently, by design.
   *FAIL:* every hit falls in one narrow band.
8. **Passed** — Hit a creature with basic attacks.
   *Should:* each hit's effect plays once.
   *FAIL:* the effect plays twice.

**Cannot be watched in play today, and why:**

- **Cannot be done now** — **ST-3-L (creatures count their gear's main stats)** — the only creature wearing gear is the Axolot, and its Sword carries no main stats, so there is nothing to see move.

### 4. The skill and server plumbing (`MT-1`)

Open the Grimoire with **J** to tick or untick a passive and to put any skill on your bar. `/bind <skillId>` does the same from chat.

**Passives on a landed hit and on a kill:**

1. **Passed** — **B1 — a passive on a landed basic attack.** Tick **Momentum** in J. Land basic attacks on a creature.
   *Should:* a **Mo** tile appears on your buff bar and its count climbs with each landed hit.
   *FAIL:* nothing appears.
2. **Passed** — **B2 — a passive on a kill.** Tick **Encore** in J, put **Blade Dance** on your bar, kill a creature with Blade Dance.
   *Should:* Encore's effect (read its card in J) fires on the kill.
   *FAIL:* nothing happens on the kill.

**Then the rest:**

3. **Passed** — **A5** — type `/weapon sword rare`, wear it, press a skill from its kit. *Should:* the skill fires on the first press. *FAIL:* the tile is blank or the press does nothing.
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
16. **D3** — kill creatures until one drops something. *Should:* the item lies on the ground where the creature fell, and standing beside it and pressing **F** puts it in your bag. *FAIL:* the item goes straight into your bag, or **F** picks nothing up. A kill with no drop is not a fail. *Status:* re-opened 2026-09-15 — kill loot now lands on the ground instead of in your bag (batch 7).
17. **Passed** — **E1** — walk up to a hostile creature and walk away. *Should:* it chases you. *FAIL:* it stands still.
18. **Passed** — **E2** — walk through a portal to another map. *Should:* the new map has creatures in it. *FAIL:* the map is empty.
19. **Passed** — **E4** — let a creature kill you. *Should:* it stops attacking once you are dead. *FAIL:* it keeps swinging at your body.

**Needs two players at once** — the tester plays through the editor, which is one player. Start a second copy of the game with the command in [Starting the Network ▸ 5. The players](../2-systems/networking/starting-the-network.md), signed in on a second account:

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
6. Type `/dummy`, hit the dummy a few times, then `/killall`.
   *Should:* your gold does not change.
   *FAIL:* gold goes up when the dummy dies.
7. **Passed** — Press **Brace** and take several hits while it is up, then wait for it to end.
   *Should:* the held hits show in pale blue, and the bill that lands when Brace ends is drawn violet — not the red of an ordinary hit.
   *FAIL:* the bill is drawn red.
8. **Passed** — Sign in and press **C** straight away.
   *Should:* the sheet never shows "0-0" or "next +0 at 0" — it stays empty for a moment, then fills with real numbers.
   *FAIL:* zeros appear, even briefly.

### 6. The first armour pieces (`MT-7`, and `ST-3-S` armour rolling when it drops)

1. Type `/item item.padded-vest` five times. Hover each copy in the bag.
   *Should:* each card's defence reads between **2** and **4**, and the five copies do not all read the same.
   *FAIL:* every copy is identical, or a defence outside 2 to 4.
2. Double-click one Padded Vest to wear it, then press **C**.
   *Should:* DEF on the sheet rises by the defence on its card.
   *FAIL:* DEF does not move.
3. Type `/item item.leather-cap`, `/item item.travellers-cloak` and `/item item.wooden-buckler`, and wear each one, watching the sheet.
   *Should:* the Leather Cap raises DEF and M.DEF; the Traveller's Cloak raises M.DEF only; the Wooden Buckler raises DEF.
   *FAIL:* a number does not move, or the wrong one moves.
4. Wearing a piece, log out and back in, then drop it and pick it up. Hover it after each.
   *Should:* its numbers never change.
   *FAIL:* any number on its card changes.
5. Wear the Wooden Buckler and let a creature hit you about 60 times.
   *Should:* now and then a hit is blocked — no damage and a block sound — about 1 hit in 20.
   *FAIL:* not one block in 60 hits.

### 7. Creature loot on the ground and random creature gear

1. Type `/summon 5 normal`, then CLICK each creature in turn to pick a fight with it.
   *Should:* in the panel at the top CENTRE — the one for whoever you are fighting — below the creature's name and states, one line per piece it wears: the item's name in its rarity colour and a drop chance — Common **20%**, Uncommon **12%**, Rare **6%**, Epic **3%**, Legendary **1%**. Some creatures wear nothing.
   *FAIL:* a chance that does not match the colour of the name beside it, or no creature out of the five wears anything after three summons.
2. Kill them all.
   *Should:* your gold goes up as before, and anything they gave up lies on the ground beside the bodies — a sparkle, with its name shown while you hold **Alt**.
   *FAIL:* an item lands in your bag without you picking it up.
3. **Cannot be done by tester** — with a second player signed in beside you, kill a creature that drops something, and have the second player press **F** on it straight away, then again after 10 seconds.
   *Should:* the first press takes nothing; after 10 seconds the second player can take it.
   *FAIL:* the second player takes it within 10 seconds.
4. Leave a dropped item on the ground and wait 70 seconds.
   *Should:* it vanishes at about 60 seconds.
   *FAIL:* it is still there after 70 seconds.
5. Click creatures until you are fighting one whose top-centre panel lists a **Wooden Buckler**, stand in front of it with invincibility off, and hit it about 60 times with basic attacks.
   *Should:* now and then a hit does no damage at all — about 1 hit in 20.
   *FAIL:* every one of the 60 hits does damage.
6. Type `/dummy` and click the dummy to fight it.
   *Should:* its top-centre panel lists no gear.
   *FAIL:* any gear line in the dummy's panel.
7. Kill a creature whose top-centre panel listed gear, wait for it to come back, and click it again.
   *Should:* its list is rolled fresh — it may wear something different or nothing.
   *FAIL:* the panel still lists the exact piece that dropped from it.

### 8. Gear bonuses beside the main stats on the character sheet

1. Type `/item item.leather-cap` until a copy's card lists a main stat (for example **STR +2**), wear that copy, and press **C**.
   *Should:* that stat reads your bought points followed by the gear bonus in brackets — for example **12 (+2)** — and every stat nothing on you adds to shows the plain number with no brackets.
   *FAIL:* the number does not change, the brackets show a different amount from the card, or brackets appear on a stat nothing adds to.
2. Take the copy off.
   *Should:* the brackets disappear within about half a second.
   *FAIL:* the brackets stay until you close and reopen the sheet.

### 9. Creatures cast their weapon's skills

Before starting, type `/log cast on` and `/log passive on` in chat. Only the Sword has published skills, so only a creature holding a **Sword** casts its weapon's skills. Every creature also carries one extra skill (batch 13), so any creature may cast that one.

1. Type `/summon 5 normal` and hover over each creature until one's panel lists **Sword** — summon again if none does. Attack it and stay in its reach with invincibility off.
   *Should:* within a minute its skill's name floats in a bubble over its head, the skill's effect plays and lands on you, and the MP bar in the top-centre panel drops. `logs/server.log` has a line naming that creature followed by `casts skill.sword.` and the skill.
   *FAIL:* after 60 seconds at full MP it has only swung, or its name bubble appears and its MP bar never moves.
   *Status:* 2026-09-16 play-test — no name bubble over any creature. A flat sprite creature had no head for a bubble to ride, so the bubble was silently skipped; it now sits above the sprite. Cast logging was off, so that run did not record whether any creature cast at all. Second run, about an hour: only Lunge and Parry were ever seen. Replayed offline with the real costs, a level 3–5 creature holds 17–23 MP and refills 1.5 a second, so its two 8-MP skills take almost every cast (about 5 a fight), each 12-MP skill gets about 1, Committed Strike about 0.3, and no 40-MP ultimate can ever be afforded below level 11. **Settled 2026-09-17:** a creature takes its ultimate first whenever that is off cooldown and affordable, and otherwise one of its ready ordinary actives at random — it never saves up. So seeing only the cheap skills on a low-level creature is correct, not a failure.
2. Keep fighting until it casts a skill that shows a cast bar over its head.
   *Should:* on your screen the bar fills over its head and a ring appears on the ground if the skill has one; the creature neither walks nor bites until the bar is gone.
   *FAIL:* it walks or bites while its bar is still filling, or the ring stays on the ground after the bar is gone.
3. Fight the same creature for two minutes, then search `logs/server.log` for `fired skill.sword.`.
   *Should:* at least one line names that creature — its passives firing.
   *FAIL:* every passive line naming the creature says `skipped … not switched on`.
4. Stand so the sword creature and two other creatures are all within a few steps of you, and let it cast.
   *Should:* its skills only ever take health off you; the other creatures' health bars stay full until you hit them.
   *FAIL:* another creature's health drops when no player has hit it.
5. With `/log cast on`, fight a creature whose panel lists **Sword** for 60 seconds, then search `logs/server.log` for `weighed its skills`.
   *Should:* a line naming that creature each time it picks a skill, plus a `none ready, swinging` line only when its reasons change — never the identical wait repeated swing after swing. Each line lists every skill it holds with `ready` or the reason it was not — on cooldown with seconds left, not enough mana with held and needed, out of reach with the distance — and ending with `picked` a skill or `none ready, swinging`.
   *FAIL:* no such line for a creature that was fighting you, a line whose pick is a skill it listed as not ready, or the same `none ready` line repeated back to back for one creature.
6. With `/log cast on`, fight a creature whose panel lists **Bow**, or no weapon, for 60 seconds, then search `logs/server.log` for `weighed its skills` on lines naming it.
   *Should:* every such line lists exactly one skill — its extra skill — and any name bubble over it is that skill. No line at all is also correct when its extra skill is a passive.
   *FAIL:* a line naming it lists two or more skills, or a bubble shows a skill its line never listed.
7. Type `/summon 5 normal` until a creature's panel lists **Bow**, then stand still and let it come at you.
   *Should:* it stops about as far away as you shoot with a Bow yourself and hits you from there.
   *FAIL:* it walks up to arm's length before it hits you.
8. Type `/summon 5 normal shadow` until a 3D creature's panel lists **Sword** or **Bow**.
   *Should:* the weapon listed is in its hand. After it dies and comes back, its hand shows whatever its panel now lists, or nothing.
   *FAIL:* an empty hand while its panel lists a weapon, or a weapon in its hand that its panel does not list.
9. Fight a 3D creature holding a **Sword** until it casts.
   *Should:* while its cast bar fills it holds the same casting pose you hold when you cast, and when the skill goes off it plays the same release animation you play for that skill.
   *FAIL:* it stands in its idle or walking pose through its cast bar, or the skill goes off with no animation.
10. Type `/summon 1 normal shadow`, kill the 3D creature, and wait for it to come back.
   *Should:* when it is back it stands, walks, runs and swings with its animations, exactly as before it died.
   *FAIL:* it stands static with no pose after coming back.
   *Status:* 2026-09-16 play-test — static after coming back. A body that came back had its animations switched off by a distance check that looked for players in the wrong place; fixed, needs re-test.
11. **Cannot be done by tester** — with a second player signed in and watching you fight the sword creature.
   *Should:* they see the same name bubble, cast bar and skill effect on it that you see.
   *FAIL:* the creature casts on your screen and stands still or only swings on theirs.
12. Type `/summon 5 normal shadow` until a 3D creature's panel lists **Sword**, and fight it until it casts **Onslaught** (its damage numbers start landing about twice as fast as its normal swings).
   *Should:* for as long as those fast numbers land, its sword swings once for every number — the swing is visibly faster than its normal attack.
   *FAIL:* its swing stays at normal speed, or it restarts the swing before finishing, while the numbers land fast.
13. **Cannot be done by tester** — with a second player signed in, have them press **Onslaught** on a creature while you watch them.
   *Should:* on your screen their sword swings as fast as their damage numbers land, the same as on their own screen.
   *FAIL:* on your screen they swing at normal speed while their numbers land fast.

### 10. Creatures get every skill moment, the arrow, and other people's weapons

Before starting, type `/log cast on` and `/log passive on` in chat.

1. **Without Play mode** — open the player wiki's Sword page (or press **K** in game and hover **Retribution**).
   *Should:* its words read that it lands five strikes each worth 100% Attack plus 250% of the banked total, and that with an empty bank it still casts for five plain attacks.
   *FAIL:* it still says the damage is "split across 5 strikes", or that it "does nothing with an empty bank".
2. Type `/summon 5 normal` until a creature's panel lists **Sword**, fight it for two minutes with invincibility off, then search `logs/server.log` for `fired skill.sword.` on lines naming that creature.
   *Should:* lines for moments that used to be yours alone — `OnParry`, `OnDamageReceived`, `OnLowHP`, `OnBasicAttackStart` and `OnBasicAttackDealt` — as its skills answer them.
   *FAIL:* every line naming that creature is on `OnCast`, `OnKill` or `OnSkillCastComplete` only.
3. Fight that same creature down to under a third of its health without killing it, then search the log for `OnLowHP` on a line naming it.
   *Should:* one line at the moment its bar crossed about a third.
   *FAIL:* no such line, or a line every hit after that.
4. Type `/summon 5 normal` until a creature's panel lists **Bow**, then stand still and let it shoot you.
   *Should:* a thin pale line flies from the creature to you, and your health only drops when the line reaches you — not at the moment it fires.
   *FAIL:* no line appears, or your health drops the instant it fires.
5. Equip a Bow yourself (`/weapon bow`), stand well back from a creature and attack it.
   *Should:* the same thin pale line flies from you to the creature, and the damage number appears over it when the line arrives.
   *FAIL:* no line, or the number appears instantly.
6. Type `/summon 5 normal` until a creature's panel lists **Sword**, stand about eight steps away from it — well outside biting distance — and let it come at you.
   *Should:* it can jump at you with **Lunge** from that distance instead of walking all the way in first.
   *FAIL:* it never casts anything until it is close enough to bite.
7. Press **T**.
   *Should:* the tag near the bottom of the screen reads **AUTO-PILOT — press S to stop**.
   *FAIL:* it reads AUTO-COMBAT.
8. **Cannot be done by tester** — with a second player signed in, stand where you can see each other and have them type `/weapon sword`.
   *Should:* a sword appears in their hand on your screen within a second, and changing or removing it changes what you see.
   *FAIL:* their hands stay empty whatever they equip.
9. **Cannot be done by tester** — with a second player signed in, have them shoot a creature with a Bow while you watch.
   *Should:* you see the same line fly from them to the creature.
   *FAIL:* you see the creature take damage with nothing flying at it.
10. Type `/summon 5 normal` until a creature's panel lists **Sword**, turn invincibility off, and let it cast **Retribution** at you (its five strikes land a fifth of a second apart).
   *Should:* the five white numbers appear over you one after another AND a big yellow number sits above them, climbing with each strike, then fading a second after the last one.
   *FAIL:* only the white numbers, with no yellow total — which is what this step exists to catch.
11. Let two creatures hit you with a multi-hit skill at the same time.
   *Should:* two separate yellow totals, one per creature.
   *FAIL:* one yellow number adding both creatures' damage together.
12. Stand poisoned (or with a Brace bill due) while a creature lands a multi-hit skill on you.
   *Should:* the poison's own coloured numbers stay out of the yellow total — it counts only the skill's strikes.
   *FAIL:* the yellow total jumps by the poison tick's amount.

### 11. Dash trails and the stop previous on replay setting

1. **Without Play mode** — open the VFX Composer, pick any effect step that plays at a spot or rides a character.
   *Should:* a **stop previous on replay** checkbox under **looping**. On **skill_dash** it is unticked, so Blade Dance keeps drawing its star.
   *FAIL:* no such checkbox.
2. Tick **stop previous on replay** on a test step that rides you, play that effect twice in a row within a second.
   *Should:* the first copy vanishes the instant the second starts; with it unticked, both show and each fades on its own.
   *FAIL:* ticked and both still show, or unticked and the first vanishes.
3. Equip **Lunge** and **Lock Blades**, press Lunge on a creature, and keep attacking so Lock Blades sends you lunging again.
   *Should:* any trails that overlap in time sit on top of each other along the path you actually travelled — never two copies side by side.
   *FAIL:* two trails running side by side a short distance apart.
   *Status:* 2026-09-17 — seen by the user: two trails side by side. Measured: 8 of 78 trails started up to 1 unit to the side of the body they follow, because the dash had already moved the body by the time the trail started, and the trail kept that gap for its whole life. Fixed so a trail keeps only the height difference; not yet watched in play.
4. Press **Blade Dance** on a group of creatures.
   *Should:* the dashes leave the star shape on the ground.
   *FAIL:* only one line at a time, or no star.

### 12. The training dummy's health

1. Type `/dummy` and look at the dummy's health bar straight away, before hitting it.
   *Should:* the bar is full.
   *FAIL:* the bar is empty or nearly empty.

### 13. One extra skill slot on every creature

Before starting, type `/log cast on` in chat.

1. Type `/summon 3 normal skill.sword.onslaught` and pick one whose panel lists **Bow** or no weapon (summon again if none does). Fight it with invincibility off for 60 seconds, then search `logs/server.log` for `weighed its skills` on lines naming it.
   *Should:* the chat reply ends `with extra skill skill.sword.onslaught`; it casts **Onslaught** at you (its name floats over its head), and its log lines list `skill.sword.onslaught`.
   *FAIL:* it never casts Onslaught with full MP, or its log lines list a different skill.
2. Kill that creature, wait for it to come back, and fight it again for 60 seconds.
   *Should:* it still casts **Onslaught**, and its new log lines still list `skill.sword.onslaught`.
   *FAIL:* after coming back its lines list a different extra skill.
3. Type `/summon 1 normal skill.ember_bolt` (a skill that is not published) and fight it.
   *Should:* it is summoned, and it casts **Ember Bolt** at you.
   *FAIL:* the command is refused, or it never casts Ember Bolt with full MP.
4. Type `/summon 1 normal skill.does_not_exist`.
   *Should:* the reply is `No skill with the id 'skill.does_not_exist'.` followed by the usage line, and no creature appears.
   *FAIL:* a creature is summoned.
5. Type `/summon 5 normal`, fight each creature holding a **Bow** or nothing for a while, then search `logs/server.log` for `weighed its skills`.
   *Should:* each of those creatures lists one skill, and not all of them list the same one.
   *FAIL:* a Bow or unarmed creature lists no skill across several fights where its extra skill is not a passive, or all five always list the same skill.

### 14. Bosses drawn whole, with the normal health bar

1. **Passed** — Type `/summon 5 boss` and look at each boss while it stands still.
   *Status:* 2026-09-18 08:46, watched in the editor — SquidRed, TenguRed, TenguBlue, GiantRacoon, GiantRacoonGold, GiantSpirit, GiantFlam and DemonCyclop2 all drawn whole, feet on the ground. A boss summoned on the far side of the tavern roof from the camera is hidden by the roof, like any creature standing there.
   *Should:* each shows its whole creature, feet on the ground, playing a short standing animation.
   *FAIL:* only a small square corner of a picture shows, the picture is cut off at an edge, or the creature floats above or sinks into the ground.
2. Walk away so the bosses follow you, and watch them move.
   *Should:* a boss with legs plays a walking animation, and a slime, frog or raccoon boss plays a hopping one; it switches back to its standing animation when it stops.
   *FAIL:* a moving boss keeps playing its standing animation (GiantFlam and GiantSpirit have no walking picture, so for those two only that is correct), or it jumps in size when it starts or stops moving.
3. **Passed** — Type `/summon 1 normal` beside a boss, and hit both once.
   *Status:* 2026-09-18 08:46, watched in the editor — every boss bar the same width as the Bamboo's and the Slime3's, shown before any hit.
   *Should:* the boss's health bar is the same size as the normal creature's; the boss still shows its name under the bar, and its bar shows before you hit it.
   *FAIL:* the boss's bar is wider or taller than the normal creature's.
4. Type `/summon 20 boss` twice and look at every boss. Needs the game server REBUILT first: the server build from 2026-09-17 20:40 still has both dragons in its roster, and a dragon it picks has no body on the game side, so it would stand invisible.
   *Should:* no dragon made of separate body parts (DragonBlue, DragonGreen) appears.
   *FAIL:* a DragonBlue or DragonGreen appears.

### 15. GMCP — an AI plays the built game as text (`MT-38`)

1. **Passed** — With the four services up, run `tools/agent-play/game_link.py launch`, then `login alpha@test.local testpass123`, `play Tessa`, and `say hello` while you stand at the town spawn in the editor.
   *Should:* Tessa appears beside you and her line shows in your chat; her `look` lists you under **players** with the right distance.
   *FAIL:* she never appears, her line never shows, or her players list is empty while you stand next to her.
   *Status:* 2026-09-17 — three players (Alpha in the editor, Tessa on port 7790, Tester on port 7791) met at the town spawn and read each other's chat.
2. **Passed** — Type `/tp field_11`, then `/tp town_0`, and try to walk.
   *Should:* you land on the town's Start spawn spot at (-1.6, -1.8) and can walk away.
   *FAIL:* you land near the map's centre point, sunk into the ground, and every step is refused.
   *Status:* 2026-09-18 — fixed and re-tested on a fresh client build: two full field_11 ↔ town_0 round trips, every arrival logged "placed in 'town_0' at spawn 'Start' (-1.58, 0.00, -1.81)" and walking worked each time. Cause: the map being left is still loaded for the frame the new map arrives in, so the arrival search saw BOTH maps' "Start" spots and sometimes chose the field's (0,0) inside the town. Arrival now only considers the new map's spots and ground.
3. **Failed** — Type `/tp field_15` and try to walk.
   *Should:* you can walk away from where you land.
   *FAIL:* every step is refused.
   *Status:* 2026-09-17 — field_15's Start spawn at (0,0) is inside a tree's collider (Deciduous_tree2, also Rye and Oak_tree3 named), so anyone teleported or respawned there is stuck until they teleport away.
4. **Passed** — In the text client, `cast` Committed Strike at any creature.
   *Should:* the skill lands and the client answers with the screen.
   *FAIL:* the client process dies.
   *Status:* 2026-09-18 13:00 — on the rebuilt client that skips all drawing, a tester cast it (`cast Committed Strike`, `m3 119`, the server logged the kill) and the client was still listening 5 s later with a clean log. Earlier that morning the old client, which still tried to draw with no window, died 4 times out of 4 within a second of the cast, in its drawing thread.
5. In the text client, enter `town_0` with creatures standing near the spawn and `look` a dozen times over a minute.
   *Should:* every creature within 25 m is listed with its name, health and distance for as long as it stands there.
   *FAIL:* a creature that is hitting you is missing from the list.
   *Status:* 2026-09-18 — not settled. In one run a DemonCyclop hitting Tessa from melee range was never listed, a freshly summoned KappaGreen was never listed, and in `field_11` the list drained from 33 creatures to none in 2.4 seconds with no map change; in three later runs the lists stayed full. A creature dropped from the list still hits you and its damage numbers still draw.
   *Status, 2026-09-18 14:10:* **not a fault in the list — the player is falling out of the world.** A debugger reproduced it and measured 22,326 units down in 123 s at a frozen x/z; the list only shows what is within 25 m, and 25 m down takes about three seconds, which is exactly when it empties. It is the player's own machine that falls, it happens when you WALK to a field's centre as well as when you teleport there, and it hits `field_11`, `field_12` and `field_0`. See the new step 11 below — the list itself is working.

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
7. Send `cast 3 m2` (Lunge) at a creature within reach, then `pickup g1` on anything it drops, `go d2` through a door, `talk n1` to a villager, `use 1` on a bag item, `equip`/`unequip`, `stat STR`, `revive` after a death.
   *Should:* each answers with one line saying how it ended (`cast Lunge`, `picked up …`, `new map field_11`, the villager's lines, `used …`, `equipped …`, `spent…`/the stats line, `revived`).
   *FAIL:* a command answers with no reason line, or a wrong one.
   *Status:* 2026-09-18 15:00 — tester: `cast Lunge` / `m2 68` with XP and gold counted and the creature gone; `used Grilled Fish` (state `Grilled Fish 118s`); `unequipped Weapon`, `equipped Sword`; `stat STR` STR 19→20 (it cost 3 points, as the character sheet's rising cost says); `go d2` → `new map field_11` with ids restarted; `talk n1` returned the villager's two lines; `revived` / `not dead`; `wait 45` while dead took 45.09 s; `unknown id 'd9'`, `no hotbar skill '9'`. **Pickups took two goes to get right:** the tester found `picked up Bow` answered for a Bow that had vanished on its own (a kill's drops expire after 60 s); the first fix then went the other way and said `still on the floor: Mana Draught` while the bag had already gained it. Both settled by asking the bag first: watched on the final build, `picked up Grilled Fish` and `picked up Padded Vest`, both in the bag afterwards. Talking to a villager now answers like every other action (a line, then the screen) instead of being the only command with no screen — built, and watched only refusing an id in a map with no villagers.
8. **Failed** — Enter `field_11` from town through its door as a weak character, die there, `revive`, then `go d1`.
   *Should:* you walk away from field_11's Start spot.
   *FAIL:* every step is refused.
   *Status:* 2026-09-18 14:10 — tester: every step refused, the client naming `Tavern_LOD0` as what is in the way, at (-0.2,-2.0) while the screen said field_11; a creature stood half a metre away for 8 minutes and never hit him. The debugger settled where it comes from: **field_11 carries its own tavern** (every field has one; the town has none in that map file), and the server had him correctly in field_11 the whole time — so no map leaked into another. He was standing a metre above the floor, on the building. Whether the building is also what stops the creature's blows is unmeasured; the test is to park a player there with the hit log on and see whether the server writes a refusal or nothing at all.
9. Send `say /tp field_10` on its own.
   *Should:* the answer waits for the hang-up and re-dial and says `said; new map field_10` with the new map's screen (a command that moves nobody, such as `/log all on`, answers `said` after two seconds).
   *FAIL:* the answer shows the old map's screen half a second after the say.
   *Status:* 2026-09-18 14:40 — watched: `say /tp field_11 w0_from_town_0` answered `said; new map field_11` with the new map's screen. A command that moves nobody answers `said` after two seconds.
10. `cast 3 d1` (a door as the target).
   *Should:* `a skill is aimed at a creature (m#), a player (p#), me, or a spot (x z) — not 'd1'`.
   *FAIL:* the character walks toward the door for 15 s.
   *Status:* 2026-09-18 14:40 — watched: `cast 3 d1` answers `a skill is aimed at a creature (m#), a player (p#), me, or a spot (x z) — not 'd1'` and nobody moves.
11. **Failed** — Stand at a field's centre (`say /tp field_11`, or walk to (0,0) in `field_11`, `field_12` or `field_0`) and send `ground`, then `wait 3` and `ground` again.
   *Should:* `ground` names the field's own floor below you and says you are grounded, and your height does not change.
   *FAIL:* `ground` says IN THE AIR with nothing below you, and your height falls by tens of units between the two reads.
   *Status:* 2026-09-18 14:30 — **run, and it answered.** At `field_11`'s Start: `IN THE AIR`, nothing below for 500 units. At both of that map's door spawns: standing on `Ground_Floor`, the 42 × 1 × 42 floor box, at 0.02 — the floor is present and works. Walking in from a door you are stopped 1.5 m short of the centre, on the floor, touching two taverns, four trees and a sunflower. `field_0`'s Start: 30 units down within 3 seconds. `field_10`'s Start leaves you 0.56 up on its props. So the arrival spot is inside the scenery in every field; moving those spots clear of the props is an authoring decision and is left to you.

### 17. Every map's centre arrival point removed

**Rebuild and restart first:** the game server build (the maps are inside it), then the game server process, then the game you play. The character server and the account service need neither.

1. Type `/tp field_11`, then `/tp field_12`, then `/tp field_0`, and after each one try to walk.
   *Should:* you land on the ground beside that map's door and can walk.
   *FAIL:* you fall out of the world, or every step is refused.
2. Walk through the door from town to `field_11` and back.
   *Should:* both arrivals put you at the doorway exactly as before — door arrival points were not touched.
   *FAIL:* you arrive somewhere else, or inside something.
3. Make a brand-new character and enter the world.
   *Should:* you start in town, beside the field_11 doorway, standing on the ground.
   *FAIL:* you start inside the fountain or the plaza props, or you fall.
4. Die in a field and press Respawn.
   *Should:* whatever your new respawn rule says. **Until that rule exists this is unanswered by design:** a map with door points puts you at the first one, and the 23 maps with no arrival point at all leave you standing where you already were.
5. Re-import a painted map, or regenerate one from its recipe, then open it and search it for an arrival point at its centre.
   *Should:* none is created; only the door ones the World Creator makes.
   *FAIL:* a new Start point appears at the map's middle.

### 18. Closing the game no longer tries to reconnect

**Rebuild and restart first:** the game you play (the change is in the client). The game server build, the game server process, the character server and the account service need none of it — nothing the server runs was touched.

1. Sign in, get into the world, then close the game — the red button on its window, or **⌘Q**.
   *Should:* the window closes and nothing else happens. The client's own log file ends with the shutdown lines and does NOT contain `lost the world — retrying (1 of 3)` after your last move.
   *FAIL:* that retry line is written as the game closes, or "Reconnecting…" flashes over the screen on the way out.
   *Where to read it:* the client writes `~/Library/Logs/DefaultCompany/unwritten/Player.log`, and keeps the run before it as `Player-prev.log`.
2. Close the game from the sign-in screen, before entering a world.
   *Should:* the window closes, and the log has no retry line.
   *FAIL:* a retry line appears.
3. While in the world, stop the game server, wait, and watch the client.
   *Should:* "Reconnecting…" appears and the log counts `lost the world — retrying (1 of 3)`, then 2 and 3, then gives up — a real drop is untouched by this change.
   *FAIL:* nothing appears, or it gives up immediately.
4. Walk through a door from town to a field and back.
   *Should:* you arrive on the other side with no "Reconnecting…" at any point.
   *FAIL:* the notice flashes during the move, or the move loops.
5. After closing the game, check whether macOS reports a crash.
   *Should:* no crash report window, and no new file for `unwritten` in **Finder ▸ Go ▸ Go to Folder ▸ `~/Library/Logs/DiagnosticReports`** dated after your quit.
   *FAIL:* a new crash report is written. **This step is the open question:** the retry at shutdown is a real fault and is now fixed, but whether it is what made the process crash on the way out is not settled — the crash itself is inside Unity's own shutdown code, with none of our code on it.

### 19. Pointing at somebody shows who they are, how they are, and what is on them

**Rebuild and restart first — all four, and here is why each:**

1. **The game server build** (`builds/server/unwritten-server.app`) — **REBUILD needed.** The server is what answers "what buffs are on the unit this person is pointing at", and that answering code is new. Without the rebuild the running server has no idea what the question means and no icons will ever appear.
2. **The map server process** — **RESTART needed**, after that rebuild. A running one keeps serving the old build.
3. **The character server** (`charserver/`) — **not needed.** Nothing it does was touched.
4. **The account service** (`accounts/`) — **not needed.** Nothing it does was touched.

**Also:** the client must be rebuilt for a build test, and the persistent `Systems` scene has been saved with the new readout on it — that save is already done, nothing to click.

1. Stand near another player and rest the cursor on them.
   *Should:* a small plate appears just above their head with four things stacked — their name, a health bar, a mana bar under it, and their buff icons wrapped underneath. Both bars are as wide as that character's body.
   *FAIL:* nothing appears, the plate sits on their face or far above them, or the bars are a fixed width unrelated to the body.
2. Rest the cursor on a creature.
   *Should:* the same plate, with that creature's name and bars — a big creature gets a long bar, a small one a short bar.
   *FAIL:* no plate on creatures, or every creature's bar is the same width.
3. Point away at empty ground.
   *Should:* the plate disappears at once.
   *FAIL:* it stays, or it lingers over nothing.
4. Keep the cursor resting on a player, and have that player cast a buff on themselves **while you keep pointing**.
   *Should:* the icon appears on the plate without you moving the cursor, and disappears on its own when the buff runs out. This is the whole point of the change — a buff list that stays true while you look at it.
   *FAIL:* the icon only appears after you point away and back, or never.
5. Do the same on a creature: poison or slow it while pointing at it.
   *Should:* the icon appears and later clears, with the cursor never moving.
   *FAIL:* the creature's icons stay empty on a real client. **This is the step that proves the hole is closed** — before this change a creature's buffs reached no watching machine at all, and only ever showed on the machine running the world.
6. Give one unit many buffs at once.
   *Should:* the icons wrap onto a second and third row under the bars, and the plate grows upward rather than down over the character.
   *FAIL:* icons run off the sides, overlap, or push down over the body.
7. Click a creature to pick a fight with it.
   *Should:* the top-center readout appears as before, with its name, element, bars, states **and the list of what it is carrying**.
   *FAIL:* the gear list is missing from the top-center readout.
8. Rest the cursor on a creature and look at the **top-right** of the screen.
   *Should:* nothing is there. That hover readout has been removed on purpose; the floating plate replaces it.
   *FAIL:* the old top-right panel still appears.
9. Point at a player, then at a creature, then back, several times quickly.
   *Should:* the plate re-points each time and always shows the right name and numbers.
   *FAIL:* it shows the previous unit's name, health, or icons.
10. **Open question, not a step to pass or fail:** hovering a creature that has already been damaged shows its health **twice** — once on the small bar under its feet, which is unchanged, and once on the new plate above its head. Whether the feet bar should hide while the plate is up is a design decision nobody has made yet.
