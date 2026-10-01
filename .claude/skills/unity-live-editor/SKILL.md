---
name: unity-live-editor
description: Use when working against this project's running Unity Editor — checking whether the link is up, reading live scene/component values, taking a Game/Scene view screenshot, running an editor action or one-shot asset fix, or checking whether edited C# compiles. Covers the connection, what is proven vs unproven, the forced-recompile ban, and the offline compile checker.
---

# The live link to Unity

**It exists, it is installed, and it works. Do not go looking for anything else.** The old
`com.coplaydev.unity-mcp` HTTP bridge was retired on 2026-08-20 — it had been dead for five days without anyone
noticing, and a whole session was worked while believing a connection existed that did not. There is now ONE
road to the editor.

- **Binary:** whatever `tools/platform.sh unity-cli` answers — never type the path. On the Mac that is
  `~/.unity/bin/unity`, which `~/.zshrc` puts on the PATH but a NON-interactive shell may not — so a bare `which
  unity` coming back empty is NOT proof it is missing. (Twice the user was told it "wasn't installed" that way.)
  On the Windows PC it is `AppData/Local/Unity/bin/unity.exe` in the user's folder (installed with the Unity Hub
  update, v1.0.0-beta.11, measured 2026-10-01).
- **In-editor half:** the `com.unity.pipeline` package in `unity/Packages/manifest.json`. Without it the CLI
  SEES the editor (project, PID, version) but cannot talk to it.
- **As tools:** `.mcp.json` registers `unity-editor-mcp` (stdio, started by `tools/start-mcp.sh`, which finds the
  binary and the project folder on whichever computer this is), so the editor's ~143 commands arrive as ordinary
  tools — no terminal needed.
- **The command surface** is documented by Unity's own skill, `~/.claude/skills/unity-cli/SKILL.md`. Read that;
  don't re-derive it.

## ALWAYS check first — never assume it's up

```bash
unity cmd --project-path <this repo>/unity editor_status   # "status":"ready" = connected
```

**Name the project every time.** On the Windows PC a bare `unity status` prints an EMPTY table while the editor is
open and answering — the same editor answers at once when asked by `--project-path` (measured 2026-10-01). An empty
`unity status` there is NOT proof the editor is down.

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

## "Main thread operation timed out" is the CALLER giving up — not a crash

Every command runs on Unity's ONE main thread, and that thread is blocked for whole seconds to whole minutes
while it compiles scripts, imports assets, loads a scene, or builds a player. The wait budgets belong to the
CALLER, and they are short:

- **30 seconds** for any `unity cmd …` — the CLI's `--timeout`, which is in SECONDS and defaults to 30.
- **5 seconds** for `eval` / `eval_file`. Both list a `--timeout` of their own, in MILLISECONDS, default 5000 —
  but **measured 2026-09-22: `--timeout 600000` on both still ended at 5000ms**, three times (a 34-scene edit and two
  96-prefab bakes). Whatever the flag does, it did not lengthen the wait. Every one of those runs finished its work
  anyway.

When a budget runs out the editor prints a red `Failed to handle /api/exec request: Main thread operation timed
out after Nms`. **It does not mean the work was undone:** a command still sitting in the queue is cancelled, but
one that had already begun on the main thread runs to completion anyway — so after a timeout on a MUTATING
command, go and look at what it did before re-firing it.

**Before any command that needs the main thread, check the editor is idle:**

```bash
unity cmd editor_status --json   # want compiling:false, domainReloadInProgress:false, playMode:"stopped"
```

The in-editor half has a busy gate of its own, but it only covers the first seconds after an editor START. A
mid-session compile, asset import or player build is NOT rejected for you — the command queues, waits, and then
times out.

**And for a slow call, plan on the caller giving up — then wait and read what it did:**

```bash
unity cmd <other-command> --timeout 300      # everything but eval/eval_file, SECONDS — this one works
unity cmd <slow-command> --detach            # returns a job id at once, then poll for the result (not yet tried on eval)
# after an eval that timed out: wait for the editor, then measure the result (files on disk, a read-only eval)
until unity cmd editor_status --json | grep -q '"status": "ready"'; do sleep 3; done
```

## Proven vs unproven — say which

**Reading is proven** (2026-08-20): the live scene tree, the console, compile state, and a scene-view screenshot
that was actually looked at. **Mutating is NOT proven** — the first person to change something through it should
verify and say so.

**Measured gotcha:** `capture_scene_view`'s `save_path` is IGNORED — it always returns the PNG inline as base64.
Decode it yourself and write the file; don't report a path it never wrote.

## What stays the USER's — do not do this

- **Anything needing their visual judgement** ("does this look right"). Always theirs.
- **Play mode, EXCEPT under the `tester` skill.** The blanket ban was lifted by the user on 2026-08-30, once
  entering Play mode, reading live values, capturing the screen and signing in were all proven to work through
  this link. It is still not something to enter casually: it takes over the user's editor, so it happens only
  when they have said "spawn a tester". Whoever enters Play mode LEAVES it, and checks the open scene came back
  undirtied.

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
