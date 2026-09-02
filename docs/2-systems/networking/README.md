---
status: current
owner: vince
last-updated: 2026-09-02
tags:
  - systems-combat-design
---

# Networking — Architecture

The whole shape of the multiplayer layer on one page: the machines, what each one is allowed to decide, how a
person gets into the world, and what crosses the wire while they are in it.

**Start here, then go deeper:**

- [Implementation Notes](implementation-notes.md) — what is actually built, the conventions, and every trap already paid for. **Read before touching anything networked.**
- [Action Alarms](action-alarms.md) — how a body says what it is *doing* across the wire.
- [Readiness Audit](readiness-audit.md) — the July survey that chose this shape. Historical; kept for the reasoning.
- [Starting the Network](../../1-planning/starting-the-network.md) — how to stand it all up and how to tell each step worked.

**A rendered version of this page**, with the same diagrams, is published at <https://claude.ai/code/artifact/2af21c76-92b4-4fe3-af06-1f7d0a7848ca> — a view for showing somebody, not a second source of truth. **This file is the truth**; when they disagree, this one is right and the other is stale.

---

## The four processes

Four programs and one database. The arrangement is Ragnarok Online's, deliberately: the thing running the world
never touches the store, and a player's own credentials never open it.

```mermaid
flowchart LR
    P1["Player's game"]
    P2["Another player's game"]
    L["Sign-in service"]
    C["Character server"]
    M["Map server<br/>headless game"]
    D[("Postgres")]

    P1 -->|"signs in, gets a token"| L
    P2 -->|"signs in, gets a token"| L
    P1 -->|"asks to enter, with the token"| C
    P2 -->|"asks to enter, with the token"| C
    C -->|"pushes the whole character<br/>then hands back a one-time ticket"| M
    P1 -->|"dials with the ticket"| M
    P2 -->|"dials with the ticket"| M
    M -->|"writes progress back"| C
    L --- D
    C --- D

    style M fill:#2d3f50,color:#fff
    style D fill:#3b3b3b,color:#fff
```

**The map server has no database and never will.** Everything that happened in the world becomes permanent by
being sent to the character server, which is the only thing that writes.

**Two roads that never cross.** A *person's* token reaches their own characters and nothing else. The two
*servers* recognise each other by a shared secret, which registers a map server, receives a pushed character and
writes a save back — and cannot read anybody's character list. Each is refused on the other's road.

---

## Getting in

A player is **announced before they knock**. The character server hands their whole character to the map server
first, and only then tells them where to go and gives them a one-time ticket. A connection that cannot produce
one was never announced and is nobody.

```mermaid
sequenceDiagram
    participant G as Player's game
    participant L as Sign-in service
    participant C as Character server
    participant M as Map server

    G->>L: email and password
    L-->>G: a token
    G->>C: let me in as this character
    C->>C: read the character out of the database
    C->>M: here is their whole character, expect them
    M-->>C: accepted
    C-->>G: go to this address, with this ticket
    G->>M: dial, then say who I am and show the ticket
    M->>M: is this somebody we were told to expect?
    M-->>G: here is your body
    Note over G,M: only now does the world come up on the player's screen
```

**Where this goes wrong, and what it looks like.** If the game is started without being told where the character
server is, it has nobody to ask, so it is never announced — and the map server refuses it at the door, forever.
It now says so and stops instead of retrying in silence. The exact launch command is in
[Starting the Network](../../1-planning/starting-the-network.md).

---

## Who decides what — the single answer

This table is the one home for it. The other documents in this folder point here rather than keeping copies.

| Thing | Who owns it | Everyone else |
| --- | --- | --- |
| Where **you** are | your machine moves you; the server **checks** it | watches |
| Where anything else is | the server | watches |
| A creature's brain, path and target | the server | watches |
| Whether a blow lands, and for how much | the server | is told, and shows it |
| **When a blow lands inside a swing** | the server, on its own clock | is told |
| Health, death, revival | the server | is told |
| **When your own animation starts on your own screen** | **your machine** | — |
| **When your action starts on everyone else's screen** | the server's clock | is told, and shows it |
| **Which clip, which model, how it looks** | **each machine, locally** | — |
| Your bag, money and worn gear | the server | asks, is sent the result |
| Writing to the database | the **character server**, only | the map server pushes progress to it and never writes |

**The one-line rule:** your machine owns how things *look*, and when *your own* body reacts to *your own* input.
The server owns everything that is *true*.

**A player's machine owns no fact and decides no outcome.** That is literal: both damage channels refuse to act
anywhere but the server, the bag refuses to be added to anywhere but the server, and a save from a player's own
machine is refused while they are in a world.

---

## One networked thing is two objects

