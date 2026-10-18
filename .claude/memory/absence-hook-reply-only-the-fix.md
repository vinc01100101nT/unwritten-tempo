---
name: absence-hook-reply-only-the-fix
description: "When the \"absence-claim check\" stop hook fires, send only the corrected sentence — never repeat the whole reply"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 12c40a91-cb06-4493-91b0-543f17fad2f5
  modified: 2026-10-18T00:59:58.134Z
---

When the stop hook "absence-claim check" blocks a reply (it fires on wording like "has no", "there is no"), send ONLY
the corrected sentence(s), not the whole reply again. Better: avoid absence wording in design statements
("left uncapped" instead of "has no cap"), and search first when the claim is about the code.

**Why:** 2026-10-18 the hook fired four times in one session and I re-sent the full reply each time; the user:
"why do you often repeat your response? it is wasting tokens".

**How to apply:** on that hook, one short line: "Correction: <sentence>" after the search. Related: [[the-five-anti-invention]].
