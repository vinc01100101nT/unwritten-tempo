# Standing habit: log notable bug fixes to memory

User directive (2026-06-27): **whenever we fix a notable/hard bug, save it to memory** as a future
reference point, so related problems are faster to debug. Two-way habit:

1. **After fixing a notable bug** — write a memory file capturing:
   - Symptom (what the user saw), and any mis-diagnosis that wasted time.
   - **Root cause** (the actual mechanism, not just the patch).
   - The fix (file + function + the key line/value changed).
   - Key live values / measurements learned (e.g. via Unity MCP `execute_code`).
   - Gotchas / workflow notes that bit us.
   Then add a one-line index entry to `MEMORY.md`.

2. **When a new bug looks related** — grep/scan memory FIRST for a prior fix and use it as a reference
   before re-deriving from scratch.

## What counts as "notable"
- Non-obvious root cause (sign error, wrong coupling, off-by-one in math, lifecycle/order bug).
- Cost real debugging time, or required live inspection to pin down.
- Likely to recur or have siblings elsewhere in the codebase.

## What to SKIP (don't clutter memory)
- Trivial/obvious fixes clear from the code, typos, one-off content tweaks.
- Things git history or docs already capture well.

## Reliability caveat
This lives in memory, so I follow it by reading it each session — it is NOT harness-enforced, so it can
slip if a session is busy. If it ever gets missed, a settings.json Stop hook could nudge me; offered but
not yet set up.
