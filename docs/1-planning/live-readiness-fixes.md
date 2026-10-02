---
status: built, not yet play-tested
owner: vince
last-updated: 2026-10-02
tags:
  - planning-production
---

# Live-Readiness Fixes

Four risks that would hurt a live world with many players, and the fix for each. Everything that can be built
without a rented machine is built (2026-10-02) and waiting to be play-tested: the item checks, saves surviving a
character server outage, the code half of encryption, and the backup and restore scripts. What still needs the rented
machine and a domain name is the encrypting front door, its certificates, and off-site storage for the backups (see
[Where the Game Is Hosted](where-the-game-is-hosted.md)).

Every claim about how the code behaves today was read from the code on 2026-10-02.

## The order, and why

1. **Item checks** — the smallest, needs no rented machine, and closes a hole a data edit could open at any time.
2. **Saves survive a character server outage** — needs no rented machine, and can be built and proven on this PC.
3. **Encryption** — the code half (a separate private delivery address) can be built now; the front door needs
   the domain and the rented machine.
4. **Continuous backups** — needs the rented machine.

---

## 1. Item checks

### The issue, before the fix

A skill that belongs to no weapon is always allowed on the hotbar and always castable, and every potion's effect
is such a skill. The only thing stopping a player from casting a potion's effect without owning the potion is
how the 19 item effects happen to be set up today: each fires only on "item used", which makes it count as a
passive, and the cast door refuses passives. No rule says an item's effect may only come from the item.

Everything else about items is already safe. The map server holds the bag, the gold and the worn gear, and checks
every item request against its own copy before acting — using, equipping, unequipping, merging, forging, folding,
quenching, inking, recycling, reordering, dropping and picking up. A player's machine has no way to write a bag.
The line in the networking notes saying otherwise is out of date. The same "is this still the stack they meant"
check is also written twice, once for item use and once for every other bag request.

### Why it is an issue

The day anyone gives an item effect a key-press form — a reasonable data edit — every player can heal, buff or
restore mana for free, as often as the cooldown allows, with nothing in the logs that looks wrong. In a live
economy that makes every potion worthless. The duplicated check means a later fix to one copy can miss the
other, and the out-of-date note sends anyone reading it after a problem that does not exist (it did exactly that
on 2026-10-02).

### The fix I recommend

1. One shared "is this still the stack they meant" check, as a plain function, called by every bag request and
   by item use. The item-use handler's own copy is deleted.
2. A rule in the one function the cast check, the hotbar check and the windows all ask: an effect named by an
   item may only ever fire through that item — refused on the hotbar and refused as a cast (decision 1).
3. Rewrite the stale line in the networking notes in place; the sprint manager re-scopes the task built on it.

What stays the same: the save format and every saved bag; requests naming a bag position plus the item expected
there; the whole bag sent back after each change; pick-up rules (reach, the 10-second owner-only window, first
request wins); quest items cannot be dropped; using an item costs no mana and no cooldown.

Rollout: map server rebuild and restart only. No database change, and no change to what the two sides send each
other.

### The expected result after the fix

An item's effect can only happen by using the item, whatever its data says. Nothing changes for an honest player.
To prove it: with no potion in the bag, put the potion's effect on the hotbar with a forged message and press it
— health does not change and the server log records the refusal. Equip, drop, fold or recycle naming a position
the player does not hold — nothing changes in the saved bag.

---

## 2. Saves survive a character server outage

### The issue, before the fix

When the character server cannot be reached, three saves are tried **once and then thrown away**: the save when
a player logs out, the save when a map server shuts down, and the save when another sign-in takes the character
over. (The save made every 15 seconds already retries while the player stays connected.) A save also replaces
the whole character with no check of which copy is newer. And nothing restarts the character server if it
crashes.

### Why it is an issue

With many players, a character server restart or crash at any moment means everyone who logged out during it
loses whatever they did since their last 15-second save, and nobody can sign in until someone notices and starts
it again by hand. Without a "which copy is newer" check, a map server that was cut off and reconnects late can
overwrite a player's newer progress with an older copy — rolling them back.

### The fix I recommend

1. One save queue per map server, holding the newest unsent save per character and retrying until it lands.
   Every save goes through it, so a save survives the player leaving.
2. A takeover sends that character's waiting save and waits for it to land before answering.
3. A map server sends its waiting saves before it reports back in, so nobody can be placed from an old copy.
4. For a short time after it starts, the character server refuses entry with "try again", until every map
   server has had the chance to report in.
5. Old saves are refused: a number on each character goes up each time it is handed to a map server, every save
   carries it back, and a save with an older number is refused and logged. Needs one database migration.
6. A supervisor that restarts the character server when it exits, running a prebuilt program.
7. One supervised character server, not several at once (decision 6).

What stays the same: the map server still never touches the database; the 15-second save beat, the 30-second
report and the 2-minute "forget a silent map server" rule; tickets; doors within one map server stay instant; a
logged-out player's name is still freed immediately.

Rollout: migration first; then the character server, accepting saves with no number for now; then map servers
one at a time; then make the number required; then the supervisor. The shared character-build string must not
change during this, or the two halves refuse each other.

### The expected result after the fix

A character server outage loses nobody's progress and ends by itself. During it, players already in the world
keep playing; new sign-ins and doors into another map server's zones wait until it is back. To prove it: kill the
character server while two players are on; both pick up loot, one logs out, one stays; restart it — both
characters in the database match their last state. Then the same with the logged-out player signing back in
within 10 seconds of the restart: refused with "try again", then let in with the loot.

---

## 3. Encryption

### The issue, before the fix

The sign-in service and the character server speak plain, readable web traffic. The front door that would
encrypt them is written but not running. And one address does two jobs: the address handed to players is also
where the character server delivers whole characters, entry tickets and the shared server secret to a map
server, also as readable traffic.

