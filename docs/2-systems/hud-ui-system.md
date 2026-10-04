---
status:
owner: vince
last-updated: 2026-10-03
tags:
  - systems-combat-design
---

# HUD/UI System — Design & Relay (unwritten)

A two-AI collaboration: **GPT** = design/UX/systems lead, **Claude** = engineering/architecture lead, **user** = relay between them. This doc is the running source of truth for the HUD/UI workstream.

Tracking issue: **VIN-22**.

## Locked foundations

- **Camera** — RO-style 2.5D: true 3D world with billboard sprites, ¾ overhead/isometric-ish framing via a shared `CameraRig`. HUD assumes a fixed overhead view + feet-Y depth sorting. (Ragnarok bucket, not FFXIV/Albion.)
- **Combat** — RO-style hybrid: click-to-move + click/point target, skills fired from the hotbar by key or click. Already have multi-hit bursts, status (stun/DoT), 10 elements, leech/reflect. No action-cam reticle.
- **Skill count** — skills belong to a *weapon*, not a class, and are bought with that weapon's mastery points (one per mastery level) in the **Skills** window (K): each names the mastery level at which it opens, out of that weapon's pool — the Sword pool alone is 22 composed skills. The window draws one weapon as a timeline of mastery 0–70 with a + button on each skill. **The bar is three rows of nine slots**, one bar per character whatever weapon is held: row 1 answers F1–F9, row 2 the number keys 1–9, row 3 the keys the player assigns in **Settings ▸ Keys**. Any slot holds an active skill or an ultimate, a usable item, or one exact piece of gear; there is no separate ultimate seat. A passive never goes on the bar: it is never pressed. See [Weapon Roster — 14 Identities](active-skill-system-v3/weapon-roster-12-identities.md).
- **Stats** — classic RO STR/AGI/VIT/INT/DEX/LUK as the flat layer + a soft-capped % skill-multiplier layer + what a crafted piece carries: its **quality grade**, its **folds**, the stats **Ink** has written on it, and its card sockets. All of it is live and all of it persists.
- **UI tech** — stay **uGUI**. We already have working, data-driven uGUI and won't rewrite it in UI Toolkit yet:
  - `HotbarPanel` + `IHotbarSkill` (a slot is pressed by its key or a click). The player fills the bar by dragging from the Skills window (K) or the bag; dragging a slot onto another swaps them, dragging it off the bar empties it.
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

Visual spec LOCKED (v3, 2026-09-28): the Ragnarok-style light theme below. The token sheet is below, and every
value in it was read off the code.

**The uGUI port happened.** Measured 2026-08-30 against the scripts on disk: the inventory and character windows (what you wear is listed at the top of the inventory), the skill book, the hotbar, the buff bar, the cast bar, item and skill tooltips, the stamina bar, the status box, the minimap (always shown; it has no key), the Map window (M — the zone you stand in, with the world map behind a button) and a settings window all exist, and windows carry a drag handle and a close button. The character sheet is described in [Active Skill System — Implementation Notes](active-skill-system-v3/implementation-notes.md).

## The theme rule

**There is ONE interface theme, and it lives in `UiKit`.** Every colour a window, panel, button, label, bar or
floor label is drawn in is a named row of `UiKit`'s palette, and every rounded shape and the title bar come from
its helpers (`Window` / `DressWindow`, `Btn`, `Field`, `Card`, `Plate`, `TitleBar`, `Round`). **A script never
types an interface colour of its own** — a new need is a new row in the palette, named for what it paints, and
rich text reads its tint with `UiKit.Hex(...)`. Changing the look of the whole game is an edit to that one file.

**What keeps its own colours, deliberately:** the floating combat numbers (damage, MISS, parry — anything
`DamagePopup` draws); colours that MEAN something — health red/yellow/green, mana blue, a skill's or weapon's
colour, an element's tint, a weather's tint, a season's tint, the minimap's dots, the dye swatches; and the
things drawn straight over the world with a black edge (the name and health plate over the creature you point at
and over every person, the cast bar), which stay light text on the world. **A meaning colour drawn as TEXT on a window goes through
`UiKit.Readable`**, which darkens it — same hue — only as far as it must go to read at 4.5 : 1 on the window body.

