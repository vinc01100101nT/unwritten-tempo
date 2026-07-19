---
name: debug-empty-result-verify-tool-coverage
description: "A search/query returning 0/empty/null is NOT proof of absence — first verify the tool can even SEE what you're hunting (its silent exclusions)."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 63cec94b-a131-4da7-b3d6-12bdfea65b00
---

When a lookup returns **nothing** (0 rows, empty list, null, "not found") for something you expect to exist, do NOT conclude "it's not there." An empty result is evidence of absence ONLY if the tool's coverage actually includes your target. Almost every lookup has **silent exclusions**: grep skips file types outside its glob, `find` skips hidden dirs, a `WHERE`/`JOIN` drops the very rows you're counting, an index hasn't ingested yet, an API filters/paginates, and reflection/engine calls skip whole categories.

**Why:** the scar that birthed this — `RuntimeLeftoverSweeper` searched for "don't-save" runtime objects using Unity's `FindObjectsByType`, the ONE call documented to **exclude** `HideFlags.DontSave` objects. It honestly reported `swept 0` forever while 252 real objects sat on screen. The `0` wasn't "clean" — it was the instrument being deaf to its own target. Swapping to `Resources.FindObjectsOfTypeAll` (which sees hidden/sceneless/DontSave objects) surfaced all 252. See [[runtime-leftover-sweeper-findobjectsbytype-blind]].

**How to apply:** before trusting a `0`/empty, either (1) prove the query catches a KNOWN-present item (add one, re-run), or (2) read the tool's documented exclusion rules. If the count contradicts what your eyes/other signals show, suspect the detector's coverage before concluding absence.

**Corollary — a failed fix is a measurement.** The WAY a fix fails narrows the cause: a repaint that did NOT clear the "ghosts" proved they were live objects, not stale pixels — which is what redirected the whole diagnosis. Don't discard a failed attempt as a miss; read what its failure rules out. Ties into [[debug-symptom-composition-not-interpretation]].
