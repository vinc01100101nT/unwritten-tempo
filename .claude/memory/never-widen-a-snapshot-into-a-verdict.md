---
name: never-widen-a-snapshot-into-a-verdict
description: "Reading today's state only proves today; never turn it into \"it never worked / it cannot work\" — check history and believe the user's live test."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: fdb3c701-3397-4d31-a247-7eec9b13454a
---

Reading the CURRENT state of code/data proves exactly one thing: what is true right now. It NEVER licenses a verdict about the past ("it never worked") or about capability ("it cannot be authored"). Those are different claims needing different evidence — the change history, and the user's own live test.

**Why:** the user tested Retribution working. I read the files as they stand today, found the pile link pointing at the wrong place, and reported "there is no way to author this" — a verdict my snapshot could not support. The truth was a REGRESSION: the gate used to match by skill alone (so it worked when tested), and a later commit split it into skill + pile without re-pointing the older skill. Same underlying finding, opposite meaning: "broken by a recent change" is actionable, "never possible" sent the user hunting for a design flaw that didn't exist. The user: *"that is deadly in the progress."*

**How to apply:** when a static read contradicts something the user says they tested, the user's live test WINS — treat my read as incomplete and go find what changed (`git log -S<the field>`) BEFORE writing a word. And keep the claim the size of the evidence: say "as saved today this points at the wrong pile", never "this never worked" or "this cannot be done". Related: [[assume-compiled-never-blame-recompile]].
