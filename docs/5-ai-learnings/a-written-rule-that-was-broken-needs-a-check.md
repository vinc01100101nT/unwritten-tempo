---
status: done
owner: vince
last-updated: 2026-09-20
tags:
  - ai-learnings
---

# A Written Rule That Was Broken Anyway Needs a Check, Not a Second Document

**Reach for this when:** an audit or a bug turns up code that breaks a rule which is already written down — in
the always-loaded instructions, a rulebook, a README — and the first instinct is to write the rule again, bigger,
or in a new file.

## The rule

Do not write it again. Turn it into three mechanical things: a **checker** that reads the code or the saved files
and names the offending file and line; a **prover** that plants each fault the checker exists for and demands it
be named (a blind checker prints exactly what a clean project prints); and, if a document must be read before
the edit, a **gate** that refuses the edit until that document is in the session's transcript. Prose is a wish;
a check that runs at the end of every turn is a rule.

## Why

On 2026-09-20 a repository-wide audit of this project found that **half its findings broke a rule that was
already written down** — and had been, for a week or more, in the instructions every session loads:

- "the player's machine never works out a game number" — the whole level-up ran on the player's machine
- "nothing changes a scene, prefab or asset unless the user clicked" — three editor scripts saved files on
  editor load, on scene save, and on any reimport

Every one of those was written in good faith by somebody who had read the rule. The other half of the findings
had no rule at all (import settings, packages, baked data, server secrets), and the two halves cost the same to
find and the same to fix. The written rule had bought nothing measurable.

What HAD held, on the same day, were the things that run: the compile check, the asset-wiring sweep, the
skill-data check, the game-math pins, and the gate that refuses a runtime-C# edit until the performance rulebook
is read — the audit found nothing in the classes those cover. The difference was not how well the rules were
written. It was whether anything read the code.

Cost: a full audit day, then a second day turning the findings into four checkers, four provers, two rulebooks
and a wider gate. The second day would have been unnecessary had the first rules been built this way.

## How to apply

1. **Name the fault in a form a machine can find** — a setting value, a method reachable from a trigger, an
   operator on a named field, a route on a service. If it cannot be stated that way, it cannot be checked, and
   that is worth knowing before writing prose about it.
2. **Write the checker to work on any root** (`--root DIR`), so the prover can copy a few real files into a
   scratch folder, plant one fault at a time, and never touch the repository.
3. **Write the prover first if you can.** Its "sound copy is quiet" case catches a checker that cries wolf, and
   its "planted fault is named" cases catch one that is blind. On 2026-09-20 a prover caught its checker being
   fooled by a commented-out line on the first run.
4. **Wire the checker to the end of the turn**, filtered to files the turn touched, so old debt never blocks and
   new debt never lands. Where old debt is large, record it in a baseline file that can only shrink.
5. **Gate the document read** only for a real rulebook — the kind with reasons and patterns, not a list of
   checks. The checker is what enforces; the gate is what makes the reasons get read.
6. **Put the marker beside the code** (`// perf-ok:`, `// editor-write-ok:`, `// server-math:`) for the cases
   the rule genuinely allows, so the reason lives where the next reader will argue with it.
