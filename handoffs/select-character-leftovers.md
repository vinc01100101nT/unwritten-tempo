# Handoff — after "Select character": old character left standing

**Task/bug:** After System window ▸ Select character, the character you left stays standing where it was, in
default colours, and one more copy piles up on every switch. Seen on the Mac build and the Windows build, client
only.

**Status:**
- **Missing townsfolk:** DONE, verified by the user on 2026-10-04 (commit `0861d77d`).
- **Old character left standing:** FIXED IN CODE, verified in a probe client build on 2026-10-05. NOT yet
  play-tested by the user, and the change is uncommitted. Don't call it gone until the user says so.

## What was found (measured)

Every object made during Play carries a do-not-save flag, and Unity never destroys a flagged object when its scene
closes — it is left in no scene, still drawn. A probe build proved it with two objects made side by side in
Systems: after Select character the flagged one remained in no scene, the plain one was destroyed. Each switch left
12 such things (the worn body, the weather and ink layers, the target marker); switching away from the field also
left its 36 creature bodies and their shadows (71 things). Full write-up: memory
`play-made-objects-outlive-their-scene`.

## What changed (uncommitted)

- `MapManager.DestroyWhatOutlivedItsScene`, called when any scene closes and when any scene opens: destroys every
  root object in no scene that carries the do-not-save flag (engine-hidden objects and assets excluded) and logs
  one `[map] a scene opened — N thing(s) made during play went with it … : <names>` line.
- `Afterimage.Step` drops a ghost whose object is gone.

## What's left

1. The user play-tests: several switches back and forth on the Mac and on Windows, including one from the field.
   Needs a new client build for each machine. The server needs a rebuild too, because it had the same leak on every
   map it lets go.
2. If it holds: commit, delete this handoff.
3. If a leftover remains: read the client's Player.log for the `[map] a scene … went with it` lines — they name
   exactly what was removed, so a leftover that is missing from them is the thing to chase.

## How to resume

```text
Read handoffs/select-character-leftovers.md and memory play-made-objects-outlive-their-scene. The fix is in code; ask the user for the play-test result.
```
