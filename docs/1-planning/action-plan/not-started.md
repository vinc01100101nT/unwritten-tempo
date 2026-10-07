---
status:
owner: vince
last-updated: 2026-10-06
tags:
  - planning-production
---

# unwritten — Action Plan — Not started

Tasks whose status is empty `[ ]` — not started. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

## Week 4 — Killing things pays you

**Goal:** you kill something, it pays materials and consumables, you forge the materials into gear, you put it on, and your character sheet changes.

- [ ] `MT-14` **`[E]` Play-test the loop and judge the rates.** — **1 day** — see [Mob Roster](../../3-world/mob-roster.md)
  *Done when:* you have played an hour, and either kept the drop rates or written down the new ones and applied them.

---

## Week 6 — Dungeons have a destination

**Goal:** every elite has its place in the world and every shadow stands on an end-game map, each worth the walk.

- [ ] `MT-18` **`[A]` Put the fourteen shadows on their own places.** — **2 days** — see [The World Map — Three Regions](../../3-world/world-map.md)
  *Done when:* every shadow stands in its fixed place on its own map, and the same one is there again after you leave and come back.

---

## Play-test — strangers can play it

**Goal:** people you invite download the game, make their own account, and play it on a server we rent, not on this machine.

It opens once `MT-14`, `MT-15` and `MT-17` are done; the shadows (`MT-18`) are not needed for it, and testers get one weapon with skills, the sword, so the invitation says so.

- [ ] `MT-41` **`[E]` Put the game on a rented machine.** — **2–3 days** — see [Where the Game Is Hosted](../where-the-game-is-hosted.md)
  *Done when:* you restart the rented machine, touch nothing on it, and can still sign in to the game on it from this computer; and a copy of its database exists somewhere other than that machine.

- [ ] `MT-32` **`[E]` Rebuild both halves and play the packaged game end to end, against the rented server.** — **2–3 days** — see [Starting the Network](../../2-systems/networking/starting-the-network.md)
  *Done when:* you install it on a machine that is not this one, sign in, fight, pick up loot, forge a piece of gear, put it on and quit, without opening Unity once.

- [ ] `MT-33` **`[E]` Write down what broke that the editor never showed.** — **1 day** — see [Starting the Network](../../2-systems/networking/starting-the-network.md)
  *Done when:* every difference between the editor and the packaged game is written down, fixed or deliberately accepted.

---

## Week 7 — The world has people

**Goal:** you can talk to somebody with a name who says the same thing twice.

- [ ] `MT-21` **`[A]` Name and write the first town's people.** — **2 days** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)
  *Done when:* at least three people in the first town open a dialogue box with their own name at the top and their own lines, and say the same thing the next time you talk to them.

---

## Week 9 — A quest can be finished

**Goal:** the full loop — a named person sends you somewhere, you go, you come back, you are paid.

- [ ] `MT-25` **`[A]` Write the first three quests** — kill some of a thing; fetch some of a thing; go to a place and come back. — **2 days** — see [Quests and Tutorials](../../2-systems/quests-and-tutorials.md)
  *Done when:* all three can be accepted, completed and turned in, and the rewards arrive in the bag.

- [ ] `MT-26` **`[E]` Play the whole loop end to end** — town → quest → field or dungeon → elite → loot → back → reward. — **1–2 days** — see [Roadmap to Launch](../roadmap-to-launch.md)
  *Done when:* you have played that circuit without a game-master command and written down what felt wrong.

---

## Week 11 — A stranger can start it

**Goal:** someone who has never seen the game can open it and know what to do.

- [ ] `MT-30` **`[E]` Title screen and new-game flow.** — **3–4 days** — see [Roadmap to Launch](../roadmap-to-launch.md)
  *Done when:* launching the game shows a title, and a brand-new player reaches their first fight without being told anything by you.

---

## Backlog — real work, deliberately not in the weeks above

Each of these is worth doing and none of them blocks the loop above. They are here so they are not forgotten, and so nothing pretends they are free.

- [ ] `MT-34` **`[A]` Author the bow's skills.** — **5–8 days** — see [Weapon Roster — 14 Identities](../../2-systems/active-skill-system-v3/weapon-roster-12-identities.md)

- [ ] `MT-35` **`[E]` Make cards something you earn** — decided 2026-09-26: every card is a "vs something" card (a size, a kind or an element; race and biome left out); a weapon rolls 3 to 4 sockets and an armour piece 0 to 1 when forged; a card is permanent once socketed. — **5–7 days** — see [Roadmap to Launch](../roadmap-to-launch.md)

  - [ ] `ST-35-A` **`[D]` First decide:** the card roster, and which creature drops each card. — see [Roadmap to Launch](../roadmap-to-launch.md)

- [ ] `MT-36` **`[E]` Settle the fair-targeting gap.** — **unestimated until played** — see [Networking — Implementation Notes](../../2-systems/networking/implementation-notes.md)

- [ ] `MT-37` **`[E]` Give the eight orphan skills a weapon, or delete them.** — **1–2 days** — see [Roadmap to Launch](../roadmap-to-launch.md)

- [ ] `MT-45` **`[A]` Make a rock-face tile for cliffs.** — **0.5–1 day** — see [Biomes](../../3-world/biomes.md)

- [ ] `MT-57` **`[A]` DeepMaw's portrait is one pixel short** — 38×37, every other portrait 38×38. — **0.1 days**
  *Done when:* every creature and character portrait is 38×38.

- [ ] `MT-58` **`[E]` Give flat maps' ground its own collision** — 46 scenes (counted 2026-10-06) are walked on through a separate invisible box, `Ground_Floor`, under the drawn ground; re-lay only their ground, props and doors stay. — see [Biomes](../../3-world/biomes.md)
  *Done when:* no scene has a `Ground_Floor`, and walking and creature routes on those maps behave as before.

---

## Not scheduled at all

Named so nobody thinks they were forgotten: class and job advancement, skills for the weapon families that have none, trading, parties, guilds, auction house, player-versus-player, pause, controller support, translations, anything to do with Steam (the play-test is handed out without it), and live-service operations — monitoring, anti-cheat and a way to update the servers without taking the game down (players' own copies are updated by the launcher, `MT-50`). None of it is started.
