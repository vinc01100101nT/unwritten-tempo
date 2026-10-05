---
status:
owner: vince
last-updated: 2026-10-05
tags:
  - ai-learnings
---

# Prove Your Decoder on a Known-Good Sample

## Reach for this when

**You wrote a small parser or probe to inspect some data, and it is telling you the data is corrupt, empty or
wrong.** Or **you built a throwaway copy of something to test against, and the code you are testing appears to
do nothing to it.**

## The rule

An instrument you just wrote is a hypothesis, not a measurement. Before reporting anything it says, run it on a
sample you already know is GOOD. If the good sample comes back looking broken too, the broken thing is your
decoder — say nothing about the data until it can tell the two apart.

**A throwaway OBJECT is an instrument too, and it is a worse one, because it can be in a state the real thing
never is.** Something you assembled by hand skips whatever the real one goes through to become valid — it can be
dead, unregistered, unowned, uninitialised — and every one of those makes real, working code do nothing to it,
for reasons that have nothing to do with the bug. **So make it succeed at something FIRST**, and only then trust
it to show a failure.

## Why

2026-08-31. A game server crashed with "this file is corrupted". A throwaway script decoded that file's header
and returned obvious nonsense — a size field in the hundreds of trillions — and this was reported to the user as
"the file looks malformed", which sounded like hard evidence because it came with numbers.

It was wrong. The same script was later run on the equivalent file from a **client build that launches and runs
perfectly**, and produced the identical nonsense. The field offsets in the script were simply wrong for that
file format. The data had never been malformed; the ruler was.

Cost: one false statement to the user inside a report of otherwise measured facts — which is the expensive kind,
because it makes the real measurements around it untrustworthy. The control took one command and would have
caught it before a word was sent.

**2026-09-05 — the same failure, with an OBJECT instead of a parser, twice in ten minutes.** Two of a player's
skills did nothing. To test the machinery offline I built a bare character in a throwaway object, fed it the
real authored skill data, and read back: nothing applied. Every skill of that kind came back empty. It looked
like clear evidence the whole delivery layer was broken.

It was not. **The character had 0 health, so it counted as dead**, and the very first line of the code I was
testing turns dead casters away. Reviving it did nothing either — a fresh object has no health to restore.
Handing it health directly finally worked, and then **every one of those skills applied perfectly**.

Ten minutes later the same probe lied a second way: a full end-to-end cast returned success and still armed
nothing. The reason was that **a second character on a player's machine destroys itself** — the object I was
measuring was gone by the next frame, before the effect landed.

Cost: about half an hour, and I came close to reporting a healthy system as broken inside a report of otherwise
measured facts.

This is the sibling of the rule that an empty search result is not proof of absence. That one says *prove your
search can see a known-present case*. This one says *prove your decoder can read a known-good file*, and *prove
your test subject is alive enough to be acted on*. Same failure, three directions: the instrument blind, the
instrument lying, or the instrument in a state that guarantees the answer.

### The same failure, 2026-09-21: an enum read by counting, with a comment in the middle

To audit which sword skills a weapon bonus could lift, I turned each saved skill's effect NUMBERS into names by
counting the members of the enum in its source file. A comment block inside the enum, part-way down, was split
by my parser into two fake members — so **every number from 14 up came out one too high**. Parry (12) decoded
correctly, which made the whole table look trustworthy.

It cost two rounds of wrong design advice. I told the user their pick **Opening** could never be boosted (I read
it as "adds stacks"; it is a +200% damage boost) and replaced it; I included **Unwavering** (I read it as damage
reduction; it is only disable immunity); I excluded **Pressure** (I read it as disable immunity; it cuts HP
regen). The user caught none of those — they caught a *fourth* problem, and re-reading for that exposed the rest.

**The known-good sample was sitting in the same file.** Every skill has a written description. "A parry charges
your next attack with **+200% damage**" decoded as *AddStacks* is a contradiction you can see in one line. One
cross-check of a decoded value against the thing's own description, before building a table on it, would have
caught all three.

### The same failure, 2026-10-04: a census whose filter removed the very thing it was counting

The user's old character stayed standing after Select character. A census in the editor counted every character
body after five switches and found exactly one each time — reported as "no leftover body ever appeared". It
listed only objects whose scene was valid. The leftover was precisely an object in NO scene: Unity never
destroys an object carrying a do-not-save flag when its scene closes. The filter deleted the answer before the
count was taken.

It cost a full round and a request to the user to reproduce it on two machines. The next census dropped the
filter, PRINTED the scene of each object instead, and planted a known case: two plain objects made side by side,
one flagged and one not. After one switch the flagged one was still there with no scene, and the plain one was
gone. That one control settled the cause.

## How to apply

- **Never filter a census on the property the bug might be about — print it instead.** "Only objects in a valid
  scene", "only active ones", "only ones in the registry": each is a guess that the bug does not live there.
- **Plant a known case of the suspected state** (an object made exactly the suspect way) and check the census
  sees it before trusting a count of zero.
- **Find the known-good twin first.** The same file from a working build, a previous version, a second machine,
  a record you can verify another way. If no good sample exists, you cannot yet report what a bad one means.
- **Run the decoder on BOTH and print them side by side.** Do not eyeball one in isolation — the whole point is
  the difference.
- **Same shape from both ⇒ the decoder is wrong.** Fix it or abandon it, and go and get a different kind of
  evidence. Do not report the reading.
- **Different ⇒ you have a real finding**, and now you can say so with the comparison attached.
- **When you cannot build a control, say the reading is unverified** in the same sentence as the reading. A
  number with no control is a guess wearing a lab coat.

**When the instrument is a hand-built OBJECT rather than a parser, add these:**

- **Make it succeed at something first.** Run the code on a case you are certain works, or on the real live
  object, before you trust it to report a failure. A subject that produces nothing for every input is not
  evidence about any of them.
- **Read the very first lines of the code you are testing and check your subject passes them.** Alive, owned,
  registered, enabled, in the right list. Those gates exist for the real thing and they will turn your stand-in
  away without a word.
- **Check your subject is still THERE when you read the answer.** Anything that spans more than one frame gives
  the system a chance to tear down something it did not expect to exist. Read the object back by name before
  believing what it says.

