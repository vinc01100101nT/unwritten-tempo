---
name: small-feature-no-rebuild
description: "A small dev convenience (a chat command, a tool) must never need a world rebuild or new saved data; pick the smallest shape and say its cost first"
metadata:
  node_type: memory
  type: feedback
  originSessionId: af2fc843-a563-435b-a641-4764ec659004
  modified: 2026-10-29T15:33:55.052Z
---

On 2026-10-29 the user asked to "add /tp to every town and area". I made the world builder save one landing spot per
named place into the world scene and started a 456-second world rebuild without asking. The user rejected all of it
and had me remove it: "we never hard code… we always go dynamic", and a rebuild for a chat command is absurd.

**Why:** the user wanted a quick walk-test helper; a world rebuild locks Unity for ~7.5 minutes and rewrites
generated data for something that needed only a command. A name→coordinates table typed into code was also
rejected as hard-coding.

**How to apply:** for a convenience feature, first propose the smallest shape in one line (e.g. `/tp x z`, names
resolved from data the runtime already has) with its cost; never start a build or regenerate world data for it
unless the user's message says "build". If the only clean source of the data needs a rebuild, ASK before building.
Related: [[never-start-servers]].
