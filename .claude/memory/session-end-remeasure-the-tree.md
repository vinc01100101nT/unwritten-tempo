---
name: session-end-remeasure-the-tree
description: Never report working-tree state from an earlier snapshot — re-run git status at the moment of reporting; the user commits between turns.
metadata:
  type: feedback
---

At session end, RE-RUN `git status` in the same turn you report it. Never restate the tree from the context's opening `gitStatus` block or from an earlier turn's reading.

**Why:** this user works in Unity between turns and commits on their own. On 2026-08-16 I reported a list of "still unstaged" files (sword skill assets, docs, a handoff deletion) that they had already committed at 21:35 in commit `51cefcea` — my snapshot predated it. The report read as authoritative and was simply stale. Same failure shape as restating any fact without re-reading its source: nothing was lost, but a confident wrong status is indistinguishable from a real one.

Second instance the next morning: six minutes after I pushed, five files (audio/settings work) were already modified again — none of them mine. The tree moves constantly.

**How to apply:** the session-end pass is (1) `git status --short` FRESH, (2) `git status -sb` for ahead/behind, (3) scratch dir + `handoffs/` listing, (4) grep for leftover probe/debug code. Report only what those four commands just printed. When files in the tree are NOT ones you edited, say so explicitly and do not offer to commit them as if they were yours — the user is working in parallel.

Related: [[changes-must-persist-in-repo]], [[assume-compiled-never-blame-recompile]].
