---
status: "BUILT 2026-10-04, not yet play-tested. Two quests exist: the crafting tutorial and Torvald's Ploplings."
owner: vince
last-updated: 2026-10-04
tags:
  - systems
---

# Quests and Tutorials

A **quest** is an ordered list of steps. A character holds a record of every quest it has ever been given: which
step it is on, how far along that step is, whether it is finished, and whether it is the one being followed. The **Quests window** (J, or the
Quests button in the status box) lists the open ones under three tabs — **Tutorial**, **Main Story**, **Side
Quests**. Choosing a quest shows its steps. A **tutorial** has a **Play** button (**Stop** while it plays): playing it
turns on its **guide** — at the place a step is worked, the whole screen dark except the one part to press next, with
an arrow and a short text — and its path. Any other quest has a **Show path** button. A path is **arrows on the
ground** to where the step happens, and the same whole route drawn on the minimap and in the Map window's zone view
(M).

## Play, Show path and requirements

- **One path, whatever kind of quest.** At most one quest is followed. A playing tutorial is the followed one, so its
  path is on. Pressing a side or main quest's Show path moves the path to it, and the tutorial stops playing;
  pressing the Show path that is already on follows nothing. Stop follows nothing too — no arrows, no guide.
- **Only a tutorial has hints**, so the guide only ever teaches the tutorial that is playing.
- **Play checks requirements, on the server, at the press only.** If the character lacks any of them, Play does
  nothing and says "Requirements are not met" over their head. What is spent after Play started is the guide's to
  explain ("you are carrying too little to pay").
- **A tutorial's requirements are what the rest of it still needs**, from the step it is on to its end:
  - The forging and folding its steps name, worked out from the items' own costs. A piece already held is not
    forged again, folds already made are not paid again, and a piece no step ahead needs costs nothing. For "Your
    First Gear" from its start that is 24 Copper and 1,900 gold, the starter kit exactly.
  - The character level the quest names, if any. Gold and items are never typed on a quest: they are only ever what
    its steps cost.
  - Each tutorial shows its list in the Quests window, every line green when the character has it and red when not —
    the server's numbers and the server's yes or no, refreshed when the bag, the purse or the level changes.
- **Every open tutorial has Play or Stop**, whatever step it is on — a step with nowhere to lead to still has a guide.
  Show path on any other quest shows only while the server says its current step happens somewhere the arrows can
  lead (or while it is followed, so it can be turned off).
- **A quest given or accepted while nothing is followed becomes the one followed** — a tutorial only when its
  requirements are met, so a brand-new character's crafting tutorial plays on its own. A quest the game no longer has
  is never the followed one.

## Kill counts

Every kill that counts toward a quest is said over your head, in the same stacking lines as "No MP", in the quest
marks' yellow: the creature's name and the step's count after it — "Plopling 3 / 10", up to "Plopling 10 / 10". The
numbers are the server's. Kills that land in the same moment make one line per quest, carrying the latest count.

## What decides what

- **The game server decides everything.** Which quests a character has, which step each is on, when a step is done,
  where the followed step happens and whether it can be done right now. A player's machine is told the answers and
  draws them; which quest to follow, which tutorial to play, or neither is the only thing it asks for, and the
  server ignores a quest that is not one of that character's own open ones — and plays no tutorial whose requirements
  are not met.
- **The route on the ground** is the one thing worked out on the player's machine: the arrows follow the same
  route-finder the character walks by, and the minimap and the zone view draw that one route, whole. It decides nothing (user's decision 2026-10-04, as part of movement's
  exception). Where the target is, and which door leads toward it when it is on another map, come from the server.

## How a step is finished

| Objective | Finished when |
| --- | --- |
| Talk To a townsperson | you press F within reach of them (the reach a station or a shop needs). A moment, not a state. |
| Have an item | a copy of it is in your bag **or worn** |
| Folded To an item, N folds | a copy (bag or worn) has N folds or more, **or is of any grade above Raw** (a quench spends the folds) |
| Wear an item | a copy of it is worn |
| Kill a creature, N of them | N of that creature have been defeated by you **while this is the current step** — earlier kills do not count. A kill counts for the person the kill pays (the last to land a damaging blow); a summoned creature counts for nobody, exactly as it pays nobody, and neither does a saved character wearing the creature's body. The step may name the map it is hunted in (the arrows lead there); otherwise they lead to the nearest map, by doors, that places the creature. |

- **Holding steps judge what you hold, not what you did** (decided 2026-10-04). A Have, Folded To or Wear step
  already true when it is reached finishes at once — a Sword forged early, or given to you, counts. A Kill step is the
  exception: it counts only what you defeat after reaching it.
- **Forward only.** A finished step never comes undone (selling the Sword later changes nothing), and a finished quest
  is never given again.
