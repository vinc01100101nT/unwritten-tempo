---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - ai-learnings
---

# 🧊 The Offline Checker Says Clean and the Build Says Compile Errors

**Reach for this when:** your fast offline compile check passes, and then the real build fails on compiler
errors — in the very files you just added or changed, or in no file at all.

> An offline checker compiles **the files on disk**. The editor builds **the files it has imported**, and it
> also carries a one-bit memory of whether its LAST compile failed. Any of the three can disagree with the
> other two, and the build believes only the editor.

## The rule

**Before any build driven through a live editor, force the editor to import and recompile, and confirm it came
back clean. Then build.** Not the other way round, and never on the strength of the offline checker alone.

The two answer different questions:

- the offline checker → *would this source compile?*
- the editor's own recompile → *has this editor actually taken the source in?*

A clean checker with a stale editor looks exactly like a clean project right up until the build fails.

## Why — what it cost

Three consecutive failed server builds, roughly forty minutes.

- **Build 1** failed with `script class layout is incompatible between the editor and the player` — a message
  field had been renamed on disk; the editor still had the old one compiled.
- **Build 2** failed with `The name 'ServerHttp' does not exist in the current context` — a file created four
  minutes earlier that the editor had never imported.
- **Build 3** failed on `Unsaved scenes`, which is a different cause and is the user's to clear.

The offline checker reported **clean** before every one of them, and it was right every time: the source on disk
did compile. It simply cannot see what the editor is holding.

The cost was doubled by the build script deleting the previous output before it starts, so each failure left the
machine with no server at all (see [A Rebuild May Delete the Old Output First](a-rebuild-may-delete-the-old-output-first.md)).

**The second incident, 2026-09-07 — the same symptom with nothing broken at all.** Two server builds in a row
died on `Error building Player because scripts have compile errors in the editor`, the second in 1.6 seconds
after the first had burned 17 minutes. Every instrument said the project was fine: the offline checker clean on
all three halves, the editor's own error list empty, every assembly present on disk, and a recompile answering
*"up to date — no scripts needed recompilation"*. The editor's `scriptCompilationFailed` flag was reading
**True** on its own, with no error behind it — a memory of a failure, not a failure. A recompile with the build
cache cleared rebuilt everything, came back clean, and the flag went False. Nothing in the project had been
wrong for either of the two failed builds.

## How to apply it

1. **Recompile first, and WAIT for it.** Fire the editor's recompile, then poll its status until it says
   completed — the editor drops its link during the reload, so "no answer" is part of the normal sequence and
   not a failure.
2. **Check the editor's error list, not the checker's.** The recompile reports its own errors; that is the list
   that decides whether a build can succeed.
3. **If the build still says compile errors and nobody can find one, read the editor's own flag.** A plain
   recompile is allowed to answer *"up to date — no scripts needed recompilation"* and change nothing, leaving
   a stuck failure memory exactly where it was. Ask for the flag directly, and if it is set with a clean
   checker and a clean error list, clear the compile cache and make it rebuild everything from scratch.
4. **Then build.**
5. **Afterwards, prove the artefact contains your change** — search the built assembly for something unique to
   it, with a control string you know predates the edit so you can tell a real absence from a broken search.
   Watch the encoding: .NET stores string LITERALS as UTF-16 and identifier NAMES as UTF-8, so one probe shape
   cannot find both, and the wrong one reports a false ABSENT.

```bash
tools/check-unity-code.sh          # would the source compile — necessary, not sufficient
unity cmd recompile                # make the editor take it in
unity cmd recompile_status         # poll until "completed", then read its errors
tools/build-game.sh server         # only now
```

And when the build refuses over errors nobody can find — read the flag, then force a full rebuild:

```bash
unity cmd eval --timeout 120000 --code \
  'return UnityEditor.EditorUtility.scriptCompilationFailed;'      # True with nothing broken = stuck
unity cmd eval --timeout 120000 --code \
  'UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(
     UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache); return "requested";'
```

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [A Rebuild May Delete the Old Output First](a-rebuild-may-delete-the-old-output-first.md) — what a failed build costs you on top.
- [A Busy Host App Never Gets To Your Request](wait-for-the-host-app-to-be-idle.md) — the other half of driving an editor you do not control.
