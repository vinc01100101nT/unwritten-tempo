# Forcing Humanoid on Quaternius FBX silently drops all clips

**Symptom:** After running the 3D-character spike tool (`Build3DCharacterSpike`), the Quaternius FBX
(`Ninja_Female`, `Knight_Male`) showed **"Last import generated 1 error"** in the Inspector and ALL
embedded animation clips vanished — the AnimatorController builder then failed with "Clip 'Idle' not
found in <model>.fbx".

**Root cause:** the tool set `ModelImporter.animationType = Human` +
`avatarSetup = CreateFromThisModel`. The Quaternius Ultimate Animated skeleton does **not** auto-map to
a Unity Humanoid avatar — avatar creation fails, and a failed Humanoid retarget **drops the clips** from
the imported asset (they don't survive as playable clips). The FBX binary is untouched, so the takes
still exist; only the import output is broken.

**Fix (`unity/Assets/Scripts/Editor/Build3DCharacterSpike.cs` → `EnsureAnimations`):** KEEP the pack's
native **Generic** rig. All Quaternius models share one skeleton, so each plays its OWN embedded Generic
takes perfectly — which is all we needed. The method now force-restores Generic if a model was left
Humanoid, then re-derives `defaultClipAnimations`, sets loop flags on Idle/Walk/Run(+Carry), and assigns
the fresh set to `clipAnimations` (clobbers any stale entries). Humanoid is ONLY needed later to retarget
FOREIGN clips (Mixamo) onto Quat — not for the pack's own animations.

**Recovery when it happens:** the FBX `.meta` holds the broken import settings; the binary is fine. Just
`git checkout -- unity/Assets/Art/Quat/FBX/<model>.fbx.meta` and force-reimport (`refresh_unity
scope=all force`). Clips come back.

**Key facts / gotchas:**
- Quaternius clip sub-assets are named `CharacterArmature|Idle` (armature-name prefix). Look them up by
  the suffix after `|` (`CleanName()`), not the raw name — an exact "Idle" match misses.
- Embedded Quat take set (17): Idle, Walk, Run, Walk_Carry, Run_Carry, Jump, Roll, Punch, SwordSlash,
  Shoot_OneHanded, PickUp, SitDown, StandUp, `RecieveHit` (sic — misspelled in the FBX), Death, Defeat,
  Victory.
- The AnimatorController wires clips by **object reference**, so the `|` prefix doesn't matter there —
  only for dictionary-key lookups.

**⚠️ For the future Mixamo step:** Mixamo clips ARE Humanoid, so retargeting them onto Quat WILL need a
Humanoid avatar — but expect the auto-map to fail (as it did here). Plan on a hand-configured avatar or
"copy from other avatar", not the default `CreateFromThisModel`.