## Window headings

**Every window's heading is its blue title bar, and the title is always in it** — "Login", "Your Characters",
"Character Creation", "Inventory", "Skills", the status box, the right-click menu, the card dialog, the drop
prompt. The bar is **`UiKit.TitleBarHeight` = 24 units high on every one of them**, whatever it carries, and is
built by the one `UiKit.Header` (through `DressWindow`) or, for a menu's strip, `UiKit.TitleBar`.

At the bar's right-hand end an in-game window carries **_ (minimise)** and **X (close)**, close right-most.
Minimise leaves only the bar on screen, still draggable; pressing it again brings the window back exactly as it
was. The front screens (Login, Your Characters, Character Creation) carry neither button and do not drag. The
status box minimises but has no close. The card dialog and the drop prompt carry no bar buttons — each has its
own Close / Cancel below.

## The status box

Top-left by default, draggable by its bar, and it remembers where it was left. Its bar carries the character's
name, HP and MP. Its body shows **Lv**, **Ring Lv** and **[held weapon] Mastery Lv**, each with its experience bar
(the mastery row only while a weapon is held), then the zeny with a + beside it that shows or hides a button for
every HUD window: Inventory, Character, Skills, Quests, Calendar, Map, Chat, Settings, System. Each button
opens its window by the key that already opens it (`Hotkeys.PressOnScreen`, a one-frame press of that key from
the screen), so it adds no second way into any window. It replaced the experience strip along the bottom of
the screen and the health and mana bars at the bottom left.

## Design tokens — read off the code 2026-09-28

**Every value here was verified against the running game's source, not against an earlier version of this
doc.** Colours are authored as floats in `UiKit`; the hex beside each one is that float converted. When a value
here and the code disagree, the code is right and this table is stale — re-verify before trusting it.

### Surfaces

| Token | Hex | What it is |
| --- | --- | --- |
| PanelFill | `#F7F8FA` | every window's body, and a speech bubble |
| PanelRaise | `#EDF1F7` | a sub-panel inside a window — the bag's column header, a stat row, the chat log |
| SlotFill | `#FFFFFF` | the well of a text field, the bag's search box, a checkbox |
| WindowEdge | `#7F97BD` | the edge around every window, tooltip, menu and HUD plate |
| TitleBarTop | `#E6EEFA` | a window's title bar fades from this at its top edge… |
| TitleBarBottom | `#B9CCE9` | …down to this at its bottom edge |
| ButtonFill | `#E9EBEF` | a button's face |
| ButtonEdge | `#B5BDC9` | a button's edge, a text field's edge, a slider track, an unselected row's edge |
| Rule | `#C9D3E2` | a divider line, a bar's empty track |
| Hover | `#D6E5FB` | behind whatever the pointer is over; a text selection |
| CloseFill | `#D9DFE8` | the close button |

**The title bar is the one gradient**: it fades top to bottom between the two title-bar colours. Every other
surface is a flat fill. `#0D0E10` is a real authored colour but it belongs to the **sign-in screen** (its camera
background and veil), not to any window.

### Text

| Token | Hex | What it is |
| --- | --- | --- |
| TextMain | `#1B1B1B` | all primary copy |
| TextDim | `#6C7380` | labels, units, secondary copy |
| TitleText | `#22324D` | a window's title and headings |
| ButtonText | `#222222` | a button's label |
| TextOnDim | `#FFFFFF` | text laid straight over a darkened screen — the world map, the reconnect notice, the loading screen |
| Accent | `#5B86C9` | hover edge, text caret, checkbox tick, slider fill, a selected border, highlighted numbers |
| Good | `#2E7D41` | a stat that went up |
| Bad | `#B83A3A` | a stat that went down; a refusal |

