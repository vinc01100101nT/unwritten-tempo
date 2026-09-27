# A new CSS class name can already mean something on the page

**Reach for this when** an element you just added to an existing web page renders far too tall, too wide, hidden
or stretched, and your own styles for it look correct.

## The rule

**Before naming a class on an existing page, search the page's stylesheet for that name as a whole selector.** A
short, common name (`wrap`, `row`, `card`, `box`, `content`) is very likely already a layout class on the page, and
its rules land on your element as well as yours. When an element's size is wrong, measure it in the browser
before touching your own CSS: if the measured size has nothing to do with your content, another rule is applying.

## Why

2026-09-26. A generated table on the wiki showed one row per screen: every row was 1,826 px tall. I gave its two
long cells `class="wrap"`, and the page already had `.wrap{display:flex;min-height:100vh}` for its sidebar layout,
so every long cell became a full screen tall. It took four render-and-screenshot rounds, the first two spent
changing the table's width rules. Those were a real problem too, but they had nothing to do with the height.
Printing each cell's height and line count showed a cell holding two lines of text drawing 913 px tall. That was
the clue, and renaming the class fixed it at once.

## How to apply

1. Before adding a class, search the page for it as a selector (`grep -n '\.wrap\b' index.html`). Any hit means pick
   another name.
2. When an element's size is wrong, have the browser report it: each element's box size, and how many lines its
   text wraps to (`document.createRange()` → `selectNodeContents(cell)` → `getClientRects().length`). A two-line
   cell that is hundreds of pixels tall is being sized by a rule other than yours.
3. Only then adjust your own CSS.
