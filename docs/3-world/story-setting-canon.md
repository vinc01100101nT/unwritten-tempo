---
status: canon seed
owner: vince
last-updated: 2026-09-25
tags:
  - world-content
---

# Unwritten — Story & Setting (Canon)

The anchor lore doc. Everything else (world, mobs, classes, Ring of Progression, progression visuals) should be consistent with this. The core discovery: **the game's visual split — 3D charcoal players vs. flat 2D mobs — IS the story.** Nothing about the art is arbitrary.

## The premise

In ancient history, one person wrote down the **truth of the world** — a record of what was and what is, kept current as the world happened. Then the scribe was **silenced, mid-sentence**. The record simply stops.

Everything before that line has a name, a shape, a settled truth. Everything after it was **never written down** — and what is never written does not hold together. The blank pages that should have carried the rest of the record scattered across the world, and where they landed they **transformed into monsters**: unrecorded space given hostile form.

With no record still being kept, **people lost their identities** — their color, their self, drained away, leaving them **charcoal-like husks**. Nobody's name was ever inked. Now they wander a seemingly endless journey, hunting the scattered pages, trying to **finish what the scribe never got to write**.

The world is *Unwritten* — not un-done, but **never recorded**. The quest is not to restore a lost book; it is to write the part that was never there.

> **Naming note (2026-08-10).** The earlier canon said an entity "un-wrote" the book. That contradicted the title — *unwritten* means *never written*, not *undone*. The premise above replaces it. The paper-monsters, charcoal-people, and page-loot logic all survive the change unaltered.

## Why the art means what it means

- **2D flat mobs = blank pages.** They are literally paper. Flatness is not a style choice — it is what they are: pages that should have held the rest of the record and never did.
- **3D-but-charcoal players/NPCs = people who were never named.** The body is there (3D, present, real) but no ink ever reached it — no color, no self. The Quaternius charcoal-body + colored-hair + ink-eye rig telegraphs exactly this: a trace of identity clinging on.
- **Loot / drops = text you put on a blank page.** Killing a page = filling it in = one more line of the record that now exists because you wrote it.
- **"Unwritten"** = the state of everything past the scribe's last sentence — including you.

## The Fourteen Unwritten

Fourteen people fought the thing that silenced the scribe. Because they acted **after** the last written line, none of it was recorded. Whether they won is not known. They left **fourteen weapons** and no names.

- **Each of the 14 weapons is one of them.** A weapon is not a tool; it is the only surviving trace of a person the record never captured. The one-sentence identity in `active-skill-system-v3/weapon-roster-12-identities.md` **is** that person's character — the Shield is the one who stood in front, the Dagger is the one nobody saw arrive.
- **They are known only by epithet, never by name.** An epithet is what witnesses called someone when no one had written the name down. The missing name is the point — and the reward.
- **Learning a weapon is remembering its wielder.** A weapon holds all of one of the Fourteen, and what you can draw out of it is however much of them you have lived: each skill comes back at the point where your practice with that weapon reaches it. A finer blade lets you remember further. Two people carrying the same kind of weapon know different halves of the same person.
- **Carrying one bleeds their identity into you** — that is the charcoal → color return. You are not recovering *the* truth; you are finishing *a* story.
- **Naming an Unwritten is the mastery endgame.** Learn enough of a weapon's pool and its true name can finally be written — a codex entry authored by play. Fourteen names to write, fourteen mastery tracks.

| Weapon | Epithet | The deed nobody recorded |
| --- | --- | --- |
| Sword | **The One Who Answered** | Met every challenge alone, one at a time, and never lost the exchange. |
| Greatsword | **The One Who Cleared the Road** | Walked ahead of the others so there would be a path behind. |
| Axe | **The One Who Never Stopped** | Would not be turned, would not defend, only pressed forward. |
| Warhammer | **The One Who Said No** | Whatever the enemy meant to do, it did not get to. |
| Spear | **The One Who Moved the Line** | Decided where everyone stood, on both sides. |
| Shield | **The One Who Stood** | Took what was meant for the other thirteen. |
| Bow | **The One Who Kept Distance** | Was never where the blow landed, and never out of range. |
| Dagger | **The Late Arrival** | Was not in the fight until it was already ending. |
| Firearm | **The One Who Fired Once** | Needed one shot; waited as long as that took. |
| Staff | **The One Who Burned the Field** | Unmade the ground the enemy was standing on. |
| Wand | **The One Who Brought Them Back** | Is the reason the other thirteen were still standing. |
| Scythe | **The One Who Waited** | Did nothing quickly, and nothing survived it. |
| Fist | *not written yet* | *not written yet* |
| Kick | *not written yet* | *not written yet* |

*Rule for authoring: an epithet describes a **deed**, never a name, a title, or a rank — the moment it reads like a name, it stops being unwritten.*

## Mechanical hooks this justifies (design directions, not commitments)

- **Charcoal → color as progression.** As a player writes more of the record (levels, ring points, story beats, weapon mastery), color returns to them — identity leaking in for the first time. The rig already supports a gradient from charcoal husk toward a fully-colored self.
- **Killing a 2D mob = filling in a page.** Drops can be *fragments* that assemble into written lore entries — a codex the player authored. Ties directly into loot + MapRecipe content.
- **Ring of Progression = a vessel that holds written identity.** Tradeable, with lineage; the conservation economy fits — what's written is *passed on, never minted*. See the **Ring System — Current Facts (Trio-Ring)** doc (the tradeable-ring direction is recorded there as a proposed, uncommitted concept).
- **Bosses / the silencer = concentrated blankness.** Larger, self-assembled masses of blank page; the thing that silenced the scribe is the deepest one.
- **Conservation as balance driver.** Every gain pays a cost — mirrors "a line written here is a line not written elsewhere."
- **Weapon mastery = naming an Unwritten.** Per-weapon codex track; see **The Fourteen Unwritten** above and the weapon roster doc.

*Status: canon seed, written 2026-07-04, premise renamed 2026-08-10. Expand as story beats, the entity, and the codex get designed.*

---

## Related

- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the fourteen identity sentences these epithets are the flavour half of.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the growth engine the tradeable-ring lore hangs off (recorded there as proposed, not committed).
- [Biome Mob Families — Spawning by Biome and Level](biome-mob-families.md) — the blank pages, as data.
- [World Creator — Spec](world-creator-spec.md) — the world these beats have to be placed in.
- [Crafting & Recycling](../2-systems/crafting-and-recycling.md) — "a dropped weapon remembers its wielder in fragments", as a system.
- [Mixamo Animation Integration — Workflow](../4-art/mixamo-animation-workflow.md) — the charcoal-body rig the fiction is built on.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — M2, where the world still has no reasons in it.
