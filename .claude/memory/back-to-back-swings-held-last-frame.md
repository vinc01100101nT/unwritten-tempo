---
name: back-to-back-swings-held-last-frame
description: "A watched body swinging back-to-back with a NON-looping attack clip froze on the clip's last frame — the one-shot override never re-entered a state that was already current. Fixed 2026-10-14 in StanceController; compiles, unplayed."
metadata:
  node_type: memory
  type: project
  originSessionId: 1eab0280-e3aa-4695-8ff0-c9d971ae2011
  modified: 2026-10-14T00:34:47.849Z
---

**Symptom (built Windows client, 2026-10-14):** melee Shadow creatures (spear, dagger, katar, scythe) stood frozen while
attacking; bow and thrown-shuriken Shadows animated; running was fine for all.

**Root cause:** a watched body's swing arrives through `Agent3DVisual.TellAction` → `StanceController.PlayActionFor`
(the one-shot override). Each frame the override calls `pose.Play` → `Character3DPose.GoTo(hash)`, which returns when
the state is already current. At level 100+ a Shadow swings every 0.233 s (measured in `logs/server.log` with
`/log all on`), so one override ends exactly as the next begins and the state is never left. A clip that does not loop
plays once and holds its last frame. The hand-made RO-cloned `.anim` attacks have `m_LoopTime: 0`; `AttackBow.fbx`,
`AttackSword2H.fbx` and `ReleaseSingleSkill.fbx` have `loopTime: 1`, so they kept cycling and hid the bug.

**Fix:** `StanceController` remembers the override shown last frame (`shownOverride`, `shownOverrideId`); a new
`PlayActionFor` of that same stance enters at fraction 0 (`PlayFrom`), the same rule the action layer already had
(`newBeat`). Do NOT fix this by switching the attack clips to loop — see [[locked-attack-animations]].

**Every attack clip is play-once (user decision, 2026-10-14):** `AttackBow.fbx`, `AttackSword2H.fbx` and `Punch.fbx`
were switched to `loopTime: 0` to match the hand-made ones. The other imported one-offs (the skill releases, Cast,
Jump, Roll, Hurt, PickUp) still loop, because `BulkHumanoidRig.ApplyClipRoots` sets loop on every imported clip — and
re-running `Fix Humanoid Clip Roots` would switch the three attacks back to looping.

**Gotchas:** the server side was identical for melee and ranged (same beat, same announcement) — the split was a clip
import flag. `/killall` does not remove `/summon`ed creatures: they come back after 5 s.

Related: [[watched-bodies-must-be-told-not-asked]], [[shadow-body-frozen-after-revive]].
