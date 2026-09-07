---
status: built and play-tested
owner: vince
last-updated: 2026-09-07
tags:
  - systems-combat-design
---

# Action Alarms — what a body is DOING, across the wire

**The gap this closes.** The server already tells every machine what *happened* — the damage, the crit, the death. Nothing tells them what a **body was doing** when it happened. So numbers pop off a monster that never moves a limb, and a player's swing is invisible to everyone but themselves.

An **alarm** is one announcement that says: *this unit began this action, at this moment, and it lasts this long.* Nobody ever sends "stop" — it expires on its own clock. Borrowed from Ragnarok, whose server source was read for this doc rather than remembered.

Sibling docs: **[Architecture](README.md)** (the shape on one page) · [Networking — Implementation Notes](implementation-notes.md) (what is actually built) · [Network-Readiness Audit](readiness-audit.md) (the survey that chose the shape) · [Starting the Network](starting-the-network.md) (how to run it).

**Status: built, and played on 2026-09-02 — it works.** [What shipped, and what has not](#what-shipped-and-what-has-not) is the ledger.

---

## Ownership

**The table lives in [the architecture overview](README.md)**, which is the one home for it. The rows that matter
here: when your own animation starts is your machine's; when your action starts on everybody else's screen is the
server's clock; how long an action lasts is a number the server holds, never a clip length; and which clip, model
and effect are drawn is each machine's own business.

**The one-line rule:** your machine owns how things *look* and when *your own* body reacts to *your own* input.
The server owns everything that is *true*.

---

## The flow, end to end

### Your own swing

1. You press attack. **Your character swings that same frame**, locally, waiting for nothing.
2. Your machine sends one message: *"I am swinging at that thing."* Nothing else — no timestamp, no damage, no element. The server holds all of it.
3. The map server checks: is that target real, does it hold a fighter for you, **is your next swing due yet**. If not due, the request is dropped and the refusal logged.
4. The map server stamps the moment on the shared clock, runs the swing on its own timer, and at the contact point judges reach and decides the outcome — reach at the moment of contact, exactly where it was judged before.
5. It announces **one** message to everyone who can see you: who swung, at what moment, for how long, and what the blow did.
6. Every other machine plays your swing from that moment and pops the damage number partway through it, wherever its own art says the contact frame is. Your machine ignores its own echo — you already swung.

**Why the timestamp is the server's and not yours.** A machine that writes its own timestamp can backdate a swing to slip past the cadence check, or post-date one to shove everyone's view of it out of step. The server's own stamp costs nothing and cannot be gamed. Ragnarok's action packet carries the **server's** tick for this exact reason.

**Why one message and not two.** Your machine used to send a second message at the contact frame of its own animation, which handed it the say over *when* the blow landed. That message is gone: the server runs the swing on its own timer, so the number and the animation are **the same message** and cannot disagree. The delay is identical either way — one trip out, a swing length, one trip back — so it cost nothing in feel.

### A monster's swing

Same shape with no request at the front: the monster's brain is already on the server, so the moment it bites, the server stamps it and announces it alongside the blow.

### Locomotion

Not an alarm — a **continuous value**. The server publishes each monster's actual ground speed with its position. Watchers pick walk or run from that number on the same threshold every character uses, and churn the legs at the right cadence for a slowed monster. See [decision 5](#5-locomotion-is-told-as-a-speed-not-a-flag).

**The sprint gear is the one exception, and it is told rather than measured.** A hasted run and a sprint are both simply fast, so no threshold can separate them — a speed-buffed jog would be drawn as a sprint and there is no number that would fix it. The machine that owns the legs says which gear it is in, once, on the frame it changes. Walk and run stay on the speed, because the speed genuinely does separate those two.

---

## Three kinds of traffic, three different rules

Confusing these is how a channel ends up carrying something it cannot serve. Every new thing that crosses the wire is one of these:

| Kind | Examples | Delivery | A latecomer… |
| --- | --- | --- | --- |
| **Continuous state** | position, facing, health, ground speed, the travel gear, the pose a body is holding | a value, re-sent, smoothed | is told the current value |
| **An action that expires by itself** | a swing, a skill's release swing, a cast wind-up, a death | an alarm: which, when, how long | **joins it at the right frame** |
| **A one-off with no duration** | a damage number, a hit spark, a heal sparkle, a crit flag | announced once | **is told nothing** |
| **Something that lasts until it is over** | a poison glow, the charge-up glow under a cast | named when it starts, named again when it ends | is told nothing, and picks it up when it next starts |

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

The networking library carries a server-synchronised clock, and every beat is stamped on it. Without it a start moment would be meaningless, because two machines' uptimes differ by however long each has been running.

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

## What shipped, and what has not

Built and played on 2026-09-02 — creatures walk, run, swing and flinch, and it holds up in play. Two clients have since been run against it and both hold their place in the world, though **no fight with two people in it has been watched yet**: nobody has seen another person's swing on their own screen, and the server-timed landing (item 7) has not been watched in a fight either.

**Done:**

1. **Locomotion is told as a speed** — creatures publish the mover's own live figure; people have theirs worked out on the server from the position reports it accepted, and zeroed when they go quiet for longer than two report intervals (a person who stops walking stops reporting).
2. **The held action value and the alarm**, on creatures and on people alike. The clip plays at its own natural length, capped by the beat.
3. **The flinch has one owner** — the announced blow. The body's inference from its health number dropping is gone.
4. **A person's swing is announced**, with the pose chosen from the weapon the server holds for them — the one pose their own machine does NOT report, precisely because this already owns it.
5. **The map server builds no rigs** — it applies the capsule, the footprint and the character travel gears, and fetches no model.
6. **The shared clock is in use** — every beat is stamped on it, by the server.

7. **The server owns the beat AND the landing** — decision 7, both halves. A swing is asked for at the moment it begins; the server refuses one that arrives before that fighter's attack speed allows it, announces it, and lands it part-way through on its own clock. When in the swing that is happens to be one number in the shared character spec, which the swinger's animation and the server both read, so they cannot drift.

**The one exception, and the swinger's machine says so by asking at a different moment: a bow.** An arrow's blow lands when the arrow arrives, which is worked out by flying it — so a bow still asks at the moment its arrow lands, and the server lands that one straight away. Two consequences, both accepted: a bow's swing appears on other screens when its arrow lands rather than when it is loosed, and for a bow the flight time is still the swinger's to measure. Fixing it properly means the server flying its own arrow, which is a movement simulation rather than a timer.

The early-swing guard stays deliberately loose — a blow may arrive up to half a beat early and still be believed — because the swinger's machine still times its own animation and refusing an honest player is worse than missing a cheat. It cuts the fastest a machine can possibly swing from the flood ceiling's twenty a second down to twice its real rate. Every refusal is logged rather than dropped in silence.

8. **Every pose a body takes travels, and so does every cue** (2026-09-07). Both are general: nothing names a
   particular pose or a particular effect anywhere, so one added later crosses the wire without being wired up.
   - **Poses** are reported from the one place every pose in the game resolves — so a jump, a dodge, a flinch, a
     death, a seat, a guard and a skill's release swing all reach every screen. Two are deliberately not sent, and
     both because something else already owns the answer: a travel gear (worked out from the speed and the gear
     each machine is already told) and a weapon swing (the server announces that itself, from the weapon it holds).
   - **Cues** — every visual reaction in the game — are announced by whichever machine DECIDED the event, which in
     a shared world is always the server. A machine that has joined somebody else's world fires none of its own and
     draws only what it is told, which is why the same puff can no longer play twice on one screen and not at all
     on another. That is the same bargain your own spell effects already make.
   - A **cast wind-up** now carries where it is aimed, so the ring on the ground appears under every watcher's copy
     of the caster and not only the caster's own.

What already worked and was NOT rebuilt: a blow on a creature or a person is announced to everyone in range with its amount, element, crit, miss, overkill and whether it folds into a rising total, and each machine runs it through the same door the offline game used.

---

## Related

- [Networking — Implementation Notes](implementation-notes.md) — what is actually built, the shell/body split, and the traps already paid for.
- [Network-Readiness — Combat Audit & Refactor Plan](readiness-audit.md) — the survey that chose the shape.
- [Starting the Network](starting-the-network.md) — the runbook for standing the world up.
- [Active Skill System — V3](../active-skill-system-v3/README.md) — the effects the server now runs, and the vocabulary a cast's alarm would use.
