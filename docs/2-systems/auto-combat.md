# Auto-Combat

**BUILT 2026-10-12, not compiled in Unity, not play-tested.** The character hunts by itself: it picks a creature,
walks there, swings, presses up to four chosen skills, drinks potions and picks up loot it was told to.

## Switching it on and off

- **T**, or the **Start** / **Stop** button in the Auto-Combat window (System menu ▸ Auto-Combat), asks the server to
  switch it on. The server says yes or no; the player's machine never switches it on by itself.
- **Never in a town.** Free Roam only in a field or a dungeon. A refusal is said on screen.
- **It stops** when the player gives any order of their own (a click, WASD, a skill aimed by hand that has to walk
  into range, T again), when the character dies, and on every trip: a door, a Butterfly Wing, a Keeper's teleport, a
  GM's `/tp`. A Fly Wing keeps it on and moves the Radius spot to where it lands.
- **Doors are refused while it is on** — the door says "Switch auto-combat off to use a door.", and the server refuses
  one as well if it is asked anyway.
- A creature's taunt, or a skill's own dash or lunge, does not stop it: the hunt carries on afterwards from the same spot.
- **Whether it is on is never saved.** Every sign-in starts with it off.
- While it is on, the tag **AUTO-COMBAT — press T to stop** shows near the bottom of the screen.

## The three hunts

- **Standing** — fights only what comes within weapon reach, and never takes a step.
- **Radius** (the default) — starts a fight only with a creature within **10** of the spot it was switched on at, drops a
  fight once the character or the creature is more than **14** from that spot, and walks back to the spot when there is
  nothing to fight.
- **Free Roam** — goes for the nearest creature anywhere on the map.

Whichever hunt, the nearest qualifying creature to the character is chosen. A target that has neither lost health
nor been walked any closer for **6 seconds** — one the hunt cannot reach or cannot hurt — is given up on and left
alone for 20 seconds (a taunter never is).

## Auto-skills

Four slots, skills only (never an item or a piece of gear), chosen from every skill the character may put on the bar that belongs to a weapon — a skill of no
weapon (a heal, a summon, a banner) is pressed by hand only, and a slot saved holding one is emptied when the
character is loaded.
A few times a second the first slot whose skill is ready — off cooldown and affordable, the same test the hotbar draws
by — is pressed at the current target. A target outside the skill's range is no press: auto-combat never walks for a
skill. Every press is an ordinary cast the server judges; a skill in an auto-skill slot counts as on the bar for the
server's "is this skill theirs" check. A skill the server refuses is left alone for 10 seconds.

## Auto-loot

Decided on the server at the moment a creature falls, **entry by entry**, for whoever the kill's loot belongs to — and only when they are
on that map, alive, with auto-combat on. An entry goes straight into the bag when its kind is ticked (Gear, Potions and
food, Materials, or Everything) **and** the chance it was won at is at most the player's **drop rate** limit
(0–100%; 0 takes nothing). Everything else falls to the ground as usual. A creature's own material counts as
Materials. **Nothing an elite drops is ever auto-looted**: all of it falls, so an elite is hunted by hand.

## Auto-potion

On the server. When health falls below the player's percentage, the first item in the bag that restores health is
used; the same for mana. At most one drink a second of each kind. A draught that works over time (a mana draught, food)
is never drunk while one of its kind is still running. 0% = never.

## What is saved

The settings are one row per character, `character_auto_combat` (`accounts/sql/013_character_auto_combat.sql`),
written only when the player changes a setting, the same way the hotbar's row is — through the character server's
`PUT /server/characters/{id}/auto-combat` door, guarded by the server secret and the hand-over number. The row travels
to a map server with the character. A character that never changed a setting has no row and reads as: empty slots, no
auto-loot, no auto-potion, Radius. The shared character build string is `unwritten-character-v27`.

## Where it lives in the code

- `AutoCombatSettings.cs` — the settings document and `AutoCombatMath`, every rule as a plain function.
- `NetPlayer.AutoCombat.cs` — the server: on/off, the settings door, the row save, auto-potion.
- `Rewards.PayLoot` — auto-loot, fed each item's drop chance by `LootDropper`.
- `AutoCombatClient.cs` — the player's machine: the server's last word, and its three asks.
- `Player3DController.AutoCombat.cs` — the hunt's legs and the auto-skill presses.
- `AutoCombatWindow.cs` — the window: Hunt, Loot and Potions tabs.
- `charserver/CharacterRows.cs` — the row's door, read and announcement.

## Related

- [HUD/UI System](hud-ui-system.md) — the window and the System menu.
- [The Keeper and the Wings](keeper-and-wings.md) — the trips that stop it, and the Fly Wing that does not.
- [Networking — Implementation Notes](networking/implementation-notes.md) — the character's own rows beside its progress.
