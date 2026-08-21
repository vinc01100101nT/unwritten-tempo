using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the built-in <see cref="SkillDef"/> assets under <c>Resources/Skills/</c> (so the game can load them by
/// id via <see cref="SkillCatalog"/>) and wires <see cref="SkillRunner"/>s onto the player for play-testing. This
/// is the PROOF of the data-driven skill pipeline: it authors Meteor entirely as data (a referenced VfxSequence +
/// tick bindings) — no hand-written skill class (the legacy Player3D* skill classes were retired).
/// Idempotent: re-running rebuilds the default bindings.
/// </summary>
public static class SkillDefTools
{
    const string Dir = "Assets/3_Data/Resources/Skills";

    [MenuItem("Tools/unwritten/Skills/Build Default Skills")]
    public static void BuildDefaults()
    {
        EnsureFolder();
        BuildMeteor();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        SkillCatalog.Reload();
        Debug.Log("[unwritten] Built default Skills in " + Dir);
    }

    static void BuildMeteor()
    {
        var def = LoadOrCreate("skill.meteor");
        def.id = "skill.meteor";
        def.displayName = "Meteor (data)";
        def.manaCost = 16;
        def.cooldown = 9f;
        def.aim = SkillAim.Ground;
        def.castRange = 11f;
        def.shortLabel = "Meteor*";
        def.keyLabel = "";
        def.slotOrder = 20;
        def.tint = new Color(1f, 0.55f, 0.3f);
        def.vfx = AssetDatabase.LoadAssetAtPath<VfxSequence>("Assets/3_Data/Resources/VfxSequences/meteor.asset");
        if (def.vfx == null)
            Debug.LogWarning("[unwritten] skill.meteor: meteor VfxSequence not found — run Tools ▸ unwritten ▸ Art ▸ Build Default Vfx Sequences first.");

        // Values mirror the meteor VfxSequence's previously hand-authored touch steps, so the data pipeline
        // reproduces the tuned meteor: fall = Fire ×8 @ r1.36 (one-shot land), after-burn = Fire ×0.51 @ r1.14 (×5).
        def.bindings = new List<SkillTickBinding>
        {
            // The falling ball's single land = the main AoE burst.
            new SkillTickBinding
            {
                tickId = "meteor_ball",
                targets = SkillTargets.Mobs, scope = SkillScope.Area, radius = 1.36f,
                effects = new List<SkillEffect> { new SkillEffect { kind = SkillEffectKind.Damage, damageType = DamageType.Magic, damageMult = new ScaledNumber(8f), element = Element.Fire } },
            },
            // The lingering fire (5 beats, 0.372s apart) = a small repeated tick — the multi-tick demonstration.
            new SkillTickBinding
            {
                tickId = "fire_pillar",
                targets = SkillTargets.Mobs, scope = SkillScope.Area, radius = 1.14f,
                effects = new List<SkillEffect> { new SkillEffect { kind = SkillEffectKind.Damage, damageType = DamageType.Magic, damageMult = new ScaledNumber(0.51f), element = Element.Fire } },
            },
        };
        EditorUtility.SetDirty(def);
    }

    [MenuItem("Tools/unwritten/Skills/Attach Data Skills To Player (Systems scene)")]
    public static void AttachToPlayer()
    {
        SkillCatalog.Reload();
        var host = ResolvePlayer();
        if (host == null) { Debug.LogWarning("[unwritten] No player found in the open scene (need a Player3DController)."); return; }

        var existing = new HashSet<SkillDef>();
        foreach (var r in host.GetComponents<SkillRunner>()) if (r.def != null) existing.Add(r.def);

        int added = 0;
        foreach (var def in SkillCatalog.All)
        {
            if (def == null || existing.Contains(def)) continue;
            var runner = host.AddComponent<SkillRunner>();
            runner.def = def;
            added++;
        }
        EditorUtility.SetDirty(host);
        Debug.Log($"[unwritten] Attached {added} data skill(s) to {host.name}. Re-bake the UI shell if the hotbar doesn't refresh.");
    }

    static GameObject ResolvePlayer()
    {
        var ctrl = Object.FindAnyObjectByType<Player3DController>();
        return ctrl != null ? ctrl.gameObject : null;
    }

    static SkillDef LoadOrCreate(string id)
    {
        string path = $"{Dir}/{id}.asset";
        var def = AssetDatabase.LoadAssetAtPath<SkillDef>(path);
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<SkillDef>();
            AssetDatabase.CreateAsset(def, path);
        }
        return def;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/3_Data/Resources"))
            AssetDatabase.CreateFolder("Assets/3_Data", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/3_Data/Resources", "Skills");
    }
}
