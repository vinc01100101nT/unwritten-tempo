---
name: no-editor-tools-by-default
description: Do NOT build Unity editor menu tools by default — the user rejects them; ask what the feature needs AT RUNTIME first, and prefer a GM chat command.
metadata:
  type: feedback
---

**Do not reach for a `Tools ▸ unwritten ▸ …` editor menu item as the default way to ship a feature.** The user's words: *"i dont prefer editor tool bullshits"* and *"you are able to implement without bullshit tool menus. why didnt you do that"* (2026-08-13).

Before designing ANY setup step, ask: **what does this actually need at RUNTIME, and is it already loaded?** Very often the data an editor tool would "prepare" is already live in the running game, and the whole author-time layer (ScriptableObject + Resources folder + menu item + the click) is pure invented ceremony.

**Why:** for the 3D "shadow mob" spike I built a `CharacterCast` ScriptableObject + a `Build Character Cast` menu item to hand the game a list of rigged character models — models that were **already instantiated and running on the live player**. The correct implementation was to clone the player's own live body: zero assets, zero menu, zero setup, and truer to the feature. The editor tool then failed to appear at all (Unity's editor-assembly source list was stale — see [[unity-editor-assembly-source-list-stale]]), which burned three extra rounds diagnosing infrastructure for code that should never have existed.

**How to apply:**
1. Default to **runtime** code. If the feature can read what it needs from the live scene/player at Play, do that.
2. For anything the user needs to trigger, prefer a **GM chat command** (`GmCommands.cs` — one `Register(new GmCommand(...))` entry, no menu, no recompile ceremony) over a menu item. Extend an EXISTING command with a modifier rather than adding a new one.
3. Only build an editor tool when the work genuinely must happen at author time (baking prefabs, writing assets, mutating scenes) AND the user asked for it.
4. This **overrides** the project `CLAUDE.md` standing rule "Prefer editor tools over manual editor steps; extend `Tools ▸ unwritten ▸ …` for new one-click ops" and the [[editor-tools-menu]] memory. That rule is about not handing the user manual multi-step editor chores — it is NOT a licence to invent author-time setup for something runtime code can do itself. When the two conflict, runtime wins.

Related: [[act-editor-actions-via-mcp]] (don't hand the user a click for something you can run), [[name-knobs-concretely-one-job]].
