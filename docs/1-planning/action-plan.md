---
status:
owner: vince
last-updated: 2026-09-13
tags:
  - planning-production
---

# unwritten — Action Plan

Twelve weeks of work, as tasks. **This is the "what next" list.** [Roadmap to Launch](roadmap-to-launch.md) is the "how far are we" picture — what is built, what is half-built, what is missing, all counted. Neither repeats the other.

**Every fact in this plan was measured on 2026-09-12 or 2026-09-13** by reading the game's own files, its database or its builds.

---

## How to read it

Each week opens with **the goal** — what you can do in the game at the end of it that you could not do at the start. Then its tasks.

Every task carries:

- **A status**, inside the brackets in front of the task: `[DONE]`, `[ON-GOING]`, or empty `[ ]` when it has not started.
- **An ID.** A main task is `MT-N`; a subtask sits indented under its main task as `ST-N-A`, where `N` is the parent's number and `A`, `B`, `C` … count its subtasks in order. An ID never changes and is never reused: a new main task takes the next free number, and a new subtask takes the next free letter under its parent.
- **`[D]` decision · `[A]` authoring · `[E]` engineering.** A decision task is yours alone. An authoring task can be handed to the data-author agent. An engineering task is code.
- **An estimate in days**, as a range. **The range already carries the swing from any decision it depends on** — you do not need to answer anything now for the estimate to hold.
- **Done when** — the concrete check. If you cannot watch it happen or count it, it is not a check.

**Decisions are tasks, never gates.** Each one sits in the week *before* the work that needs it, with a recommendation attached so you can settle it by agreeing. If a decision slips, the work after it still starts — on the recommendation — and the estimate already covers changing course.

**My assumptions, labelled as mine:** a week is five working days, you and me together. The twelve weeks are ordered by dependency, not by calendar. Slipping one pushes the rest; it does not break them.

**Where the weeks are tight, said plainly.** Every week fits inside five days at the low end of its estimates. Three do not fit at the high end: **Week 2** (up to 5.25 days), **Week 4** (up to 5.5) and **Week 10** (up to 6). Those are the weeks where a decision going the long way costs you a day, and they are the three most likely to spill into the next week. Nothing is padded to hide it.

---

## Week 1 — Prove the ground is solid

**Goal:** nothing changes for a player. Everything after this is built on a game we know works rather than one we assume does.

- [DONE] `MT-1` **`[E]` Finish the play-test open since 3 September.** About half its checklist was never run. Start with its one inconclusive item: whether a passive fires when a normal attack lands, and again when something dies. — **2–3 days**
  *Done when:* every line of that checklist reads passed or failed with none unrun, and the passive question has a watched answer either way.

- [DONE] `MT-2` **`[E]` Kill the last descriptions of the deleted equipment window.** The editor-tools catalogue says one tool drops an Inventory/Equipment/Character trio toggled with I, E and C. The window code's own comments still call them a trio and still mention dragging onto a paperdoll. Those stale comments are what misled the first draft of this plan. — **0.5 days**
  *Done when:* no document and no code comment describes an equipment window.

