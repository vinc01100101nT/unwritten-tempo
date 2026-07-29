# HUD/UI System — Design & Relay (unwritten)

A two-AI collaboration: **GPT** = design/UX/systems lead, **Claude** = engineering/architecture lead, **user** = relay between them. This doc is the running source of truth for the HUD/UI workstream.

Tracking issue: **VIN-22** (Inventory + Equipment + Character window trio).

## Locked foundations

- **Camera** — RO-style 2.5D: true 3D world with billboard sprites, ¾ overhead/isometric-ish framing via a shared `CameraRig`. HUD assumes a fixed overhead view + feet-Y depth sorting. (Ragnarok bucket, not FFXIV/Albion.)
- **Combat** — RO-style hybrid: click-to-move + click/point target, skills fired from the hotbar by key or click. Already have multi-hit bursts, status (stun/DoT), 10 elements, leech/reflect. No action-cam reticle.
- **Skill count** — small per-class kit (classes = an active-skill identity layer on universal stats/passives; Swordsman #1 = Bash + Charge). A single 10-slot hotbar is enough; no multi-page action bars.
- **Stats** — classic RO STR/AGI/VIT/INT/DEX/LUK as the flat layer + a soft-capped % skill-multiplier layer + planned equipment rarity / card slots / enhancement (+7 etc).
- **UI tech** — stay **uGUI**. We already have working, data-driven uGUI and won't rewrite it in UI Toolkit yet:
  - `HotbarPanel` + `IHotbarSkill` (any active auto-appears; click + keybind both call `Activate()`; wired to Bash/Charge/Caster)
  - `StatPanel` (RO stat allocation)
  - `DevSettings` panel (P key, live tunables)
  - pooled `DamagePopup`, `SpriteSheetCache`
  - skill icons from a Ninja Adventure UI set; shell baked via the **Build UI Shell** editor command
  - Revisit UI Toolkit only for movable windows + saved layouts (later phase).
- **Scope** — SOLO RO-like. Chat / Guild / Party / Marketplace / Mail are **deferred** (fake-MMO liveliness comes later via ambient simulation, not networking).

## Architecture agreements (with GPT)

- Prefab-per-panel, modular (HUD layer / window layer / overlay layer).
- Event-driven data flow: `Character` owns state → events → UI listens. UI never stores game values.
- Item definitions = ScriptableObjects; saves store instance IDs + amounts.
- `EquipmentInstance { ItemID, UpgradeLevel, Rarity, InsertedCards[] }` — each gear piece is a unique instance, not a shared definition.

## Status

Visual spec LOCKED (v2). Selection = cyan inner ring, hover = cyan outer bloom (both sit over an untouched rarity border — no collision). Portrait = billboard front-facing frame (no separate portrait-art pipeline). Token sheet from GPT: bg #0D0E10→#121316, panel fill #15161A, border #2A2316 + gold accents, gold #C8A862→#E6C77A, text #E7E3D5 / #A79E88; Cinzel (titles 28-32 / sections + item names 16-18) + Inter (body 13-14 / numbers 14-16). Next = Unity uGUI port.

## Asset sourcing (settled with GPT)

- **Flourishes** — GPT authors a custom "unwritten" ornament kit (corner filigree, dividers, empty-slot watermark, portrait backplate) as transparent gold-only PNGs, separable pieces (not baked mockups). Claude implements them as a **decorative overlay layer** (Panel → Border → Background → Flourish Overlay) so future re-skins stay possible. Kit delivered later; build structural UI first, drop overlays in when they land.
- **Item & card icons** — ~95% from a curated icon pack (breadth: weapons/armor/consumables/materials/cards), ~5% custom GPT-generated "hero" art for named Legendaries / boss drops / quest artifacts / class relics. The slot frame (rarity + N + pips) unifies mixed sources. Rule: Common→Epic = pack, Legendary = custom.
- **Stat icons** — reuse the approved brand-sheet family (Might/Magic/Finesse) for BOTH base (STR/AGI/VIT/INT/DEX/LUK) and derived (ATK/MATK/DEF/MDEF/CRIT/ASPD/HIT/FLEE). One icon language, no second set.
- **Built in uGUI, no art files** — panel fills/gradients, borders, cyan selection ring + hover bloom, +N badges, ◇ pips, tabs, stat-row strips, dividers. **Fonts** Cinzel + Inter = open-license, imported as TMP assets.

## Phase plan (revised for solo scope)

- **Phase 1** — Inventory, Equipment, Character windows (the gap). Hotbar + stat alloc already done.
- **Phase 2** — Quest tracker, buff/debuff bar, tooltips.
- **Phase 3 (deferred)** — anything multiplayer (chat/guild/marketplace/mail).
- **Phase 4** — movable windows + saved layouts + UI scaling (reconsider UI Toolkit here).