```mermaid
flowchart TB
    subgraph server["Map server"]
        RC["The real creature<br/>brain, pathing, health"]
        SH["Shell<br/>id, position, facing, health"]
        RC -->|"copied onto the wire"| SH
    end
    subgraph client["Every player's machine"]
        SH2["Shell<br/>the same few values"]
        B["Body<br/>built locally from the id"]
        SH2 -->|"builds and moves"| B
    end
    SH ==>|"a few dozen bytes,<br/>ten times a second"| SH2

    style RC fill:#2d3f50,color:#fff
    style B fill:#3f2d50,color:#fff
```

**Why, and do not undo this.** Bodies are heavy — a character is a rigged model, a creature is a baked asset.
Sending an **id** and letting each machine build its own copy costs a few dozen bytes and reuses the spawn code
the game already had, instead of shipping models over the wire.

**A watched copy does not think.** Its brain is silenced and its mover switched off — but the brain is left
switched *on* as a component, because the registry it belongs to is how the game finds something to attack,
picks area-effect targets and draws map blips.

---

## Three kinds of traffic, three different rules

Confusing these is how a channel ends up carrying something it cannot serve. Everything that crosses is one of
these three, and a latecomer is treated differently by each.

| Kind | Examples | How it is sent | Somebody who arrives late… |
| --- | --- | --- | --- |
| **Continuous state** | position, facing, health, ground speed | a value, re-sent and smoothed | is told the current value |
| **An action that expires by itself** | a swing, a cast, a death | which action, when it began, how long | **joins it at the right frame** |
| **A one-off with no duration** | a damage number, a hit spark, a crit flag | announced once | **is told nothing** |

A late *number* is dropped; a late *action* is joined. Opposite rules, so one channel cannot carry both — but
both halves of one event are fired from the same instant on the server, or the number floats up before the arm
moves. The detail is in [Action Alarms](action-alarms.md).

---

## A swing, end to end

The one flow worth knowing by heart, because every rule above shows up in it.

```mermaid
sequenceDiagram
    participant Y as Your machine
    participant S as Map server
    participant O as Everyone else

    Y->>Y: you press attack — your character swings THIS frame
    Y->>S: I am swinging at that thing
    Note right of Y: nothing else — no damage,<br/>no element, no timestamp
    S->>S: is that target real? is your next swing due?
    S->>O: this person began a swing, at this moment, for this long
    S->>S: hold the blow until the contact point of the swing
    S->>S: is the target still in reach? roll the outcome
    S->>O: the blow: amount, element, crit, miss
    S->>Y: the same blow
    Note over Y,O: the swing and its number come from one server moment,<br/>so they can never disagree
```

**Your own swing never waits.** The animation starts the frame you press. What waits for the server is the
*outcome* — whether it landed, and for how much.

**The server stamps the moment, not your machine.** A machine that writes its own timestamp could backdate a
swing past the cadence check or post-date one to shove everybody's view out of step.

**One exception: a bow.** Its blow lands when the arrow arrives, which only the machine flying the arrow knows —
so a bow asks at that moment instead, and the server lands it straight away.

---

## The rules that hold this up

Each of these was paid for. The incidents are in [Implementation Notes](implementation-notes.md).

- **There is no offline game.** A player who is not joined is not in the world, so no local code is ever the
  authority and there is no fallback branch. The editor is not exempt.
- **Never gate behaviour on "is the wire up right now."** That is a timing accident. Ask what job this copy was
  *given*, which is settled at start-up and cannot change while the process lives.
- **The wire comes up before the world.** A server *is* the world and may start it immediately; a player earns
  the right to try, and their world comes up only once the server has actually handed them a body.
- **A request carries only what the server cannot already know.** If the far end could look it up, the field is
  not information — it is an invitation.
- **Everything watching receives a blow; only one machine may be taught by it.** The announcement names who
  swung, and each machine credits the person at its own keyboard only when that matches.
- **Per-hit and per-frame paths obey [PERFORMANCE.md](../../../unity/Assets/Scripts/PERFORMANCE.md).** Shell
  lookups are dictionaries, and idle bodies — people and creatures alike — skip their position blend entirely.

---

## Related

- [Implementation Notes](implementation-notes.md) — the code-facing companion, and the trap list.
- [Action Alarms](action-alarms.md) — what a body is doing, across the wire.
- [Readiness Audit](readiness-audit.md) — the survey that chose this shape.
- [Starting the Network](../../1-planning/starting-the-network.md) — the runbook.
- [Querying the Database](../../1-planning/querying-the-database.md) — reading back what it persisted.
- [Roadmap to Launch](../../1-planning/roadmap-to-launch.md) — where this sits against the plan.
