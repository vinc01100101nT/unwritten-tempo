# unwritten — Roadmap to Launch

RO-style **2.5D** action-RPG (3D world + billboard sprites), built in Unity. *Status as of 2026-07-02.*

> **Progress snapshot (2026-07-02):** against **Tier 1 (Minimum Shippable Steam game)** we are **~30–35% done**; against the **full MMORPG dream** ~**8–10%**. Nearest real target — a **playable vertical slice (M1 + thin M2)** — is ~**60–70%** there. Per-milestone: **M1 ~75%**, **M2 ~30%**, **M3 ~25%**, **M4 ~5%**, **M5 ~2%**. Biggest gaps blocking the demo: the **first fully-realized class kit** (only `skill.meteor` exists in the composed pool) and a **quest → dungeon → boss** story thread. The hard *technology* is essentially done; what remains is **content and connective tissue**.

> **What this doc is.** The single source of truth for *how far we are from a real, shippable game* and *what stands between here and a Steam launch*. It replaces the old phase-by-phase 2D plan (the 2D build order is now history — see **Appendix**). It is deliberately honest about scope: the dream is a full MMORPG; the **shippable path** is a single-player (or small co-op) RO-style ARPG first.

---

## TL;DR — the honest distance

- **Against the full MMORPG dream** (huge map, 8 classes w/ advancements, dungeons, NPCs, stories, **massively-multiplayer**, polished on Steam): we are roughly **5–10% of the way** there. That remaining 90% is, in industry terms, a **multi-year, multi-person, funded-studio** effort. The "MMO" alone is a *second entire game* the size of the one we have.
- **Against a Minimum Shippable Game** (single-player/small-co-op RO-style ARPG, one region, one class line, a few dungeons, a real story thread, on Steam): that is **reachable** on a plannable horizon — **months, not years** — and it is the target we should actually aim at first.
- **Strategy:** Ship Tier 1 first. *The MMO is what kills projects like this* — it's the most expensive, least-finished part, and it makes the whole thing feel "0% done" forever. Prove the game is fun and find an audience with Tier 1; let success justify Tier 2.

---

## Where we actually are (built & working)

The foundation — the *hardest part to start* — is real:

- **2.5D rendering** validated (spike `f195a5d` on branch `experiment/3d-world`): 3D world + billboard sprites, merged Kenney terrain platforms, per-feet depth sorting.
- **Spatial layer:** WASD/click movement, camera rig, grid **A** pathfinding (`Pathfinder3D` + `PathAgent`), portals/spawns, persistent global-systems scene with additive map loading.
- **Combat loop:** click-to-target, auto-swing, two-directional damage (`max(1, atk−def)`), pooled damage popups, monster AI (wander→chase→leash), spawners.
- **RPG numbers:** `Character` stats + per-level growth + equipment bonuses, XP→level-up→heal.
- **Signature systems (built 2026-06-18):** **TRIO-RING** percent-passive skill draft (MVP, all 10 stats, magic foundation) **and** the **RO-style flat stat layer** (STR/AGI/VIT/INT/DEX/LUK, allocatable points + StatPanel).
- **World tooling:** procedural town/map generator (bake-then-hand-finish), floor autotiling via RuleTiles.
- **Polish primitives:** day/night cycle, SFX, in-game DevSettings tuning panel, a perf pass + perf HUD.
- **Built since 2026-06-18 (folded in):**
  - **Save/load** progression-spine slice + **death/respawn** flow (`DeathScreen`) — M1's spine is real, not planned.
  - **RO combat depth:** `Element.cs` (10 elements + modifier table), unified **status/DoT/CC** controller (mobs + player), life-steal / reflect, multi-hit bursts + stun. Pre-renewal ATK/MATK formula port in progress.
  - **Skill system reboot:** legacy hand-written skills retired → **composed `SkillDef` pool** + `SkillCatalog`, per-def `SkillRunner`, **Grimoire (J)** browse/bind window. :warning: only `skill.meteor` authored so far — the class-kit content is the M1 gap.
  - **VFX pipeline:** Hovl particle library (`Vfx.PlayAt/Attach`), event→VFX **VfxCue** choke point, and the edit-mode **Vfx Composer** authoring window (replaced in-Play tuners).
  - **World life & content tooling:** ambient townsfolk + reactive barks (`AmbientLife`/`TownNPC`), tiered town interiors, weather (rain/snow/wet/reflections) tied toward a calendar, additional-lights lighting on ground + billboards, night lamps.
  - **Data-driven content:** mobs/items/maps as ScriptableObject tables (`MonsterDef`/`MonsterCatalog`, stable kebab ids) — the foundation the quest/loot systems will reference.
  - **Art pipeline (in-house props):** GPT→**Trellis 2**→Blender→Unity prop pipeline proven (Tier-1 town landmarks), + 8-dir sprite baker for characters/mobs. Locked style guide v2.1.
  - **HUD progression windows:** Inventory / Equipment / Character trio + data-driven hotbar (`IHotbarSkill`), locked charcoal+gold palette.

