---
name: watched-sprite-mob-one-facing
description: "Flat-picture mobs on a watching machine showed one facing and never stepped: their mover is switched off there, and the sprite animator only asked the mover."
metadata:
  node_type: memory
  type: project
  originSessionId: eab9755f-b6b7-48cc-9b3e-4d6a57f8b978
  modified: 2026-09-27T09:07:07.287Z
---

Fixed 2026-09-27. Every flat-picture (billboard) mob showed its front picture and slid without walk frames on the
player's screen, whichever way it walked.

**Cause:** `NetMob.SilenceTheBrain` switches off the watched body's `PathAgent3D`; `BillboardAnimator3D.Update`
read only `agent.Moving` / `agent.Facing`, so it saw "standing, facing down" forever. The sprite root's yaw is the
camera billboard turn, so `netYaw` never carried travel direction for sprite mobs.

**Fix:** `PathAgent3D.Heading` (world x,z, camera-free) → server sends it as `NetMob.netHeading` (only for non-shadow
mobs, only while moving) → watcher calls `BillboardAnimator3D.TellTravel(netSpeed > 0, netHeading)`, which runs it
through `Facing3D.FromWorldDir` for the watcher's own camera. Same "told, not asked" shape as
`Agent3DVisual.TellTravelSpeed`.

**Gotcha:** a new SyncVar changes NetMob's wire size — an editor client against an old server build floods the console
with "OnDeserialize size mismatch" / EndOfStreamException on every NetMob. Rebuild the server build.

Related: [[facing-lives-on-the-model-not-the-root]], [[shadow-body-frozen-after-revive]].
