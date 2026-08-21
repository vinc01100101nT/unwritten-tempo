---
name: grep-the-bypass-not-the-call
description: "To prove a shared path is really shared, search for what BYPASSES it — grepping the call sites only ever finds the compliant ones."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 561dec3d-ed0c-4d5c-9d36-f2915b18ccea
---

Centralising a rule into one function proves nothing about coverage. Grepping for **calls** to that function
returns only the places that already obey it — the violation is, by definition, the place that never calls it,
so it is invisible to that search.

**Search for the bypass instead:** the raw field read, the direct primitive, the hand-rolled copy. On
2026-08-21 the stack formula lived in exactly one function with 22 call sites, all correct — and one further
site read a number's raw typed value without ever resolving it, so a duration paid for entirely by stacks
applied for real while the buff tray decided the effect had no duration and drew no icon. A grep for the
resolve call could never have found it; a grep for `.value` outside the editor found it immediately.

**How to apply:** after centralising anything, run the *inverse* grep — the old pattern the central function
replaced — and expect zero hits outside author-time/display code. Do this before claiming a concern is
centralised. Same shape as [[find-or-create-hides-duplicates]]: the dangerous case is never the one that looks
like the rule, it is the one that skipped the rule entirely.

Corollary, from the same session: when a value is read in two places for two purposes (once to decide whether
to show it, once to use it), resolve it ONCE and hand both the same number — otherwise they drift and the UI
lies about what gameplay did.
