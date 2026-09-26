---
status:
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# unwritten — Action Plan — On-going

Tasks whose status is `[ON-GOING]` — built, or under way, and not yet proven. The plan is three files by status — [done](done.md), [on-going](on-going.md), [not-started](not-started.md) — and a task moves between them when its status changes, wording untouched. A subtask sits in the file of its OWN status, indented as it was, and its ID names its parent (`ST-3-K` belongs to `MT-3`). Week goals repeat in every file that has a task from that week. The intro and the **How to read it** key are in [on-going.md](on-going.md).

Twelve weeks of work, as tasks. **This is the "what next" list.** [Roadmap to Launch](../roadmap-to-launch.md) is the "how far are we" picture — what is built, what is half-built, what is missing, all counted. Neither repeats the other.

**Every fact in this plan was measured on 2026-09-12 or 2026-09-13** by reading the game's own files, its database or its builds.

---

## How to read it

Each week opens with **the goal** — what you can do in the game at the end of it that you could not do at the start. Then its tasks.

Every task carries:

- **A status**, inside the brackets in front of the task: `[DONE]`, `[ON-GOING]`, or empty `[ ]` when it has
  not started — and a task sits in the file of its status: [done](done.md), [on-going](on-going.md),
  [not-started](not-started.md). Changing a status moves the task's whole block to the other file, wording
  untouched. **A task is never marked superseded, dead or stale.** When a later design changes what a task is
  about, the task is REWRITTEN to say the current design, keeps its ID, and takes the status that truth has
  today — a settled decision is `[DONE]`, work built but not yet watched is `[ON-GOING]`. Nothing on these pages
  describes a design that no longer exists.
- **An ID.** A main task is `MT-N`; a subtask sits indented under its main task as `ST-N-A`, where `N` is the parent's number and `A`, `B`, `C` … count its subtasks in order. An ID never changes and is never reused: a new main task takes the next free number, and a new subtask takes the next free letter under its parent.
- **`[D]` decision · `[A]` authoring · `[E]` engineering.** A decision task is yours alone. An authoring task can be handed to the data-author agent. An engineering task is code.
- **An estimate in days**, as a range. **The range already carries the swing from any decision it depends on** — you do not need to answer anything now for the estimate to hold.
- **Done when** — the concrete check. If you cannot watch it happen or count it, it is not a check.

**Decisions are tasks, never gates.** Each one sits in the week *before* the work that needs it, with a recommendation attached so you can settle it by agreeing. If a decision slips, the work after it still starts — on the recommendation — and the estimate already covers changing course.

**My assumptions, labelled as mine:** a week is five working days, you and me together. The twelve weeks are ordered by dependency, not by calendar. Slipping one pushes the rest; it does not break them.

**Where the weeks are tight, said plainly.** Every week fits inside five days at the low end of its estimates. Two do not fit at the high end: **Week 4** (up to 5.5 days) and **Week 10** (up to 6). Those are the weeks where a decision going the long way costs you a day, and they are the two most likely to spill into the next week. Nothing is padded to hide it.

---

---

## Related

- [Roadmap to Launch](../roadmap-to-launch.md) — the counted picture of how far the game is from shippable.
- [Gameplay System V4](../../2-systems/gameplay-system-v4/README.md) — forging, folding, quenching, recycling and mastery; built.
- [Ring System — Current Facts (Trio-Ring)](../../2-systems/ring-system-trio-ring.md) — the progression engine; the ring is part of the character, not an item.
- [Weapon Roster — 14 Identities](../../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — ten families with no skill at all, and the staff's single unpublished one.
- [Networking — Implementation Notes](../../2-systems/networking/implementation-notes.md) — the deliberate limits that are still true, and what they sit on top of.
- [Starting the Network](../../2-systems/networking/starting-the-network.md) — how to stand the game up to test any of this.