### Shapes

Windows, tooltips, menus and HUD plates have **5-unit rounded corners**; buttons, fields and checkboxes **3**.
There are **no corner ornaments and no divider flourishes** on any window. The shapes are white images the
game tints — `Resources/UI/Theme`, written by `tools/ui-theme-sprites.py` — so a colour change never touches
them; a corner-radius change means re-running that script.

### Item colour — retired

**Nothing colours an item by a tier any more.** [V4](gameplay-system-v4/README.md) removed rarity from the game
on 2026-09-20: an item's name, its bag row, its card and a drop's sparkle are all drawn in the ordinary text
colour, and what a piece is worth is written in words — its **grade and its folds**, "B +12". The five-tier
palette that used to live here (Common grey through Legendary gold) is gone from the code with it.

### Fonts

**Two.** Both are loaded at runtime as TextMeshPro assets; the UI is uGUI + TextMeshPro throughout.

| Font | Role |
| --- | --- |
| DejaVu Sans Condensed | everything — titles, headings, body, numbers, labels, names |
| PixelFont | the floating combat numbers only |

DejaVu Sans Condensed is the free stand-in for Tahoma: Tahoma's own licence (Microsoft's) forbids shipping it
inside a game. Its licence text ships beside it in `Resources/Fonts`.

**There is no monospaced font in the project.** Anything needing columns to line up must align them by layout
and by tabular figures, not by character width.

### Type sizes

A window's title bar is **14, bold**; a button's label is **14, regular**. Elsewhere, 13–14 is body, 16–18 is
sections and item names, and 28+ is a front-screen heading (sign-in, character select).

### Text crispness

One shared rule, and every canvas in the game must route through it: the HUD bakes glyphs at **3×** density, so
text stays sharp when the window is smaller than the 1280×720 reference. A canvas that skips it goes blurry.

## Asset sourcing (settled with GPT)

- **Item & card icons** — ~95% from a curated icon pack (breadth: weapons/armor/consumables/materials/cards), ~5% custom GPT-generated "hero" art for boss drops, quest artifacts and named relics.
- **Stat icons** — reuse the approved brand-sheet family (Might/Magic/Finesse) for BOTH base (STR/AGI/VIT/INT/DEX/LUK) and derived (ATK/MATK/DEF/MDEF/CRIT/ASPD/HIT/FLEE). One icon language, no second set.
- **Built in uGUI** — the palette, two corner radii and the title bar fade above; no painted frame art.

## Phase plan — where it actually stands (measured 2026-08-30)

- **Phase 1 — DONE.** The Inventory and Character windows exist; what you wear is listed at the top of the inventory. Hotbar + stat allocation were already done.
- **Phase 2 — mostly done.** The buff/debuff bar and both tooltips (item and skill) are built. **The quest tracker is not**, and cannot be: no quest system exists anywhere in the game.
- **Phase 3 — no longer "deferred", and no longer one phase.** Chat is built. Guild / marketplace / mail are still absent, but they are now ordinary features on a real networked game rather than a bracket nobody expected to open.
- **Phase 4 — partly done.** Windows are movable by their title bar, and in-game windows carry minimise and close buttons. **Saved layouts and UI scaling are not built**, and UI Toolkit was never revisited — it is still uGUI throughout.

## Related

- [Active Skill System — V3](active-skill-system-v3/README.md) — what the hotbar, skill book and tooltips are showing.
- [Active Skill System — Implementation Notes](active-skill-system-v3/implementation-notes.md) — the Character sheet's live state readout.
- [Weapon Roster — 14 Identities](active-skill-system-v3/weapon-roster-12-identities.md) — where each weapon's skill pool comes from.
- [Ring System — Current Facts (Trio-Ring)](ring-system-trio-ring.md) — what the draft panel drafts.
- [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md) — every number the character sheet prints.
- [Networking — Implementation Notes](networking/implementation-notes.md) — the sign-in door and the reconnect notice now in front of all of it.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the HUD line under M1.
