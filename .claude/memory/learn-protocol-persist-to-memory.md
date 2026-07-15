---
name: learn-protocol-persist-to-memory
description: "Protocol: when the user asks 'what did you learn', PERSIST the learnings to memory (topic files + MEMORY.md index), don't just print them in chat"
metadata: 
  node_type: memory
  type: protocol
  originSessionId: f3e5f22b-f7a2-42c5-b701-e170d3811ed5
---

**Protocol (user-confirmed 2026-07-01):** whenever the user asks *"what did you learn (this session)?"* — that is an instruction to **persist** the learnings into this memory store: write focused topic file(s) here and add short index line(s) to `MEMORY.md`. It is NOT satisfied by just reciting them in chat.

Reciting in chat is fine as a bonus, but the durable write is the actual deliverable. Keep MEMORY.md index lines short (one line, ~<200 chars) — MEMORY.md is already at/over its size cap, so put detail in the topic file, not the index.
