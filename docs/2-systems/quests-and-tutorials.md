---
status: "BUILT 2026-10-04, tutorials reworked 2026-10-07 (unlock conditions, free first runs, replays, the healing tutorial); not yet play-tested. Three quests exist: two tutorials and Torvald's Ploplings."
owner: vince
last-updated: 2026-10-07
tags:
  - systems
---

# Quests and Tutorials

A **quest** is an ordered list of steps. A character holds a record of every quest it has ever been given: which
step it is on, how far along that step is, whether it is finished, and whether it is the one being followed. The **Quests window** (J, or the
Quests button in the status box) lists them under three tabs — **Tutorial**, **Main Story**, **Side
Quests**. Choosing a quest shows its steps. A **tutorial** has a **Play** button (**Stop** while it plays): playing it
turns on its **guide** — the part of the screen to press next lit, with an arrow and a short text — and its path. Any
other quest has a **Show path** button. A path is **arrows on the ground** to where the step happens, and the same whole
route drawn on the minimap and in the Map window's zone view (M).

## Unlock conditions

Every quest that is not offered by somebody has an **Unlock Condition** — the moment it is given (decided 2026-10-07):

| Unlock Condition | The moment | Quest |
| --- | --- | --- |
| Enter the world | entering the world, for every character that has never had it | Your First Gear |
| Return to last saved point | coming back from dying through the death window's "Return to last saved point" | Low on Health |

- **The Tutorial tab lists every tutorial in the game from the start.** One not yet unlocked shows "Unlock Condition:"
  and what it is, and has no Play.
- **A tutorial PLAYS the moment it unlocks** — it is followed at once, taking over from the quest followed until then,
  and Stop still works. The quest it took over from is remembered on its record and followed again the moment it is
  done (unless something else has been chosen meanwhile).
- **Any other quest with an unlock condition** is followed only when nothing else is.
- A quest is given one way: an unlock condition, or offered by a townsperson. A tutorial always has an unlock
  condition.

## Free the first time, paid on a replay

- **A character's first run of a tutorial takes nothing its steps use up** (decided 2026-10-07). It hands out nothing:
  - every piece it has you make carries **one free forge and its free folds** — the forge and the folds cost no Copper
    and no gold;
  - every item it has you **buy** is free — as many as the step asks for, or, when a Health step after it is healed
    with that item, as many as it takes to get from the health the character came back on to that step's share (the
    item's own written heal, e.g. 30 HP a Minor Healing Potion).
- **What is free is saved on the record and used up by the press that does the work** — the forge, the fold or the
  purchase itself, in the same call — so a quick double press, a door to another map server or a restart can never
  spend it twice. A free fold goes only on a Raw piece, and only on the copy the run made (or the first one it folded).
- **Free lasts until the tutorial is first finished.** Stopping and playing again carries on with whatever is still free.
- **The station and the shop say so**: a forge row or the Fold button reads "Free", and a shop row says how many are
  free before its price — the server's answer.
- **A finished tutorial can be played again** (a **replay**): Play from its first step, paying its whole bill. A replay
  never pays the reward again and never makes the quest unfinished; its steps count **only the pieces made during the
  replay**, so gear already held cannot finish it at once.
- A new character's starting kit is **empty** — no items, no gold — because the tutorials give the first gear and the
  first potions free.
- Characters that already had a tutorial before 2026-10-07 have nothing free on it: they were handed the old starting
  kit (33 Copper, 2,350 gold, 10 potions) that paid for it.

## Play, Show path and requirements

- **One path, whatever kind of quest.** At most one quest is followed. A playing tutorial is the followed one, so its
  path is on. Pressing a side or main quest's Show path moves the path to it, and the tutorial stops playing;
  pressing the Show path that is already on follows nothing. Stop follows nothing too — no arrows, no guide.
- **Only a tutorial has hints**, so the guide only ever teaches the tutorial that is playing.
- **Play checks requirements, on the server, at the press only.** If the character lacks any of them, Play does
  nothing and says "Requirements are not met" over their head. What is spent after Play started is the guide's to
  explain ("you are carrying too little to pay").
- **A tutorial's requirements are what the rest of it still needs**, from the step it is on to its end (from its first
  step for a finished one, which a replay starts):
  - The forging and folding its steps name, worked out from the items' own costs, and what its Buy steps cost at the
    shop's price. A piece already held is not forged again, folds already made are not paid again, a piece no step
    ahead needs costs nothing, and whatever a first run still has free costs nothing. A first run of "Your First Gear"
    therefore needs nothing; a replay needs 33 Copper and 2,350 gold.
  - The character level the quest names, if any. Gold and items are never typed on a quest: they are only ever what
    its steps cost.
  - Each tutorial shows its list in the Quests window, every line green when the character has it and red when not —
    the server's numbers and the server's yes or no, refreshed when the bag, the purse or the level changes.
