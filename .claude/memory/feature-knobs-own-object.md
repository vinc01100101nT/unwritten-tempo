# Feature authoring knobs live on their OWN object/asset — never piled onto a shared host

## Preference (user, emphatic)
When a feature exposes tunable knobs (Inspector fields), give them a DEDICATED GameObject named for the
feature. Do NOT dump the components onto an existing crowded host (e.g. the `Sun`, which already carries the
whole weather/day-night stack: DayNightLight, CloudShadows, SkyboxSky, Wind, Rain, WetGround, PlanarReflection,
Snow…). Mixing a new feature's knobs into that pile is "bullshit confusing" — the user can't find them.

## Rule
- One feature → one clearly-named object (or asset). Example: water reflection + ripple knobs live on a
  **`WaterFX`** GameObject in `Systems.unity`, NOT on `Sun`. Look knobs stay on the material (`Water.mat`).
- Keep unrelated knob groups SEPARATE even if they're both "global FX" — discoverability beats tidiness-by-proximity.
- Applies to the scene builder too: `SystemsSceneBuilder` creates `WaterFX` as its own object, not `sun.AddComponent`.

## Why it matters here
Singletons resolve by `FindFirstObjectByType`, so placement is free of runtime cost — there's no excuse to
co-locate. Put each feature where a human will look for it.
