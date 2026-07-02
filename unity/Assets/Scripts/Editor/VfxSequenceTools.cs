using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the built-in <see cref="VfxSequence"/> assets under <c>Resources/VfxSequences/</c> so skills that
/// load a sequence by name (e.g. <see cref="Player3DMeteor"/>) work out of the box. Idempotent: re-running
/// rebuilds the default step list. Hand-tuning on the asset will be overwritten — tune skills' OWN copies, or
/// edit here if a new default is wanted.
/// </summary>
public static class VfxSequenceTools
{
    const string Dir = "Assets/Resources/VfxSequences";

    [MenuItem("Tools/unwritten/Art/Build Default Vfx Sequences")]
    public static void BuildDefaults()
    {
        EnsureFolder();
        BuildMeteor();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[unwritten] Built default Vfx sequences in " + Dir);
    }

    static void BuildMeteor()
    {
        var seq = LoadOrCreate("meteor");
        seq.steps.Clear();

        // 1) The falling meteor — a carrier that drops from 9 units up onto the impact point and raises "impact"
        //    on arrival (fallSpeed 16, ~0.56s fall + 1s safety). Scatter reads the salvo as a shower. Exposes the
        //    "meteor_ball" TICK (one-shot on land) that a SkillDef binds the impact damage onto. Keeps the "impact"
        //    emit-event too, so the legacy Player3DMeteor code path still fires.
        seq.steps.Add(new VfxStep
        {
            note = "fall",
            schedule = VfxSchedule.AtTime, delay = 0f,
            kind = VfxStepKind.Carrier, anchor = VfxAnchor.Target,
            effectId = "fire.magic-pro-orange", scale = 1f,
            carrierFrom = new Vector3(0f, 9f, 0f), carrierScatter = 0.6f,
            carrierSpeed = 16f, carrierArriveRadius = 0.4f, carrierMaxLife = 9f / 16f + 1f,
            emitEventOnEnd = "impact",
            tickId = "meteor_ball", tickCount = 1, tickInterval = 0f,
        });

        // 2) Ground explosion — fires ON the "impact" event (i.e. when the meteor lands). Exposes the "fire_pillar"
        //    TICK: 5 lingering damage beats at the impact point (0.35s apart) — the multi-tick example. The visual
        //    is one burst; the gameplay cadence is owned here (option #1), the SkillDef says what each beat does.
        seq.steps.Add(new VfxStep
        {
            note = "explosion",
            schedule = VfxSchedule.OnEvent, startEvent = "impact",
            kind = VfxStepKind.PlayAt, anchor = VfxAnchor.Target,
            effectId = "aoe.ground-explosion", scale = 1f,
            tickId = "fire_pillar", tickCount = 5, tickInterval = 0.35f,
        });

        // 3) Screen-shake — also on impact.
        seq.steps.Add(new VfxStep
        {
            note = "shake",
            schedule = VfxSchedule.OnEvent, startEvent = "impact",
            kind = VfxStepKind.Shake, shakeStrength = 0.3f, shakeDuration = 0.18f,
        });

        EditorUtility.SetDirty(seq);
    }

    static VfxSequence LoadOrCreate(string name)
    {
        string path = $"{Dir}/{name}.asset";
        var seq = AssetDatabase.LoadAssetAtPath<VfxSequence>(path);
        if (seq == null)
        {
            seq = ScriptableObject.CreateInstance<VfxSequence>();
            AssetDatabase.CreateAsset(seq, path);
        }
        return seq;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/Resources", "VfxSequences");
    }
}