- **Every unlocked tutorial has Play or Stop**, whatever step it is on — a step with nowhere to lead to still has a
  guide, and a finished one is played again. Show path on any other quest shows only while the server says its
  current step happens somewhere the arrows can lead (or while it is followed, so it can be turned off).
- **A quest accepted while nothing is followed becomes the one followed.** A quest the game no longer has is never the
  followed one.

## Kill counts

Every kill that counts toward a quest is said over your head, in the same stacking lines as "No MP", in the quest
marks' yellow: the creature's name and the step's count after it — "Plopling 3 / 10", up to "Plopling 10 / 10". The
numbers are the server's. Kills that land in the same moment make one line per quest, carrying the latest count.

## What decides what

- **The game server decides everything.** Which quests a character has, which step each is on, when a step is done,
  what is free, where the followed step happens and whether it can be done right now. A player's machine is told the
  answers and draws them; which quest to follow, which tutorial to play, or neither is the only thing it asks for, and
  the server ignores a quest that is not one of that character's own — and plays no tutorial whose requirements are
  not met.
- **The route on the ground** is the one thing worked out on the player's machine: the arrows follow the same
  route-finder the character walks by, and the minimap and the zone view draw that one route, whole. It decides nothing (user's decision 2026-10-04, as part of movement's
  exception). Where the target is, and which door leads toward it when it is on another map, come from the server. A spot the player clicks on the zone view (`MapGuide`) is led to by the same arrows, ahead of the quest, until it is reached — and for that one the next door is found on the player's machine (`WorldRoutes`), since where they want to walk decides nothing.

## How a step is finished

| Objective | Finished when |
| --- | --- |
| Talk To a townsperson | you press F within reach of them (the reach a station or a shop needs). A moment, not a state. |
| Have an item | a copy of it is in your bag **or worn** |
| Folded To an item, N folds | a copy (bag or worn) has N folds or more, **or is of any grade above Raw** (a quench spends the folds) |
| Wear an item | a copy of it is worn |
| Kill a creature, N of them | N of that creature have been defeated by you **while this is the current step** — earlier kills do not count. A kill counts for the person the kill pays (the last to land a damaging blow); a summoned creature counts for nobody, exactly as it pays nobody, and neither does a saved character wearing the creature's body. The step may name the map it is hunted in (the arrows lead there); otherwise they lead to the nearest map, by doors, that places the creature. |
| Buy an item, N of them | N have been bought **while this is the current step**. The arrows lead to the nearest shopkeeper, by doors, whose shop sells it. |
| Health, a share of full health | your health is at least that share, e.g. 80%. Judged whenever your health goes up or down while this is the current step — a drink, a heal, regeneration. |

- **Holding steps judge what you hold, not what you did** (decided 2026-10-04). A Have, Folded To or Wear step
  already true when it is reached finishes at once — a Sword forged early, or given to you, counts. On a replay they
  judge only the pieces made during it. Kill and Buy steps count only what happens after reaching them.
- **Forward only.** A finished step never comes undone (selling the Sword later changes nothing), and a finished quest
  is never given again — a replay is the same quest played again, never given again.
- Checked by the server whenever your bag changes, when you talk to someone, when you defeat a creature, when you buy
  something, when your health moves during a Health step, and when you arrive in the world.

## Who is given a quest

- **By its unlock condition** (above).
- **Offered by a townsperson:** pressing F on them shows their offer in the conversation box, with **Accept** on its
  last line. The server takes it only from within reach of that person, and only if the character has never had it.
  The giver is never a station worker or a shopkeeper — those open a window, not a conversation.
- A quest is given one way. Quests cannot be skipped or abandoned.

## Marks over townspeople

- **"!"** over a townsperson who offers a quest this character can accept — told only for the map you are standing in.
- **"?"** over a townsperson where one of this character's open quests' current step happens — someone to talk to,
  the nearest smith (by doors) for a forge or fold step, or the nearest shopkeeper selling a Buy step's item.
- When both are true, "?" shows. Both are the server's lists; a player's machine only draws them.

## Rewards

A quest's reward — gold, experience, items — is paid the moment its last step is first done, by the server, once: a
finished quest never finishes again, and a replay pays nothing. Experience goes through the same door a kill's does, so
the weapon in hand earns its share of mastery. The reward and the finished record are written in the same save, so
neither can land without the other. The chat says the quest is complete and what it paid — and, since handing in is a
conversation with the chat hidden, the open conversation box says it too. Both tutorials pay nothing; the gear and the
potions are their reward.

## The guide

