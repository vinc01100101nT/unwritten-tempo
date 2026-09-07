---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# 🔢 Your Number Is Real, and It Counts the Wrong Thing

**Reach for this when:** you are about to put a count or a size in front of the user — how many files, how much
memory, how big the download — and you got it from a search, a folder listing or a `du`.

> A number you measured only answers the question you actually asked the tool. Search HITS are not FILES. Bytes
> in a FOLDER are not bytes in MEMORY, and not bytes that SHIP. The number is real; the sentence around it is
> the invention, and it reads exactly as authoritative as a true one.

## The rule

**Name the unit out loud before the number goes in a sentence, then check the sentence claims that same unit.**
Every one of these is a different quantity and they differ by multiples, not percentages:

- lines a search matched · files it matched in · items those files describe
- bytes on disk · bytes in the shipped build · bytes resident in memory while running
- assets in the project · assets the game actually references

RULE ZERO already bans inventing a value. This is the step after: the value was read from a real source, and
the WRONG QUESTION was asked of it. No amount of re-reading the number catches that — only naming its unit does.

## Why — what it cost

2026-09-07, twice in one session, on the same task.

**"About 9,000 images."** Counting a per-image setting across the project's import files, I reported ~9,000
image entries. Each import file carries that setting several times, once per platform, so I had counted lines,
not images. The real figure was **3,203** — wrong by nearly three times, and it went into a table of
recommendations. The user's next message was *"what are those 9000 image entries, where can i find them"*, which
is the cheapest possible way to be caught and still cost a round.

**"276 MB of music unpacks into memory."** I read the size of the music folder on disk and described it as what
every client holds in RAM. Only **three** of those clips are ever loaded by the game; measured properly, they
cost **66.6 MB** per client. Same session, same shape: a real number, a sentence about something it never
measured. I had already recommended action on it before catching it.

Both figures came from commands that ran correctly and printed the truth about what they were asked.

## How to apply

- **Count files, not hits.** `grep -l … | wc -l` counts files; `grep -c` and a bare `grep … | wc -l` count lines.
  If you mean "how many things", the per-thing number must be exactly 1 — prove it on ONE item before trusting
  the total.
- **A folder size answers "what is on disk" and nothing else.** For what actually ships, read the build's own
  size report rather than adding up source files; Unity writes one into its editor log after every build, broken
  down by textures / meshes / audio, with the biggest assets listed. It settled in seconds that this project's
  build is three-quarters textures and only 5% audio — the opposite of what the folder sizes suggested.
- **For memory, compute it from what is loaded**, not from what exists. Duration × sample rate × channels ×
  bytes-per-sample is the real answer for an audio clip; the file on disk is compressed and most of the library
  is never referenced.
- **Say where the number came from, in the same sentence, whenever it changes a decision.** "227 MB of textures,
  from the build's own report" survives scrutiny; "227 MB of textures" does not.
- **When you correct one of these, give the corrected number and the reason in one sentence** and move on — the
  user needs the right figure, not an account of how the wrong one happened.

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [Cheap for Whom, and How Many Times](cheap-for-whom-and-how-many-times.md) — the same trap on a timing: a
  measured number is not an answer until you know who pays it and how often.
- [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md) — when the
  instrument itself is what is wrong.