- Checked by the server whenever your bag changes, when you talk to someone, when you defeat a creature, and when you
  arrive in the world.

## Who is given a quest

- **Given automatically:** given to every character that has never had it — new and existing — the moment they enter
  the world. The crafting tutorial.
- **Offered by a townsperson:** pressing F on them shows their offer in the conversation box, with **Accept** on its
  last line. The server takes it only from within reach of that person, and only if the character has never had it.
  The giver is never a station worker or a shopkeeper — those open a window, not a conversation.
- A quest is one or the other, never both. Quests cannot be skipped or abandoned.

## Marks over townspeople

- **"!"** over a townsperson who offers a quest this character can accept — told only for the map you are standing in.
- **"?"** over a townsperson where one of this character's open quests' current step happens — someone to talk to, or
  the nearest smith (by doors) for a forge or fold step.
- When both are true, "?" shows. Both are the server's lists; a player's machine only draws them.

## Rewards

A quest's reward — gold, experience, items — is paid the moment its last step is done, by the server, once: a finished
quest never finishes again. Experience goes through the same door a kill's does, so the weapon in hand earns its share
of mastery. The reward and the finished record are written in the same save, so neither can land without the other.
The chat says the quest is complete and what it paid — and, since handing in is a conversation with the chat hidden,
the open conversation box says it too. The crafting tutorial pays nothing; the gear is its reward.

## The guide

- **Which hint shows:** a step lists its hints in order; the guide shows the **last one whose part is on screen right
  now**. Nothing is remembered, so going back a page or picking the wrong piece simply brings an earlier hint back.
- **Station steps light only the station the step is worked at** — a smith's step never lights the loom's rows.
- **It never traps you.** The dark stops the mouse only while its one part is on screen; the keyboard is never
  touched (Esc, movement and F work as always); it is gone while you are dead or out of the world.
- **When the step cannot be done right now** the server says why, and the guide shows a sentence instead of lighting
  a button: you are carrying too little to pay; the piece is being worn (a station works only on what you carry); or
  you are not carrying it.
- **After a press that spends something** (a forge, a fold), the lit part cannot be pressed again until the server
  answers, so a quick double press cannot spend the starter kit twice.
- A row scrolled out of view is scrolled into view.

## The crafting tutorial — "Your First Gear"

Given automatically. Ten steps: talk to Gunnar the Smith; forge a Sword and fold it twice; forge a Chainmail and fold
it twice; forge an Iron Kite Shield and fold it twice; wear all three. A new character's starting kit (24 Copper,
1,900 gold, 10 Minor Healing Potions) is exactly the bill for the forging and folding.

## Torvald's Ploplings — a side quest

Offered by Torvald, the off-duty raider who wanders Lastline. Two steps: defeat 10 Ploplings (they live in Lastline
Field 1, west of town — the arrows lead there), then tell Torvald. Reward, first guess: 25 experience, 300 gold and 5
Minor Healing Potions.

## Saving

Every quest record lives in its own row of `character_quest` (migration `009`) — step, progress, followed, finished. The rows that changed since the last
save that landed ride **inside the progress save** and are written in the same transaction, after the progress was
taken under the same hand-over check — so the bag and the quest that judged it land together or not at all. A save
refused for good is kept in a file for a person and not resent. The rows travel with the character on every hand-over
(entering the world, walking through a door to another map server). The hand-over number is raised before the
character is read, so a late save from a server the character has left cannot slip in between.

Rows are deleted with their character. A soft-deleted character keeps its rows, as it keeps its progress and hotbar,
until the character itself is purged — and that purge does not exist yet (SERVER.md rule 10, still open).

## Writing a new quest

One asset in `unity/Assets/3_Data/Resources/Quests` (Create ▸ unwritten ▸ Quest). Ids are stable: never rename or reuse
a quest id or a step id in a live quest — add new ones. A step points at a part of the screen only if that part is
marked in code (`GuideMarker`): today the station window's rows (Forge and Fold pages, Back, each forge row, each piece
row, the Fold button) and the bag's rows. A quest with a fault — an item the game does not have, a townsperson nobody
lists, two steps with one id — is **suspended**: nobody is given it or shown it until the data is fixed.
`tools/check-quest-data.py` catches all of these before they ship, and also: a Kill step for a creature the world
spawn list places nowhere (it could never be finished); an offered quest with no offer text; a giver who keeps a
station or a shop; **a rewarded quest with any step judged on holding something** — one lent item would finish
it, and pay it, for every character it passes through; **hints or a level needed on a quest that is not a tutorial**;
and a tutorial's fold step asking for more folds than a Raw piece takes (5), whose bill would need a quench.

## Not built yet

- Main Story content — the tab exists and is empty.
