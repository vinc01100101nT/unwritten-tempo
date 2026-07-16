using System.Text;
using UnityEditor;
using UnityEngine;

// QUICKFIX 2026-06-27 — one-shot: materialize the hardcoded MobRoster seed list into editable MonsterDef
// assets in Resources/Monsters. Delete after the assets are confirmed the source of truth and MobRoster is removed.
public static class MobRosterMigration
{
    const string ResourceDir = "Assets/3_Data/Resources/Monsters";

    [MenuItem("Tools/unwritten/Quick Fixes/Migrate MobRoster to Assets", priority = 2000)]
    static void Migrate()
    {
        EnsureFolder(ResourceDir);

        var sb = new StringBuilder();
        int made = 0, skipped = 0;

        foreach (var arc in MobRoster.All) { if (Create(arc, false, sb)) made++; else skipped++; }
        if (Create(MobRoster.Boss, true, sb)) made++; else skipped++;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        MonsterCatalog.Reload();

        Debug.Log($"[unwritten] MobRoster → Assets: {made} created, {skipped} already existed.\n{sb}");
        EditorUtility.DisplayDialog("Migrate MobRoster → Assets",
            $"{made} MonsterDef asset(s) created in {ResourceDir}\n{skipped} already existed (left untouched).\n\n" +
            "These are now the source of truth — edit them in the Inspector. MonsterCatalog reads them by id.", "OK");
    }

    static bool Create(in MonsterArchetype arc, bool isBoss, StringBuilder log)
    {
        string id = "mob." + Kebab(arc.name);
        string path = $"{ResourceDir}/{Sanitize(arc.name)}.asset";
        if (AssetDatabase.LoadAssetAtPath<MonsterDef>(path) != null) { log.AppendLine($"  • {id} — exists, skipped"); return false; }

        var def = ScriptableObject.CreateInstance<MonsterDef>();
        def.id = id;
        def.displayName = arc.name;
        def.isBoss = isBoss;
        def.sheet = arc.sheet;
        def.weight = arc.weight;
        def.tint = arc.tint;
        def.scale = arc.scale;
        def.flash = arc.flash;
        def.maxHealth = arc.maxHealth;
        def.defense = arc.defense;
        def.magicDefense = arc.magicDefense;
        def.flee = arc.flee;
        def.hardDefPercent = arc.hardDefPercent;
        def.hardMagicDefPercent = arc.hardMagicDefPercent;
        def.luk = arc.luk;
        def.attackType = arc.attackType;
        def.attack = arc.attack;
        def.hit = arc.hit;
        def.xpReward = arc.xpReward;
        def.damageSpread = arc.damageSpread;
        def.touchRange = arc.touchRange;
        def.touchInterval = arc.touchInterval;
        def.moveSpeed = arc.moveSpeed;
        def.aggroRadius = arc.aggroRadius;
        def.leashRadius = arc.leashRadius;
        def.stopDistance = arc.stopDistance;
        def.wanderRadius = arc.wanderRadius;
        def.preferredRange = arc.preferredRange;
        def.fleeHpFraction = arc.fleeHpFraction;
        def.element = arc.element;
        def.attackElement = arc.attackElement;
        def.onHitAilment = arc.onHitAilment;
        def.onHitAilmentChance = arc.onHitAilmentChance;
        def.onHitAilmentSeconds = arc.onHitAilmentSeconds;
        def.onHitAilmentMagnitude = arc.onHitAilmentMagnitude;
        def.goldMin = arc.goldMin;
        def.goldMax = arc.goldMax;
        def.itemDropNames = arc.itemDropNames ?? System.Array.Empty<string>();
        def.itemDropChances = arc.itemDropChances ?? System.Array.Empty<float>();

        AssetDatabase.CreateAsset(def, path);
        log.AppendLine($"  • {id}{(isBoss ? " (boss)" : "")} → {path}");
        return true;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        if (!AssetDatabase.IsValidFolder("Assets/3_Data/Resources")) AssetDatabase.CreateFolder("Assets/3_Data", "Resources");
        AssetDatabase.CreateFolder("Assets/3_Data/Resources", "Monsters");
    }

    static string Kebab(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unnamed";
        var sb = new StringBuilder(s.Length);
        bool lastDash = false;
        foreach (char c in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); lastDash = false; }
            else if (!lastDash) { sb.Append('-'); lastDash = true; }
        }
        return sb.ToString().Trim('-');
    }

    static string Sanitize(string s)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "Monster" : s.Trim();
    }
}