- [DONE] `MT-3` **`[D]` Decide the gear shape.** Six places on the body can hold something — head, armour, garment, weapon, shield and the ring. Only the weapon has any item authored. *Recommendation: fill head, armour, garment and shield — four places — with a low, a middle and a high piece each. Twelve pieces.* — **0.5 days**
  *Done when:* you have written down which places get gear, how many pieces each, and roughly what each does.
  - [DONE] `ST-3-A` **`[D]` Armour gives stats, not skills.** The weapon stays the only item that grants skills.
  - [DONE] `ST-3-B` **`[D]` The two accessory places go.** The ring already fills that role. That leaves head, armour, garment, weapon, shield and the ring.
  - [DONE] `ST-3-C` **`[D]` Any armour piece can carry any of the six main stats — STR, AGI, VIT, INT, DEX, LUK — in any place, and no sub-stats.** The ring handles sub-stats.
  - [DONE] `ST-3-D` **`[D]` Main-stat bonuses are flat, never scaled to level.** +10 STR is +10 at level 1 and at level 99. It stays worth chasing late because the last points a player buys into a stat cost the most, and gear adds on top of the 99 that buying stops at.
  - [DONE] `ST-3-E` **`[D]` Each place's main job is a flat number on each copy of the piece, never scaled to the wearer's level.** Better gear has to come from harder creatures, or there is no reason to hunt them.
  - [DONE] `ST-3-F` **`[D]` Each place's main job.** Armour — defence, the largest share; head — defence and magic defence, smaller; garment — magic defence; shield — defence plus block chance, which already works in combat. The main job is the one exception to `ST-3-C`'s "no sub-stats". It does not overlap the ring: the ring multiplies the defence a character already has and adds none of its own.
  - [DONE] `ST-3-G` **`[D]` Gear has no level needed to wear it.** Any character can wear any piece.
  - [DONE] `ST-3-H` **`[D]` Rarity is rolled when a piece drops, and raises both its main job and its main-stat bonuses.** Each tier lifts the main job by 0 / 3 / 9 / 18 / 30% — the weapon skill ladder — and adds +0 / +1 / +2 / +3 / +5 to each rolled main stat. A weapon's own attack is lifted by the same ladder.
  - [DONE] `ST-3-I` **`[D]` A piece's main job and its main stats are both rolled each time it drops**, within what the item allows — so two copies of the same piece can differ, the way two copies of a weapon already carry different skills.
  - [DONE] `ST-3-J` **`[D]` How the jumps in main stats work.** Each main stat has one headline jump every 10 points, gear included: STR → melee attack, INT → magic attack, VIT → defence, AGI → evasion, DEX → ranged attack, LUK → crit damage. Every jump uses the same shape: (the stat ÷ 10, rounded down) squared, plus 3 for every 10 — so crossing 10 adds 4 and crossing 100 adds 22, on top of what each point already gives. The stat screen shows what the next jump will give.
  - [ON-GOING] `ST-3-K` **`[E]` Remove the two accessory places from the game.** Built: gone from the game code, the equipment screen and the creature table in the editor. No item and no saved character ever used them. Waiting on a play-test.
  - [ON-GOING] `ST-3-L` **`[E]` Make creatures count main stats from the gear they wear.** Built: a creature's attack, hit, max HP, max MP, HP and MP regen, defence, magic defence, evasion and crit shield are worked out from its bought stats plus its gear's, as a player's already were. Waiting on a play-test.
  - [DONE] `ST-3-M` **`[D]` Physical damage splits into melee attack and ranged attack.** Melee attack gets +1 per STR plus STR's jump, and +1 per 2 DEX. Ranged attack gets +1 per DEX plus DEX's jump, and +1 per 2 STR. Level, LUK and gear attack feed both. The weapon held decides which one a normal attack and a physical skill use: a Bow or Firearm uses ranged attack, anything else uses melee attack. Magic damage is untouched — a skill whose damage is magic rolls magic attack whatever is held. The ring's attack node and attack buffs raise whichever of the two is in use, creatures follow the same rule, and the stat screen shows both.
  - [ON-GOING] `ST-3-N` **`[E]` Build ranged attack.** Built: holding a Bow or Firearm, normal attacks and physical skills roll ranged attack, for players and creatures. Waiting on a play-test.
    *Done when:* holding a bow, raising DEX raises the damage of normal attacks and physical skills by DEX's full amount; holding a sword, DEX still adds only 1 attack per 2 points.
  - [ON-GOING] `ST-3-O` **`[E]` Build the jumps for all six main stats.** Built, for players and creatures. Waiting on a play-test.
    *Done when:* crossing 10 in STR, INT, VIT, AGI or DEX moves that stat's headline number by 4 more than a normal point does, for a player and for a creature — and crossing 10 LUK adds 4% crit damage more than a normal point does.
  - [ON-GOING] `ST-3-P` **`[E]` Show melee attack, ranged attack and each main stat's next jump on the stat screen.** Built. Waiting on a play-test.
    *Done when:* the stat screen shows both attack numbers, and next to each main stat what its next jump gives and at which value.
  - [DONE] `ST-3-Q` **`[D]` A staff's and a wand's normal attack rolls melee attack.** It is a physical melee swing, as a staff's is today; magic damage comes only from skills.
  - [DONE] `ST-3-R` **`[D]` LUK's jump counts in crit damage percent.** The jump numbers are percentages: crossing 10 LUK adds 4% crit damage, crossing 100 adds 22%, and by 100 LUK the jumps add up to +130% — on top of the 0.2% each point already gives and the 140% every crit starts at.
  - [ON-GOING] `ST-3-S` **`[E]` Roll armour when it drops.** Built: a head, armour, garment or shield copy rolls its rarity, main job and main stats as it enters the world — a drop, a GM grant, or a creature spawning wearing it — and keeps them through the bag, the body, a save and a drop off a creature's corpse. A weapon's attack is lifted by its rarity. Waiting on a play-test.
    *Done when:* two copies of the same piece from two kills show different rolls, and both keep them after the game restarts.