**Honest read:** this is a strong **single-player vertical-slice foundation** — and materially fuller than the 2026-06-18 snapshot (save/load, RO combat depth, VFX, world life, data tables, in-house art pipeline all landed since). It is *not yet a game* — still missing **content volume** (one class kit exists in skeleton, ~5 mobs), an **end-to-end progression spine** (quests/loot/advancement), the **game-shaped wrapper** (menus/settings/Steam), and **any networking**.

---

## TIER 1 — Minimum Shippable Game (the Steam target)

**Definition of done:** a stranger buys it on Steam, plays 3–8 hours, and feels they got a complete RO-flavored ARPG. **Single-player or small co-op.** No "MMO."

Milestones are in dependency order; each ends in something you can actually *play*.

### M1 — Core loop is fun for 30 minutes

- **Death → respawn/game-over flow** — *already built* (`DeathScreen.cs`: "YOU DIED" vignette, death pose, countdown, Respawn → revive at full HP + teleport to the map SpawnPoint). The roadmap's old "instant-revive" note was stale.
- **Save/load — progression spine (slice 1, built 2026-06-18):** level, XP, HP/MP, the six RO primary stats + unspent points, and the full Trio-Ring passive walk persist to JSON (`SaveData`/`SaveSystem`/`SaveManager`). Auto-loads on launch; auto-saves on level-up / respawn / quit / every 20s when changed. Self-bootstraps — no editor setup. Editor: *Tools ▸ unwritten ▸ Save ▸ …* to wipe/inspect. **Out of scope (→ M3):** equipment + bag persistence (UI-layer; lands with the inventory system).
- **Enemy variety + behavior feel (built 2026-06-18):** a 5-archetype **Bestiary** — Slime grunt, Bat swarmer (fast/fragile), Kappa bruiser (slow/tanky), Flam caster (kites at range), Cyclope darter (flees when wounded) — each its own sprite sheet + stats + tint/size. Two new *allocation-free* behavior axes on `MonsterAI3D` (`preferredRange` kiting, `fleeHpFraction` fleeing). *Tools ▸ unwritten ▸ Populate Mobs (Nature)* now scatters a weighted mix. Floating combat text / hit-flash / death-poof already existed.
- One fully-realized **starting class** (kit + a few TRIO skills that feel good) — *next up.* Plus a combat-feel polish pass (hit-stop / knockback / crit emphasis), and a real ranged projectile for casters (currently a magic "touch" at range).
- *Playable when:* you can start a character, fight a varied field, level, die, reload — **and your progress survives a restart.**

### M2 — A world worth exploring (one region)

- One hand-finished **region**: town hub + 3–5 connected field/zone maps + 1–2 **dungeons** (instanced, with a boss).
- **NPCs + dialogue + a quest system** (track/turn-in), and a **story thread** that gives the region a reason to exist.
- Map transitions, minimap, points of interest.
- *Playable when:* you can follow a quest from the town into a dungeon and beat its boss.

