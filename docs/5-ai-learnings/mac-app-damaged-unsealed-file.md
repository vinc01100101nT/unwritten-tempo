---
status: current
owner: vince
last-updated: 2026-10-02
tags:
  - ai-learnings
---

# A Mac Says an App You Built Is "Damaged"

**Reach for this when** a Mac refuses an app you built or signed yourself with "is damaged and can't be opened. You
should move it to the Trash", and System Settings offers no "Open Anyway".

## The rule

"Damaged" means the app's signature does not match its contents — not that the download broke. Ask the Mac which
file: `codesign --verify --deep --strict --verbose=4 <app>`. The usual cause is a file that is not a program sitting
in `Contents/MacOS`: a Mac treats everything there as code needing its own signature, so it is never sealed. Put
settings and data in `Contents/Resources`, then check that the app's `_CodeSignature/CodeResources` lists every such
file with a matching fingerprint before handing the app out.

## Why

2026-10-02. A launcher built and signed on Windows (rcodesign) carried its settings file beside the program in
`Contents/MacOS`. Both Mac downloads were refused as "damaged" with no way round it; codesign said "a sealed resource
is missing or invalid". The signature's list of sealed files was empty. Two rounds: one building and serving the Mac
version, one diagnosing it from the user's Mac. Moved to `Contents/Resources`, the file appeared in the sealed list
with a matching fingerprint.

## How to apply

- Read the seal without a Mac: parse `Contents/_CodeSignature/CodeResources` (a plist) and compare each `files2`
  entry's `hash2` with the SHA-256 of the file. An empty list in an app with non-program files is the fault.
- On the Mac, one line gives the verdict: `codesign --verify --deep --strict --verbose=4 <app>`.
