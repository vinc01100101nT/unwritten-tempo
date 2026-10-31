---
status:
owner: vince
last-updated: 2026-10-31
tags:
  - planning-production
---

# unwritten — Roadmap to Launch

How far the game is from being something a stranger can play and finish. **This page is the picture. The
[Action Plan](action-plan/on-going.md) is the list of what to do next** — the checkboxes, the done-checks and the
order. Neither repeats the other.

**Counts never live on this page.** Every count — squares built, towns, creatures, skills, items, quests, townsfolk —
is printed live by:

```bash
python3 tools/project-status.py
```

A count typed into a doc is wrong the moment somebody commits, so this page only says what each count means for
launch. Where a sentence below needs a number, it names the line of that script to read.

---

## The one-paragraph answer

The technology is built; the blocker is breadth. Fighting is finished and runs wholly on the server, saving and the
online half work, and the world is one seamless map built square by square from one layout. What is thin is content:
most weapon families have no skill anyone can learn, most towns have no town piece and nobody in them, much
of the world's land is not built yet, and there are no cards and no main-story quest.

---

## What is finished

- **Fighting.** Players and creatures resolve damage through one shared set of rules and one landing point, on the
  server. Crit, then hit, then damage, in a fixed order. Every timed stat change is one buff or one debuff effect.
- **The numbers behind it** are plain functions, pinned and checked outside Unity (`tools/check-game-math.sh`).
- **The programs that make it online.** A sign-in service (`accounts/`), a character server that owns every save
  (`charserver/`), a game server that owns the world and never touches the database, one shared database, and a
  download server for the website, the launcher and every update. One account signs in once. There is deliberately
  no offline game.
- **The world's shape.** One seamless map, `world`, built from `data/world-layout.json` by the world builder; the old
  zone world of separate maps was deleted on 2026-10-28. How it is built and extended is the `world-authoring` skill.
- **The checks.** The code compiles without opening Unity (`tools/check-unity-code.sh`), and each checker has a prover
  that breaks it on purpose to confirm it complains.

## What is thin — read the count from the script

- **Weapon skills** — "weapon families with a published skill" against 14. Every other family can be held but has
  nothing to learn. "belonging to nothing" lists the skills no weapon and no item grants (task `MT-37`).
- **Towns** — "towns with a built town piece" against "towns in the layout". A town with no piece has no walls,
  gates, streets or houses; its spot is plain world ground. Every townsperson stands in Lastline ("townsfolk rows")
  except Parley's Keeper — each town with a built piece has its own Keeper ("keepers") — and the shadow summoners,
  who stand out in the world.
- **The world's land** — "land squares not built" and the "not built" lines under it. The builder makes the sea, its
  shores, sand and the low-tide path, but no boats, so neither island can be sailed to; and the dark isle's ground look
  is undecided (the `world-authoring` skill, section 6).
- **Shadows' places** — "landmarks built / in the layout". A shadow's place on unbuilt land cannot be reached.
- **Cards** — none exist, and the card roster is undecided (task `MT-35`).
- **Quests** — tutorials and a side quest; no main-story quest.

## What is missing entirely

No main-story quest. No class or job advancement. No title screen or new-game flow — a sign-in box is the first thing
a player meets (task `MT-30`). No controller support, no translations. No parties, guilds, trading between players,
auction house or player-versus-player. No anti-cheat beyond the server owning every number, and no monitoring beyond
the public Server Status page. No rented machine: where the game will be hosted is decided
([Where the Game Is Hosted](where-the-game-is-hosted.md)), nothing there is set up (task `MT-41`).

*(Each absence was checked by searching the code for it by name, 2026-10-31. A search proves absence only as far as
its reach.)*

## Known limits of what is built

- Movement is reported by the player's machine and checked by the server — deliberately, see
  [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md), which also lists the other
  deliberate limits (among them that two people attacking one creature is not resolved fairly, task `MT-36`).
- A fall into deep water strands whoever falls: the game has no swimming. The Tide Temple's low-tide path is always
  wadeable, because the game has no tide. Both deferred by the user (the `world-authoring` skill, section 2).

---

## Related

- [Action Plan](action-plan/on-going.md) — the trackable list of what to do next, in three files by status.
- [Active Skill System — V3](../2-systems/active-skill-system-v3/README.md) — what the finished combat is.
- [Gameplay System — V4](../2-systems/gameplay-system-v4/README.md) — dropped gear, folding, quenching, Ink, mastery.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the weapon families.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the progression engine.
- [Quests and Tutorials](../2-systems/quests-and-tutorials.md) — the quest system and the tutorials.
- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — the online half and its limits.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — how to stand the multiplayer half up.
- [Mob Roster](../3-world/mob-roster.md) — the creatures and how they are placed.
- [Story & Setting (Canon)](../3-world/story-setting-canon.md) — the story the world still needs put into it.
- [Where the Game Is Hosted](where-the-game-is-hosted.md) — the machine, the latency numbers, and the cost.
