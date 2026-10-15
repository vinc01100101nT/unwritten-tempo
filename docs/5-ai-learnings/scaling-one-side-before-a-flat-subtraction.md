---
status:
owner: vince
last-updated: 2026-10-16
tags:
  - ai-learnings
---

# Scaling One Side of a Formula That Ends in a Flat Subtraction

**Reach for this when:** you are about to halve, double or rescale one constant to hit a target number (a hit count,
a time to kill, a price), and somewhere downstream the result has something SUBTRACTED from it rather than multiplied.

## The rule

A flat subtraction does not scale with you. Cut one side of `attack − defence` in half and the defence is now twice
as large compared with each blow — and it hits small, frequent blows hardest. Before proposing the cut, list every
consumer of the constant and replay one fast case and one slow case with the real numbers. Prefer a lever on the
OTHER side of the subtraction (the pool it drains), which scales every case the same.

## Why

On 2026-10-16 a level 1 player had to survive five blows from the weakest creatures. I proposed halving every
weapon's base attack and said "everything scales together". A red team then showed a katar's two blows landing 15
and 1 against 17 defence while a greatsword landed 42: every weapon is built to deal equal damage per second, defence
comes off every blow, so the cut wrecked the fast and two-handed weapons for players and creatures alike. It cost a
red-team round and the user's trust in the recommendation. Raising starting health — the pool, not the blow — hit the
same five-blow target with no side effect on weapon balance.

## How to apply

1. Write the whole chain from the constant to the number you are targeting, and mark every `+`/`−` of a value that
   does not come from the constant.
2. Replay the cut for the extreme cases (fastest and slowest weapon, lowest and highest level) with the game's own
   functions — in this project, a one-line `unity cmd eval` over the real formulas.
3. If any case moves by a different share than the target, pick a lever after the subtraction instead.
