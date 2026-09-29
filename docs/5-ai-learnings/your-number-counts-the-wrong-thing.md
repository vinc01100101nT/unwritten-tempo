---
status:
owner: vince
last-updated: 2026-09-30
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
- entries in a folder listing · the sub-folders among them · the things those sub-folders hold
- bytes on disk · bytes in the shipped build · bytes resident in memory while running
- assets in the project · assets the game actually references
- **POINTS added to a multiplier · a PERCENTAGE of the finished number** — an additive bonus is only "+X%" at
  one specific baseline, and is a smaller share of every larger one
- **one sample's frequency · the actual probability** — a measured run answers "does the code do what I think",
  never "what are the odds"

## Two shapes that look like measurement and are not

**An ADDITIVE bonus stated as a percentage.** If a value is added to a multiplier rather than to the result,
"+30%" is true only where that multiplier is 1. Everywhere else the same addition is a smaller slice — at
double the multiplier it is +15%. Say **"+30 points"**, then say what that is worth at the baseline the reader
is standing on. The tell: the word "added" appears anywhere in the mechanism, and a "%" appears in the sentence.

**A sampled frequency published as the answer.** When a distribution is COMPUTABLE — a known roll over known
ranges — compute it and publish the exact figures. The sample is still worth running, but its job is to prove
the code matches the maths, not to be the answer. The tell: a printed figure carries more precision than the
run can support, or the four figures do not sum cleanly.

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

2026-09-10, both shapes in one afternoon, on the same feature. A weapon's rarity adds a fixed amount to the
multiplier a player's ring already gives a skill. I wrote "a Legendary's skills land 30% bigger" into the design
page, the player wiki AND the code comment — true only for a character whose ring contributes nothing, and worth
+15% at double the ring multiplier. The channel doc had it right, so the same fact was correct on one page and
wrong on three.

In the same pass I rolled 20,000 kits, measured 18.8 / 43.6 / 31.4 / 6.1, and published those as the drop odds.
The roll is two coin-flips and a quarter-chance, so the exact answers are 18.75 / 43.75 / 31.25 / 6.25 — two of
my four printed figures were wrong, and the measurement I was so pleased with was the thing that made them look
verified. Both were caught by another session reading the docs, not by me.

2026-09-29, a folder listing. Asked which character sprites could become mobs, I counted 95 entries in the folder,
found 89 standard sheets plus 3 odd ones, and reported "3 more, unusual size, not checked". The other 3 entries
were loose preview pictures sitting beside the character folders, not characters. The user then asked me to
assess and resize "the 3 unusual ones", and the round went on finding out there was nothing to resize.

## How to apply

- **Count files, not hits.** `grep -l … | wc -l` counts files; `grep -c` and a bare `grep … | wc -l` count lines.
  If you mean "how many things", the per-thing number must be exactly 1 — prove it on ONE item before trusting
  the total.
- **Count folders, not entries.** A folder listing mixes loose files with sub-folders; filter to directories
  (`[ -d ]`, `os.path.isdir`) before a count becomes "N characters", and make the parts add up to the whole
  before reporting a remainder as a finding.
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
- **Before publishing any distribution, ask whether it is computable.** If yes, compute it and use the sample
  as the CHECK. A sample and an exact answer that disagree in the second digit means the sample, not the maths.
- **Before writing "%" beside a bonus, find out whether it is added or multiplied.** Added into a multiplier is
  points; only a multiplication onto the result is a true percentage of what the reader sees.
