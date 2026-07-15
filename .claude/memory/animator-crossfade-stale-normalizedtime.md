---
name: animator-crossfade-stale-normalizedtime
description: Reading GetCurrentAnimatorStateInfo.normalizedTime right after CrossFade returns the OUTGOING state — froze the jump on a run frame
metadata: 
  node_type: memory
  type: project
  originSessionId: 14deb0a3-ffc4-4371-a6dc-e380fb3af402
---

# Animator freeze-on-stale-frame: CrossFade + normalizedTime read same-frame

**Symptom:** running, press Space → the jump clip is stuck on a **paused Run frame** forever. Adjusting stance `priority` did NOT help (Jump *was* winning the resolve — the pose code was freezing the wrong state).

**Root cause (Character3DPose.PlayClipPhase):** on the first Jump frame we `CrossFadeInFixedTime(h,…)` and set `currentState = h`, return. **Next** frame `currentState == h`, so we read `anim.GetCurrentAnimatorStateInfo(0).normalizedTime` and freeze the animator (`anim.speed = 0`) when `t >= freezeN`. But the crossfade (crossFade=0.08s) is still in progress, so `GetCurrentAnimatorStateInfo(0)` returns the **OUTGOING** state (looping Run), whose `normalizedTime` has accumulated to a large value (e.g. 8.4) ≫ freezeN. → `speed=0` pauses the animator **mid-transition on a run frame**, and because speed=0 the transition never completes → stuck forever.

Two compounding traps: (1) a looping state's `normalizedTime` keeps growing past 1 across loops (it's not 0..1); (2) during a transition, "current" state info = the state you're leaving, not entering.

**Fix** (`unity/Assets/Scripts/Character3DPose.cs`, PlayClipPhase): before trusting normalizedTime, bail while transitioning / not yet on the target state — keep `speed=1`, don't freeze:
```csharp
var st = anim.GetCurrentAnimatorStateInfo(0);
if (anim.IsInTransition(0) || st.shortNameHash != h) { anim.speed = 1f; return; }
float t = st.normalizedTime;   // now safe — it's the jump state
```
`h = Animator.StringToHash(clipName)`; states are named after the clip (base layer root), so `st.shortNameHash == h` confirms the jump state is actually current.

**Gotchas / rule of thumb:** never read `GetCurrentAnimatorStateInfo(0)` the frame(s) after a `CrossFade`/`Play` — guard with `IsInTransition(0)` + `shortNameHash`. The 2D twin (`BakedBillboard8`) was immune: it resets `clock=0` on action change (no transition concept). Part of the 3-phase jump (crouchEnd/airborneFreeze) — see [[stance-system]], [[character-3d-visual-system]].
