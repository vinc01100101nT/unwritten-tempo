---
name: unity-auto-refresh-off
description: WRONG AS OF 2026-09-03 — Unity Auto Refresh is ON (and refreshes during Play too). Editing files with Unity open WILL trigger an import, and during Play mode that can destroy a running play session. MEASURE before assuming.
metadata:
  type: project
---

**Auto Refresh is ON on this machine, in the mode that also refreshes DURING Play mode.**
Measured 2026-09-03 from Unity's own editor preference (`kAutoRefreshMode = 1`; 0 would be off, 2 would be
"outside Play mode only"). An earlier version of this memory said it was permanently off; that was true once and
is not true now, which is exactly why it must be MEASURED rather than remembered.

**What it cost, 2026-09-03:** a tester sub-agent was playing the game through the editor while this session
edited a C# file. I had told the user AND the tester, citing the old memory, that editing was safe. Roughly five
minutes in, an asset refresh carrying a 6.4-second code reload ran mid-play: the player's character handle went
to nothing, the game kept running against it, and two components then threw errors at about 120 per second. In
about 8 seconds that flood filled the entire 1000-entry console buffer and ERASED every earlier line, so the
history of the run was unrecoverable from the console. The play session was destroyed and the checklist run was
lost. The tester also counted EIGHT refreshes in about fifty seconds.

**How to apply:**
- **Never edit any file under the Unity project while a play session — mine or a tester's — is running.**
  Queue the edit and make it after Play mode stops.
- Before relying on refresh behaviour, MEASURE it: `defaults read com.unity3d.UnityEditor5.x kAutoRefreshMode`
  (0 = off, 1 = on including during Play, 2 = on but not during Play). Do not quote this memory as the answer.
- The old raw-asset-move guidance that depended on Auto Refresh being off is therefore UNSAFE as written: a raw
  move of an FBX plus its `.meta` can now be re-imported live. Close Play mode first, and prefer the editor's
  own move for assets with external dependencies.
- A console flood can erase the console. When something goes wrong mid-play, go to the on-disk editor log
  rather than trusting that the console still holds the cause.

Relates to [[changes-must-persist-in-repo]] and [[never-widen-a-snapshot-into-a-verdict]] — this file was itself
a snapshot widened into a standing verdict.
