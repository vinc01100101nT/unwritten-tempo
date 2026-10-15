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
- `EquipmentInstance` — each gear piece is a unique instance (its grade, folds, written stats, cards and trademark or word), not a shared definition. Its rarity is saved on the copy (`EquipmentInstance.rarity`), travels in the copy's roll text, and is what `Rarities.Of` answers.

## Status

Visual spec LOCKED (v3, 2026-09-28): the Ragnarok-style light theme below. The token sheet is below, and every
value in it was read off the code.

**The uGUI port happened.** Measured 2026-08-30 against the scripts on disk: the inventory (a grid of boxes, with what you wear in a see-through frame on its left where the camera stands your own character) and the character window, the skill book, the hotbar, the buff bar, the cast bar, item and skill tooltips, the stamina bar, the status box, the kill combo, the minimap (always shown; it has no key), the Map window (M — the zone you stand in, with the world map behind a button; both list every mob and NPC that lives in a map with its level, the zone view beside the map it shows and the world view beside the pointer, and each mob with how many are alive there now over how many the map keeps ("12/30"), both from the server — alive alone for a creature only a random mix puts there; the zone view also draws every door out of the map it shows, in the colour of the kind of map it leads to and named for it — clicking a door opens that map, and clicking anywhere else on the picture sets the spot the ground arrows lead to, on any map, and a right click drops it), the name of each map flashed across the top of the screen in its kind's colour as you arrive, a settings window and the Auto-Combat window (System menu ▸ Auto-Combat; see [Auto-Combat](auto-combat.md)) all exist, and windows carry a drag handle and a close button. The character sheet is described in [Active Skill System — Implementation Notes](active-skill-system-v3/implementation-notes.md).

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
"Character Creation", "Inventory", "Skills", the status box, the drop
prompt. The bar is **`UiKit.TitleBarHeight` = 24 units high on every one of them**, whatever it carries, and is
built by the one `UiKit.Header` (through `DressWindow`) or, for a menu's strip, `UiKit.TitleBar`.

At the bar's right-hand end an in-game window carries **_ (minimise)** and **X (close)**, close right-most.
Minimise leaves only the bar on screen, still draggable; pressing it again brings the window back exactly as it
was. The front screens (Login, Your Characters, Character Creation) carry neither button and do not drag. The
status box minimises but has no close. The card dialog and the drop prompt carry no bar buttons — each has its
own Close / Cancel below.

## List windows

**The Shop, the Station and the Keeper are one window shape, built by one set of pieces in `UiKit`** — and the System
menu's entries are the same button:

- `UiKit.ListWindow` — a window whose body is one scrolling column.
- `UiKit.ListLine` — a line of text in it, one or more rows tall.
- `UiKit.ListButton` — a full-width button of a stated height (30 in a list window, 34 in the System menu).
- `UiKit.ListDeal` — a line that is a deal: what it is on the left, one or two 70-wide presses on the right
  (Take 1 / Take all, Store 1 / Store all).

A window that needs a list is built from these, never from a copy of them. On the Keeper's storage rows, an item's
**name** is written in the colour of its rarity (`Rarities.Coloured`), as the bag writes it. The Shop's three lists (For
sale, Cart, Inventory) use the list's **item row** (`UiListView.Slot.ShowItem`): the name in its rarity's colour, cut
with "…" when room runs short, the count, a small tag ("Buy back", "2 free", "Buy"/"Sell"), and the price of one, whole,
at the right — the row carries the item card on hover and can be dragged.

**A list that can grow long builds only the rows in view** — `UiListView`, which the Keeper window uses. It draws the
same three kinds of line at the same sizes, colours and 70-wide presses, keeps only enough rows to fill the view, and
re-points them as the list scrolls; its owner says how many lines there are, how tall each is, what one shows when it
comes into view and what a press does. It redraws at most once a frame, after a scroll or a change. 600 stored things
cost the twenty-odd rows on screen; built line by line they were about 10,800 objects made and destroyed per press.

