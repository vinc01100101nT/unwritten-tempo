---
status:
owner: vince
last-updated: 2026-09-02
tags:
  - ai-learnings
---

# 🧊 The Offline Checker Says Clean and the Build Says Compile Errors

**Reach for this when:** your fast offline compile check passes, and then the real build fails on compiler
errors — in the very files you just added or changed.

> An offline checker compiles **the files on disk**. The editor builds **the files it has imported**. Those are
> two different sets, and a file you created a minute ago is in the first and not yet the second.

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

## How to apply it

1. **Recompile first, and WAIT for it.** Fire the editor's recompile, then poll its status until it says
   completed — the editor drops its link during the reload, so "no answer" is part of the normal sequence and
   not a failure.
2. **Check the editor's error list, not the checker's.** The recompile reports its own errors; that is the list
   that decides whether a build can succeed.
3. **Then build.**
4. **Afterwards, prove the artefact contains your change** — search the built assembly for something unique to
   it, with a control string you know predates the edit so you can tell a real absence from a broken search.
   Watch the encoding: .NET stores string LITERALS as UTF-16 and identifier NAMES as UTF-8, so one probe shape
   cannot find both, and the wrong one reports a false ABSENT.

```bash
tools/check-unity-code.sh          # would the source compile — necessary, not sufficient
unity cmd recompile                # make the editor take it in
unity cmd recompile_status         # poll until "completed", then read its errors
tools/build-game.sh server         # only now
```

---

## Related

- [AI Learnings & Improvements](README.md) — the index.
- [A Rebuild May Delete the Old Output First](a-rebuild-may-delete-the-old-output-first.md) — what a failed build costs you on top.
- [A Busy Host App Never Gets To Your Request](wait-for-the-host-app-to-be-idle.md) — the other half of driving an editor you do not control.
