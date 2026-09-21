---
status: open
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# Play-Test Checklist — Not done yet

Steps nobody has run. This file also holds the batch list and the rebuild-and-restart header the other three link to. Every step keeps the section and number it had in the single checklist, so a step referred to elsewhere as "step 2 of section 10" is still that step, and a step moves between these files as its verdict changes. The four files are [passed](passed.md), [failed](failed.md), [on-going](on-going.md) and [not done yet](not-done-yet.md).

Everything built but never watched working, in two lists: the checks that can be settled **without Play mode** — by searching the game's code and saved files, with no server and nothing rebuilt — and the checks that need the game running **in Play mode**. Every step says what to do, what should happen, and what counts as a **FAIL**. A step that is blocked by something other than the rebuild below says **Cannot be done now** and why; a step the tester cannot run says **Cannot be done by tester**. The steps come from twenty batches:

1. **The bag's columns** (2026-09-14).
2. **Your machine works out no game number** (2026-09-14) — the server sends every number and your screen only draws it.
3. **The gear shape** (2026-09-13) — [Action Plan](../action-plan/on-going.md) `MT-3` and its subtasks `ST-3-K`, `L`, `N`, `O`, `P`, `S`.
4. **The skill and server plumbing** (2026-09-03) — [Action Plan](../action-plan/on-going.md) `MT-1`, the half of its list that was never run.
5. **Fixes from the 2026-09-14 play-test** — one-hit kills, the endless fall, the removed second sound player and creature hit tint.
6. **The first armour pieces** (2026-09-15) — [Action Plan](../action-plan/on-going.md) `MT-7`: Leather Cap, Padded Vest, Traveller's Cloak and Wooden Buckler.
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
19. **Pointing at somebody shows who they are, how they are, and what is on them** (2026-09-18) — one floating plate over any player or creature under the cursor, with name, bars, live buff icons and, for a creature, its gear; the old top-right hover panel is gone.
20. **Repository audit fixes** (2026-09-20) — every creature now carries its authored level and stats (they all loaded at level 1 with every stat at 1); the level-up is worked out on the server and your screen copies the finished level, points and experience; the account service no longer answers for characters at all, which closes the door through which a signed-in player could overwrite their own save; the servers' shared secret is no longer the word "change-me" and the character server refuses to start without a real one; sign-ups and sign-ins are limited to ten a minute per address; the leaderboard reads twenty index entries instead of every character; entering the world asks only the map server that holds you; the door players arrive through no longer shuts itself on a hiccup; the main shadow map is 1024 instead of 4096, characters are animated on the graphics card, and eight unused packages are gone; the ground tiles are compressed (about 71 MB off the build) and three of them no longer ship twice; every plant lost its collider and its shadow, 28 hard props are batching-static and all foliage is occludee-static; the simplified copies of 17 prop families are wired into level-of-detail groups; saving a scene no longer moves portals on the save before Play and every snap can be undone; and opening the project only WARNS when effect timings drift — the write is a menu item.

21. **Gameplay System V4 — forging, quality, Ink and mastery** (2026-09-20) — gear is no longer dropped or rolled: it is FORGED at a station in town from materials, FOLDED with the material band of its own grade, and QUENCHED to move that grade one rung on the F-to-A ladder, spending every fold each time. Rarity is gone from the game — labels, colours and all. Stats are written onto a piece with Ink. Weapons no longer carry skills: a character LEARNS skills by practising a weapon, and both that Weapon Mastery and each skill's own Skill Mastery are earned by fighting and capped by the grade of the weapon in hand. The Ring became part of the character and its slot is gone. Creatures stop dropping equipment. [Gameplay System — V4](../../2-systems/gameplay-system-v4/README.md).

---

## Without Play mode

Nothing needs rebuilding or restarting for these.

3. **Every baked creature carries its stats** *(batch 20)*. After the re-bake, open any creature prefab under `1_Authored/Prefabs/Mobs` as text and look inside its stat block component for an `inputs:` entry.
   *Should:* every one of the 84 has it, with the level and six primaries from its creature data file.
   *FAIL:* a creature prefab with no `inputs:` entry, or one whose level is 1 while its data file says otherwise.
