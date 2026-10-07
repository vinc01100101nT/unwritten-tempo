---
name: casual3-male-pulled-from-roster
description: Casual3_Male is TEMPORARILY out of the game (2026-10-05) until its broken skeleton is fixed — the four places to put it back
metadata:
  type: project
---

On 2026-10-05 the user ordered Casual3_Male removed from everywhere in the game until it is fixed. Every import of
it logs "Rig Error: Invalid Avatar Rig Configuration … Required human bone 'LeftFoot' not found" (measured by
re-importing each model with a log listener — only this one). Its mesh is also not Read/Write, so its saved outfit
parts came from a whole-texture scan, and it is the only model on a loose `.mat` instead of an embedded material.

**Removed from (restore all four once it imports cleanly):**
1. `accounts/data/character-models.json` — the creation roster the character server reads at start (restart it).
2. `CharacterSpec.asset` `civilianModels` (it was between Casual3_Female and Casual_Bald).
3. Addressables group `Characters` — entry `Characters/Casual3_Male`, label `CharacterModel` (this is also what `/model` lists).
4. `WorldTownsfolk.asset` — Fenwick (Tool Dealer, lastline) was moved to `Casual2_Male`, my pick as the nearest look; ask the user whether he goes back.

**Left in place on purpose:** the FBX, texture and `.mat` (nothing loads them now), and its entry in
`CharacterDyeParts.asset` (inert data; the asset-rules check requires every model file to have one). No character in
the database wore it.

**How to apply:** don't re-add it to any roster until the rig error is gone; when fixing, restore all four in one
change and delete this note.
