---
status:
owner: vince
last-updated: 2026-09-09
tags:
  - process
---

# Prototyping a UI Before Building It

How the text-based inventory went from an idea to shipped C# on 2026-09-09, written down as the order to do it
in next time. The worked example is the bag; the sequence is the same for any window.

**Why prototype at all:** the inventory was rebuilt in an afternoon and the design was settled *before* a line of
C# existed. Every argument about columns, sorting, the right-click menu and what a name should say happened in a
file that took seconds to change, instead of in a Unity window that takes minutes per round.

## The order, and the one step that is not optional

### 1. Import the game's own colours and fonts — never type one

**This is the step that was skipped, and it cost the whole design.** The prototype was built in fonts the project
does not own and a colour palette invented from scratch, and had to be restyled afterwards.

Run `tools/ui-tokens.py`. It reads the game's own code and writes `prototypes/ui-tokens.css` — the shared
palette, all five rarity tiers, and the game's three real font files loaded from where the game keeps them. The
page links that file and uses `var(--gold)`, `var(--rarity-rare-text)`, `var(--font-ui)`. **A hand-typed hex in a
mock page is a bug**, because the same value then lives in the code, in the token doc and in the page, and copies
drift.

The **[HUD/UI System](../2-systems/hud-ui-system.md)** doc's *Design tokens* section is still worth reading for
what the values MEAN and for the type sizes, which the game has no single table of and which stay hand-kept.

### 2. Run the checker before showing anyone the page

A token sheet is a document, and a document cannot vouch for its own freshness. On 2026-09-09 three of the nine
documented colours existed **nowhere in the project** — not in a script, a scene, a prefab or a material — and a
fourth was real but belonged to a different screen. The window border was documented as brown and is actually
gold.

**Mark the element that IS the window**, naming the screen in the game's own word for it, never a file name:

```html
<section class="pane" data-mocks="Inventory"> … </section>
```

Mark **every** piece the game draws separately, each with its own name — a window, its tooltip and its
right-click menu are three different screens in the game, with three different sets of colours:

```html
<div id="card" data-mocks="Item Tooltip"> … </div>
```

Then run `tools/check-ui-prototypes.py`. It opens the page in a real browser and reads the colour and font of
every element **painted inside a marked region** — not what the file says, what the screen shows. Everything
outside a marked region is ignored completely, which is what lets a page carry a title or an explanation for the
reader without any of it being mistaken for the design.

Every painted colour lands in one of four piles: from the shared palette (fine), declared by the screen this
region names (fine, and noted as not shared yet), **real but belonging to a different screen** (flagged), and
existing nowhere in the game (flagged). Fonts are checked the same way. The third pile is why the naming exists:
a borrowed colour passes any "does the game have this" test and is still wrong.

**A marked region with nothing inside it is FLAGGED, never called clean.** A right-click menu that only exists
once you right-click paints nothing at rest, so a tool that read it would report three colours from its own
frame and call that a pass. If a mock marks a state, the page must SHOW that state — the same way the inventory
page opens with the first row's tooltip already up.

### 2b. Check the SIZES against the real window, not just the colours

Colours can be read out of the game's source. Sizes cannot: a column's width is whatever the layout works out at
the window's real size, not the number typed in a file. So `tools/ui-geometry.py` stands the real screen up
inside the running editor, at the window's true size, measures every part and writes the result down; the checker
then reports every size the mock and the game disagree about, both numbers side by side.

Label the parts of the mock so they can be paired up — four attributes, and column names come from the headings
a person already reads:

```html
<div class="search" data-part="search">…</div>
<div class="hdr"    data-part="header">…</div>
<ul  class="rows"   data-part="list">…</ul>
<div class="row"    data-part="row">…</div>
```

The bag's own numbers, measured on 2026-09-09: the window gives the list **488px**, a row is **22px** tall, every
cell's text is **14px**, headings are **12px**, and the columns are Name 142 · Slot 78 · Tier 70 · Skills 46 ·
Atk/Def 58 · Out 48. The mock of it was drawn at 712px with 33.7px rows and 11–12px numbers, and its layout
demanded 710px — it did not fit the window it was a mock of. **That is the gap this step exists to close, and
none of it was visible to the colour check.**

A screen appears in the size check only once someone has written its recipe — a short piece of C# that builds it
offline. Adding a screen is adding a recipe, nothing else; a screen with none is told so rather than passed.

After any edit to that checker, run `tools/prove-ui-prototype-check.py`. It feeds the checker ten pages whose
answer is known — including a bad colour placed outside a marked region, which must be ignored; an empty region,
which must be flagged; and a mock built to the game's own measurements, which must report no size differences at
all — and fails if a verdict is wrong. A checker nobody has watched fail is not a checker, because an empty
report reads the same whether the page is clean or the tool is deaf.

The real check needs Chrome. Without one the checker falls back to reading the page's text, which also reads the
furniture, and it says so out loud rather than quietly doing the weaker job.

Anything the doc still gets wrong gets fixed in the doc **in the same session**, or the next prototype inherits
the same fiction. And when converting a value by hand: colours live as floats in `UiKit`, and Unity serialises
them as floats in scenes too, so **a hex search of a scene can never match** — convert before you search, or you
will report a real colour as missing.