4. **Sign-in is limited per address** *(batch 20)*. With the account service running, send eleven sign-in requests in one minute with a wrong password: `for i in $(seq 1 11); do curl -s -o /dev/null -w "%{http_code} " -X POST -H 'content-type: application/json' -d '{"email":"nobody@example.com","password":"wrong"}' http://127.0.0.1:8080/sessions; done`.
   *Should:* the first five answer 401, the sixth to tenth answer 429 "too many attempts" (the one-minute lockout on that address and email), and the eleventh 429 "Rate limit exceeded". `/health` still answers 200 throughout.
   *FAIL:* all eleven answer 401, or `/health` is refused.
5. **The character server refuses a missing secret** *(batch 20)*. Run `SERVER_SECRET= charserver/run.sh`.
   *Should:* it prints that `SERVER_SECRET` is not set, names `accounts/.env`, and quits without listening.
   *FAIL:* it starts and prints "using the default SERVER_SECRET".
6. **A player can no longer write their own save** *(batch 20)*. With the account service running, send a save to it: `curl -s -o /dev/null -w "%{http_code}\n" -X PUT -H 'content-type: application/json' -d '{"progress":{"version":9,"level":9999}}' http://127.0.0.1:8080/characters/00000000-0000-0000-0000-000000000000/progress`, and the same to the character server on port 8081.
   *Should:* 404 from the account service (the route is gone) and 405 from the character server (it never had a player-side write). Signing in still works.
   *FAIL:* 401 from the account service — that means the route still exists and only wants a token.
7. **The ground tiles are compressed and ship once** *(batch 20)*. After the refresh, select `1_Authored/Art/GroundTiles/ground_soil.png` in the Project window and read the preview's footer in the Inspector.
   *Should:* it names a compressed format (DXT1/BC) at about 2.7 MB, not RGB24 at 16 MB; and `1_Authored/Prefabs/Props/Textures/` no longer contains ground_soil, ground_road, ground_grass or ground_rock.
   *FAIL:* the footer still says an uncompressed format, or the import settings show Compression: None after Apply.
8. **Plants carry no collider and cast no shadow** *(batch 20)*. Open any prefab under `1_Authored/Prefabs/Props/Plants` as text.
   *Should:* four blocks only (object, transform, mesh filter, mesh renderer), with `m_CastShadows: 0`.
   *FAIL:* a MeshCollider block, or shadow casting on.
9. **Opening the project writes nothing** *(batch 20)*. Open the project and, without touching anything, look at the console and at `3_Data/Resources/EffectLibrary.asset` in source control.
   *Should:* at most a yellow warning naming how many effect timings drifted; the file is unmodified.
   *FAIL:* the effect library shows as modified with nobody having run the Re-measure menu item.
10. **The leaderboard still answers, in the same order** *(batch 20)*. `curl -s http://127.0.0.1:8081/stats`.
   *Should:* the same twenty-or-fewer names as before the audit, best first.
   *FAIL:* an error, or a different order than lifetime experience then level then name.

## In Play mode

GM commands are typed into chat and need a GM account — see [Starting the Network ▸ GM accounts](../../2-systems/networking/starting-the-network.md).

### Before you start — what to rebuild and restart

0. **Batch 21 needs all four of these, and a re-bake.** The save's shape changed (masteries, learned skills, a
   piece's grade and its written stats), so **both servers must be rebuilt and restarted together**: the string
   they compare is now `unwritten-character-v13`, and one side left on v12 refuses the other. Run **Tools ▸
   unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs** as well — every creature gained a Weapon Mastery, authored as its
   level, and a creature baked before that knows none of its weapon's skills. The UI shell was rebuilt with the
   station window on 2026-09-21 (the saved HUD prefab and the Systems scene both carry it now), so that step is
   done. **Every character's gear loads as Raw +0 with nothing written on it**: refines bought
   under the old rule are not carried over, and nothing wearable is handed out at the start any more.

