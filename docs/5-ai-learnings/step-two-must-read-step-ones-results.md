---
status:
owner: vince
last-updated: 2026-09-25
tags:
  - ai-learnings
---

# 🔗 Step Two Ran on Files Step One Never Rebuilt

## Reach for this when

You run a batch in two steps — build or regenerate a set of files, then patch, link or wire them — especially
inside an app somebody else is using at the same time.

## The rule

**The second step takes its list of files from the first step's successes, never from its own copy of the list.**
If step one stopped at file 2 of 12, step two must touch file 1 and nothing else. And **right before each write,
re-check that the file is not open in the other person's app** — they may have opened it a minute ago.

## Why

2026-09-25, this project. Twelve test maps were being rebuilt and then given their doors, in two editor scripts.
The user had opened the second map in the editor to look at it, so the rebuild correctly refused that map and
stopped. The door script had its own hard-coded list of all twelve. It added doors to ten maps that had NOT been
rebuilt, so those ten ended up with every door twice. It also opened the map the user had on screen, added doors to
it and saved it. Nothing was lost, because they were throwaway test maps, but it cost a round: the user's open map
got changed behind their back, and ten maps had to be rebuilt again.

## How to apply

- Have step one write the names of the files it actually finished, one per line. Have step two read that file and
  loop over it. A step one that stopped early then limits step two by construction.
- Step two refuses a file that is open in the other person's app, checked at the moment of the write, not once at
  the start.
- After both steps, count what you meant to create in each file (doors, entries, rows). A doubled count is the
  signature of this mistake.
