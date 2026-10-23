---
status:
owner: vince
last-updated: 2026-10-22
tags:
  - ai-learnings
---

# 🎨 Rebuilding Onto an Existing Asset Keeps Its Old Values

## Reach for this when

A script authored many assets and one of them looks different from its siblings — a colour, a flag, a leftover
binding — or you are about to write a builder that loads an asset when it exists and creates it when it does not.

## The rule

**A builder that reuses an existing asset must set EVERY field it cares about, not only the ones it changed.**
Loading the old asset (to keep its ID, so references survive) also keeps every value nobody wrote this time. A fresh
asset starts from defaults; a reused one starts from whatever it said before — so the same builder code produces
two different results depending on whether the file was already there.

## Why

2026-10-22, the Staff's 20 combination skills. Nineteen were created new; Meteor reused the old unbuilt Meteor skill
so its ID would carry over. The builder set name, costs, effects and icon, but not the name colour — and the old
Meteor had an orange one. The user found it in the Skills window ("why is meteor orange?"), where every other
combination was white. It cost a question and a fix round.

## How to apply

1. In a load-or-create builder, write a reset of every field you rely on right after the load — the same values a
   new asset would get — then apply your changes.
2. After the build, diff the reused assets against a fresh one of the same kind, or list the fields that differ from
   their siblings: any value only the reused asset carries is a leftover.
