# NightLight shadows: ranked lamps against a STATIONARY pivot (Character.Instance @ origin)

Fixed 2026-06-29. Cost a long debugging session because the symptom (no lamp shadow) sent me chasing
URP/shader/atlas theories when the real bug was a **wrong player position** in the lamp-shadow selection.

## Symptom
- Lamp/night point-light shadows looked broken: the lamp the player stood right next to often cast NO
  shadow; cranking `MaxShadowCasters` from 4→5→6 would "fix" specific lamps. Felt like a prioritization bug.
- THE TELL (don't miss this again): in the on-screen debug overlay, the per-lamp **`dist` values did not
  change as the player ran around**. Frozen distances ⇒ the pivot isn't moving.

## Root cause
`NightLight.SelectNearest()` ranked lamps by distance to **`Character.Instance.transform.position`**.
`Character.Instance` is a **stationary logical object sitting at (0,0,0)** — NOT the moving avatar. The
avatar that actually moves is **`Player3DController`** (GameObject "Player3D"), with the visual on
`BakedBillboard8` ("PlayerBakedVisual"). So every lamp was ranked from the origin → the same "nearest 4"
always won regardless of where the player walked, and the lamp you stood under was usually not in that set.

Measured live (Play): `Character.Instance pos=(0,0,0)`; `Player3DController on 'Player3D' pos=(-1.45,0.25,-0.41)`.

## Fix
`unity/Assets/Scripts/NightLight.cs` — added a cached `static Transform PlayerTransform()` that resolves the
MOVING player via `FindFirstObjectByType<Player3DController>()` once (re-resolves only if null/respawned), and
use it in `SelectNearest()` and the debug overlay instead of `Character.Instance`. Selection is plain
nearest-distance (`sqrMagnitude`), top-N into reused scratch buffers, `MaxShadowCasters` default 4.

## Gotchas / lessons
- **`Character.Instance` ≠ the moving player.** It's a stationary stats/logical root at origin. For anything
  spatial (distance, nearest, range checks) use `Player3DController`'s transform (or `BakedBillboard8`), not
  `Character.Instance`. Check other systems that may make the same mistake.
- **Debugging lesson:** when "nearest X to the player" misbehaves, FIRST verify the player position is
  actually updating (print it / watch it move) before theorizing about the ranking metric, URP, or shaders.
  A frozen distance readout = wrong/stationary source transform. One check, not six screenshots.
- Confirmed working along the way (so NOT the cause): URP asset (`UniversalRP.asset`) has additional-light
  shadows supported + Per-Pixel + atlas 4096; `GroundBlend.shader` correctly RECEIVES additional-light
  shadows via `Lib/AdditionalLights.hlsl` (3-arg `GetAdditionalLight` overload). Billboard casters work
  (sun shadows fine). The "influence ranking" `dist²/range²` experiment was a wrong turn — it favors
  wide-range house lamps over the near post lamp; reverted to pure distance.
- PC-only target now (no web/mobile) → 4 point-light shadow casters is comfortable; URP atlas bumped to 4096
  and per-object light limit to 8.
- Debug scaffolding left behind (off by default, behind flags): `NightLight.DebugOverlay` + OnGUI readout,
  `MinAdditionalShadow` in `AdditionalLights.hlsl`, `_LampShadowDbg` in `GroundBlend.shader`, and DevSettings
  ▸ Shadows toggles "Lamp shadow debug"/"Lamp shadow paint". Strip when no longer needed.
