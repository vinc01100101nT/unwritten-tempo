---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - systems-combat-design
---

# HUD/UI System — Design & Relay (unwritten)

A two-AI collaboration: **GPT** = design/UX/systems lead, **Claude** = engineering/architecture lead, **user** = relay between them. This doc is the running source of truth for the HUD/UI workstream.

Tracking issue: **VIN-22** (Inventory + Equipment + Character window trio).

## Locked foundations

- **Camera** — RO-style 2.5D: true 3D world with billboard sprites, ¾ overhead/isometric-ish framing via a shared `CameraRig`. HUD assumes a fixed overhead view + feet-Y depth sorting. (Ragnarok bucket, not FFXIV/Albion.)
- **Combat** — RO-style hybrid: click-to-move + click/point target, skills fired from the hotbar by key or click. Already have multi-hit bursts, status (stun/DoT), 10 elements, leech/reflect. No action-cam reticle.
- **Skill count** — small per-class kit (classes = an active-skill identity layer on universal stats/passives). A single 10-slot hotbar is enough; no multi-page action bars. ⚠️ **The kit got much bigger than this assumed.** Skills now come from the *weapon*, not a class, and a weapon grants a rolled 4-skill kit out of a pool — the Sword pool alone is 22 composed skills. The 10 slots still hold, because a rolled kit is 4; see [Weapon Roster — 12 Identities](active-skill-system-v3/weapon-roster-12-identities.md).
- **Stats** — classic RO STR/AGI/VIT/INT/DEX/LUK as the flat layer + a soft-capped % skill-multiplier layer + planned equipment rarity / card slots / enhancement (+7 etc).
- **UI tech** — stay **uGUI**. We already have working, data-driven uGUI and won't rewrite it in UI Toolkit yet:
  - `HotbarPanel` + `IHotbarSkill` (any active auto-appears; click + keybind both call `Activate()`). *It used to name Bash / Charge / Caster as what it was wired to — those hand-written skills are gone; the bar is now filled from the equipped weapon's rolled kit.*
  - `StatPanel` (RO stat allocation)
  - `DevSettings` panel (P key, live tunables)
  - pooled `DamagePopup`, `SpriteSheetCache`
  - skill icons from a Ninja Adventure UI set; shell baked via the **Build UI Shell** editor command
  - Revisit UI Toolkit only for movable windows + saved layouts (later phase).
- **Scope** — ⚠️ **This line no longer holds.** It said SOLO RO-like, with Chat / Guild / Party / Marketplace / Mail deferred and liveliness faked by ambient simulation rather than networking. The game went the other way in August: there is a real server, a real sign-in door, and **no offline game at all**. **Chat is built.** Guild / Party / Marketplace / Mail are still absent. See [Networking — Implementation Notes](networking/implementation-notes.md).

## Architecture agreements (with GPT)

- Prefab-per-panel, modular (HUD layer / window layer / overlay layer).
- Event-driven data flow: `Character` owns state → events → UI listens. UI never stores game values.
- Item definitions = ScriptableObjects; saves store instance IDs + amounts.
- `EquipmentInstance { ItemID, UpgradeLevel, Rarity, InsertedCards[] }` — each gear piece is a unique instance, not a shared definition.

## Status

Visual spec LOCKED (v2). Selection = cyan inner ring, hover = cyan outer bloom (both sit over an untouched rarity border — no collision). Portrait = billboard front-facing frame (no separate portrait-art pipeline). Token sheet from GPT: bg #0D0E10→#121316, panel fill #15161A, border #2A2316 + gold accents, gold #C8A862→#E6C77A, text #E7E3D5 / #A79E88; Cinzel (titles 28-32 / sections + item names 16-18) + Inter (body 13-14 / numbers 14-16).

**The uGUI port happened.** Measured 2026-08-30 against the scripts on disk: the inventory / equipment / character trio, the skill book, the hotbar, the buff bar, the cast bar, item and skill tooltips, the stamina and XP bars, the world map, a settings window and a wardrobe panel all exist, and windows carry a drag handle and a close button. The character sheet is described in [Active Skill System — Implementation Notes](active-skill-system-v3/implementation-notes.md).

## Asset sourcing (settled with GPT)

- **Flourishes** — GPT authors a custom "unwritten" ornament kit (corner filigree, dividers, empty-slot watermark, portrait backplate) as transparent gold-only PNGs, separable pieces (not baked mockups). Claude implements them as a **decorative overlay layer** (Panel → Border → Background → Flourish Overlay) so future re-skins stay possible. Kit delivered later; build structural UI first, drop overlays in when they land.
- **Item & card icons** — ~95% from a curated icon pack (breadth: weapons/armor/consumables/materials/cards), ~5% custom GPT-generated "hero" art for named Legendaries / boss drops / quest artifacts / class relics. The slot frame (rarity + N + pips) unifies mixed sources. Rule: Common→Epic = pack, Legendary = custom.
- **Stat icons** — reuse the approved brand-sheet family (Might/Magic/Finesse) for BOTH base (STR/AGI/VIT/INT/DEX/LUK) and derived (ATK/MATK/DEF/MDEF/CRIT/ASPD/HIT/FLEE). One icon language, no second set.
- **Built in uGUI, no art files** — panel fills/gradients, borders, cyan selection ring + hover bloom, +N badges, ◇ pips, tabs, stat-row strips, dividers. **Fonts** Cinzel + Inter = open-license, imported as TMP assets.

## Phase plan — where it actually stands (measured 2026-08-30)

- **Phase 1 — DONE.** Inventory, Equipment and Character windows all exist. Hotbar + stat allocation were already done.
- **Phase 2 — mostly done.** The buff/debuff bar and both tooltips (item and skill) are built. **The quest tracker is not**, and cannot be: no quest system exists anywhere in the game.
- **Phase 3 — no longer "deferred", and no longer one phase.** Chat is built. Guild / marketplace / mail are still absent, but they are now ordinary features on a real networked game rather than a bracket nobody expected to open.
- **Phase 4 — partly done.** Windows are movable and carry drag handles and close buttons. **Saved layouts and UI scaling are not built**, and UI Toolkit was never revisited — it is still uGUI throughout.

## Related

- [Active Skill System — V3](active-skill-system-v3/README.md) — what the hotbar, skill book and tooltips are showing.
- [Active Skill System — Implementation Notes](active-skill-system-v3/implementation-notes.md) — the Character sheet's live state readout.
- [Weapon Roster — 12 Identities](active-skill-system-v3/weapon-roster-12-identities.md) — where a hotbar kit comes from.
- [Ring System — Current Facts (Trio-Ring)](ring-system-trio-ring.md) — what the draft panel drafts.
- [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md) — every number the character sheet prints.
- [Networking — Implementation Notes](networking/implementation-notes.md) — the sign-in door and the reconnect notice now in front of all of it.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the HUD line under M1.
