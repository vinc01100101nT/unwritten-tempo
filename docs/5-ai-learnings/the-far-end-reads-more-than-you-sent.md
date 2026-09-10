---
status: current
owner: vince
last-updated: 2026-09-08
tags:
  - ai-learnings
---

# The Far End Reads More Than You Sent

## Reach for this when

**You made something travel that never travelled before — and it arrives, but it looks wrong.** Not missing, not
late, not in the wrong place: *there*, and wrong. The thing you sent is exactly what you meant to send.

Sibling: [It Arrived in the Wrong Place](it-arrived-in-the-wrong-place.md) — reach for that one when you built a
SECOND way to do something and can compare against the first. Reach for THIS one when there was no first: nothing
to diff against, so the missing pieces have to be found by asking instead.

## The rule

Two questions, both asked before you write the channel, and neither answerable from the thing you are sending.

**1. What does the far end read LOCALLY to render this — and what else WRITES the same thing?** You sent *what*
it is doing. Whatever turns that into something on screen also reads a rate, a scale, an anchor, a stat, a
component — and every one of those it reads from itself. On the machine that owns the thing, those local reads are
correct. On a copy, they are whatever that copy happens to have, which is usually a default. So walk the render
path from your value to the pixel and write down every *other* input it touches. Each one either has to travel
too, or you have to prove the copy derives the same answer.

Then ask the same question the other way round, because it is the half that gets missed: **who else writes the
value you are now writing?** A reader that disagrees with you is a wrong picture; a *writer* that runs after you
is your value deleted, every frame, with nothing reporting anything. Search for assignments to the property
itself, not for the feature — and check the order they run in, because the last writer of a frame is the only one
that counts.

**2. Which existing rules were only ever right because your case did not exist?** Old code is full of assumptions
that were true of every case at the time — "this thing is always on the floor", "there is always a controller
here", "this only runs on one machine". They are not comments and not checks; they are shapes. Your change adds
the first case that breaks one. Find them by asking, of each thing your new case touches: *why is this allowed to
assume that, and am I still an example of it?*

## Why

2026-09-07. A change made every character pose travel to the other players, generally: one report from the single
place poses resolve, so any pose — present or future — crossed with no wiring. It worked, it was verified, and it
was reported as done. Then it was played, and a jump was wrong in two ways at once.

**The pose arrived; the speed it plays at did not.** A pose is half an animation — the rate is the other half, and
it was read locally from a component that only the owner has. A watched copy fell back to a bare default. Measured
against the authored values: the jumper played the jump at **1.5×** and every watcher at **3.0×**, exactly twice
as fast. It also threw the crouch, the airborne hold and the landing out of step, because the owner times those
three from that same figure. "The stance travels" was true, and the stance still looked wrong.

**And the body never left the ground at all.** The height genuinely crossed the wire; what met it was a rule that
seats a character by pinning its feet onto the floor every frame. That rule was correct for every body that
existed when it was written — a townsperson and a monster cannot leave the ground. My change introduced the first
body that can, and I never went back to ask what that broke. The copy rose, and had its visible model shoved back
down, every frame.

Cost: the work was reported complete, the user play-tested it, and both faults came back from them rather than
from me. Both were one question away from being caught before shipping.

2026-09-08, the same shape again, and this page already said how to catch it. Creatures with a live-3D body were
pointing the wrong way for everybody except the machine that owned them. The direction was made to travel — that
part was correct and measured — and it still looked wrong, so a whole round was spent on a fix that only changed
the symptom. Every creature in this game is ONE object, and the component that swings a flat sprite round to face
the camera was still sitting on it, writing that object's rotation every frame, later in the frame than anything
else. The copy was handed the camera's direction as "which way am I facing" for its whole life. The owner never
saw it, because a machine with no screen has no camera and that component does nothing without one — which is
exactly why reading the owner's code proved nothing was wrong.

The tell was the last question in **How to apply** below, and it went unasked: this was **the first creature whose body has a real
front**, and the camera swing was written when every creature was a flat quad.

## How to apply

1. **Follow your value to the screen and list every other input on that path.** Not what you sent — what the
   renderer touches. A rate, a multiplier, an anchor, a stat, a sibling component.
2. **For each one, ask: does the copy have this?** A component only the owner carries is the common answer, and it
   fails silently — it falls back to a default, which is a real, legal-looking value.
3. **Then list every OTHER writer of the property you are setting, and their order.** Grep for assignments to the
   property, not for the feature's name, and check the whole object — a thing built out of one object carries every
   component the old version needed, and the one that runs last wins with nothing logged. An owner-only symptom is
   the fingerprint: if the fault appears on copies and never on the owner, look for something the owner alone
   cannot run, such as anything that needs a camera or a screen.
4. **Send it, or prove the copy derives the identical answer.** Do not let the copy re-derive something the owner
   already resolved — that is two owners of one number, and they drift. Send the owner's answer and use it as-is.
5. **Name what your new case is the FIRST of**, then grep for rules that assumed otherwise. "The first body that
   can be off the ground." "The first caller that isn't on the server." That sentence is the search term.
6. **When you cannot test it yourself, say which half is unverified.** Both faults here sat behind "two people in
   a world", which no check in the repo can stand up. Reporting the work as done without naming that is what made
   the user find them.
