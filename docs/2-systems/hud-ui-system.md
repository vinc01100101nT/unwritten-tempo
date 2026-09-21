---
status:
owner: vince
last-updated: 2026-09-13
tags:
  - systems-combat-design
---

# HUD/UI System — Design & Relay (unwritten)

A two-AI collaboration: **GPT** = design/UX/systems lead, **Claude** = engineering/architecture lead, **user** = relay between them. This doc is the running source of truth for the HUD/UI workstream.

Tracking issue: **VIN-22**.

## Locked foundations

- **Camera** — RO-style 2.5D: true 3D world with billboard sprites, ¾ overhead/isometric-ish framing via a shared `CameraRig`. HUD assumes a fixed overhead view + feet-Y depth sorting. (Ragnarok bucket, not FFXIV/Albion.)
- **Combat** — RO-style hybrid: click-to-move + click/point target, skills fired from the hotbar by key or click. Already have multi-hit bursts, status (stun/DoT), 10 elements, leech/reflect. No action-cam reticle.
- **Skill count** — skills belong to a *weapon*, not a class, and are LEARNED by practising it: each names the mastery level at which it opens, out of that weapon's pool — the Sword pool alone is 22 composed skills. **The bar is four numbered slots plus the ultimate**, and above them a read-only row of four smaller squares shows the passives that are running. See [Weapon Roster — 12 Identities](active-skill-system-v3/weapon-roster-12-identities.md).
- **Stats** — classic RO STR/AGI/VIT/INT/DEX/LUK as the flat layer + a soft-capped % skill-multiplier layer + what a crafted piece carries: its **quality grade**, its **folds**, the stats **Ink** has written on it, and its card sockets. All of it is live and all of it persists.
- **UI tech** — stay **uGUI**. We already have working, data-driven uGUI and won't rewrite it in UI Toolkit yet:
  - `HotbarPanel` + `IHotbarSkill` (any active auto-appears; click + keybind both call `Activate()`). *It used to name Bash / Charge / Caster as what it was wired to — those hand-written skills are gone; the bar is arranged by the player from what they have learned.*
  - `StatPanel` (RO stat allocation)
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

Visual spec LOCKED (v2). Hover and selection are tinted cyan. Portrait = billboard front-facing frame (no separate portrait-art pipeline). The token sheet is below, and every value in it was read off the code.

**The uGUI port happened.** Measured 2026-08-30 against the scripts on disk: the inventory and character windows (what you wear is listed at the top of the inventory), the skill book, the hotbar, the buff bar, the cast bar, item and skill tooltips, the stamina and XP bars, the world map, a settings window and a wardrobe panel all exist, and windows carry a drag handle and a close button. The character sheet is described in [Active Skill System — Implementation Notes](active-skill-system-v3/implementation-notes.md).

## Design tokens — read off the code 2026-09-09

**Every value here was verified against the running game's source, not against an earlier version of this
doc.** Colours are authored as floats in `UiKit`; the hex beside each one is that float converted. When a value
here and the code disagree, the code is right and this table is stale — re-verify before trusting it.

### Surfaces

| Token | Hex | Alpha | What it is |
| --- | --- | --- | --- |
| Panel fill | `#15161A` | 0.98 | every window's body |
| Panel raise | `#212228` | 0.98 | detail and sub-panels — the same charcoal ~6% lighter |
| Slot fill | `#121317` | 1.0 | the well behind a text field, the bag's search box and a slider track, darker than the panel it sits on |

There is **no background gradient and no gradient of any kind** — every surface is a flat fill. `#0D0E10` is a
real authored colour but it belongs to the **sign-in screen** (its camera background and veil), not the HUD.

### Text

| Token | Hex | What it is |
| --- | --- | --- |
| Text main | `#E7E3D5` | warm ivory; all primary copy |
| Text dim | `#A79E88` | labels, units, secondary copy |

### Accents

| Token | Hex | Alpha | What it is |
| --- | --- | --- | --- |
| Gold | `#C8A862` | 1.0 | **the window border**, and every gold accent |
| Gold (header wash) | `#C8A862` | 0.16 | the title strip behind a window's header |
| Cyan | `#59F2FF` | 1.0 | hover and pressed tints on buttons, text carets, checkbox ticks, the selected character's border |
| Cyan glow | `#4CD9FF` | 0.22 | in the shared palette, but nothing in the game draws it at this alpha |

**The window border is GOLD, at full alpha.** There is no brown border and no gold gradient anywhere in the
project — not in a script, a scene, a prefab or a material. Do not add either: a border is one flat gold.

### Item colour — retired

**Nothing colours an item by a tier any more.** [V4](gameplay-system-v4/README.md) removed rarity from the game
on 2026-09-20: an item's name, its bag row, its card and a drop's sparkle are all drawn in the ordinary text
colour, and what a piece is worth is written in words — its **grade and its folds**, "B +12". The five-tier
palette that used to live here (Common grey through Legendary gold) is gone from the code with it.

Selection and hover are still cyan, and the window border is still one flat gold.

### Fonts

**Three, not two.** All are imported as TextMeshPro assets; the UI is uGUI + TextMeshPro throughout.

| Font | Role |
| --- | --- |
| Cinzel | window titles, section headings, item names |
| Inter | body copy, numbers, labels |
| PixelFont | the floating combat numbers only |

**There is no monospaced font in the project.** Anything needing columns to line up must align them by layout
and by tabular figures, not by character width.

### Type sizes

The four bands this doc used to lock (titles 28–32 / sections 16–18 / body 13–14 / numbers 14–16) describe body
and section copy correctly and nothing else. **Thirteen sizes are actually in use:** 11, 12, 13, 14, 15, 16, 17,
18, 19, 20, 22, 28, 34. The most common by far are **14 and 13** (body), then **15**, then 12, 16, 17.

Treat 13–14 as body, 16–18 as sections and item names, 28+ as a window title, and read the rest as drift rather
than as a scale anyone designed.

### Text crispness

One shared rule, and every canvas in the game must route through it: the HUD bakes glyphs at **3×** density, so
text stays sharp when the window is smaller than the 1280×720 reference. A canvas that skips it goes blurry.

## Asset sourcing (settled with GPT)

- **Flourishes** — GPT authors a custom "unwritten" ornament kit (corner filigree, dividers, portrait backplate) as transparent gold-only PNGs, separable pieces (not baked mockups). Claude implements them as a **decorative overlay layer** (Panel → Border → Background → Flourish Overlay) so future re-skins stay possible. Kit delivered later; build structural UI first, drop overlays in when they land.
- **Item & card icons** — ~95% from a curated icon pack (breadth: weapons/armor/consumables/materials/cards), ~5% custom GPT-generated "hero" art for boss drops, quest artifacts and named relics.
- **Stat icons** — reuse the approved brand-sheet family (Might/Magic/Finesse) for BOTH base (STR/AGI/VIT/INT/DEX/LUK) and derived (ATK/MATK/DEF/MDEF/CRIT/ASPD/HIT/FLEE). One icon language, no second set.
- **Built in uGUI, no art files** — flat panel fills, gold borders, cyan hover and selection tints, tabs, stat-row strips, dividers. **Fonts** Cinzel + Inter + PixelFont = open-license, imported as TMP assets (see the token sheet above; there are three, and PixelFont is the combat numbers alone).

## Phase plan — where it actually stands (measured 2026-08-30)

- **Phase 1 — DONE.** The Inventory and Character windows exist; what you wear is listed at the top of the inventory. Hotbar + stat allocation were already done.
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
