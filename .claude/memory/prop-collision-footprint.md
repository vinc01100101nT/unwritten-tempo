# "Collision bigger than the visible object" = the PLAYER capsule, NOT prop colliders

Diagnosed 2026-06-20. Symptom: player couldn't fit between two houses; felt like trees had a huge
collision radius. **Real cause: the player's `CharacterController` capsule was full-height (height 1,
radius 0.3), so its TOP collided with overhead prop geometry — overhanging roof eaves and tree leaves —
even when the feet had clear floor.** It was never the props.

**Props are correct as-is: a non-convex `MeshCollider` using the EXACT visible mesh.** That's what gives
the good behavior the dev liked — stepping on the front-stairs actually climbs them, tree roots are
walkable bumps. DON'T replace prop mesh colliders with assumed box/capsule "footprint" primitives — the
dev explicitly rejected that ("fit the visible sprite, don't ASSUME the shape"). (An earlier attempt,
`Editor/PropColliderBaker.cs`, did this and was reverted/deleted; `FbxPropExtractor` keeps exact-mesh
colliders + a name-based foliage skip for DETACHED leaf meshes.)

**The fix — shrink the player capsule** so its top clears overhead geometry while the bottom still
bumps/climbs ground-level walls/stairs/roots. Dev-tuned defaults (their exact numbers):
`radius 0.15, height 0.5, center (0, 0.1, -0.2), stepOffset 0.3, slopeLimit 45, skinWidth 0.08`.

Wired as a single source of truth in **`Player3DController`**:
- consts `Default*Capsule*` + serialized fields `capsule*` (tunable in Inspector, persist).
- `ApplyCapsule()` is called in `Awake()` → enforces the capsule on EVERY player (even one built before
  this) and re-applies across map loads, so no rebuild needed.
- `static ApplyDefaultCapsule(CharacterController)` is what the three player builders call
  (`Build25DSpike`, `NinjaGreenPlayer`, `ScaryPlayer`) so a freshly-built player matches in edit mode too.

⚠️ **Former stomper (fixed 2026-06-20): `ApplyScaryGrounding()` in `Player3DController.Scary.cs`.** It is
a HIDDEN second writer to the capsule — it runs from `BuildScary()` in `Awake()` AFTER `ApplyCapsule()`, so
for the Sscary player (the active art path) it silently overwrote the tuned capsule with hardcoded
`height 1, radius 0.3`. That's why tuned defaults appeared to "not take effect" (tall capsule on screen).
Now it calls `ApplyCapsule()` first and only re-seats `center.y` for foot-grounding, preserving the tuned
x/z. RULE: grounding code may move the capsule center, NEVER redefine radius/height/step/slope/skin — those
stay owned by `ApplyCapsule`. If the capsule looks wrong again, suspect a second writer like this one.

To re-tune later: change the Inspector `capsule*` fields on the player (or the `Default*` consts). Capsule
dims are RAW component values (pre-scale); `PersistentPlayer3D.MoveTo` re-grounds from
`(center.y - height/2)*scale`. Visualize it: select Player → Scene view → green wireframe capsule.
NOTE: mob capsules (`Build25DSpike` line ~430, PathAgent3D) were left alone — only the player was changed.
