---
status:
owner: vince
last-updated: 2026-10-07
tags:
  - ai-learnings
---

# 🔎 Check a Helper's Claim Before You Repeat It

**Reach for this when:** a helper's report says something is missing, never happens, or nobody sees it, and you are about to pass that on to the user or build on it.

> A helper's report reads as finished work, so its claims arrive looking checked. A claim that something is ABSENT
> is the easiest one for it to get wrong, because it only had to look in one place. Open the code that would show
> the thing before you repeat the claim.

## The rule

Before relaying any claim from a helper that the user will decide on, find the one place that would prove it wrong
and read it. For "X never reaches the screen", read the code that receives X on the screen side. For "nothing calls
Y", search for every caller. Say which claims you checked, and label the rest as the helper's.

## Why — what it cost

A planning helper mapping the server's drawing code reported five things that no player ever saw. One of them, the
yellow multi-hit totals, was wrong: the screens already drew them, from a flag in each hit message, with the
screen's own sum. That claim went to the user word for word, as one of five things to restore, and the user said
yes to restoring all five. The mistake only came out mid-build, when the restore went looking for the missing code
and found it working. That meant a wrong report, a correction owed to the user, and a planned task that was never
needed. The real problem was a different one: the screen was adding up a game number itself.

## How to apply

- When a helper's report comes back, list every "missing / never / nobody" claim in it.
- For each one, open the receiving side (the message handler, the screen code) and confirm it.
- In the reply, mark which claims you checked and which are still only the helper's.