### 3. Build the prototype as one local HTML file, in `prototypes/`

Not an artifact on a hosting service — one published this way vanished from the gallery the same day. A local
file is versioned beside the code it describes, has no content-security restrictions, and can eventually read
the project's real data instead of hand-copied values.

`prototypes/` is for pages you can **click**; `mockups/` is for static pictures of screens. Keep them apart.

**Inside a marked region, the page is the screen and NOTHING else.** No headline, no explanatory paragraph, no
colour legend, no slider that is not in the window, no invented tab, no footnote saying which data is real.
Every one of those is a thing the reader can mistake for the design — and the whole point of a mock is that what
you see is what gets built. A page that shows a ring-investment slider gets a ring-investment slider built.

Anything written **for the reader** goes outside every marked region, where the checker never looks and nobody
can confuse it with the window. Even there, keep it clear of the frame: a legend or a slider tucked directly
under the window still reads as part of it to a person, and no tool can fix that.

### 4. Feed it real data, and label what is invented

The inventory prototype used the actual rarity colours, all twenty-two composed sword skills with their real
two-letter symbols, the kit sizes a drop rolls, the +3-ATK-per-refine value and the ring's real soft-cap
curve. Only the base attack figures, the armour and the carried weight were invented.

Real data is what makes a prototype answer questions. Invented data that is not labelled is what makes it lie —
so **the list of what is invented goes in the reply that hands the page over, never on the page**, by the rule in
step 3. A footnote on the page is furniture, and furniture gets built.

### 5. Iterate on the cheap thing

The bag went through nine rounds in one sitting: rows, then the tooltip, then no focus rings, then the cursor
following, then armour, then search, then sorting, then the redundant header row removed, then right-click and
double-click. **Every one of those was a two-minute change in HTML and would have been a compile-and-look cycle
in the editor.**

Spend the rounds here. This is the whole point.

### 6. Before promising a port, measure what the UI layer can actually do

Ask of the real game, not of the prototype:

- **What already exists?** The cursor-following tooltip, its screen-edge clamping, double-click to equip and
  right-click to use were all already built. None of it needed porting.
- **What is genuinely new?** One thing: a runtime right-click menu. The game had none.
- **What does the prototype silently DROP?** This is the question that matters most. The grid had drag-to-reorder,
  drag-to-equip and a hover stat-delta preview. A text list with sortable columns has none of those for free, and
  **sorting and manual drag-ordering cannot both own the order.**
- **What does the prototype get WRONG about the game?** Ours invented a Quest tab and dropped the All tab. The
  game's tabs are All / Equip / Use / Material / Etc.

Write that list down and hand it over **before** building. A prototype that quietly loses three working features
is not an upgrade.

### 7. Port by reusing the interaction component, not by rewriting it

The bag's squares carried an `ItemSlot` plus a `SlotDragHandler`, and between them they owned hover, the tooltip,
the stat preview, clicking, double-clicking and both drag behaviours — all driven by the bound `BagEntry`, none
of it by the icon.

So each **text row** was given the same two components with none of their visual references wired, and every one
of those behaviours kept working without being reimplemented. The presentation was replaced; the behaviour was
not touched.

**Look for this before writing any new interaction code.** The one cosmetic consequence here: a drag's floating
ghost is a copy of the icon, so with no icon there is nothing to see mid-drag. The drop still resolves.

### 8. Keep the new work additive until it compiles

The two new files were written and compiled **before** anything was removed from the working window. The swap
itself was then a handful of lines, reversible in one edit. At no point was the inventory in a broken state.

### 9. Prove it reached the editor, and say what you did not verify

Compile check, then the editor's own recompile, then the console read in full. Then search the built assembly
for a name unique to the change **with a control that predates it** — the strongest control is a name you
DELETED, because its absence and the new name's presence together prove the swap landed, which neither proves
alone. Identifier names are UTF-8 in the assembly, string literals are UTF-16.

Then state plainly what remains unverified. None of the above proves the window looks right or feels right —
that is a play-test, and it is the user's.

## What this cost when done in the wrong order

| Skipped step | What it cost |
| --- | --- |
| Importing the game's colours and fonts (1) | the entire prototype restyled — fonts, palette, and a hover colour that broke the game's own rule that hover is never a rarity colour |
| Running the checker (2) | would have shipped a border colour that exists nowhere |
| Listing what the prototype drops (6) | three working features nearly replaced by silence |

## Related

- [HUD/UI System](../2-systems/hud-ui-system.md) — what the values mean, and the type sizes step 1 still reads by hand.
- [The Three-Brain Design Process](design-process.md) — the wider design flow this sits inside.
- [Reuse the Widget's Behaviour, Replace Only Its Look](../5-ai-learnings/reuse-the-behaviour-replace-the-look.md) — step 7 as a general lesson.
- [A Doc Cannot Vouch for Its Own Freshness](../5-ai-learnings/a-doc-cannot-vouch-for-itself.md) — step 2 as a general lesson.
