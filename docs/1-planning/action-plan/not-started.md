---
status:
owner: vince
last-updated: 2026-09-30
tags:
  - planning-production
---

# unwritten — Action Plan — Not started

Tasks whose status is empty `[ ]` — not started. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

## Week 4 — Killing things pays you

**Goal:** the phase that turns two months of finished combat into a game. You kill something, it pays materials and consumables (and, when it turns back into a character that never fights, gold), you forge the materials into gear, you put it on, your character sheet changes.

Every creature wears the pieces authored for it, at its own authored grade, folds and Ink, but **gives up none of it** — since V4, gear is made, not taken off a
corpse. Since 2026-09-22 a kill pays the creature's own level's band of metal, cloth and leather, a chance of an Ink
or a Salt (much more from elites, more again from shadows), and a 30% chance of one random consumable; gold comes from
saving a character that never fights.

- [ ] `MT-14` **`[E]` Play-test the loop and judge the rates.** The first session where the numbers from Week 2's decision meet a real fight. — **1 day**
  *Done when:* you have played an hour, and either kept the drop rates or written down the new ones and applied them.

---

## Week 6 — Dungeons have a destination

**Goal:** every elite has its place in the world and every shadow stands on an end-game map, each worth the walk.

18 elites and 14 shadows are defined ([Mob Roster](../../3-world/mob-roster.md)). Where every creature lives is the
world spawn list, one row per spawn area, which the server reads as it loads each map; today every one of the 18 elites
has a row in the new world's fields and dungeon floors (not yet walked in play), and no shadow has one.

- [ ] `MT-18` **`[A]` Put the fourteen shadows on their own places.** One row each, naming the shadow, once the
  shadows' place maps exist ([World Map](../../3-world/world-map.md)). — **2 days**
  *Done when:* every shadow stands in its fixed place on its own map, and the same one is there again after you leave and come back.

---

## Play-test — strangers can play it

**Goal:** people you invite download the game, make their own account, and play it on a server we rent, not on
this machine. This is the play-test: the phase where interested players gather before the official release. Steam
is not part of it — the game is handed out from our own download page. Quests, shops and the title screen are not
needed to open it; they arrive as updates while it runs, which gives testers a reason to come back.

**It opens once `MT-14`, `MT-15` and `MT-17` are done** — the loop's rates judged, every map reachable, every elite
placed. The shadows (`MT-18`) are not needed for it. **Testers get one weapon with skills, the sword** — the bow has
none yet — so the invitation says so, or it gets reported as a bug.

**It does not fit in one week.** Its tasks — the five below, and `MT-50` and `MT-51`, which are under way — add up
to 11–15 days, so it is two to three weeks of work.

- [ ] `MT-41` **`[E]` Put the game on a rented machine.** One Vultr machine in Singapore, 2 cores / 4 GB, $24 a
  month ([Where the Game Is Hosted](../where-the-game-is-hosted.md)). The world server needs a Linux build, which
  has never been made — the Linux add-on is installed for the project's Unity version, so it is untested rather
  than blocked. All four programs — database, sign-in, characters, world — must start when the machine starts and
  restart after a crash; today they are started by hand. A domain name and an encrypted connection (the setup file
  for it is already written), the plain unencrypted doors shut, and a database backup kept on a different machine.
  — **2–3 days**
  *Done when:* you restart the rented machine, touch nothing on it, and can still sign in to the game on it from
  this Mac; and a copy of its database exists somewhere other than that machine.

- [ ] `MT-42` **`[E]` Make a Windows version of the game.** The only player build is a Mac one, and the Windows
  add-on is not installed for the project's Unity version. Needs a Windows PC to try it on. — **1–2 days**
  *Done when:* on a Windows PC, double-clicking the game reaches the sign-in box of the rented server, with nothing
  typed into a terminal.

- [ ] `MT-43` **`[E]` A download page, and one place testers sign up and report problems.** — **1 day**
  *Done when:* somebody who has never seen the game follows one link, downloads it for Windows or Mac, and knows
  where to report what went wrong.

- [ ] `MT-32` **`[E]` Rebuild both halves and play the packaged game end to end, against the rented server.** A
  player build (571 MB, 26 September) and a server build (253 MB, 27 September) exist, both for Mac. **Whether the
  player build runs on a machine that is not this one has not been measured.** — **2–3 days**
  *Done when:* you install it on a machine that is not this one, sign in, fight, pick up loot, forge a piece of
  gear, put it on and quit, without opening Unity once.

