---
status:
owner: vince
last-updated: 2026-09-28
tags:
  - ai-learnings
---

# Test the Packaged Form That Ships, Not the Code in Another Wrapper

**Reach for this when:** you proved a program works by running its code inside a test harness, and the thing you
hand the user is a packaged build of it — single-file, compressed, self-contained, signed.

## The rule

Packaging is not neutral: it changes which runtime runs, how the code is loaded, and what the program links to.
Run the test in the SAME packaged form, with the same build settings, before calling it working. And check what
the packaged program depends on outside itself — a build machine's own library folders leak in silently.

## Why

2026-09-28: a game launcher's code passed every test — downloads, updates, damaged files, drawn off-screen —
inside a harness that ran it as an ordinary .NET program. The shipped launcher was built single-file, compressed
and self-contained. The user opened it and it crashed within five seconds, a round lost. Rebuilding the same
harness the shipped way reproduced it every time: `AccessViolationException` inside .NET's own web code, only when
the single file was compressed. The same check found a second defect nobody had seen: the Apple-silicon build took
.NET from Homebrew, whose copy points at Homebrew's library folder, so it could never start on a player's Mac.

## How to apply

1. Give the test harness the shipping build's exact settings (single-file, compression, self-contained, platform).
2. Run it several times — the crash above was every run, but not every packaging fault is.
3. Search the packaged binary for the build machine's own paths: `python3 -c "print(open('<binary>','rb').read().count(b'/opt/homebrew'))"`
   — anything above 0 means it will not start elsewhere.
4. Toggle one packaging setting at a time to find which one breaks it; the fix is usually to drop that setting.
