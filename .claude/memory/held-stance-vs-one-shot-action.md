---
name: held-stance-vs-one-shot-action
description: A cast pose is a HELD stance, not a one-shot action — the swing channel clamps to the clip's natural length and would drop a long wind-up back to idle.
metadata:
  type: project
---

Built 2026-09-07, when the spellcasting pose was made to reach watchers.

**`StanceController` resolves three layers, highest first:** a one-shot override (`PlayActionFor`), the per-frame
action stance (`DriveStance`, set by the brain), then the held stances (`Push`/`Pop`) by priority, then Idle.

**A cast is a HELD stance, and must stay one.** `Casting` is authored `FitWindow` (priority 30), so `Push(Casting,
dur)` stretches the clip across the whole wind-up. The obvious-looking reuse — sending it down the action-alarm
channel that already carries a swing (`NetPlayer.BeganAction` → `Agent3DVisual.TellAction`) — is WRONG: that path
does `play = Min(natural, beatSeconds)` and then `PlayActionFor`, which plays the clip once and self-expires. A
four-second wind-up with a 1.2-second cast clip would animate for 1.2 s and stand in idle for 2.8 s.

**Why pushing a held stance on a WATCHED body works at all:** the caster is fully rooted for the wind-up
(`LockActions`), so they are not moving — and `Agent3DVisual` only calls `DriveStance` while walking, else
`ClearAction()`. With the action layer clear, the held layer resolves and `Casting` wins. Identical to how the
local player's own cast pose shows, because `PlayerStanceBrain` returns `hasAction = false` when standing still.

**Watch for:** the watched body's catch-up glide can read as "walking" for a frame and drive locomotion over the
held pose. Cosmetic, converges in a fraction of a second; it is the pre-existing glide, not the stance.

**WHO PUSHES IT ON THE WATCHED BODY (changed 2026-09-07):** not the wind-up handler any more. Every held pose now
rides one channel reported from `StanceController` itself, and `NetPlayer.ShowTheirHeldStance` pushes whatever that
channel names — so `Casting` travels the same way `Guard` and `Sit` will, with no cast-specific code. The wind-up
message keeps only the two things that are the cast's own: the bar, and where the ring on the ground goes.

The wind-up's start/length/end/aim reach watchers as `CastWindupStarted` / `CastWindupEnded` — see
[[chat-crosses-the-wire]] for the same shape used by chat.
