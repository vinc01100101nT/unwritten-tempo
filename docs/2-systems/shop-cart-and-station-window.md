---
status: built 2026-10-14, not play-tested
owner: vince
last-updated: 2026-10-14
tags:
  - systems
  - ui
  - economy
---

# The Shop Cart, the Tax, and the Station Window

The shop and the crafting station, rebuilt from the prototype agreed with the user on 2026-10-14
(`prototypes/station-and-shop-grid.html`). The plan was attacked by a red team before any code; every one of its
findings is folded into what is written here.

## What did not change

- What a fold, a quench, an Ink and a recycle cost and give back. The station's server work is untouched, except that
  its offer carries the piece's numbers after one more fold.
- What selling anything pays, to the unit, for every count (proved for every old sell price from 0 to 20,000 by
  `tools/check-game-math.sh`).
- A tutorial's free buys stay free, and are counted first.
- Gear is never sold BY a shop (decided 2026-09-20); gear can be sold TO a shop.
- The bag window, the storage window and the item card.
- The server's log lines keep their wording ("bought … for … gold", "sold …", "bought back …").
- The first-gear tutorial and the station's guide spots — the forging design is still changing (the user, 2026-10-14).

## 1. One value per item, and Tax

Each item has ONE value, `Item.value`. A shop sells one for its value plus **Tax**, and buys one for its value less
Tax: **a third of the value, rounded up, on each unit** (`ShopMath.TaxEach`). Taxed per unit, so a hundred sold pay
exactly a hundred times one; taxed on buying and on selling separately, so a cart that buys and sells the same amount
still pays Tax on both.

The two old hand-typed prices (`buyPrice` and `zeny`) were replaced on 2026-10-14 in all 202 item files: the old
`zeny:` key was rewritten to `value: zeny + ceil(zeny / 2)` (`ShopMath.ValueForSellPrice`) and every `buyPrice:` line
deleted, in one pass over the files — the old key is gone, so the pass cannot be run twice by mistake. Selling one pays
exactly the old sell price for every item. Buying one moved by 1u on four items: Fly Wing and both minor potions 50 →
51, Healing Potion and Mana Draught 225 → 224; the other nine shop items cost what they did.

**Buying back** what you sold costs what the shop paid you plus Tax on that — a third of each unit's amount,
rounded up — and always at least 1u more than you were paid, so a shop is never free storage
(`ShopMath.BuybackPrice`). A Healing Potion sold for 112u comes back for 150u; a new one costs 224u.

A creature material's price rule still names what a shop PAYS; the baked value is that price through the same
formula (`CreatureMaterialPriceBake`, checked by `tools/check-creature-materials.py`).

## 2. The trade: one cart, checked once

The player's machine never prices anything. It keeps the cart as a list of intents (`CartLine`: Buy, Sell or BuyBack;
the item; for a sale the bag position and, for gear, the piece) and asks the server:

- **`CmdQuoteCart`** — every time the cart changes and then sits still for 0.2 s, the latest cart only. The answer
  (`CartQuote`) carries Buying, Selling, Tax and what changes hands, or the sentence saying why the cart would be
  refused. A late answer for an older cart is ignored.
- **`CmdTrade`** — on Confirm. All of it or none of it, in one call, with nothing awaited between the check and the
  change. A cart already carried out (the same cart count) is never carried out twice.

Both go through the ONE check, `ShopCart.Check`, so a quote and a trade can never judge a cart two ways. It refuses:
a line kind no shop knows; more than 50 lines; a Buy of something the shop does not sell, a count outside 1…999, or
the same item on two Buy lines; a Sell whose bag position no longer holds that item (and that very piece, for gear),
a count above the stack it names (gear: exactly 1), the same position or piece twice, something the shop will not
buy, or something a tutorial's first run still needs; a BuyBack that has ended or is named twice; a purse short of
what changes hands; and a bag that would not hold what comes in once what goes out has left it (judged per tab and by weight on
the bag after the sales). A player leaving the world is never traded with.

**Every sale is its own buy-back entry**, with its own number, its own price and its own hour — a later sale of the
same thing never joins an earlier one, so buying one back returns exactly that sale. A cart names a buy-back by that
number. At most ten are kept; the oldest goes first.

Carrying it out: the buy-backs come off the list first; the sales are taken highest bag position first, each kept for
buy-back with exactly what it paid; the purse moves once; the buys come in through the one door into the bag (free
tutorial units taken first, and each counted for a Buy step); the buy-backs return — a piece of gear as the very copy
that was sold. Then the whole sentence in chat ("Bought 10 Healing Potion, sold 23 Plopling Jelly — you got 6,316u.") and its short
form in the stacking notice on screen ("Trade done — got 6,316u"; a refused cart says "Trade refused" there and gives
its reason in chat and under the cart), the bag and buy-backs pushed (the hotbar too when a stack was sold to its last one), one save.

