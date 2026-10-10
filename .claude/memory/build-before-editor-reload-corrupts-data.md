---
name: build-before-editor-reload-corrupts-data
description: A build made while the editor still runs the OLD scripts crashes on start with "resources.assets is corrupted! [Position out of bounds]" — code and saved data disagree.
metadata:
  type: project
---

**Symptom (2026-10-09):** the map server crashed the instant it started; the log said `resources.assets is corrupted! Remove it and launch unity again! [Position out of bounds!]`, crashing inside `Resources.Load` from `CharacterSpec.Active` (via `Player3DController.Awake`).

**Root cause, measured by decoding the built file:** two fields (`offHandHitTiming`, `offHandPower`) were added to `CharacterSpec.cs`, and the server was built at 12:29 — but the editor did not reload its own scripts until 12:30:31. The build compiles the player code fresh from source (which had the fields) but writes the data from the editor's loaded classes (which did not). The built CharacterSpec object was 824 bytes with `hitTiming 0.7` followed directly by `unarmedReach 0.5`; server builds carry no type trees, so the reader expects the 8 extra bytes and runs off the end. The file header itself was intact.

**Fix:** rebuild once the editor has reloaded (`tools/build-game.sh server`). After the rebuild the object was 832 bytes with `0.7, 0.9, 0.5` in place.

**How to apply:** this crash with "corrupted" + "Position out of bounds" means a code/data mismatch, not a damaged disk. Check whether any `.cs` is newer than `unity/Library/ScriptAssemblies/Assembly-CSharp.dll` at the build's time, and rebuild. Decoding the object (pathID/byte size from the serialized-file object table, then float words) proves it in minutes. Related: [[may-restart-local-server-to-test]].
