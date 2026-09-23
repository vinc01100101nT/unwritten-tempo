---
status:
owner: vince
last-updated: 2026-09-23
tags:
  - ai-learnings
---

# Check an Outside Fact Before It Becomes a Decision

**Reach for this when:** you are about to tell the user how some OTHER product, game or service works — from
memory — and they are in the middle of deciding how their own thing should work.

## The rule

A fact about an outside system that the user will copy into a design is load-bearing: look it up in a real
source before saying it, the same turn. Hedging it ("my knowledge, not verified") is not enough, because the user
decides on the content and skims past the hedge. If a web search or the project's own docs can answer it in one
call, make the call first; if nothing can, say "I don't know how X does it" instead of a confident description.

## Why

On 2026-09-23 the user asked whether an elemental monster in Ragnarok Online still attacks with no element. The
answer came from memory, hedged in one line, and was wrong: it said a monster's ordinary attacks carry its own
element. The user immediately decided "make our mobs attack and defend with their element — the same as
Ragnarok", and a plan was drawn up around it. The very next research call (the iRO wiki's Element page, and
rAthena's notes) said the opposite in plain words: monster melee attacks have no element at all, and a monster
uses its element only to defend and in its skills. One web fetch before the first answer would have cost
seconds; instead a design decision was taken on a false premise and had to be reopened.

## How to apply

1. Notice the trigger: the question is about an outside system, and the answer will shape a design choice.
2. Search or fetch a primary source first (the product's own wiki, docs or source code) and quote it.
3. If the sources disagree or say nothing, say so and show what each one says — never pick one silently.
4. If you already answered from memory and the source contradicts it, say "I was wrong" first, name the
   decision that was built on it, and hand that decision back to the user.