### M3 — Progression & loot spine

- **Items**: a real loot table, equip/inventory/stat-bonus UI, consumables, a shop/economy.
- **Class advancement**: at least ONE class → its advanced job (proves the advancement system end-to-end; the other classes are content, not new tech).
- Stat + skill respec, level cap for the demo.
- *Playable when:* killing things drops gear that meaningfully changes your build.

### M4 — Game-shaped product

- Main menu, settings (audio/video/controls), pause, controller support, a tutorial/onboarding.
- Audio pass (music + SFX coverage), UI polish, localization scaffolding.
- **Steam integration:** Steamworks app, store page, achievements, cloud saves, build pipeline.
- *Shippable when:* a first-time player needs zero outside instructions.

### M5 — Content multiplication (the real 80%)

- Scale to **7–8 classes** (+ their advancements), dozens of monsters, hundreds of items, more zones/dungeons, more story. *This is where most of the calendar time goes — each bullet is weeks-to-months.*
- Balance pass, QA/bug pass, "no unfinished work" polish.
- **Optional small co-op** (2–4 players, friend-hosted) — far cheaper than a true MMO and a big Steam selling point.
- *Shippable when:* the content depth matches the price tag.

> **Recommended first public step:** a **vertical-slice demo** (M1 + a thin M2) — one class, one dungeon, one story beat — hosted as a **WebGL build** or a **Steam "Next Fest" demo** to test whether it's fun *before* committing to M5's content grind.

---

## TIER 2 — Full MMORPG Vision (deferred until Tier 1 lands)

Only attempt after Tier 1 proves the game is fun and has an audience. This is a **business and an infrastructure project**, not just code.

- **Networking core (a second whole game):** authoritative game server, state replication, client prediction/reconciliation, interest management / zoning.
- **Backend & accounts:** login/auth, character & world **persistence** (database), sharding, an inventory/trade authority that can't be cheated.
- **Live service:** 24/7 hosting + ops cost, anti-cheat, monitoring, patching/live-ops, community/moderation, customer support.
- **MMO content systems:** parties/guilds, chat, trading, auction house, PvP, world bosses, social spaces.

**Reality check:** solo devs almost never ship a *true* MMO; they ship co-op or "online-lite." Plan for **funding and/or a small team and a backend specialist** before this tier — or scope it permanently to small co-op and call the dream answered.

---

## What it would realistically take

- **Honest playable demo (M1 + thin M2):** months of focused work — very achievable at our pace.
- **Full Tier 1 on Steam (M1–M5):** roughly a **year-plus** of sustained work, dominated by M5 content volume.
- **Tier 2 (true MMO):** **multi-year**, and the part that most needs people/money beyond just direction.

The good news: nothing in Tier 1 is blocked by missing *technology* — it's blocked by **content and connective tissue**, which is exactly the kind of work we can chew through milestone by milestone.

---

## Appendix — build history (the 2D → 2.5D path)

The original plan built a **2D** foundation in phases A–G (engine → character/world → towns/interaction → monsters/combat → UI shell → RPG numbers → skill draft), single-player-first with MMO deferred. As of **2026-06-16** the project **pivoted to RO-style 2.5D** (3D world + billboard sprites), validated by spike `f195a5d`; the RPG/UI/combat/art systems ported over while the spatial layer (movement/depth/pathfinding/maps) was rebuilt for 3D. On **2026-06-18** the skill system was redesigned to **TRIO-RING** (the old 28-node graph is retired) and the RO-style flat stat layer was added. Those built systems are folded into "Where we actually are" above; this appendix exists so the lineage isn't lost.

---

*Related docs: **Ring System — Current Facts (Trio-Ring)**, **Procedural Town Map Generator — Spec**, **Performance Checkup — My Routine**, **Dev Contribution Log**.*