1. **Game server build — REBUILD.** **Batch 20 first needs three things in the editor, in this order: (a) a refresh (Ctrl+R) — it resolves the upgraded live-link package, compiles the new code and reimports the nine ground tiles with compression; (b) Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs — or every creature stays at level 1; (c) the level-of-detail wiring, run through the live link as `LodGroupWirer.WireAllCore()`.** Then: then batch 20 changed the message that carries your experience (it now carries the finished level, points and ring), so an old server and a new game cannot read each other's rewards. Game code changed in batches 1 to 5, 7 to 10, 12 and 13 (the server decides every creature's extra skill and runs `/summon`), batch 14 removes the two dragons from the roster the server picks a boss from, and batch 10 adds a new message that says an arrow was fired, a new value that carries what a person is wearing, and two new pieces on the message that carries a blow to a person, and batch 6 adds four new items, and a server only runs the code and items it was built with. The message that carries a blow changed shape in batch 5, so an old server and the new game cannot understand each other's blows. In Unity, let the editor finish compiling, then run `tools/build-game.sh server`.
2. **Game server process — RESTART** after the rebuild. Stop it with `pkill -f 'unwritten-server.app/unwritten'`, check `lsof -nP -iUDP:7777` prints nothing, then start it with the command under *The game server ▸ Running it* in [Starting the Network](../../2-systems/networking/starting-the-network.md) — **that command now reads `SERVER_SECRET` out of `accounts/.env`; a server started with the old word "change-me" is refused by the character server.**
3. **Character server — RESTART.** Batch 20 changed its code (it now remembers which map server holds each character) and it now REFUSES to start unless `SERVER_SECRET` is set in `accounts/.env` — it is, and `charserver/run.sh` reads it from there; a value typed on the command line is no longer needed. The one string both servers must agree on did not change.
4. **Account service — RESTART.** Batch 20 removed its character routes (the player-writable save door), added the per-address limit on sign-ups and sign-ins (a new dependency was installed) and rekeyed the wrong-password lockout. Also run `npm run migrate` in `accounts/` if it has not run since 2026-09-20 — it adds the leaderboard index (already applied on this machine).
5. **The game you play — REBUILD** if you play the built game. Batch 20 changed the experience message (see 1), turned on GPU skinning, shrank the shadow map and removed eight packages — Unity resolves the package change and rewrites its own `packages-lock.json` the first time the editor regains focus; that is expected. **Batch 17 changed the MAPS, which live in both builds — so the game server must be rebuilt and restarted for it too, not just this one.**: `tools/build-game.sh client`. Batch 16 is entirely inside the windowless client (already rebuilt on 2026-09-18 12:30, nothing on the server side changed shape). Batches 1 and 2 changed what your own screen draws, batch 6 adds items your screen must know to draw them, batch 7 changed the message that describes a creature, batch 8 changed the message that carries your character sheet, batch 9 changed how your screen draws a creature's cast, and batch 10 adds the message that says an arrow was fired, the value that says what a person is wearing, and two new pieces on the message that carries a blow to YOU — an old game and a new server cannot read each other's creatures, sheets, arrows or blows on a person, and an old game draws no arrow, no weapon in anybody else's hand and no rising total over you. Batch 11 changed only what your screen draws, so it needs this rebuild and nothing on the server side. Batch 13 changes nothing your screen draws — only the `/help` line for `/summon`. Batch 14 changes how your screen draws a boss and the saved boss creatures it loads. Playing from the editor needs nothing extra: the game reads the character server's address from its own settings file.

**Never refresh the editor while a play session is running** — its automatic refresh is switched off, so an edit waits for a refresh, and a refresh mid-play reloads the game's code and ends the session.

---

### 5. Fixes from the 2026-09-14 play-test

6. Type `/dummy`, hit the dummy a few times, then `/killall`.
   *Should:* your gold does not change.
   *FAIL:* gold goes up when the dummy dies.

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

2. Kill them all.
   *Should:* your gold goes up as before, and anything they gave up lies on the ground beside the bodies — a sparkle, with its name shown while you hold **Alt**.
   *FAIL:* an item lands in your bag without you picking it up.
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

Before starting, type `/log cast on` and `/log passive on` in chat. Only the Sword has skills written for it, so only a creature holding a **Sword** casts any of them. **Batch 21 changed where those skills come from:** a creature now knows every skill of the weapon in its hands up to its own Weapon Mastery, which is authored as its level — so a level-20 creature with a sword knows the sword skills that open at 20 and below, instead of a kit of three or four rolled onto the weapon. Every creature also carries one extra skill (batch 13), so any creature may cast that one.

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
12. Type `/summon 5 normal shadow` until a 3D creature's panel lists **Sword**, and fight it until it casts **Onslaught** (its damage numbers start landing about twice as fast as its normal swings).
   *Should:* for as long as those fast numbers land, its sword swings once for every number — the swing is visibly faster than its normal attack.
   *FAIL:* its swing stays at normal speed, or it restarts the swing before finishing, while the numbers land fast.

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

