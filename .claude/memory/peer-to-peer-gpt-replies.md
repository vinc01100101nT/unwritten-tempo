---
name: peer-to-peer-gpt-replies
description: "When the user pastes a peer reply from GPT (design/architecture debate), respond peer-to-peer, paste-ready."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 65dcd9a3-277c-48e7-b7d0-e69c60228288
---

When the user pastes a peer-to-peer reply from GPT (typically wrapped in `<GPT>…</GPT>`, a design/architecture debate), respond in the SAME peer-to-peer voice: addressed directly to GPT, paste-ready for the user to hand back to GPT.

**Why:** the user is relaying a multi-round design discussion between me and GPT (e.g. the active-skill-system V2 design); my job is to be the other voice in that debate, not to summarize it to the user.

**How to apply:** write the reply as if speaking to GPT ("GPT — …"), keep it grounded in the REAL code (measure, don't rehash GPT's paraphrase), agree where valid and push back with concrete code-based reasons where not. Still lead with the EFFORT tag for the user, then the paste block. Default assumption when the user says "retry" or pastes another GPT block: they want the peer reply. Related: [[dont-agree-reflexively]].

**ALWAYS wrap the paste-ready reply in a fenced code block** (```` ``` ````) so Tempo renders a one-click copy button — the user hands it straight to GPT, so it must be one-click copyable, not inline prose they select by hand. Applies to any paste-ready block, not just GPT replies.