### Why it is an issue

Over the internet, anything on the path between a player and the server — a café's Wi-Fi, an internet provider —
can read a player's password and sign-in token, and take the account. Worse, on a rented machine the character
deliveries would cross the internet too, and anyone who reads the shared server secret from one of them can
write any save for any character.

Built games already refuse plain web traffic to anything but the machine they run on. That protects copies
handed to players, and it is also why a map server pointed at 192.168.1.8 could not register on 2026-10-02.

### The fix I recommend

1. Domain names, the front door and certificates, with the plain doors still open.
2. A map server on the same machine as the character server keeps talking to it on localhost; one on another
   machine uses the encrypted address. No code change.
3. A second address used only for delivering characters to a map server (a private network address, or
   localhost on one machine), separate from the address players get, and only the character server allowed to
   reach the delivery port. The setting is `MAP_SERVER_ARRIVALS_ADDRESS` (decision 7).
4. New game and launcher builds with the encrypted addresses; bump the published version so old copies are told
   to update.
5. Shut the plain doors: both services answer on localhost only, and the plain ports are blocked from outside.
6. Copies handed to players keep refusing plain web traffic (decision 9 covers LAN testing).

What stays the same: the players' game connection and the one-time entry ticket; the server secret check; only
map servers write progress; local development on localhost; one settings file for every address.

### The expected result after the fix

No password, token, save or server secret ever crosses the internet readable. Players see only that the game's
addresses are named web addresses. To prove it: both services answer over their encrypted addresses with a valid
certificate; the plain ports refuse from outside; a packet capture during a remote sign-in and world entry shows
nothing readable; a copy on another network signs in, enters, picks something up, relogs, and still has it.

---

## 4. Continuous backups

### The issue, before the fix

The database is copied once a day, at 04:10, and the newest 14 copies are kept on the same machine as the
database. Nothing copies them anywhere else, and a restore has never been practised.

### Why it is an issue

If that machine's disk dies, every player loses up to a day of progress — and if the machine itself is lost, the
backups go with it. A restore that has never been tried may not work, and nobody knows how long it takes, at the
one moment it is needed.

### The fix I recommend

Send the database's change log to storage at a different company from the host, at least once a minute, so the
database can be rebuilt as it was at any moment (decision 11).

1. A storage bucket at another company, with a key that can add files but never delete them. *Waits for the
   rented machine.*
2. Turn change-log sending on: one database restart of a few seconds, in a quiet hour. *Built: a script writes the
   settings and says when the restart is needed; it has not been turned on for the database on this PC.*
3. A full copy weekly, keeping three so a rebuild reaches back 14 days. *Built.* Daily partial copies are not.
4. The existing daily copy kept as a second, independent backup, and uploaded to the same bucket. *The daily copy
   stays as it is; the upload waits for the bucket.*
5. A restore script that rebuilds a spare database at a chosen moment and checks it. *Built, and rehearsed on a
   spare database: rebuilt to after a change it had the change, rebuilt to before it did not, 6 seconds each.*
6. An alert when sending fails or the disk is filling up. *Not built.*
7. Compress each archived file before it is kept. Postgres writes a full 16 MB file even when it closes one after a
   minute nearly empty, so a busy day archives about 23 GB. *Not built; needed before going live.*

A second live database kept in step is a way to stay up, not a backup — it copies a bad deletion within a second
— so it is a later, separate step.

What stays the same: the daily copy and its checks; keeping 14; the 15-second save beat; the character server as
the only writer of progress.

Prices and timings here are from general knowledge, not checked.

### The expected result after the fix

If the database's machine dies, players lose at most about the last minute, and the backups survive on another
company's storage. To prove it, monthly: note the time, raise one test character's level, wait 2 minutes. Rebuild
a spare database to 30 seconds after the noted time — the level is there. Rebuild to 30 seconds before — it is
not. Write down how long the rebuild took.

---

## Decisions

All taken as recommended, on 2026-10-02.

**Item checks**
1. An effect an item runs may only ever fire through that item — refused on the hotbar and refused as a cast.
2. "Insert card" is off the item menu until cards are designed and the server holds them. Slotting one changed only
   the player's own copy, and its picker listed every card whether owned or not.
3. Every bag request must name the item it expects at that position; a blank one is refused.
4. A potion whose effect did not run (the drinker died first) stays in the bag.

**Character server outages**

5. A map server keeps its unsent saves on disk too, so they survive that map server crashing during an outage. The
   disk is touched only when a send fails (and at shutdown): one file per character, replaced by a newer save,
   deleted when it lands.
6. One character server, restarted automatically. Running several at once is revisited only if a measured load test
   shows one cannot keep up.

**Encryption**

7. The private delivery address setting is `MAP_SERVER_ARRIVALS_ADDRESS`. Empty means the address players are given,
   which is right only on a desk or a home network.
8. The old plain update address keeps serving old launchers until the version check has turned away the last old
   copy.
9. Plain web traffic is allowed in development builds only, and the build script has a development-build option
   for testing on your own Wi-Fi. Copies handed to players keep refusing it.
10. The phone's typed sign-in address may be a whole address (`https://…`), used exactly as typed.

**Backups**

11. Self-run database with change-log sending.
12. The storage company is chosen when the rented machine is — any one outside the host with the common storage
    interface.
13. A restore reaches back 14 days.
14. Each monthly restore rehearsal is recorded in the network runbook.

## Related

- [Where the Game Is Hosted](where-the-game-is-hosted.md) — the machine these fixes go on.
- [Networking — Architecture](../2-systems/networking/README.md) — the four programs and how they talk.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — the runbook these fixes change.