- [DONE] `MT-4` **`[D]` Decide whether gear comes only from kills, or from a shop too.** Every item already carries a sell value, so selling works the moment a shop exists; nothing carries a buy price, so buying needs one more number per item. *Decided 2026-09-13: the shop is deferred; gear comes from loot for now.* — **0.25 days**

---

## Week 2 — Nothing can rot silently

**Goal:** still nothing for a player, but from here on a mistake in the loot data gets caught by a machine instead of by you, six weeks later.

- [ ] `MT-5` **`[E]` Teach the wiring sweep to catch a drop name that matches nothing.** A creature's drop list names its items by typing their names. Rebuilding the creatures prints one warning per missing name, then never mentions it again, and the sweep that checks everything else follows proper links only. **This is not hypothetical:** the older creature roster already asks for nine items that do not exist — Life Potion, Silver Ring, Wooden Guard, Leather Vest, Fire Scroll, Jade Amulet, Katana, Iron Sword and Boots. — **1–1.5 days**
  *Done when:* misspelling an item in one creature's drop list makes `tools/check-asset-wiring.py` fail and correcting it makes the check pass — and `tools/prove-asset-wiring-check.py` still passes afterwards.

- [ ] `MT-6` **`[A]` Author the ring as a real item.** Every character wears a "Ring of Beginnings" the game builds in memory as it loads, because no ring item exists. The game already prefers an authored one, so no code changes. — **0.5 days**
  *Done when:* a ring exists alongside the other items — created with **Create ▸ unwritten ▸ Item**, filled in through **Tools ▸ unwritten ▸ Catalogs ▸ Items ▸ Rings** — and a freshly created character wears that one rather than the invented one.

- [ ] `MT-7` **`[A]` Author the first four pieces of gear** — the low tier of each place you chose. Name, place on the body, the range its main job and main stats roll within, how much each rarity tier adds, and sell value. Each is created singly with **Create ▸ unwritten ▸ Item**, then filled in as a batch in **Tools ▸ unwritten ▸ Catalogs ▸ Items ▸ Armor**. — **2–3 days**
  *Done when:* you can wear one piece in each of the four places and watch the character sheet numbers move for each.

- [ ] `MT-8` **`[D]` Decide how rare gear should be.** A creature gives up what it wears 15 times in 100 today; a worn ring is 1 in 100,000. *Recommendation: leave both alone until Week 4 puts real fights in front of you, then judge once.* — **0.25 days**

---

## Week 3 — A full wardrobe exists

**Goal:** every place on the body has something to find and something better to find later — even though nothing drops it yet.

- [ ] `MT-9` **`[A]` Author the middle and high tiers.** Eight more pieces, two per place. — **3–4 days**
  *Done when:* every place on the body has three pieces, and a high piece's main job can roll higher than a low piece's can.

- [ ] `MT-10` **`[E]` Check the whole item set at once.** — **0.5 days**
  *Done when:* `tools/check-asset-wiring.py` and `tools/check-unity-code.sh` both run clean, and the editor console is read in full and reported.

- [ ] `MT-11` **`[D]` Decide which creatures drop which tier.** *Recommendation: tie it to the creature's level band rather than naming creatures one by one — it survives adding creatures later.* — **0.5 days**

---

## Week 4 — Killing things pays you

**Goal:** the phase that turns two months of finished combat into a game. You kill something, gear falls out, you put it on, your character sheet changes.

Today that works for exactly one creature in the whole game: an Axolot wears a sword and gives it up 15 times in 100. The other 85 give nothing but gold — all 86 have an empty drop list.

- [ ] `MT-12` **`[A]` Author the drop tables.** A drop list on each of the 86 creatures: which items, at what chance. Creatures can also simply **wear** gear — what a creature wears is what it can give up, so putting a sword on a bandit needs no separate list. — **3–4 days**
  *Done when:* every creature has at least one thing it can give, and the wiring sweep reports no unresolved names.

- [ ] `MT-13` **`[E]` Rebuild the creatures so the drop lists reach the game.** Loot is attached when a creature's prefab is baked, not when the game runs, so editing a drop list changes nothing until this is run: **Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs**. — **0.5 days**
  *Done when:* killing the same creature about twenty times lands at least one item in your bag.

- [ ] `MT-14` **`[E]` Play-test the loop and judge the rates.** The first session where the numbers from Week 2's decision meet a real fight. — **1 day**
  *Done when:* you have played an hour, and either kept the drop rates or written down the new ones and applied them.

---

## Week 5 — The world opens

**Goal:** you can walk from the first town to any map in the game. Today 23 of the 41 maps have no doorway at all — 7 towns, 4 fields, and 12 of the 14 dungeons — and nothing anywhere leads to them.

