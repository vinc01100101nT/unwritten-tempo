---
status: "BUILT, not play-tested. Storage needs migration accounts/sql/012_account_storage.sql applied."
owner: vince
last-updated: 2026-10-31
tags:
  - systems
---

# The Keeper and the Wings

Every town is meant to have one **Keeper**, a townsperson who never moves. Pressing **F** on them opens the **Keeper
window**, a menu of three services — Save Point, Storage, Teleport — each opening its own page in the same window with
a **← Back** row. Two consumables, the **Butterfly Wing** and the **Fly Wing**, do a little of a Keeper's work anywhere.

Everything here is decided on the server. The window sends only what was pressed — which service, which town, which
bag position (with the item id and, for gear, the piece's own id it expected to find there), which stored thing, and
whether one or all of it. It never sends a price or a count worked out on the player's machine. Prices and the storage
list arrive finished.

## Where a Keeper is

A Keeper is a row of the townsfolk list (`unity/Assets/3_Data/Resources/WorldTownsfolk.asset`) with **Keeper** ticked.
The row owns where they stand and which way they face; nothing else keeps a copy. Every town built in the world so far
has its Keeper there, one row each: Lastline's (`keeper-lastline`) stands just north of the default arrival point
beside the Central Fountain; Parley's is `keeper-parley`.

A Keeper stands still, keeps no shop and works no station. `tools/check-quest-data.py` fails when a town of the world
has no Keeper or two, when a Keeper stands outside a town, strolls, or also keeps a shop or a station. A town of the
seamless world is one of its named Town areas, so every town built into the world needs its Keeper row in the same
change.

The server holds every Keeper service to the same reach as a shop: within 3.5 m of a Keeper, plus 0.6 m for a position
that is a tenth of a second old. Outside it the answer is "You are not at a Keeper."; a dead character is told "You
cannot do that while dead."

## Save Point

One press. The character's save point becomes **the Keeper they stand at** — the map and that Keeper's person id
(`SaveData.savePointKeeper`), written with their next save — and they are told "Save point set: *town*.", the town
named as the Teleport page names it (below). Where they stand back up is worked out each time they use it, 1.5 m in
front of that Keeper, from the Keeper's row — so a Keeper who is moved never leaves anybody saved inside a wall. A save
with no Keeper id (written before the id was recorded), or one naming somebody who is no longer a Keeper there, goes to
the map's first Keeper (`WorldTownsfolk.KeeperIn`); a map with no Keeper any more sends them to the default instead.
The game-master's `/save` saves the exact spot the GM stands on. Until a character sets one they come back at the
default: the arrival point `center` of `world`, beside Lastline's Central Fountain.

## Teleport

The page shows the character's Unsigned and one line for **every other Keeper**: the town's name and its price,
cheapest first, then by name. A town is named by the world layout's nearest town to that Keeper
(`WorldPlaces.NearestTownName`); a Keeper in a map outside the seamless world is named by its map. A town whose price is
more than the Unsigned the server last sent is drawn dim; pressing it still asks — naming the map and the Keeper's id
(`KeeperTrip.keeper`) — and the server answers.

- **Price: 4 kills of income for each door's worth of the trip — 40u × the character's level per door**
  (`KeeperMath.KillsPerDoor`, a kill being worth 10u × the level). Setting a save point and using storage are free.
- **A door's worth inside one map** is the ground distance between the two Keepers (height left out): one door for
  every 80 m or part of it (`KeeperMath.MetresPerDoor`, the width the old separate maps had; `KeeperMath.DoorsBetween`).
  So every trip inside the seamless world costs at least one door. **To another map** it is the doors on the shortest
  way there (`WorldRoutes.NextMap`).
- The Unsigned is taken first, then the character is moved, arriving 1.5 m in front of that Keeper. If the move
  does not happen the Unsigned is handed back and they are told "The trip could not be made — your *N*u is back."
- Refusals: "There is no way there." and "Needs *N*u — you have *M*u."

## Storage

One storage per **account**: every character of the account opens the same one.

- It holds **600 things** (`KeeperMath.StorageLimit`). A stack of one item is one thing however many it holds; each
  piece of gear is one thing. Storing more of a stack already stored always fits.
- A piece of gear goes in and comes out as itself: its folds, grade, written stats, cards, trademark and its own id.
- **An item tagged Unstorable cannot be stored** ("That cannot be stored."); the window does not offer it. The Newbie
  Potion carries the tag.
- The page lists what is stored — **Take 1** / **Take all** for a stack, **Take** for a piece — then what is carried —
  **Store 1** / **Store all**, or **Store**.
- **A take hands over only what the character can carry.** "All" of a pile too heavy takes the most that fits ("Took
  *N* of *M* … — that is all you can carry."); nothing fitting leaves it in storage, and they are told why.
- One move at a time per character. A second press while one is still being written down is answered "Your last storage
  move is still being written down."

### How a move is saved

Storage lives in the character server's database, in tables of its own (`account_storage`, one row per stored thing;
`account_storage_head`, one row per account). **Every move rides the character's own save**: the progress save gains a
`storage` object naming the move, and the character server applies the move in the same transaction as the progress
document. So a thing is either in the bag that was written or in storage — never both, never neither.

- **Store:** the thing leaves the live bag at once and a save goes immediately. That save's bag no longer holds it, and
  its move puts it in storage.
- **Take:** the thing stays out of the live bag until the save has landed. That save's bag is given the thing, and its
  move takes it out of storage. When the save lands the thing is laid into the live bag — not announced as a new
  arrival, because it was already the account's.
- **A move is applied once.** Each has its own id; the character server remembers the last one it applied for each
  character and ignores a save that carries it again. Every save written while a move is waiting carries it, because the
  server sends only the newest save and an older one that carried the move might never go.
- **A move that cannot be made takes the whole save back with it** (the character server answers 422 and writes
  nothing): storage is full, a piece is already stored or still carried, or there is nothing left to take. The game
  server then puts things back — a stored thing returns to the bag, a taken one never arrives — tells the player
  ("Storage is full.", "That is no longer in storage."), and sends the same save again with the move taken out. A newer
  save waiting behind the refused one has the move taken out too, so the character server is never asked the same move
  twice after the player has been told it was refused.
- Two moves for one account can never race: each locks the account's head row for the length of its transaction.
- The storage list is read from the character server when the Storage page opens, at most once a second per player,
  and sent in chunks of 100 rows; opened again within that second, the window shows the copy it already holds. After a
  move the server sends only the row that changed. The server's copy of a player's list is let go when they change map.
- The window builds only the rows on screen and re-points them as the list scrolls; whatever arrives in one frame
  redraws it once.

## Doors

Walking through a door into another map asks the server to change map. The server refuses unless the world layout has a
door between the two maps (`WorldAtlas.links`) **and** the character stands within 3 m of it (plus the same 0.6 m). A
refusal is logged, and the player's loading curtain is lowered with "There is no door here." A game master is let
through any door, and that is logged too. The world layout lists no doors today, so for anybody but a game master every
such door is refused.

## Where the server puts you

**The server decides which map a character is in, and where they appear after any trip it orders.** Their machine
never tells it.

- **The map.** On arrival it is the map the character server sent them to. On a door, a save point, a Keeper's trip or a
  game-master `/tp`, the server changes their map itself before telling their machine. Their machine's report of its
  map is accepted only when it names that same map; any other is refused and logged.
- **The spot.** Every trip the server orders — a teleport (a Fly Wing's included), a save point, a Keeper's trip, a
  door, being put back after impossible moves, and arriving with a save that says where — sets the position the server
  holds to the destination at once, and believes no report until one lands within 1.5 m of it. Reports that do not are
  dropped and not counted against the player. A far trip inside the seamless world is waited for like a new map, while
  the ground where they land loads. Once their machine has the ground loaded, it is given 2 seconds to report the spot;
  after that the server stands them there itself, and again every 2 seconds until it does.
- **One case is still taken on trust:** a door or `/tp` into a map with no arrival point named — their machine then
  picks the map's first, which the server cannot know. Their next report is believed, and the server logs it.

## The wings

| Item | Does |
| --- | --- |
| Butterfly Wing | Returns you to your save point |
| Fly Wing | Sets you down on a random spot of the stretch of ground you stand on |

Both are in the Tool Dealer's stock and no creature drops either; their prices are in
[Town Supplies and Hunting Finds](town-supplies-and-hunting-finds.md).

Each runs a skill, like every usable item, built from one of two effect kinds:

- **Return To Save Point.** A person goes to their save point, or to the default one when they have set none. The trip
  is taken on the server's next frame rather than inside the skill, because the wing is spent after its skill fires.
  The trip remembers the wing that paid for it: if it does not happen — the player fell first, or the trip could not be
  made — the wing goes back into the bag and they are told why. A creature goes back to the home it spawned at.
- **Teleport To Random Spot.** The unit lands on a random walkable spot of the same connected stretch of ground it
  stands on, so it can always walk back. The connected stretches are worked out for each named area of the world (a
  town) once all of its ground has loaded, and let go when it unloads; each stretch's cells are listed then too, so a
  pick is one lookup, moved to the nearest cell a body fits when the first is too tight. The spot is rolled once: the
  check made before the wing is spent finds it, and the wing's effect lands there.

A dead character's wing does nothing and is not spent. A Fly Wing with nowhere to land is refused before it is spent,
with "There is nowhere to land here." **Current limit:** outside a named area of the world there are no worked-out
stretches, so a Fly Wing used in the open field is always refused that way.

## What it costs to run

- **Per frame:** two true/false tests per player on the server (an owed Butterfly Wing trip; an arrival being waited
  for), plus one distance compare per position report while an arrival is waited for. The Keeper window, while open,
  makes one test per frame and redraws only after a scroll or a change.
- **A Keeper service:** one walk over the townsfolk standing on the server to find a Keeper in reach — the same walk a
  shop press already makes.
- **Opening Storage:** one read of the account's rows through an index on (account, order stored); at most 600 rows,
  at most once a second per player. The list crosses to the player in messages of 100 rows.
- **A storage move:** one extra save of that character, and inside it a handful of small statements on the character
  server. Not measured under load.
- **A Fly Wing:** one lookup per try, up to 8 tries, each with a fitting test and a ground probe. Measured on a 20 m test
  floor: 1.4 µs per pick. Not timed in the world.
- **Memory:** 4 bytes per walkable cell of each named area whose stretches are worked out, plus one whole number per
  stretch; and each online player's copy of their storage list on the server (at most 600 rows).

## Proving it

- `charserver/test-storage.sh` — storage end to end on a throwaway database it creates and removes: stores, takes,
  the same save sent twice, every refusal, one storage per account. It never touches the real database's tables.
- `tools/check-game-math.sh` — the Keeper's numbers: the price, the doors between two Keepers of one map, the arrival
  spot, the reach, the limit and the listing of each stretch's cells.
- `tools/check-quest-data.py` — one Keeper per town.

## Open

- Not play-tested. The window, a wing's use, and the server-decided map and arrival have only been checked by
  compiling and by the tests above.
- Only Lastline has a Tool Dealer, so wings can be bought in one town.
