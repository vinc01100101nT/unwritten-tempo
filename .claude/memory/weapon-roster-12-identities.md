---
name: weapon-roster-12-identities
description: The locked 12-weapon roster + one-sentence identity per weapon for the V3 skill system (canonical doc).
metadata: 
  node_type: memory
  type: project
  originSessionId: e557a24b-2889-49de-ad81-838480cc4d4d
---

Weapon roster LOCKED 2026-07-27 (diversity pass with GPT). Identity comes from the equipped weapon's skill kit — see [[no-classes-weapons-define-gameplay]]. Canonical doc: **"Weapon Roster — 12 Identities (V3)"** `q17d4q80p9rj4exmezrxhnkc8h8bad3x` (child of the Active Skill System — V3 doc `q17f4w7tvgkycbp8w58vyzy3gs8azyah`).

The 12 (each owns one job; a skill that doesn't reinforce its weapon's one-sentence identity belongs on another weapon):
- **Might:** Sword (single-target duel burst), Greatsword (AoE cleave), Axe (Berserker snowball — dual-wield is ANIMATION only, maps to `WeaponType.Axe`), Warhammer (Stun+Knockback disable, NO Taunt), Spear (Pull+Dash-through+Vulnerability reposition), Shield (Taunt+Guard+Reflect protect, ~no damage).
- **Finesse:** Bow (mobile kite: Slow+Dash), Dagger (Execute finisher), Firearm (single big crit on a cooldown "reload rhythm").
- **Arcane:** Staff (burst AoE + Freeze/Slow — freeze was moved here, standalone Frost Orb CUT), Wand (heal/support), Scythe (DoT curse-attrition + Silence; the `hex_field` skill fits it).

Control-weapon split (kept clean): Shield="Hit me", Warhammer="You don't act", Spear="Stand over there".

Engine backlog from this roster: **Scythe** and **Shield** each need a NEW `WeaponType` + attack-animation bucket (Shield is OFF-HAND/left). **Firearm** reload approximated by a long per-skill cooldown for v1 (true ammo state deferred). Everything else maps to an existing `WeaponType` and rides KayKit attack clips already in-repo.

Kit-size model LOCKED: each weapon type has a **12-skill pool** (3 ultimates + 9 normals = 144 total). Each weapon DROP rolls a random **4-skill kit** = strictly **1 ult + 3 normals** from that pool (this IS the "loot surprise source"). Existing Sword/Wand drafts were ~5 fixed skills — re-slot them into the pool model. Also RE-AUDIT drafts vs identity — the Sword draft's **Whirlwind** (AoE) belongs on **Greatsword**, not Sword.

Player-facing wiki updated 2026-07-27: `wiki/content/weapons.md` (source doc q170dqg8b6pe370nj8sj3wwnmn8bb88q) + `wiki/content/skills.md` (source doc q176a4jkptgq6j4xwjp7ymh92x8ba9dw) carry the 12 identities + pool/drop rule.

Crafting/economy LOCKED 2026-07-28 → doc **"Weapon Crafting & Economy (V3)"** q17d85xnk3atf3np59achy4c6s8bc15w (under V3 parent). Key rules: drop kit is **rarity-scaled** (skill count 1→4, ult chance, and Potential all scale with rarity — SUPERSEDES the earlier "every drop = 1 ult + 3 normals"; that's now the high-rarity max). **Ult = drop-only, never craftable** (build anchor). **Potential** = per-weapon, non-refillable, gates ONLY skill Add + Reforge. **Weapon Essence** = universal recycle-powder for all mods (+Gold); Stones for heavy mods (refinement). Recycle bad weapon → Essence (yield scales base rarity) OR keep as same-type transmog (appearance-only, wardrobe). Wiki synced 2026-07-28: `skills.md` now rarity-scaled + "ult points at your build"; `weapons.md` rarity/crafting pointer; NEW page `wiki/content/crafting.md` (source doc q17cy3qese3w2k7hjcpz0nw91h8bdgr7, manifest.json updated). Implementation-Notes design doc intentionally NOT mirrored to the player wiki (internal/code-facing).
