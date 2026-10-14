---
status: "BUILT 2026-10-12 (red-team holes closed the same day), not play-tested. Every one of the 16 towns has a Keeper; the storage tables are in migration 012, which must be applied before a server built from this code is used."
owner: vince
last-updated: 2026-10-12
tags:
  - systems
---

# The Keeper and the Wings

Every town has one **Keeper** — a townsperson standing just inside one of the town's doors. Pressing **F** on them
opens the **Keeper window**, a menu of three services, each opening its own page in the same window with a **← Back**
row. Two consumables, the **Butterfly Wing** and the **Fly Wing**, do a little of a Keeper's work anywhere.

Everything here is decided on the server. The window sends only what was pressed — which service, which town, which
bag position (with the item id and, for gear, the piece's own id it expected to find there), which stored thing, and
whether one or all of it. It never sends a price or a count worked out on the player's machine. Prices and the storage
list arrive finished.

## Where a Keeper is

A Keeper is a row of the townsfolk list (`WorldTownsfolk`) with **Keeper** ticked. The row owns where they stand and
which way they face; nothing else keeps a copy. All sixteen wear the same body and are named "Keeper". Each stands about
five metres inside the door with the lowest number in that town, two metres to one side of the path, facing into the
town.

A Keeper stands still, keeps no shop and works no station. `tools/check-quest-data.py` fails when a town has no Keeper,
has two, when a Keeper is placed in a map that is not a town, strolls, or also keeps a shop or a station.

The server holds every Keeper service to the same reach as a shop: within 3.5 m of a Keeper, plus 0.6 m for a position
that is a tenth of a second old. Outside it the answer is "You are not at a Keeper."; a dead character is told "You
cannot do that while dead."

## Save Point

One press. **The town** becomes the character's save point, and they are told "Save point set: *town*." It is written
with the character's next save. A save point set at a Keeper names the town only: where they stand back up is worked out
each time they use it, 1.5 m in front of that town's Keeper, from the Keeper's row — so a town that is re-laid, or a
Keeper who is moved, never leaves anybody saved inside a wall. A town that no longer has a Keeper sends them to the
default instead. The game-master's `/save` still saves the exact spot the GM stands on. Until a character sets one they
come back at Lastline's west door.

## Teleport

The page shows the character's Unsigned ("You have 1,220u") and one line per town: its name and its price. A town whose price is more than the
Unsigned the server last sent is drawn dim; pressing it still asks, and the server answers.

- **Price: 100u for each door on the shortest way there on foot.** One number (`KeeperMath.PerDoorPrice`), a
  placeholder until prices are balanced. Today the nearest pair of towns is 1 door apart (100u) and the farthest 13
  (1,300u); every town can be reached from every other.
- The Unsigned is taken first, then the character is moved through the one door every map change uses (see **Where the
  server puts you** below). They arrive 1.5 m in front of that town's Keeper.
- A trip to a town another server holds is written down with the character already standing there, before anybody is
  told: a player who drops during the loading screen comes back in that town, with the trip paid for and made.
- If the move does not happen — the save before it failed, or the other server would not take them — the Unsigned is handed
  back, the true position is written straight back, and they are told "The trip could not be made — your *N*u is
  back."
- Refusals: "There is no way there." (not a town with a Keeper, the town they are in, or no way of doors) and "Needs *N*u —
  you have *M*u."

## Storage

One storage per **account**: every character of the account opens the same one.

- It holds **600 things** (`KeeperMath.StorageLimit`). A stack of one item is one thing however many it holds; each
  piece of gear is one thing. Storing more of a stack already stored always fits.
- A piece of gear goes in and comes out as itself: its folds, grade, written stats, cards, trademark and its own id.
- **Quest items cannot be stored** ("That cannot be stored."); the window does not offer them.
- The page lists what is stored — **Take 1** / **Take all** for a stack, **Take** for a piece — then what is carried —
  **Store 1** / **Store all**, or **Store**.
- One move at a time per character. A second press while one is still being written down is answered "Your last storage
  move is still being written down."
- There is no bag size limit in the game, so a take can never fail for want of room.

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
  nothing): storage is full, a piece is already stored or still carried, or there is nothing left to take. The map
  server then puts things back — a stored thing returns to the bag, a taken one never arrives — tells the player
  ("Storage is full.", "That is no longer in storage."), and sends the same save again with the move taken out. A newer
  save waiting behind the refused one has the move taken out too, so the character server is never asked the same move
  twice after the player has been told it was refused.
- Two moves for one account can never race: each locks the account's head row for the length of its transaction.
- The storage list is read from the character server when the Storage page opens, at most once a second per player,
  and sent in chunks of 100 rows; opened again within that second, nothing is sent and the window shows the copy it
  already holds. After a move the server sends only the row that changed. The server's copy of a player's list is let
  go when they change map.
- The window builds only the rows on screen and re-points them as the list scrolls, so 600 stored things cost the
  twenty-odd rows in view; whatever arrives in one frame redraws it once.

## Doors

