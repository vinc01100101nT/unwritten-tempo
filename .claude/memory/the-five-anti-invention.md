---
name: the-five-anti-invention
description: "An invention disguises itself as continuity — the four mechanical rules that stop it, agreed 2026-08-21."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 561dec3d-ed0c-4d5c-9d36-f2915b18ccea
---

Inventions do not feel like inventions. Coining a word feels like **labelling**; carrying an old code rule
through a redesign feels like **preserving**. So "don't invent" cannot be enforced by noticing — it needs
artifacts and mechanical tests. THE FOUR, in the project rules file (agreed 2026-08-21):

1. **The contract lists what STAYS, not just what changes.** Behaviour already in the code is NOT agreed — the
   user has never seen most of it. State each preserved rule in one plain sentence, before building.
2. **Any branch on what a number MEANS is a design decision.** Age is not consent; "it already did that" is not
   a defence.
3. **A question mark with no imperative ⇒ answer, touch nothing.**
4. **Ask at most once per task, then decide and label the decision as mine.**

**Why:** a full day was lost to exactly these four failures — a coined UI term ("banked ×"), two edits made in
answer to questions, an inherited "banked damage skips the attack roll" rule silently carried through three
redesigns, and eight rounds of questions while nothing shipped. The user's words: *"no hidden implementation
from you. honest to the contract."*

**How to apply:** read the four before starting any build in this repo; they sit above the standing rules in the
project rules file. **Do NOT add a "New words I introduced" section to reports** — the user removed it on
2026-10-04 as a waste of tokens. Coined words are still never shipped unasked (RULE CONTRACT); they are just not
listed in every report. Related:
[[dont-agree-reflexively]], [[apply-review-fixes-dont-gatekeep]].
