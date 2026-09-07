---
name: facing-lives-on-the-model-not-the-root
description: "A live-3D character turns by yawing its MODEL child; nothing rotates the root, so reading a facing off the root gives a constant."
metadata: 
  node_type: memory
  type: project
  originSessionId: 3ea91dff-99e2-4f09-afe2-d7374b950f2f
  modified: 2026-09-07T10:01:23.912Z
---

Fixed 2026-09-07. Watchers saw every other player run around permanently facing one direction while their legs
moved.

**The rule, for anything that needs to know which way a character is pointing:** read it off the pose sink,
never off the root. `Character3DPose.Update` slerps its OWN transform toward `facing`, and that component sits
on the MODEL child. Grepping every rotation write across the scripts folder finds nothing that touches a player
root — `Player3DController`, its traversal partial, `PersistentPlayer3D` and `Player3DBody` between them never
write one.

**What it broke:** `NetPlayer.ReportMyPosition` sent `me.transform.eulerAngles.y` off `PersistentPlayer3D`, so
`netYaw` was the angle the root was built at, unchanged for the whole session. That value is the ONLY thing said
about anybody's facing, so every watched body was drawn at it.

**The handle to use:** `StanceController.Player.Pose` (a getter added that day, matching `Character3DVisual.Pose`
and `Agent3DVisual.Pose`), cast to `Component` for its transform. `IStancePose` has `SetFacing` but no read side.

**Do not "fix" it at the far end.** The watched body's ROOT is what gets turned there, and `Agent3DVisual` then
feeds the root's forward into the model's `SetFacing` — that chain was always correct and turning the model
directly would fight it.

See also [[chat-crosses-the-wire]].
