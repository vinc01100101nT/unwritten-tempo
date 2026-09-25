---
name: just-do-it-dont-instruct
description: "When the user asks \"where is X\" or needs something opened/run, DO it with a command — never hand them menu paths or keyboard shortcuts."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 73f7b807-8bfb-4334-8e59-ab88bdbd36b2
---

If I can do the thing with a command, I do it. I never hand the user a navigation route.

Banned as a first response: keyboard shortcuts, Finder/menu paths, "go to X then click Y", symbol
glyphs for modifier keys. If I must name a key, spell it out in words — never as a symbol.

**Why:** asking "where is that folder" means *show me*, not *teach me to navigate*. Sending him
through hidden system folders and a shortcut that silently does nothing unless the right app is
focused wasted several rounds and made him genuinely angry — when `open "<path>"` would have
finished it in one line. This is the same principle as the project rule "run it yourself through the
live editor — don't hand the user a menu click", which I already had and did not apply.

**How to apply:** before writing any instruction for the user to follow, ask "can I just run this?"
If yes, run it and say what happened in one line. Only give manual steps when the action genuinely
cannot be run from here (a visual judgement, Play mode, a web service, their own decision) — and then
make each step literal, with words for keys, per [[the-five-anti-invention]].

**A standard engineering convention is not a question (2026-09-26).** Asked to make scene files match the names on
the world map, I asked three questions (now or later, which format, what to do with two repeated names). The user was
furious: one id per map, derived from its name, snake_case, one source of truth, used everywhere — that is standard
practice, and "do whatever it takes" (rebuild maps, edit the tools) is implied. **How to apply:** when a request is
about consistency (ids, names, file names, keys), apply the industry-standard shape without asking — snake_case ids,
one owning file, every copy generated or checked from it, a guard that fails on drift — and label any genuinely new
call (like `_2` for a repeated name) as mine in the report instead of asking. See [[world-creator-pipeline]].