2. Walk away so the bosses follow you, and watch them move.
   *Should:* a boss with legs plays a walking animation, and a slime, frog or raccoon boss plays a hopping one; it switches back to its standing animation when it stops.
   *FAIL:* a moving boss keeps playing its standing animation (GiantFlam and GiantSpirit have no walking picture, so for those two only that is correct), or it jumps in size when it starts or stops moving.
4. Type `/summon 20 boss` twice and look at every boss. Needs the game server REBUILT first: the server build from 2026-09-17 20:40 still has both dragons in its roster, and a dragon it picks has no body on the game side, so it would stand invisible.
   *Should:* no dragon made of separate body parts (DragonBlue, DragonGreen) appears.
   *FAIL:* a DragonBlue or DragonGreen appears.

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
3. **The character server** (`charserver/`) — **not needed for this batch** — but batch 20 changed it, so it IS restarted; see *Before you start* at the top.
4. **The account service** (`accounts/`) — **not needed for this batch** — but batch 20 changed it, so it IS restarted; see *Before you start* at the top.

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

### 20. Repository audit fixes

1. Hit an ordinary field creature once with a basic attack.
   *Should:* it survives with a health bar left, and the fight takes several hits. Before the re-bake every creature loaded with 3 health and died to anything.
   *FAIL:* every creature dies in one hit, or its bar is a sliver from the first blow.
2. Fight a boss and then a low creature.
   *Should:* the boss takes visibly more hits and hits visibly harder than the low creature — its data says level 46 against level 3.
   *FAIL:* they fight identically.
3. Kill creatures until you level up.
   *Should:* the sting plays once, the toast names the new level, the burst plays, the XP bar drops to the remainder, the character sheet shows new unspent points, and your health and mana refill to full within a blink. All of it now comes from the server; your machine works out none of it.
   *FAIL:* no toast, an XP bar that does not reset, unchanged points, or a level on screen that differs from what the server logs.
4. As a GM, type `/xp 2000`.
   *Should:* several levels at once — one toast per level, in order, each naming the level reached; the XP bar and points land on the final answer.
   *FAIL:* one toast for several levels, every toast naming the same level, or the toasts out of order.
5. Wear the ring and level it (kills, or `/ringxp 200`).
   *Should:* the ring bar moves, the ring toast names its new level, and the passive draft badge gains one point per ring level.
   *FAIL:* the ring bar stuck, no ring toast, or the badge unchanged.
6. Enter the world, walk out to another map server and back, and log out and in again.
   *Should:* each entry works as before. **Cannot be done by tester beyond that:** whether only ONE map server was asked to let go of you is visible only in the character server's own output, where it used to print a line per map server.
7. Stand under a tree at midday and watch your own shadow.
   *Should:* a shadow with clean edges, as before; the map is a quarter of its old size and covers the same 18 units.
   *FAIL:* blocky, stepped or missing shadows.
8. Watch your character and a townsperson walk.
   *Should:* the same animation as before — the skinning moved to the graphics card and nothing about the pose changed.
   *FAIL:* a frozen pose, stretched limbs, or a mesh that lags a step behind the body.
10. Walk straight through a patch of grass, rye or flowers, then into a wall and a rock.
    *Should:* plants never stop you; walls and rocks still do, exactly as before.
    *FAIL:* you bump into grass, or you walk through a wall.
11. Stand where grass is between you and the sun at midday.
    *Should:* the grass throws no shadow; trees, walls and houses still do.
    *FAIL:* blades of grass with shadows, or a house without one.
12. Walk away from a stone, a tavern or a windmill and watch it at a distance, then walk back.
    *Should:* at distance it swaps to a visibly simpler copy and back to the full one as you approach, with no gap where it vanishes. (Needs the LOD wiring in *Before you start*.)
    *FAIL:* it never simplifies, it pops out entirely at distance, or it draws twice (a full and a simple copy at once).
13. In the editor, drag a portal a little above the ground, press Ctrl+S, then press Ctrl+Z.
    *Should:* the save drops it back onto the ground; the undo lifts it again. Then press Play with a portal raised: it stays raised — Play's own save never snaps.
    *FAIL:* no undo entry for the snap, or a portal moving when you press Play.

### 21. Forging, quality, Ink and mastery

Nothing about this batch can be checked without the rebuilds above, and one thing has to be set up by hand first:
**no townsperson is a smith, a loom or a tannery yet.** Stand next to any villager in town and type
`/station smith` to make that one a smith for this session (`/station loom`, `/station tannery` for the others).

