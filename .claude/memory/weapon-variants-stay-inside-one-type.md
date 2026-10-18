---
name: weapon-variants-stay-inside-one-type
description: "A weapon \"variant\" is another item of the SAME weapon type (another Sword), never a different weapon kind — Sword and TwoHandSword are separate types with separate skills."
metadata:
  node_type: memory
  type: feedback
  originSessionId: e3c90374-95a0-46e1-8be9-b08aacf33c59
  modified: 2026-10-17T13:51:47.712Z
---

When proposing weapon variants, every example must be the same `WeaponType` (three different Swords), differing
only by per-item properties (swing time, element, a fixed trait). Never name another kind (rapier/greatsword) — those
read as other weapon types, and Sword vs TwoHandSword already have separate skills. Never invent new weapon kinds.

**Why:** 2026-10-17 I illustrated "sword variants" as Rapier / Longsword / Greatsword; the user: "you are naming 3
different kinds of weapon … dont create new kinds."

**How to apply:** before giving a variant example, check the `WeaponType` enum and keep all examples in one member.
Related: [[mobs-get-everything-players-get]]