## The status box

Top-left by default, draggable by its bar, and it remembers where it was left. Its bar carries the character's
name, HP and MP. Its body shows **Lv**, **Ring Lv** and **[held weapon] Mastery Lv**, each with its experience bar
(the mastery row only while a weapon is held), then the character's Unsigned — the game's currency, every amount written as the number and a u, "250u" — with a + beside it that shows or hides a button for
every HUD window: Inventory, Character, Skills, Quests, Calendar, Map, Chat, Settings, System. Each button
opens its window by the key that already opens it (`Hotkeys.PressOnScreen`, a one-frame press of that key from
the screen), so it adds no second way into any window. It replaced the experience strip along the bottom of
the screen and the health and mana bars at the bottom left.

## The kill combo

On the left edge, between the status box and the chat. After a kill, a run of kills starts: every kill within
**10 seconds** of the one before adds one to it, and a kill after that starts it again at 1. The run multiplies what
each kill pays — **1% more per kill in it, up to 50% at 50 kills** — the experience, and the Unsigned a saved character
pays. Never a flat amount, so the bonus is the same share of a creature at any level.

The count is drawn big in the damage numbers' pixel face and the rising-total yellow, and pops every time it climbs.
Under it, "COMBO" with the bonus it is worth ("COMBO +12%"), and a thin yellow bar draining towards the moment the
run ends; at that moment the count, the word and the bar go. Under those, always once the first kill has landed:
**EXP/h** and **u/h**, what kills paid over the last 10 minutes as a rate per hour (never over less than a
minute), with thousands separators. A rate is updated with every kill and stays as it was between kills.

Every number is the server's: it counts the run, multiplies the pay and works out the rates, and tells only the
person who earned the kill (one message per kill, and one when a saved character pays its Unsigned). Their screen
copies the numbers and compares the end moment it was sent with the clock to stop drawing the count — the same
compare the hotbar makes for a cooldown. The run and the meter are not saved: a door or a disconnect starts both
again. Only kills that pay count — a summon or a training dummy is no part of a run; a kill that pays 0
experience under a world event, or at level 99, still counts. A kill pays no Unsigned of its own, so u/h reads 0
unless saved characters paid some.

## The chat's tabs

The chat window has three tabs: **All**, **PM** and **Combat Log**. All shows said lines, skill calls, arrivals
and private messages; PM only private messages. **Combat Log** shows every blow you deal and take ("You hit Poring
for 12", "Poring missed you", "You took 3 from bleeding"), every kill and what it paid ("You defeated Poring",
"+120 EXP (+12 from combo)", "+30u (+3u from combo)") and every item you obtain — and none of it ever appears
under All. Each kind keeps its own last 200 rows. The combat lines are kept as the server's numbers and turned into
words only while the Combat Log tab is open, so a fight costs nothing while you read another tab.

## Design tokens — read off the code 2026-09-28

**Every value here was verified against the running game's source, not against an earlier version of this
doc.** Colours are authored as floats in `UiKit`; the hex beside each one is that float converted. When a value
here and the code disagree, the code is right and this table is stale — re-verify before trusting it.

### Surfaces