Walking through a door asks the server to change map. The server now refuses unless the map the character is in has a
door to the map named (the world layout's doors, `WorldAtlas.links`) **and** the character stands within 3 m of it
(plus the same 0.6 m). Before this, any map name sent was a free teleport. A refusal is logged, and the player's
loading curtain is lowered with "There is no door here." A game master is let through any door — the test maps join
each other by doors the world layout does not list — and that is logged too.

## Where the server puts you

**The server decides which map a character is in, and where they appear after any trip it orders.** Their machine
never tells it.

- **The map.** On arrival it is the map the character server sent them to (their save's; or this server's starting map
  for a character never played or saved in a map that is gone — the character server names it in the announcement). On
  a door, a save point, a Keeper's trip or a game-master `/tp` into a map this server holds, the server changes their
  map itself before telling their machine. Their machine's report of its map is accepted only when it names that same
  map; any other is refused and logged. Before this, the reported map was taken as said, which defeated the door
  check, let a Keeper's price be read from another map, and let a save point be set anywhere.
- **The spot.** Every trip the server orders — a teleport (a Fly Wing's included), a save point, a Keeper's trip, a
  door into a map it holds, being put back after impossible moves, and arriving with a save that says where — sets the
  position the server holds to the destination at once, and believes no report until one lands within 1.5 m of it.
  Reports that do not are dropped and not counted against the player: one sent before the jump was still in the air
  when it happened. Once their machine has the map loaded, it is given 2 seconds to report the spot; after that the
  server stands them there itself, and again every 2 seconds until it does. Before this, the next report after a trip
  was believed wherever it said they were, and a report already in the air used that up, so the real arrival was
  refused and the player dragged back.
- **One case is still taken on trust:** a door or `/tp` into a map this server holds with no arrival point named —
  their machine then picks the map's first, which the server cannot know. Their next report is believed, and the server
  logs it. Every door in the world layout names its arrival point.

## The wings

| Item | Does | Buy | Sell |
| --- | --- | --- | --- |
| Butterfly Wing | Returns you to your save point, on whichever map it is | 300 | 150 |
| Fly Wing | Sets you down on a random spot of the map you are on | 50 | 25 |

Both are in the Tool Dealer's stock and no creature drops either
([Town Supplies and Hunting Finds](town-supplies-and-hunting-finds.md)).

Each runs a skill, like every usable item, built from one of two effect kinds:

- **Return To Save Point.** A person goes to their save point, or to the default one when they have set none. The trip
  is taken on the server's next frame rather than inside the skill: the wing is spent after its skill fires, and a trip
  to another server saves the character the moment it starts — taken inside the skill, that save would still carry the
  wing. The trip remembers the wing that paid for it: if it does not happen — the player fell first, or the other
  server would not take them — the wing goes back into the bag and they are told why. A creature goes back to the home
  it spawned at.
- **Teleport To Random Spot.** The unit lands on a random walkable spot of the same connected stretch of ground it
  stands on, so it can always walk back. Each map's connected stretches are worked out once when the map loads (the
  route areas the pathfinder already uses), and each stretch's cells are listed then too, so a pick is one lookup: even
  among that stretch's cells, moved to the nearest cell a body fits when the first is too tight. The spot is rolled once:
  the check made before the wing is spent finds it, and the wing's effect lands there. All 77 maps carry route areas.

A dead character's wing does nothing and is not spent. A Fly Wing with nowhere to land is refused before it is spent,
with "There is nowhere to land here."

## What it costs to run

- **Per frame:** two true/false tests per player on the server (an owed Butterfly Wing trip; an arrival being waited
  for), plus one distance compare per position report while an arrival is waited for. The Keeper window, while open,
  makes one test per frame and redraws only after a scroll or a change. A Keeper is one more townsperson (one per town).
- **A Keeper service:** one walk over the townsfolk standing in the loaded maps to find a Keeper in reach — the same
  walk a shop press already makes.
- **Opening the Teleport page:** one route answer per town, each worked out once per pair of maps and then kept
  (16 towns: 240 pairs at most, for the life of the server).
- **Opening Storage:** one read of the account's rows through an index on (account, order stored); at most 600 rows,
  at most once a second per player. The list crosses to the player in messages of 100 rows.
- **A storage move:** one extra save of that character (the same document the 15-second save writes), and inside it
  six to eight small statements on the character server. Not measured under load.
- **A Fly Wing:** one lookup per try, up to 8 tries, each with a fitting test and a ground probe. Measured on a 20 m test
  floor: 1.4 µs per pick, 5,000 picks, none closer than a body's width to a wall. Not timed on a real map.
- **Opening the Keeper window on 600 stored things:** about twenty rows made once and reused, instead of every row
  remade on every change (about 10,800 objects per press before).
- **Memory:** 4 bytes per walkable cell of each loaded map for the listed cells — at most about 101 KB for the largest
  map (Lastline, 80 × 80 m) and about 4.1 MB if one server held all 77 maps at once — plus one whole number per
  connected stretch; and each online player's copy of their storage list on the server (at most 600 rows), let go when
  they change map.

## Proving it

- `charserver/test-storage.sh` — storage end to end on a throwaway database it creates and removes: stores, takes,
  the same save sent twice, every refusal, one storage per account. It never touches the real database's tables.
- `tools/check-game-math.sh` — the Keeper's numbers: the price, the arrival spot, the limit, the listing of each
  stretch's cells, and what a take adds to (and a refused take removes from) the bag a save writes.
- `tools/check-quest-data.py` — one Keeper per town.

## Open

- Not play-tested. The window, a trip between two servers, a wing's use, and the server-decided map and arrival have
  only been checked by compiling, by the tests above, and by measurements made in the editor without Play.
- Only Lastline has a Tool Dealer, so wings can be bought in one town.
- The Keeper's prices are placeholders.
