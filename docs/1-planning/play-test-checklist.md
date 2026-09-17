---
status: open
owner: vince
last-updated: 2026-09-17
tags:
  - planning-production
---

# Play-Test Checklist

Everything built but never watched working, in two lists: the checks that can be settled **without Play mode** — by searching the game's code and saved files, with no server and nothing rebuilt — and the checks that need the game running **in Play mode**. Every step says what to do, what should happen, and what counts as a **FAIL**. A step that is blocked by something other than the rebuild below says **Cannot be done now** and why; a step the tester cannot run says **Cannot be done by tester**. The steps come from thirteen batches:

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

1. **Game server build — REBUILD.** Game code changed in batches 1 to 5, 7 to 10, 12 and 13 (the server decides every creature's extra skill and runs `/summon`), and batch 10 adds a new message that says an arrow was fired, a new value that carries what a person is wearing, and two new pieces on the message that carries a blow to a person, and batch 6 adds four new items, and a server only runs the code and items it was built with. The message that carries a blow changed shape in batch 5, so an old server and the new game cannot understand each other's blows. In Unity, let the editor finish compiling, then run `tools/build-game.sh server`.
2. **Game server process — RESTART** after the rebuild. Stop it with `pkill -f 'unwritten-server.app/unwritten'`, check `lsof -nP -iUDP:7777` prints nothing, then start it with the command under *The game server ▸ Running it* in [Starting the Network](../2-systems/networking/starting-the-network.md).
3. **Character server — no restart.** Nothing inside it changed, and the one string both servers must agree on did not change.
4. **Account service — no restart.** Nothing inside it changed.
5. **The game you play — REBUILD** if you play the built game: `tools/build-game.sh client`. Batches 1 and 2 changed what your own screen draws, batch 6 adds items your screen must know to draw them, batch 7 changed the message that describes a creature, batch 8 changed the message that carries your character sheet, batch 9 changed how your screen draws a creature's cast, and batch 10 adds the message that says an arrow was fired, the value that says what a person is wearing, and two new pieces on the message that carries a blow to YOU — an old game and a new server cannot read each other's creatures, sheets, arrows or blows on a person, and an old game draws no arrow, no weapon in anybody else's hand and no rising total over you. Batch 11 changed only what your screen draws, so it needs this rebuild and nothing on the server side. Batch 13 changes nothing your screen draws — only the `/help` line for `/summon`. Playing from the editor needs nothing extra: the game reads the character server's address from its own settings file.

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

1. Type `/summon 5 normal` and hover the mouse over each creature in turn, then attack one of them.
   *Should:* in the panel at the top right while hovering — and in the one at the top centre while fighting — below the creature's name and states, one line per piece it wears: the item's name in its rarity colour and a drop chance — Common **20%**, Uncommon **12%**, Rare **6%**, Epic **3%**, Legendary **1%**. Some creatures wear nothing.
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
5. Find a creature whose hover panel lists a **Wooden Buckler**, stand in front of it with invincibility off, and hit it about 60 times with basic attacks.
   *Should:* now and then a hit does no damage at all — about 1 hit in 20.
   *FAIL:* every one of the 60 hits does damage.
6. Type `/dummy` and hover over the dummy.
   *Should:* its hover panel lists no gear.
   *FAIL:* any gear line in the dummy's hover panel.
7. Kill a creature whose hover panel lists gear, wait for it to come back, and hover over it again.
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