- [ ] `MT-15` **`[A]` Connect the 23 maps that nothing leads to.** There are 36 doorways in the whole world, across 18 maps. — **4–5 days**
  *Done when:* starting from the first town you can reach every one of the 41 maps on foot, without a game-master command.

- [ ] `MT-16` **`[D]` Decide the shape of the world map.** Which town leads where, and whether dungeons hang off fields or off towns. *Recommendation: sketch it as a picture first — it is faster to redraw than to rewire.* — **0.5 days, do it before the task above**

---

## Week 6 — Dungeons have a destination

**Goal:** every dungeon has a boss at the end of it, worth the walk.

Twenty bosses are defined and none is placed anywhere. All 34 creature spawn points are set to pick any ordinary creature.

- [ ] `MT-17` **`[E]` Give each dungeon a second spawn point.** Each dungeon has exactly one, so using it for the boss leaves that dungeon with no ordinary creatures. — **1 day**
  *Done when:* every dungeon has one spawn point for its ordinary creatures and one for its boss.

- [ ] `MT-18` **`[A]` Place a named boss in each of the 14 dungeons.** **The two shortcuts do not do what their names suggest:** asking for "a boss" gives a different random one on every respawn, and the one-click placement tool always places the first boss in the list, so running it 14 times puts the same creature in all 14. Naming the creature is the only way to get a fixed one. — **2 days**
  *Done when:* every dungeon has one named boss in a fixed place, plus its ordinary creatures, and the same boss is there again after you leave and come back.

- [ ] `MT-19` **`[A]` Make a boss worth the walk.** Bosses already pay 500 to 2000 gold against an ordinary creature's 6 to 24. They need gear to match. — **1–2 days**
  *Done when:* killing a boss reliably produces something an ordinary creature never gives.

---

## Week 7 — The world has people

**Goal:** you can talk to somebody with a name who says the same thing twice. Today every villager in the game returns the same hardcoded name, "Villager", and speaks randomly chosen ambient lines.

- [ ] `MT-20` **`[E]` Give a villager a name that can be authored.** There is no field to put a name in — this is a small code change before any name can exist. — **1 day**
  *Done when:* two villagers standing side by side show different names.

- [ ] `MT-21` **`[A]` Name and write the first town's people.** — **2 days**
  *Done when:* at least three people in the first town open a dialogue box with their own name at the top and their own lines, and say the same thing the next time you talk to them.

- [ ] `MT-22` **`[D]` Decide how big a quest can be.** Kill / fetch / travel only, or chains and branches. *Recommendation: the three simple kinds only. A chain is a second system and it can come after the first quest is proven.* — **0.5 days**

- [ ] `MT-23` **`[E]` Sketch how a quest is stored.** What the saved character has to remember: which quests are open, how far along, which are finished. — **1 day**
  *Done when:* you can describe in one paragraph what a save gains, and it survives a restart in a throwaway test.

---

## Week 8 — A quest can be given

**Goal:** somebody asks you for something and the game remembers being asked. There is no quest system of any kind today — only a category an item can be filed under, which does nothing except stop that item being dropped.

- [ ] `MT-24` **`[E]` Build the smallest quest system that works.** One person asks, the game remembers you were asked, remembers your progress, lets you hand it in. No branching, no chains, no journal beyond a list. — **4–5 days**
  *Done when:* you can accept a quest, close the game entirely, come back, and find your progress still counted.

---

## Week 9 — A quest can be finished

**Goal:** the full loop — a named person sends you somewhere, you go, you come back, you are paid.

- [ ] `MT-25` **`[A]` Write the first three quests.** Kill some of a thing; fetch some of a thing; go to a place and come back. — **2 days**
  *Done when:* all three can be accepted, completed and turned in, and the rewards arrive in the bag.

- [ ] `MT-26` **`[E]` Play the whole loop end to end.** Town → quest → field or dungeon → boss → loot → back → reward. — **1–2 days**
  *Done when:* you have played that circuit without a game-master command and written down what felt wrong.

- [ ] `MT-27` **`[D]` Decide what a quest pays.** Gold, an item, experience, or all three. *Recommendation: all three, with the item being the only reliable source of one gear piece, so a quest is worth doing once.* — **0.5 days**

---

## Week 10 — Gold has a use

**Goal:** you can spend the money you have been collecting. The furthest character in the database has 4,396 gold and nothing in the game to spend it on.

- [ ] `MT-28` **`[E]` Build a shop screen.** Buy and sell, against one shopkeeper. Every item already has a sell value; **none has a buy price, so that is one more number per item** and a place to put it. — **4–5 days**
  *Done when:* you can sell a piece of loot for gold, buy a potion back, and both the gold and the bag survive a restart.