- [ ] `MT-33` **`[E]` Write down what broke that the editor never showed.** — **1 day**
  *Done when:* every difference between the editor and the packaged game is written down, fixed or deliberately accepted.

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

- [ ] `MT-26` **`[E]` Play the whole loop end to end.** Town → quest → field or dungeon → elite → loot → back → reward. — **1–2 days**
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

## Backlog — real work, deliberately not in the weeks above

Each of these is worth doing and none of them blocks the loop above. They are here so they are not forgotten, and so nothing pretends they are free.

- [ ] `MT-34` **`[A]` Author the bow's skills.** Fourteen weapon families are defined. The sword has 22, each with the Weapon Mastery level at which it can be bought. The staff has one. The other twelve have none. **The max level is the trap:** a skill is bought with mastery points, and a skill whose max level is left at 0 can never be learned and looks like a bug; each bow skill also needs the mastery level it opens at, or it opens at 1. The tree rules are the `skill-tree` skill. — **5–8 days**

- [ ] `MT-35` **`[E]` Make cards something you earn.** No cards exist: the four Ragnarok-named placeholders were deleted on 2026-09-26, and "Insert card" is off the item menu (2026-10-02) because slotting one had no server side: it changed only the player's own copy, and its picker offered whatever cards exist regardless of what they own. Settled on 2026-09-26: every card is a "vs something" card (against a size, a kind or an element — race is left out for now, and so is biome); a weapon rolls 3 to 4 sockets and an armour piece 0 to 1, when it is forged; a card is permanent once socketed and never comes back out. What is still missing: a card cannot be carried at all — a drop list can only name an item, the bag can only hold an item, and a card is neither. This is a build across the bag, the saved character and the server — not a handful of data rows. — **5–7 days**

  - [ ] `ST-35-A` **`[D]` First decide:** the card roster, and which creature drops each card.

- [ ] `MT-36` **`[E]` Settle the fair-targeting gap.** It is written down in the networking notes under a heading calling it a deliberate limit: a creature has no sense of who is fighting it, so two people attacking one is not resolved fairly. What is missing is a decision about whether it stays that way once strangers play together. — **unestimated until played**

- [ ] `MT-37` **`[E]` Give the eight orphan skills a weapon, or delete them.** Two heals, a healing circle, a fire bolt, a hex field, a bulwark, a war banner and a blink belong to no weapon — and a skill is learned only by spending its weapon's mastery points on it, so nothing in the game can teach them. Give each one a weapon's pool, a mastery level and a max level, or delete it. — **1–2 days**

- [ ] `MT-45` **`[A]` Make a rock-face tile for cliffs.** Every cliff biome now has a cliff face colour, which the face is
  drawn in instead of any tile; a biome with none would still draw its faces with the gravel ground tile, and the cliff
  material already takes any tile. — **0.5–1 day**

- [ ] `MT-57` **`[A]` DeepMaw's portrait is one pixel short.** Found 2026-09-30: its portrait is 38×37, every other
  portrait 38×38. The game draws no portraits yet, so nothing shows it. — **0.1 days**
  *Done when:* every creature and character portrait is 38×38.

---

- [ ] `MT-49` **`[E]` Stop the hover plate throwing on destroyed icons.** Seen 2026-09-28 in play: hundreds of
  "the object of type Image / TextMeshProUGUI has been destroyed" errors a session, from the hover plate over a unit
  rebuilding its status icons (`HoverPlate.RebuildIcons`, line 316, from its LateUpdate). Cause not looked into yet.
  *Done when:* a play session with fights and map changes logs none of them.
- [ ] `MT-58` **`[E]` Give flat maps' ground its own collision.** Found 2026-09-30 under RULE ONEGROUND: a map whose
  ground is flat (no levels, no cliffs, no painted heights) is drawn by one mesh but walked on by a separate invisible
  box, `Ground_Floor`, laid under it — 49 scenes today. The box's top sits exactly at the drawn surface, so nothing
  plays wrong yet, but it is the second piece the rule bans. Make the drawn ground its own collider on every map, and
  re-lay the ground of the maps that have the box (only their ground — props and doors stay).
  *Done when:* no scene has a `Ground_Floor`, and walking and creature routes on those maps behave as before.

## Not scheduled at all

Named so nobody thinks they were forgotten: class and job advancement, the remaining ten weapon families, trading, parties, guilds, auction house, player-versus-player, a real tutorial, pause, controller support, translations, anything to do with Steam (the play-test is handed out without it), and live-service operations — monitoring, anti-cheat and a way to update the servers without taking the game down (players' own copies are updated by the launcher, `MT-50`). None of it is started.

---

---
