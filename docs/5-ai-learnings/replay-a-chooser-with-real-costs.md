---
status:
owner: vince
last-updated: 2026-09-16
tags:
  - ai-learnings
---

# Replay a Chooser With the Real Costs Before Shipping It

## Reach for this when

You are building something that picks between options at random, or by "whichever is ready", and the options have
different costs, cooldowns or requirements — or a play-test says only a couple of the options ever get picked.

## The rule

"Pick at random among what is ready" is not an even spread when the options cost different amounts from one shared
pool. The cheap ones become ready first, get picked, drain the pool, and the expensive ones almost never become
ready at all. Before shipping a chooser like this, replay it offline in a throwaway script, with the real costs,
the real pool size and the real refill rate, and count how often each option actually gets picked.

## Why

Creatures were given their weapon's skills with "the ultimate first, otherwise a random ready skill". It compiled,
read correctly and matched the agreed rule. The user then play-tested for about an hour and saw only two of the ten
skills. A 30-line replay afterwards, fed the real numbers from the skill and creature data, explained it at once.
Field creatures hold 17–23 MP and refill 1.5 a second. The two 8-MP skills took about 5 casts per fight, each 12-MP
skill about 1, and no 40-MP ultimate could ever be afforded. The replay would have taken five minutes before the
build; not running it cost an hour of play-testing and a round.

## How to apply

1. Read each option's real cost, cooldown and gate out of the data, not out of memory.
2. Read the real pool: its maximum, its refill rate, and how often the chooser is asked (here, once per swing).
3. Write the chooser's loop in a throwaway script: each tick, refill, list what is ready, pick by the real rule,
   pay the cost, start the cooldown.
4. Run a few thousand fights and print how often each option is picked, per fight it was available.
5. If the counts are lopsided, or an option shows 0, take that to whoever owns the design **before** building. The
   fix is usually a rule change (save up for the picked option, or weight by cost), not a number change.