- **Which hint shows:** a step lists its hints in order; the guide shows the **last one whose part is on screen right
  now**. Nothing is remembered, so going back a page or picking the wrong piece simply brings an earlier hint back.
- **Station steps light only the station the step is worked at** — a smith's step never lights the loom's rows.
- **It never traps you.** The dark stops the mouse only while its one part is on screen; the keyboard is never
  touched (Esc, movement and F work as always); it is gone while you are dead or out of the world. **The health bar
  over your own head is framed and pointed at with nothing dark** — it is on screen the whole time, so a dark there
  would stop the mouse for as long as the step lasts.
- **When the step cannot be done right now** the server says why, and the guide shows a sentence instead of lighting
  a button: you are carrying too little to pay; the piece is being worn (a station works only on what you carry); you
  are not carrying it; or, on a Health step, you have none of its item left — buy more at a shop that sells them.
- **After a press that spends something** (a forge, a fold), the lit part cannot be pressed again until the server
  answers, so a quick double press cannot spend the same materials twice.
- A row scrolled out of view is scrolled into view.

## The crafting tutorial — "Your First Gear"

Unlocks on entering the world. Ten steps: talk to Gunnar the Smith; forge a Sword and fold it five times; forge a
Chainmail and fold it five times; forge Iron Sabatons and fold them five times; wear all three. The first run forges
and folds all three free; a replay's bill is 33 Copper and 2,350 gold.

## The healing tutorial — "Low on Health"

Unlocks on coming back through "Return to last saved point" — which always brings you back on a tenth of your health.
Two steps:

1. **Buy a Minor Healing Potion from a Tool Dealer.** The guide frames the health bar over your head (the red bar is
   your health, the blue bar under it your mana) while the arrows lead to a Tool Dealer, then lights "Buy 1" on the
   potion in the shop window. On the first run the potions it takes to get back to 80% cost nothing — 2 for a level-1
   character in the +5 starter set, who comes back on 7 of 76 health.
2. **Drink potions until your health is back to 80%.** The guide lights the potion in your bag; double-click drinks.

A character who signs out from the death window without pressing "Return to last saved point" comes back on a tenth of
their health without unlocking it; it unlocks the next time they come back through the button.

## Torvald's Ploplings — a side quest

Offered by Torvald, the off-duty raider who wanders Lastline. Two steps: defeat 10 Ploplings (they live in Lastline
Field 1, west of town — the arrows lead there), then tell Torvald. Reward, first guess: 25 experience, 300 gold and 5
Minor Healing Potions.

## Saving

Every quest record lives in its own row of `character_quest` (migrations `009` and `011`) — step, progress, followed,
finished, replaying, the quest to follow again when it is done, and the run's pieces (what each is, the copy the run
made, and the free forges, folds and purchases left). The rows that changed since the last
save that landed ride **inside the progress save** and are written in the same transaction, after the progress was
taken under the same hand-over check — so the bag and the quest that judged it land together or not at all. A save
refused for good is kept in a file for a person and not resent. The rows travel with the character on every hand-over
(entering the world, walking through a door to another map server). The hand-over number is raised before the
character is read, so a late save from a server the character has left cannot slip in between. A replay is written only
beside a finished row, so it can never make a quest unfinished.

Rows are deleted with their character. A soft-deleted character keeps its rows, as it keeps its progress and hotbar,
until the character itself is purged — and that purge does not exist yet (SERVER.md rule 10, still open).

## Writing a new quest

One asset in `unity/Assets/3_Data/Resources/Quests` (Create ▸ unwritten ▸ Quest). Ids are stable: never rename or reuse
a quest id or a step id in a live quest — add new ones. A step points at a part of the screen only if that part is
marked in code (`GuideMarker`): today the station window's rows (Forge and Fold pages, Back, each forge row, each piece
row, the Fold button), the bag's rows, a shop's "Buy 1" button, and the health bar over your own head. A quest with a
fault — an item the game does not have, a townsperson nobody lists, two steps with one id, an item to buy that no shop
sells, a tutorial with no unlock condition — is **suspended**: nobody is given it or shown it until the data is fixed.
`tools/check-quest-data.py` catches all of these before they ship, and also: a Kill step for a creature the world
spawn list places nowhere (it could never be finished); an offered quest with no offer text; a giver who keeps a
station or a shop, or is a town's Keeper; **a rewarded quest with any step judged on holding something** — one lent item would finish
it, and pay it, for every character it passes through; **hints or a level needed on a quest that is not a tutorial**;
a tutorial's fold step asking for more folds than a Raw piece takes (5), whose bill would need a quench; a Health step
asking for less than 1% or more than 100%; and a shop's buy button lit on a step that buys nothing.

## Not built yet

- Main Story content — the tab exists and is empty.
