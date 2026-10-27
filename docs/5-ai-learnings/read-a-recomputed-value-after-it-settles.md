---
status:
owner: vince
last-updated: 2026-10-28
tags:
  - ai-learnings
---

# A Value Read Right After Asking for a Recompute Is Not the Settled Value

## Reach for this when

You asked the engine to recompute something (`DynamicGI.UpdateEnvironment()`, a bake, a reimport) and read the result
in the same call — then the thing you saved from that reading looks different from the picture you approved.

## The rule

A recompute request often completes over the next frames. Read the value only after it has settled: request, wait a
few frames (or request repeatedly), read twice, and use it only when two reads agree.

## Why

On 2026-10-28 the sky's light was read right after one `UpdateEnvironment()`: (0.27, 0.34, 0.56). The approved test
picture was taken a moment later. Saving colours from a second, settled reading — (1.03, 1.28, 2.02), far bluer — made
every grey rock bright blue, and a round went into toning it back down.

## How to apply

1. Request the recompute, let several frames pass, read; read again; keep the value only when both agree.
2. Save the value that was on screen when the user approved the picture — read it from the same frame as the picture.
