---
name: unity-live-editor
description: Use when working against this project's running Unity Editor — checking whether the link is up, reading live scene/component values, taking a Game/Scene view screenshot, running an editor action or one-shot asset fix, or checking whether edited C# compiles. Covers the connection, what is proven vs unproven, the forced-recompile ban, and the offline compile checker.
---

# The live link to Unity

**It exists, it is installed, and it works. Do not go looking for anything else.** The old
`com.coplaydev.unity-mcp` HTTP bridge was retired on 2026-08-20 — it had been dead for five days without anyone
noticing, and a whole session was worked while believing a connection existed that did not. There is now ONE
road to the editor.

- **Binary:** `~/.unity/bin/unity`. `~/.zshrc` sources `~/.unity/env` to put it on the PATH, but a
  NON-interactive shell may not load that — **use the full path when a bare `unity` is not found.** (Twice the
  user was told it "wasn't installed" because `which unity` came back empty.)
- **In-editor half:** the `com.unity.pipeline` package in `unity/Packages/manifest.json`. Without it the CLI
  SEES the editor (project, PID, version) but cannot talk to it.
- **As tools:** `.mcp.json` registers `unity-editor-mcp` (stdio), so the editor's ~143 commands arrive as
  ordinary tools — no terminal needed.
- **The command surface** is documented by Unity's own skill, `~/.claude/skills/unity-cli/SKILL.md`. Read that;
  don't re-derive it.

## ALWAYS check first — never assume it's up

```bash
unity status      # a row with state `ready` = connected; an empty table = the editor isn't attached
```

An empty table usually means the package hasn't been imported yet (Unity imports it when the user focuses the
window). **If it's down, SAY SO** rather than working blind. Working blind while sounding certain is the exact
failure that killed the old bridge for five days.

## Reach for it WITHOUT being told — these moments ARE the instruction

The user should never have to say "check Unity". When any of these is true, use it as the first move and report
what you MEASURED:

- **You are about to state a live value** — a transform, a component field, what's in the scene, whether
  something is wired up. Reading the `.unity` / `.prefab` text tells you what was SAVED, not what is LOADED;
  when those can differ, ask the editor.
- **You want to know what something LOOKS like.** Take the screenshot and LOOK at it. Never describe an
  appearance you have not seen, and never ask the user to describe it for you.
- **You need the console** after an author-time action, to see what it actually printed.
- **An author-time action is on the table** — an asset edit, a one-shot fix, an existing editor tool's logic.
  Run it. Don't hand the user a menu path.
- **A fix survived one round.** For anything living in the editor, this IS the ground truth, and one look beats
  another hypothesis.

## Running an editor action

- Prefer `eval` calling the **dialog-free `*Core()`** method (e.g. `EffectLibraryBuilder.RebuildCore()`), NOT
  the `[MenuItem]` wrapper — wrappers often show a modal dialog that can HANG the editor. Fall back to firing
  the menu item only when there is no dialog-free entry point.
- `eval` compiles against the **already-compiled assemblies**, so asset and library edits run even when your
  just-edited `.cs` hasn't recompiled. Use that to sidestep compile order: patch the asset directly instead of
  waiting on a code edit.

## Proven vs unproven — say which

**Reading is proven** (2026-08-20): the live scene tree, the console, compile state, and a scene-view screenshot
that was actually looked at. **Mutating is NOT proven** — the first person to change something through it should
verify and say so.

**Measured gotcha:** `capture_scene_view`'s `save_path` is IGNORED — it always returns the PNG inline as base64.
Decode it yourself and write the file; don't report a path it never wrote.

## What stays the USER's — do not do this

- **Play mode, and anything needing their visual judgement** ("does this look right").

## Refreshing / recompiling is ALLOWED (ban lifted 2026-08-26)

Trigger a refresh yourself when the editor needs to pick up new files or edits — don't hand the user a click.
Two things still hold: **don't sit in a polling loop** on `recompile_status` / `get_console_logs` (fire it, wait,
read once), and **to learn whether your C# compiles, use the offline checker below**, not a refresh — it is far
faster and needs no editor.

## To find out whether your C# compiles, run the offline checker

Do NOT ask the user, and do NOT poll the editor.

```bash
tools/check-unity-code.sh            # both halves (default)
tools/check-unity-code.sh runtime    # game scripts only
tools/check-unity-code.sh editor     # editor tools only
```

~4s, needs no Unity window, and does **not** touch the running editor — so it doesn't violate the ban above.
Unity ships its own C# compiler in the app bundle and every reference library is already on disk, so this runs
the SAME check Unity would and prints real errors with exact file + line. Exit **0** clean, **1** compile errors,
**2** setup problem (it names what's missing; needs `unity/Library/ScriptAssemblies/`, present on any project
Unity has opened once).

It checks BOTH halves, which matters: the game (`Assets/Scripts`) and the editor tools (`Assets/Scripts/Editor`).
An editor-only mistake is otherwise invisible until someone opens that window. It was verified to CATCH
deliberate errors, not merely to report "clean".

A clean run means it BUILDS. It never means it BEHAVES — it does not replace play-testing.

## When a menu item you just wrote never appears

Suspect a **stale editor-assembly source list** before anything else. Unity has been seen reusing a two-day-old
build file listing 89 source files against 90 on disk — the missing one was the new tool. **A fresh DLL
timestamp is NOT evidence your file compiled**; check the tool's actual input list, or search the output for a
string unique to your change.