- [ ] `MT-29` **`[A]` Stock the first shops.** What each town sells. — **1 day**
  *Done when:* every town with a shopkeeper sells a list you chose, not a list the game made up.

---

## Week 11 — A stranger can start it

**Goal:** someone who has never seen the game can open it and know what to do. Today the first thing a player meets is a sign-in box.

- [ ] `MT-30` **`[E]` Title screen and new-game flow.** — **3–4 days**
  *Done when:* launching the game shows a title, and a brand-new player reaches their first fight without being told anything by you.

- [ ] `MT-31` **`[D]` Decide how much the game explains itself.** A real tutorial, or a first quest that teaches by being done. *Recommendation: the first quest. It costs one quest instead of a system, and it is already being built in Week 9.* — **0.5 days**

---

## Week 12 — It is a thing you can hand somebody

**Goal:** the game runs as a packaged program on a machine that is not this one.

A player build and a server build both exist — 458 MB from 8 September and 227 MB from 10 September — but everything in weeks 1 to 11 will have been judged in the editor unless this is done deliberately. **Whether the current client build still runs has not been measured.**

- [ ] `MT-32` **`[E]` Rebuild both halves and play the packaged game end to end.** — **2–3 days**
  *Done when:* you install it on a machine that is not this one, sign in, fight, pick up loot, finish a quest, spend gold and quit, without opening Unity once.

- [ ] `MT-33` **`[E]` Write down what broke that the editor never showed.** — **1 day**
  *Done when:* every difference between the editor and the packaged game is written down, fixed or deliberately accepted.

---

## Backlog — real work, deliberately not in the twelve weeks

Each of these is worth doing and none of them blocks the loop above. They are here so they are not forgotten, and so nothing pretends they are free.

- [ ] `MT-34` **`[A]` Author the bow's skills.** Twelve weapon families are defined. The sword has 22 and all 22 are published. The staff has one, unpublished. The other ten have none. **Publishing is the trap:** a dropped weapon only ever picks up published skills, so a full set of bow skills left unpublished arrives as an empty weapon and looks like a bug. — **5–8 days**

- [ ] `MT-35` **`[E]` Make cards something you earn.** Four cards exist and the socket screen offers all four to anyone with an empty socket, regardless of what they own. Two problems, both real: the four are named Andre, Poring, Whisper and Zipper Bear — Ragnarok creatures, and none of our 86 goes by any of those names, so "drops from the creature it's named after" has nothing to attach to. And a card cannot be carried at all: a drop list can only name an item, the bag can only hold an item, and a card is neither. This is a build across the bag, the saved character and the server — not four data rows. — **5–7 days**
  - [ ] `ST-35-A` **`[D]` First decide:** rename the four cards after creatures we have, or pick which creature each drops from.

- [ ] `MT-36` **`[E]` Settle the four trust gaps.** All four are written down in the networking notes under a heading calling them deliberate limits: a phone can sign in but cannot enter the world; the training dummy takes no damage from a player; whether a player really had the item they used is their machine's word; and a creature has no sense of who is fighting it, so two people attacking one is not resolved fairly. Most already carry a reason — what is missing is a decision about whether they stay that way once strangers play together. **The training-dummy one is the odd one out:** the notes say a swing at it is refused, while the tool that places it calls it a normal mob that takes hits. Those disagree and only playing will settle it. — **unestimated until played**

- [ ] `MT-37` **`[E]` Publish the eight orphan skills, or delete them.** Two heals, a healing circle, a fire bolt, a hex field, a bulwark, a war banner and a blink belong to no weapon and no item, and all eight are unpublished — so nothing in the game can grant them. — **1–2 days**

---

## Not scheduled at all

Named so nobody thinks they were forgotten: crafting and recycling (a full design exists, no code), class and job advancement, the remaining ten weapon families, trading, parties, guilds, auction house, player-versus-player, a real tutorial, pause, controller support, translations, anything to do with Steam, and live-service operations — monitoring, anti-cheat and a way to patch a running game. Hosting has been decided and costed; none of the rest is started.

---

## Related

- [Roadmap to Launch](roadmap-to-launch.md) — the counted picture of how far the game is from shippable.
- [Crafting & Recycling](../2-systems/crafting-and-recycling.md) — designed, not scheduled.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the progression engine the ring item belongs to.
- [Weapon Roster — 12 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — ten families with no skill at all, and the staff's single unpublished one.
- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — the six trust gaps, and what they sit on top of.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — how to stand the game up to test any of this.