1. Press **C**. Look at the equipment list down the side.
   *Should:* there is no **Ring** row at all, and the character sheet's numbers are the same as before this batch.
   *FAIL:* a Ring row, or a sheet number that moved when nothing else changed.
2. Press **K** and look at the ring draft.
   *Should:* the ring's level, its experience bar and its draft work exactly as before, with nothing about wearing one.
   *FAIL:* "No ring equipped", or the draft refusing to open.
3. Kill anything.
   *Should:* ring experience still rises and a ring level still banks a draft point.
   *FAIL:* ring experience stays still.
4. Type `/item copper 20`, then `/item "watery ink" 10` and `/item "coarse salt" 3`. Press **B** and open the **Material** tab.
   *Should:* all three are there, stacked, in the Material tab.
   *FAIL:* "unknown item", or they land in another tab.
5. Right-click the Watery Ink stack.
   *Should:* a **Merge 10 → Thin Ink** row. Click it: the ten Watery Ink are gone and one Thin Ink is there.
   *FAIL:* no Merge row, merging with fewer than ten, or both stacks surviving.
6. Stand next to the villager you made a smith and press **F**.
   *Status:* 2026-09-20 play-test — F did nothing: the HUD prefab had never been rebuilt with the station window in it. Rebuilt and saved on 2026-09-21; not yet watched since.
   *Should:* a **Smith** window opens over the same side-on close-up a conversation gets (step 23), showing a menu
   of five rows and nothing else: **Forge · Fold · Quench · Ink · Recycle**. Each opens its own page in the same
   window with a **← Back** row; Fold, Quench, Ink and Recycle each list what you are carrying that this station
   works on, and the piece you pick carries across pages (marked with a >).
   *FAIL:* the dialogue box opens instead, the window opens standing anywhere else, or every list is on screen at once.
7. Open **Forge**. With 3 Copper, click **Sword   8 Copper · 800g**.
   *Should:* a red line at the bottom of the window AND in the chat: "Needs 8 Copper — you have 3." Nothing is
   taken. Then with 8 Copper and 800 gold (`/gold 5000`), click it again.
   *Should:* a **Raw Sword** appears in the bag AND under "Work on" in the window, already picked, with its fold and
   quench costs shown; the copper and the gold are gone; the window and the chat both read "Forged a Raw Sword for
   8 Copper and 800 gold." Hover it: it reads "Raw" with no numbers of its own. Every later Fold, Quench, Ink and
   Merge answers the same way — one sentence in both places, red when refused, with the numbers.
   *FAIL:* a sword with attack on it, nothing taken, or a sword with no materials paid.
8. Open **Fold**, click the Raw Sword, then **Fold** five times.
   *Should:* each fold takes 1 Copper and 50 gold, the title reads **Raw +1 … +5**, and the sixth fold is refused
   with "Folded as far as this grade allows."
   *FAIL:* a sixth fold, a fold with no copper, or the attack not rising.
9. Open **Quench**, pick the sword, and click **Quench** (10 Copper, 500 gold).
   *Should:* the window says up 80% / unchanged 20% / down 0% beforehand; afterwards the sword is **F +0** or
   **Raw +0**, and every fold is gone either way.
   *FAIL:* it keeps its folds, it jumps two grades, or it goes below Raw.
10. Type `/mastery sword 7`, then press **J**.
    *Should:* Lunge, Parry, Momentum, Guard Stance and Lock Blades are no longer greyed; the rest still read
    "Sword mastery N" and do nothing when clicked. Bind Lunge, hold the sword, and press it: it fires.
    *FAIL:* a locked skill binds, or an unlocked one is refused when pressed.
11. With the sword in hand, type `/mastery` and read the reply; then take the sword off and read the sheet.
    *Should:* the reply names Sword and the level; with a Raw sword in hand the usable level is held at 10, and
    taking the sword off drops the mastery's stat bonus off the sheet.
    *FAIL:* a Raw sword giving more than level 10, or the bonus surviving with empty hands.
12. Hold a bow (`/weapon bow`) and kill something.
    *Should:* Sword mastery does not move; Bow mastery does.
    *FAIL:* a kill with one weapon raising another's mastery.
13. Kill a creature ten or more levels below you, then one at your own level.
    *Should:* the far weaker one pays no practice at all (watch `/log loot on`); the even one pays about a full point.
    *FAIL:* a grey creature paying practice.
