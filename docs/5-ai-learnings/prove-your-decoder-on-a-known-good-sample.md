---
status:
owner: vince
last-updated: 2026-09-01
tags:
  - ai-learnings
---

# Prove Your Decoder on a Known-Good Sample

## Reach for this when

**You wrote a small parser or probe to inspect some data, and it is telling you the data is corrupt, empty or
wrong.**

## The rule

An instrument you just wrote is a hypothesis, not a measurement. Before reporting anything it says, run it on a
sample you already know is GOOD. If the good sample comes back looking broken too, the broken thing is your
decoder — say nothing about the data until it can tell the two apart.

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

This is the sibling of the rule that an empty search result is not proof of absence. That one says *prove your
search can see a known-present case*. This one says *prove your decoder can read a known-good file*. Same
failure, opposite direction: there, the instrument was blind; here, the instrument was lying.

## How to apply

- **Find the known-good twin first.** The same file from a working build, a previous version, a second machine,
  a record you can verify another way. If no good sample exists, you cannot yet report what a bad one means.
- **Run the decoder on BOTH and print them side by side.** Do not eyeball one in isolation — the whole point is
  the difference.
- **Same shape from both ⇒ the decoder is wrong.** Fix it or abandon it, and go and get a different kind of
  evidence. Do not report the reading.
- **Different ⇒ you have a real finding**, and now you can say so with the comparison attached.
- **When you cannot build a control, say the reading is unverified** in the same sentence as the reading. A
  number with no control is a guess wearing a lab coat.
