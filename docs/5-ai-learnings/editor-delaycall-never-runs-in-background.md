# Something queued for "the editor's next tick" never runs

**Reach for this when** Unity editor code that runs later (on `EditorApplication.delayCall`) works while you are
clicking around in Unity, but does nothing when the change came from a script, an agent, a git pull or the Unity
command line while the editor sat in the background.

## The rule

**Don't schedule follow-up work on `EditorApplication.delayCall`** if it has to run without anybody touching Unity.
Set a flag and check it from `EditorApplication.update`, which kept running in the background when `delayCall` did
not. Then prove it with the editor unfocused: make a change from outside and time how long until the result appears.

## Why

2026-09-26. The wiki's Database exports queued themselves on `delayCall` after an asset import. Changed from the
command line, the queue grew to 42 waiting calls, Unity's own among them, and none ran for minutes. My probe calls
still ran the whole time, because they go through the editor's `update`. It cost three rounds of "the hook fired but
the file never changed", and `QueuePlayerLoopUpdate()` did not help. After moving the work to `update`, the files were
rewritten within 2 s of an asset change, 4 s of a file change on disk, and 6 s of a recompile, all with the editor in
the background.

## How to apply

1. `Request()` sets a flag (`queued = true`). One handler on `EditorApplication.update` checks the flag, clears it and
   does the work. Return at once when the flag is clear, because the handler runs every tick.
2. If the work must not run during Play, keep the flag set and run when Play ends (`playModeStateChanged` →
   `EnteredEditMode`).
3. Check it: blank the output file, trigger the change from outside Unity, and poll the file's size from the shell.
