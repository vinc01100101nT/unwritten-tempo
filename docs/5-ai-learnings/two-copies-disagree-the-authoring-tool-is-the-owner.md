---
status: current
owner: vince
last-updated: 2026-10-11
tags:
  - ai-learnings
---

# Two copies of a value disagree and you are about to pick which one is "the owner"

## Reach for this when

Two places hold the same fact, they disagree, and you are about to recommend which one everything else should
read from — or the user answers your recommendation with "that will destroy the tool we use for that".

## The rule

Before naming an owner, find the tool the user AUTHORS that value in. That tool's saved data is the owner, and
whatever is built from it is output, however real the output looks. A copy that disagrees with the authoring tool
is a place the tool's output failed to reach, so the fix is to make the tool reach it, never to demote the tool.

## Why

On 2026-10-11 the doors of one town sat in different places in the town's map and on the world map. The doors
were planted in the authoring tool of the time, the world creator. I measured both copies correctly, then recommended
the map as built as the owner because it is "what the player walks through". That would have made the tool's door planting
decorative. The user had to correct it, and it cost a full round on a bug whose cause was already measured: the
build skips hand-made maps, so doors planted in the tool never reached that one map.

## How to apply

1. Ask "where does a person type, drag or plant this value?" and search for that tool before comparing copies.
2. Treat every other copy as output and ask which step was supposed to write it, and why that step skipped it.
3. Recommend closing that gap. Offer "make the output the owner" only when no authoring tool exists.
