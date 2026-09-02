---
status: decided, not built
owner: vince
last-updated: 2026-09-02
tags:
  - systems-combat-design
---

# Action Alarms — what a body is DOING, across the wire

**The gap this closes.** The server already tells every machine what *happened* — the damage, the crit, the death. Nothing tells them what a **body was doing** when it happened. So numbers pop off a monster that never moves a limb, and a player's swing is invisible to everyone but themselves.

An **alarm** is one announcement that says: *this unit began this action, at this moment, and it lasts this long.* Nobody ever sends "stop" — it expires on its own clock. Borrowed from Ragnarok, whose server source was read for this doc rather than remembered.

Sibling docs: [Networking — Implementation Notes](networking-implementation-notes.md) (what is actually built) · [Network-Readiness Audit](network-readiness-audit.md) (the survey that chose the shape) · [Starting the Network](../1-planning/starting-the-network.md) (how to run it).

**Status: decided on paper, none of it built.** Every "does X" below is a decision, not a description. What is true *today* is in [What is broken today](#what-is-broken-today), and it is all measured.

---

## Ownership — the whole table, one place

This is the answer to "who owns what" for everything the alarm touches. The rows about position, health and combat outcome restate what the [implementation notes](networking-implementation-notes.md) already own; they are here so the picture is not split across two documents.

| Thing | Who owns it | Everyone else |
| --- | --- | --- |
| Where **you** are | your machine moves you; the server **checks** it | watches |
| Where anything else is | the server | watches |
| A monster's brain, path and target | the server | watches |
| Whether a blow lands, and for how much | the server | is told, and shows it |
| Health, death, revival | the server | is told |
| **When your own animation starts on your own screen** | **your machine** | — |
| **When your action starts on everyone else's screen** | **the server's clock** | is told, and shows it |
| **How long an action lasts** | **the server** (a number it holds, never a clip length) | is told |
| **Which clip, which model, how it looks** | **each machine, locally** | — |
| Your bag, money and worn gear | the server | asks, is sent the result |
| Writing to the database | the **character server**, only | the map server pushes progress to it and never writes |

**The one-line rule:** your machine owns how things *look* and when *your own* body reacts to *your own* input. The server owns everything that is *true*.

---

## The flow, end to end

### Your own swing

1. You press attack. **Your character swings that same frame**, locally, waiting for nothing.
2. Your machine sends one message: *"I am swinging at that thing."* Nothing else — no timestamp, no damage, no element. The server holds all of it.
3. The map server checks: is that target real, is it in reach, **is your next swing due yet**. If not due, the request is dropped.
4. The map server stamps the moment on the shared clock, runs the swing on its own timer, and decides the outcome.
5. It announces **one** message to everyone who can see you: who swung, at what moment, for how long, and what the blow did.
6. Every other machine plays your swing from that moment and pops the damage number partway through it, wherever its own art says the contact frame is. Your machine ignores its own echo — you already swung.

**Why the timestamp is the server's and not yours.** A machine that writes its own timestamp can backdate a swing to slip past the cadence check, or post-date one to shove everyone's view of it out of step. The server's own stamp costs nothing and cannot be gamed. Ragnarok's action packet carries the **server's** tick for this exact reason.

**Why one message and not two.** Today your machine sends a second message at the contact frame of its own animation, which hands it the say over *when* the blow lands. Dropping that message and letting the server run the swing on its own timer removes that, and it makes the number and the animation **the same message**, so they can never disagree. The delay is identical either way — one trip out, a swing length, one trip back — so this costs nothing in feel.

### A monster's swing

Same shape with no request at the front: the monster's brain is already on the server, so the moment it bites, the server stamps it and announces it alongside the blow.

### Locomotion

Not an alarm — a **continuous value**. The server publishes each monster's actual ground speed with its position. Watchers pick walk or run from that number on the same threshold every character uses, and churn the legs at the right cadence for a slowed monster. See [decision 5](#5-locomotion-is-told-as-a-speed-not-a-flag).

---

## Three kinds of traffic, three different rules

Confusing these is how a channel ends up carrying something it cannot serve. Every new thing that crosses the wire is one of these:

| Kind | Examples | Delivery | A latecomer… |
| --- | --- | --- | --- |
| **Continuous state** | position, facing, health, ground speed | a value, re-sent, smoothed | is told the current value |
| **An action that expires by itself** | a swing, a cast, a channel, a death | an alarm: which, when, how long | **joins it at the right frame** |
| **A one-off with no duration** | a damage number, a hit spark, a crit flag | announced once | **is told nothing** |

A late *number* is dropped; a late *action* is joined. Opposite rules, so one channel cannot carry both — but both halves of one event must be **fired from the same instant on the server**, or the number floats up before the arm moves.

The third kind already works: a blow on a monster is already announced to everyone watching it and run through the same door the offline game used, so the digits, the flash and the impact are not written twice. **Do not rebuild it.**

---

## The decisions

### 1. The action is HELD as a value on the unit, not shouted once

The unit carries: which action, the moment it began, how long it runs. The networking library sends a value when it changes, so the shout comes free — and **a player who arrives mid-action is handed it too**, because it is state rather than an event.

*Rejected:* a fire-and-forget broadcast. It reaches only whoever is already watching, so anyone walking into view sees a monster standing idle in the middle of a two-second cast. Ragnarok needs two packets to cover both cases — an action packet and a spawn packet carrying the current action; one held value covers both.

### 2. A viewer arriving mid-action ALWAYS joins it — never skips it

Somebody who starts watching a body 90% of the way through its swing plays the last 10% of that swing. No threshold, no "it's nearly over, don't bother".

*Decided by vince, 2026-09-02.* The alternative — skip an action that is mostly finished, so the body doesn't twitch — was offered and refused. **Consequence, accepted knowingly:** a swing joined near its end shows only its final frames and can read as a small jerk. That is preferred to a body that stands still while its damage number goes up.

### 3. The moment is stamped on the SHARED clock

The networking library carries a server-synchronised clock. **Nothing in this codebase uses it yet** — that is measured, not assumed. Without it a start moment is meaningless, because two machines' uptimes differ by however long each has been running.

### 4. The duration is a number the SERVER owns — never an animation clip's length

The map server is headless and must not read art to decide gameplay timing. Ragnarok does the same: attack motion is a field in the monster database, not something the client's animation dictates.

Staged, so nothing has to be re-authored:

- **First:** send the beat the server already computes at the moment of the bite — the attack interval after attack-speed scaling. The watcher fits the swing inside it. Every existing monster is instantly right with no new data, and a hasted monster's swing shortens by itself instead of sitting at a hand-typed 0.45 seconds.
- **Later, only if a designer wants per-monster control:** an authored swing length beside the interval. It must ship with a value filled in for **every existing monster in the same change** — a blank would read as zero and silently mean "no swing at all".

### 5. Locomotion is told as a SPEED, not a flag

The server already works out each monster's exact ground speed every think, with its Slow or Haste folded in. Send that number.

*Rejected:* a walk/run flag, and also measuring the speed on the watching side. **Measuring cannot work here, and the arithmetic says so:** positions arrive 10 times a second and a watched body closes 12/second of the gap toward them, so within each 100 ms gap the apparent speed falls to about 30% of the real one and jumps back when the next position lands — a swing of roughly 3×, ten times a second, straight across the walk/run threshold. A body that stops keeps reading as walking for about 0.3 seconds. A flag would fix the gear but not the leg cadence of a slowed monster; the number fixes both.

### 6. The VICTIM's reaction rides the blow, not the alarm

The alarm says what the **actor** did. What happened to the **victim** — the flinch, the stagger — rides the damage announcement, which already staggers a watched monster today. Ragnarok puts the victim's motion time in the damage packet for the same reason.

This settles a **double owner** that exists right now: a shadow monster's body *also* infers a flinch from its health number dropping. The health-drop copy is the one to delete.

### 7. The server owns the BEAT, not just the outcome

The swing alarm carries when the next swing is allowed, and the server refuses one that comes early.

**Why now:** the server checks reach and has a flood ceiling of **20 blows per second**, but it does **not** enforce a character's real attack cadence. The code says so itself — the real cadence "belongs to the fighter, and lands with per-connection fighters" — and per-connection fighters landed in the commit before this doc. That guard has been waiting for the thing that just arrived.

**This does not delay your swing.** Your machine still animates the instant you press. It simply no longer sets its own attack speed.

### 8. The map server renders nothing, rigs nothing, writes nothing to a database

Two of these are already true and one is broken.

- **No database:** already true. The map server pushes progress to the character server, which is the only thing that writes. Its own code says "this machine has no database and never will".
- **No rendering:** already true for effects, sound, damage numbers and heads-up text, each of which asks whether there is anybody to see or hear.
- **No rigs: broken today.** A shadow monster on the server loads a character model and assembles an animated rig for nobody, because "is this a shadow" is decided by the presence of the animation driver. Make it a fact about the monster instead. **Do not just delete the rig build** — that driver also sets the monster's travel gear, so removing it alone drops every shadow monster to the small-monster speed (0.30×) and they visibly shuffle. Move the gear choice onto the brain in the same change.

---

## What may NEVER ride this channel

**An alarm makes a watcher SHOW something. It never decides anything.**

Nothing on it may apply damage, grant or remove a state, move a body, spend a resource, or open a door. The moment it can, there are two ways to make the same thing happen — which is what every other rule in this codebase exists to prevent.

Two more, so they are not discovered later:

- **It is not a way to skip a request.** A client seeing its own alarm has learned nothing it may act on.
- **It is not a general event bus.** An alarm is *one unit doing one thing for a known length*. Anything that is not that shape belongs in one of the other two kinds of traffic.

---

## What is broken today

All measured on 2026-09-02, in the code, not reasoned.

- **A watched monster never walks or runs.** Its mover is switched off when it arrives from the server, and the animation driver asks that switched-off mover whether it is walking. A switched-off mover always answers no.
- **A watched monster never swings.** The moment "it bit someone" is stamped by its brain, and a watched copy's brain is silenced, so nothing is ever stamped.
- **A player's swing is invisible to everyone else.** Not mistimed — absent. There is no attack animation on another player's body at all; the damage numbers appear while their character stands still.
- **A monster's flinch has two owners** — the announced blow, and the body's own inference from its health dropping.
- **The server does not enforce attack cadence** — only a 20-per-second flood ceiling and a reach check.
- **The map server builds animation rigs** for shadow monsters that nobody will ever look at.
- **The shared clock is unused.** No code reads it.

What already works, and must not be rebuilt: a blow on a monster or a person is announced to everyone in range with its amount, element, crit, miss, overkill and whether it folds into a rising total, and each machine runs it through the same door the offline game used.

---

## Build order

1. **Locomotion told as a speed.** No new concepts, smallest change, and it fixes the most visible half — monsters that slide instead of walking.
2. **The held action value, and the alarm that fires from it**, on monsters first: the server stamps a bite and announces it; watchers play the swing and join one already in progress.
3. **Delete the flinch's second owner** once the blow's flinch is proven to be the one arriving.
4. **The same alarm on players**, so another person's swing exists on your screen at all.
5. **Move the beat to the server** — one message at the start of a swing, the server runs the motion and lands the blow, and an early swing is refused.
6. **Take the rigs off the map server**, moving the travel-gear choice to the brain in the same change.

Steps 1–4 are presentation and change no outcome. Step 5 changes who decides when a blow lands and should be played with two people before anything is built on top of it.

**Skills are out of scope.** A skill's damage is still the client's word until skills resolve on the server; a skill's alarm belongs with that work, not before it.

---

## Related

- [Networking — Implementation Notes](networking-implementation-notes.md) — what is actually built, the shell/body split, and the traps already paid for.
- [Network-Readiness — Combat Audit & Refactor Plan](network-readiness-audit.md) — the survey that chose the shape.
- [Starting the Network](../1-planning/starting-the-network.md) — the runbook for standing the world up.
- [Active Skill System — V3](active-skill-system-v3/README.md) — the effects the server cannot yet resolve, which is why skills are out of scope here.
