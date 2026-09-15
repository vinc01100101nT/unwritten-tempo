---
status:
owner: vince
last-updated: 2026-09-15
tags:
  - ai-learnings
---

# A Project Rule Overwrote the Setting You Just Wrote

**Reach for this when:** you changed a setting or re-saved files through an API, the call returned success, and
reading the result back shows the OLD value — and it is not a stale read, because it still reads old minutes later.

## The rule

Before a bulk settings change, **search the repo for code that writes the same setting on a hook**. Automatic
rules — import postprocessors, lint autofix, formatters, generators, config defaults applied on load — run
*after* your write and clobber it, and the write still reports success because it genuinely happened. Then
**verify the RESULT, never the return value**: read the property back off the finished artifact, not the API's
"ok". If a whole folder silently refuses your change, an automatic rule is the first suspect, not the last.

## Why

2026-09-07, a texture memory pass across four folders. Three took: 4.9 GB of textures dropped to 2.0 GB. The
fourth — the character folder — reported 52 files reimported in 9.4 seconds, no error, no warning. The formats
had not moved: still raw, still no mipmaps. An import postprocessor in the project force-sets
`textureCompression = Uncompressed`, `mipmapEnabled = false` and `textureType = Sprite` on **every** path
containing `/Art/`, on every import. It ran after each of my 52 writes and undid all of them.

The cost was small only because the check was already planned: reading the formats back caught it in one call.
Without that read, the report would have said "all four passes done" and been wrong about a quarter of it —
and the wrongness would have been invisible until someone re-measured weeks later. The near-miss is the lesson:
the API's success was a completely convincing lie.

It also explains the shape of the whole problem. **Three** different causes produced one symptom, and I first
reported them as one: a project-wide default, the automatic rule, and settings baked into each file's own
record long ago. Stopping at the first left the write-up wrong until a throwaway probe — one 64×64 image
imported into an empty folder — showed the default did *not* do what I had claimed. When you name a cause,
reproduce it in isolation before writing it down; a plausible cause sitting next to measured numbers reads as
measured too.

**The same symptom has a second cause: the write never happened at all.** 2026-09-15, re-saving 86 generated
prefabs to drop a field deleted from the code. Unity's force re-save call returned success, and all 86 files still
held the field. No hook was involved — every file's modified time was still three days old, so nothing had been
written. Loading each prefab and saving it through the prefab save call removed the field from all 86. Counting the
files that still held the old value caught it at once; the success message alone would have been reported as done.

## How to apply

- **Check the file's modified time first.** Unchanged means the write never happened, so look at the call itself,
  not at hooks. Changed but still old means something rewrote it afterwards, which is the hook case below.

- **Before the change**, grep for the setting's own name and for the hook types that can rewrite it:
  ```bash
  grep -rn "OnPreprocess\|AssetPostprocessor\|prepare\|pre-commit\|autofix" <editor/tooling dirs>
  grep -rn "<theSettingName>" .            # who else writes this?
  ```
- **After the change**, re-read the property from the finished artifact and compare — count how many files
  actually hold the new value, not how many calls returned success.
- **When one folder behaves differently from its siblings, look for a path condition.** These rules almost
  always key off a substring of the path (`/Art/`, `/vendor/`, `*.gen.*`), so the boundary of the failure IS
  the condition.
- **A rule like that is usually deliberate.** It encodes somebody's decision — a look, a convention, a safety
  net. Overriding it changes that decision, so it goes to the user as a question, not into your diff.