## 3. The shop window

One window, titled with the keeper's plate name, three lists: **For sale** | **Cart** | **Inventory**, then the purse
and **Cancel / Confirm** fixed at the bottom. Each list is the shared scrolling list (`UiListView`, only the rows in
view built) with its **item row**: the name in its rarity colour (cut with "…" — never the price), the count, a small
tag ("Buy back · 14m", "2 free", "Buy"/"Sell"/"Buy back" in the cart), and the price, whole, at the right — of one, or
of the whole lot on a buy-back row, which always comes back whole. A row
carries the item card on hover and can be dragged.

- For sale → Inventory or Cart, or a double-click, puts it in the cart to buy; Inventory → For sale or Cart, or a
  double-click, puts it in to sell; a cart row double-clicked or dragged out comes out — a stack of more than one asks
  how many go back, opening at the whole line; a piece of gear or a buy-back comes out whole. Shift-click links it in chat.
- The same thing again **stacks onto its line** already in the cart — never a second line. A piece of gear or a
  buy-back is in at most once.
- A stack asks how many in the ONE "how many?" box (`DropAmountPrompt`, its buttons now **Confirm / Cancel**
  everywhere, the bag's drop included); an empty field means the number it opened with. The number is selected when
  the box opens and whenever the field is clicked, so typing replaces it.
- **Don't ask quantity** (the tick box beside the purse) skips the box for a SALE only — the whole stack goes in. A
  purchase always asks.
- The Inventory list shows each stack's count as the bag holds it; the cart's own line says how many of it are going.
- Under the cart: the server's Buying, Selling, Tax (33%) and You pay / You get, or the server's reason in red.
  Confirm waits for the quote of the cart as it stands and stays off until the server answers the trade. A done trade
  empties the cart; a refused one keeps it, fixed against the bag as it now stands (a sold stack gone or shrunk, a
  buy-back ended), and asks again.

## 4. The station window

Tabs **Fold / Quench / Ink / Recycle** over the bag's own grid (`ItemGrid`) of what this station works on — what is
carried, then what is worn with its slot's word on its box. The picked box is lit. Under the grid, a section that
never scrolls: the piece's name in its rarity colour, bold, its folds small beside it; "Costs …" with the amounts
bold, and under it what you carry of the same — "You have 12 Temper Dust · 1,220u" on Fold, Quench Oil on Quench,
both counts the server's (`CraftStationOffer.foldMaterialHeld / quenchMaterialHeld`, written by the one `Wallet.YouHave`
the shop's purse uses too); a fold that can fail, "Chance: X% · If it fails: back to +N"; Quench's "Chance to rise" in green with what a
failure does — "drops to E +0" in red, or "stays F, back to +4" in grey; warnings in red; Salt as the game's checkbox; Ink as "Using <Ink>" with an "i" whose
tooltip lists every stat that Ink can write with its number, one a line, both the server's (`CraftStationOffer.inkName /
inkWrites`, the strongest Ink carried), a stat already in another slot greyed; then one small button per slot showing the stat it
holds ("+80 Max HP"), the next empty one "Slot N"; Recycle's "Gives back
…" — on the Recycle page a click TICKS a carried piece (any number of them; a worn one is still picked alone), the
section shows "N pieces" and the server's total for all of them, and **Recycle N** breaks them all down in one
request, judged by the single-recycle rules and refused whole if any piece or the bag's room fails; and the **fold preview** — each of the piece's own stats now → after one more fold, both numbers the server's
(`CraftStationOffer.pieceNow / pieceNext`). One button, fixed to the bottom, one word: Fold / Quench / Write /
Replace / Recycle. What a press did is said in full in chat, and in short in the stacking notice on screen ("Folded to +5",
"Quench: up to E", "Wrote +80 Max HP").

## 5. The heal tutorial's pointers

`ShopBuyButton` (9) became three spots, lit in order: `ShopItemRow` (10, the item's row in For sale), `ShopAmountBox`
(11, the "how many?" box while it asks for the shop) and `ShopConfirm` (12, Confirm while the cart holds the step's
item). A list row sets or clears its mark every time it is filled, so a reused row never keeps an old one.
`TutorialHealUp` points at all three.

## 6. What it costs to run

- A quote: one message each way per pause in changing the cart (at most five a second), the server's work one pass
  over at most 50 lines. Not measured.
- A trade: one message each way per Confirm; the database is still the existing save, at most once per 15 s.
- The windows: the lists build only the rows on screen; nothing runs per frame while nothing changes, except one
  time check while the shop is open.