| Token | Hex | What it is |
| --- | --- | --- |
| PanelFill | `#F7F8FA` | every window's body, and a speech bubble |
| PanelRaise | `#EDF1F7` | a sub-panel inside a window — a stat row, the chat log |
| SlotFill | `#FFFFFF` | the well of a text field, the bag's search box, a checkbox, a filled box in the bag, storage, the equipment frame or the hotbar |
| SlotEmpty | `#52525E` at 60% | an empty box — a hotbar slot, a bag box or an equipment slot with nothing in it |
| WindowEdge | `#7F97BD` | the edge around every window, tooltip, menu and HUD plate |
| TitleBarTop | `#E6EEFA` | a window's title bar fades from this at its top edge… |
| TitleBarBottom | `#B9CCE9` | …down to this at its bottom edge |
| ButtonFill | `#E9EBEF` | a button's face |
| ButtonEdge | `#B5BDC9` | a button's edge, a text field's edge, a slider track, an unselected tab's edge, a filled box's edge |
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
| TextOnDim | `#FFFFFF` | text laid straight over a darkened screen — the world map, the reconnect notice |
| Accent | `#5B86C9` | hover edge, text caret, checkbox tick, slider fill, a selected border, highlighted numbers, the fill of a chosen tab (its word bold and white) and of the equipment slot a dragged piece belongs in |
| Good | `#2E7D41` | a stat that went up |
| Bad | `#B83A3A` | a stat that went down; a refusal |
| PmText | `#867200` | a private message in the chat — a dark gold yellow, since bright yellow cannot be read on a window |
| RarityRare | `#2A62B8` | a Rare item's name (5.59:1 on a window) |
| RarityEpic | `#8A3FB8` | an Epic item's name (5.68:1) |
| RarityLegendary | `#A85400` | a Legendary item's name — a deep orange gold (5.03:1) |

### Shapes

Windows, tooltips, menus and HUD plates have **5-unit rounded corners**; buttons, fields and checkboxes **3**.
There are **no corner ornaments and no divider flourishes** on any window. The shapes are white images the
game tints — `Resources/UI/Theme`, written by `tools/ui-theme-sprites.py` — so a colour change never touches
them; a corner-radius change means re-running that script.

### Item colour — by rarity

**An item's name is written in its rarity's colour**: Common in **TextMain**, Uncommon in
**Good**, Rare in **RarityRare**, Epic in **RarityEpic**, Legendary in **RarityLegendary**. A piece of gear is as
rare as its own copy says — rolled when a creature dropped it, Common otherwise — a material goes by its kind and
tier, and everything else is Common (see
[V4 §4](gameplay-system-v4/README.md)) — and one function answers both the rarity and its colour (`Rarities`), so
every screen agrees. It colours the name in the item tooltip (whose next line names the rarity), the bag's boxes, an item
linked in chat, the worn list of a unit you select, and the names on the floor. A drop's sparkle is one colour for everything. What a
piece is worth is still written in words — its **grade and its folds**, "B +6" — and a grade is never coloured.

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

- **Phase 1 — DONE.** The Inventory and Character windows exist; what you wear is in the equipment frame beside the bag. Hotbar + stat allocation were already done.
- **Phase 2 — mostly done.** The buff/debuff bar and both tooltips (item and skill) are built. **The quest tracker** has a quest system to follow since 2026-10-04: the Quests window (J), and the followed quest's route drawn on the ground and on the minimap.
- **Phase 3 — no longer "deferred", and no longer one phase.** Chat is built. Guild / marketplace / mail are still absent, but they are now ordinary features on a real networked game rather than a bracket nobody expected to open.
- **Phase 4 — partly done.** Windows are movable by their title bar, and in-game windows carry minimise and close buttons. **Saved layouts and UI scaling are not built**, and UI Toolkit was never revisited — it is still uGUI throughout.

## Related

- [Active Skill System — V3](active-skill-system-v3/README.md) — what the hotbar, skill book and tooltips are showing.
- [Active Skill System — Implementation Notes](active-skill-system-v3/implementation-notes.md) — the Character sheet's live state readout.
- [Weapon Roster — 14 Identities](active-skill-system-v3/weapon-roster-12-identities.md) — where each weapon's skill pool comes from.
- [Ring System — Current Facts (Trio-Ring)](ring-system-trio-ring.md) — what the draft panel drafts.
- [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md) — every number the character sheet prints.
- [Auto-Combat](auto-combat.md) — the Auto-Combat window the System menu opens.
- [Networking — Implementation Notes](networking/implementation-notes.md) — the sign-in door and the reconnect notice now in front of all of it.
- [Roadmap to Launch](../1-planning/roadmap-to-launch.md) — the HUD line under M1.
