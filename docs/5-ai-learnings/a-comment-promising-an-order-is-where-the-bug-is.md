---
status:
owner: vince
last-updated: 2026-09-03
tags:
  - ai-learnings
---

# 🔀 A Comment Says "We Do X First" — Check That We Do

**Reach for this when:** you are reading code and a comment promises an ORDER, a guarantee or a safety property —
"the write is asked for first", "cleaned up before", "always released after", "this cannot run twice".

> A comment that states an ordering guarantee is the single best place to look for a bug, because it is the only
> place in the file where somebody has written down what the code is SUPPOSED to do. **Read the next few lines and
> confirm they do it.** When they do not, the comment is not the mistake — it is the marker on top of one.

## The rule

Treat an ordering comment as an unverified claim, and spend the ten seconds to check it against the lines directly
below. Three things make this worth doing every time:

- **The comment is the author's intent, written when they still remembered it.** If the code disagrees, the code
  drifted — and the drift is a real behavioural bug, not a documentation problem.
- **Nobody re-reads it.** The comment was true when written and has never been checked since, so a later edit that
  reversed the order left the promise standing above it, reassuring everyone who passed.
- **The failure is usually silent.** Ordering bugs do not throw. Something gets skipped, and the code reports the
  skip as a normal outcome.

Fix the CODE to match the comment, not the comment to match the code — unless you can show the current order was
deliberate.

## Why — what it cost

A disconnect path carried this comment: *"the write is asked for first, and the character is dismissed only once it
has landed."*

The two lines under it did the opposite. It dismissed first — which unbinds the character from its shell and
destroys it — and then asked for the write. The write looked for a character, found nothing, gave up **without
sending a request at all**, and reported failure.

So **every single player who left lost everything that had changed since their last periodic write** — a level, an
item, gold. The server said so in its log every single time, in plain words, and had been saying it since the
previous day's commit.

It was found only because a play-tester happened to sign out and quoted the log line back. Nobody had read the two
lines under a comment that told them what those lines were meant to do.

Cost: a day of every departure losing progress, plus the round spent proving whose fault it was — the shared
secret, the payload, and the current session's own edits all had to be cleared before the real cause surfaced.

## How to apply it

- **Grep for the promise words** when auditing a risky path: `first`, `before`, `after`, `only once`, `never
  twice`, `must be`, `is always`. Each hit is a check worth ten seconds.
- **Check the two or three lines under the comment, not the whole function.** Ordering bugs are local.
- **When a step tears something down, ask what reads it afterwards.** Release, dismiss, destroy, close, dispose,
  unbind, clear — anything after one of those that touches the same thing is suspect.
- **A "could not do X" message with no error detail beside it is a tell.** It usually means the work was abandoned
  before it started, not that it was attempted and failed. Look for the early return, not the failed call.
- **Before blaming your own change for it, diff against a copy from before your edits.** In this incident that
  took one command and ruled out the current session immediately, which is what made the real cause findable.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [A Doc Cannot Vouch for Its Own Freshness](a-doc-cannot-vouch-for-itself.md) — the same rot in prose: a heading
  written on the day it was true, never re-checked.
- [Make the Code Narrate Its Own Decision](make-the-code-narrate-its-decision.md) — for the ordering bug that
  leaves no message at all.