14. Hit a training dummy for a while.
    *Should:* no practice at all — it never dies.
    *FAIL:* practice from a dummy.
15. Before quenching, look for the Salt row under the odds.
    *Should:* with a Salt in the bag, one press adds it and the odds above change to include it; a second press
    leaves it out again. No Salt is ever spent without you turning it on.
    *FAIL:* a Salt disappears from the bag when you never asked for it, or the odds shown do not match the ones
    the quench rolled.
16. Fold the sword to its cap, quench until it is at least **E**, then open **Ink** and click **Write slot 1 with Watery Ink**.
    *Should:* one stat is written onto the piece and shown on its card, the Ink is gone, and the card's "Written"
    line counts up. The window now offers **Replace <that stat> (slot 1)** and **Write slot 2** — you choose which.
    Keep writing until Magic Defence or Crit Damage lands: it must still be on the card after you close and
    reopen the bag, and after a relog. A grade that later falls takes the last written stat with it.
    *FAIL:* two of the same stat on one piece, more written stats than the grade allows, or an Ink spent with
    nothing written.
17. Kill creatures that wear gear.
    *Should:* they drop materials, consumables and gold only — never a weapon or a piece of armour.
    *FAIL:* worn gear on the ground.
18. Press a skill you have practised, over and over, and watch its card.
    *Should:* its damage rises about 1% per skill mastery level, and a Raw sword holds that at level 4.
    *FAIL:* the number never moving, or moving past the cap.
19. Tick a sword passive on in **J**, then hold a bow and fight.
    *Should:* the sword passive does not fire while the bow is in your hands, and it is still ticked when you
    take the sword back.
    *FAIL:* it fires with the bow out, or your tick is cleared by the swap.
20. Hover a better sword in your bag while wearing a worse one.
    *Should:* the preview's attack, attack speed and HP include what your mastery would be worth at the BETTER
    sword's grade.
    *FAIL:* the preview shows the same mastery bonus as the sword you are wearing.
21. Sign in as **Alpha** (a save from before this batch that had Blade Dance, Disarm, Overclock and Challenge on
    its bar) and look at the hotbar.
    *Should:* those four keys are gone — only skills you have learned, or skills that belong to no weapon, survive
    a load. Then bind a learned skill, take the sword off and press it.
    *Should:* the chat shows a dim line saying the skill needs a Sword in hand, and nothing fires. Press a skill
    the Grimoire shows as locked (bind one on a second character with `/mastery sword 3`, then relog on the
    first): the chat line says it is not learned and which mastery level opens it.
    *FAIL:* a kit-era key still on the bar after a load, or a refused press with no chat line.
22. Wear a folded piece (fold a sword to +3, double-click it to wear it) and hover it in the equipment list.
    *Should:* it reads **Raw +3** — the fold count survives the trip to your screen.
    *FAIL:* a worn piece reading +0 while its attack says otherwise.
23. Press **F** on the smith.
    *Should:* the camera swings to the side-on two-shot of you and the smith, you face each other, the smith stops
    walking, you cannot walk, and the Station window opens over it. Esc and the window's **X** both close the
    window, bring the camera back and release the smith. Then talk to an ordinary villager: the same close-up,
    with the HUD hidden and the dialogue box instead.
    *FAIL:* the smith strolls off with the window open, or the camera stays put for a station.
24. Open **Recycle**, pick a **Raw +3 Sword**, and read the row it offers.
    *Should:* **Recycle   → 5 Copper, 475 gold**. Press it: the sword is gone from the bag and the window, 5 Copper
    and 475 gold arrived, and both the window and the chat read "Recycled Raw +3 Sword: got back 5 Copper, 475
    gold." An E +0 sword offers 9 Copper, 5 Bronze, 1,150 gold. A piece with one written stat gives no Ink back;
    with two Watery stats, 1 Watery Ink.
    *FAIL:* the recycled sword still listed, more than half of anything coming back, or Salt coming back.
25. With about thirty pieces in the bag (`/item item.sword` thirty times), open **Fold** and spin the mouse wheel
    over the list.
    *Should:* the list scrolls inside the window, the same way the Grimoire's does; nothing runs off the bottom.
    *FAIL:* rows below the window's edge that no scroll reaches.
26. Create a brand-new character and open the bag's **Material** tab.
    *Should:* empty. Consumables only, in the other tabs.
    *FAIL:* one of every material — Adamant, Sharp Ink, Pure Salt — in a fresh bag.
