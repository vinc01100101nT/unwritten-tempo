---
status: current
owner: vince
last-updated: 2026-09-21
tags:
  - ai-learnings
---

# You are about to plug a hole that only exists because of a hole upstream

## Reach for this when

You have found a real problem and a real fix for it, and the fix goes into the place where the damage SHOWS —
not the place where the bad thing was let in.

## The rule

**Before writing a guard, ask which layer let this reach here at all.** Trace the thing backwards, one hop at a
time, until you reach the first place that could have said no. Fix it there.

A guard at the point of damage is nearly always cheaper to write, which is exactly why it is the one you reach
for. It also leaves the real hole open for the next thing to come through, and adds a check that has to be
maintained forever.

## Why

2026-09-05. Two live copies of one character could write over each other. I traced it to the moment a world
server is told somebody is arriving, and proposed reserving the character's name a few milliseconds earlier —
a narrow, correct, cheap fix at the place the damage appeared.

The user's reply: **"nobody should be able to do that in the first place. the account server should already
block it. you are proposing a fix to a hole that is caused by another hole."**

They were right. The sign-in service allowed one account to be signed in twice and recorded nothing about it.
Everything downstream — two live characters, the write race, the narrow window — was one symptom of that. I had
walked past the cause to patch the symptom, and I had done it **twice in the same conversation**, because the
first patch felt like a diagnosis.

**Then it happened once more, in the other direction.** I described a failure mode — "two sign-ins land on
different world servers" — without tracing the routing. The user pointed out that routing is by MAP, one map is
held by one server, so both land on the same one, which already refuses a duplicate. **The scenario I was fixing
did not exist.** Reasoning about a failure without following the actual path invents the failure.

**And a third time, 2026-09-21, from a code comment.** Hunting a debuff that kept landing with no enemy near, I
read a comment saying the server "holds every map at once in one set of coordinates", saw that a creature
already chasing someone never re-checks their map, and told the user a creature in another map could reach them.
The user told me to fix it. Only then did I open the function that places the maps: each map is set down 2,000
units from the next, and a creature's reach is 11. The failure could not happen. One file, read before I spoke,
would have kept an invented cause out of a reply the user acted on.

## How to apply

1. **Say the sentence out loud: "how did this get here?"** Then answer it with a file you have opened, not with
   reasoning. Repeat until the answer is "a person typed it" or "another service allowed it".
2. **Trace the real path before describing a failure.** If you cannot name the actual route the bad thing takes,
   you are inventing the failure, and a fix for an invented failure is pure cost.
3. **When the cause is in another service or another layer, say so plainly and let the owner decide** — the fix
   may be theirs, may be bigger, and may make yours unnecessary. Do not build the local patch first "so there is
   something".
4. **Ask whether the cheap guard still earns its place after the real fix.** Sometimes yes, as a backstop; say
   which it is. Often it can simply be dropped.
