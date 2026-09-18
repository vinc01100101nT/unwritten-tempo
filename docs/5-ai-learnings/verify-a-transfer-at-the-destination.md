# Verify a Transfer at the Destination

## Reach for this when

You are reporting whether a MOVE worked — a pickup, a file copy, a migration, a payment, a hand-off — and you are
about to judge it by looking at where the thing came FROM.

## The rule

**Ask the destination.** "It left the source" and "it arrived" are different facts, they happen at different
moments, and only the second one is what anybody asked about. The source emptying can mean the transfer worked,
or that the thing expired, was taken by someone else, or was discarded.

## Why

2026-09-18. A command picked an item off the ground. It reported success when the item vanished from the floor —
and one of those vanishings was the item expiring on its own a minute after it dropped, so the report said
"picked up Bow" for a bow nobody had. The fix looked obvious: check the bag too. But it was written to ask the
FLOOR first and the bag only once the floor was empty — and the bag fills a moment BEFORE the floor clears, so the
very next pickup reported "still on the floor" for an item already in the bag. Two wrong answers, opposite
directions, same root: the source was being asked a question only the destination could answer.

Cost: two rebuild-and-retest cycles, about twenty-five minutes, and a wrong line in a report.

## How to apply

1. **Write the check as the destination's fact**: "the bag holds one more than it did", "the row exists in the new
   table", "the balance rose by the amount".
2. **Check it FIRST**, before any source-side condition — the two sides do not change in the same instant, and the
   destination usually moves first.
3. **Keep the source check as a different answer, not the same one**: gone from the source with nothing gained at
   the destination is its own outcome, and saying so plainly ("gone from the floor, not in your bag") is worth more
   than either success or failure.
4. Take a count before, compare after. A boolean "is it there" cannot see a duplicate or a partial move.
