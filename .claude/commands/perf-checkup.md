---
description: Measure-driven performance checkup for the 2.5D Unity game (find + fix combat hot paths, log the result)
argument-hint: "[optional area to focus on, e.g. 'pathfinding' or 'UI']"
---

You are running a recurring **performance checkup** on the 2.5D Unity game (C# at `unity/Assets/Scripts/`).
Performance is a HARD constraint (many billboard mobs + auto-combat). Follow this process — do NOT skip
straight to guessing or fixing.

## 0. Warm up (read first, don't re-derive)
- Read `unity/Assets/Scripts/PERFORMANCE.md` (the rules + reusable systems) and `unity/Assets/Scripts/PERF_LOG.md`
  (the history — the **latest entry is your baseline**; don't re-investigate already-optimized code).
- Note the previous baseline numbers so you can compare. Focus area this run: **$ARGUMENTS** (if empty, do a general sweep).

## 1. Static scan (cheap, do this yourself)
- Look at what changed since the last checkup: `git log --oneline` since the last log date and `git diff` of
  recent work. Use Grep to find NEW or edited per-frame / per-hit code:
  - `Update`/`LateUpdate`/`FixedUpdate` methods, coroutines, and anything spawned per hit.
  - Search those for the anti-patterns in PERFORMANCE.md: `new ` (objects/Lists/arrays/WaitForSeconds), `GetComponent`,
    `Find`/`FindObjectsByType`, `Camera.main`, `Sprite.Create`, per-frame `Text.text =`/string interpolation,
    returning a `new List`/array from a hot API, per-hit `new GameObject`, writing `RenderSettings.*` each frame.
- List anything suspicious as **candidates** — but do NOT trust suspicion. The profiler decides (last time, code
  review fingered the wrong culprit; the profiler proved it was pathfinding).

## 2. Measure (the user drives — there is NO local Play loop)
Ask the user to run the **standard scenario** in PERF_LOG.md (NatureStage → gather mobs → press T) and report:
- The **PerfHud** (F3) readout — `GC/s` and `peak /frame` — for the quick verdict, AND
- If `GC/s`/peak is non-trivial: **Profiler ▸ Hierarchy**, click a green spike, expand
  `PlayerLoop ▸ UpdateScene ▸ … ▸ BehaviourUpdate`, and paste the per-script **GC Alloc** rows.
Wait for real numbers/screenshots. Diagnose from the **measurement**, not the static scan.

## 3. Fix only what's proven
- Fix the allocators/hot paths the profiler actually shows, biggest first.
- **Reuse the existing systems** (don't reinvent): `CameraRig`, `SpriteSheetCache`, pooled `DamagePopup`,
  `MonsterAI3D.Active`+cached `.Health`, caller-owned-list `Pathfinder3D.FindPath`, `*NonAlloc` physics.
- Keep changes behavior-identical; these are efficiency fixes.

## 4. Re-measure
Ask the user to re-run the scenario and confirm the targeted number dropped (target ~0 B/frame from gameplay
scripts). If it didn't, the diagnosis was wrong — go back to step 2.

## 5. Record
- **Append a new entry to `unity/Assets/Scripts/PERF_LOG.md`** (newest on top) using the template there:
  mobs, alloc before→after, frame-time before→after, found, fixed, any new rule.
- If a NEW pattern or anti-pattern emerged, add it to `PERFORMANCE.md` (and `CLAUDE.md` if it's a cardinal rule).
- Do NOT commit unless the user asks. Summarize what changed and what the user should verify in a build.

Be honest about editor vs build: in-editor frame time is inflated by editor overhead — judge true frame-time
in the built client, measured the way the `measure-built-game` skill says; GC allocation from gameplay scripts is
trustworthy in the editor.
