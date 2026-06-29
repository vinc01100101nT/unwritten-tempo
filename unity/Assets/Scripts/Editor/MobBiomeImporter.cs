using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click bulk creator: turns every Ninja Adventure sprite folder under <c>Actor/Monster</c> (mobs) and
/// <c>Actor/Boss</c> (bosses) into an editable <see cref="MonsterDef"/> asset in <c>Resources/Monsters</c>,
/// pre-tagged with a <see cref="Biome"/> + difficulty <c>tier</c> so the map generator can spread them by
/// biome (see <see cref="MonsterCatalog.WeightedPick(Biome,int,int,bool)"/>).
///
/// Division (functionality-first — the actual biome/tier choice is ARBITRARY for now, re-tag freely later):
/// <list type="bullet">
/// <item>Mobs: 5 biomes × 4 tiers × <b>3</b> = 60 placed evenly into the cells; any remainder (the 6 of 66)
///   become biome-agnostic <b>commons</b> (eligible in every biome's low tier — the universal trash layer).</item>
/// <item>Bosses: 5 biomes × 4 tiers × <b>1</b> = 20, one per cell, flagged <see cref="MonsterDef.isBoss"/>.</item>
/// </list>
/// Idempotent: an asset that already exists is left untouched, so hand edits + re-runs are safe.
///
/// Sprites don't matter yet: a mob folder's <c>SpriteSheet.png</c> renders directly; boss folders have no
/// single sheet, so a boss <see cref="MonsterDef.sheet"/> stores its real folder name but rendering falls back
/// to the default monster sheet today (Build25DSpike.BuildMonster: <c>MonsterSheet(sheet) ?? MonsterSheet()</c>).
/// Boss art is a deferred pass.
/// </summary>
public static class MobBiomeImporter
{
    const string MonsterArtDir = "Assets/Art/NinjaAdventure/Actor/Monster";
    const string BossArtDir     = "Assets/Art/NinjaAdventure/Actor/Boss";
    const string ResourceDir    = "Assets/Resources/Monsters";

    // The 5 biomes mob/boss cells cycle through (same enum the map ground uses).
    static readonly Biome[] Biomes = { Biome.Grassland, Biome.Snow, Biome.Desert, Biome.Volcanic, Biome.Corrupted };

    [MenuItem("Tools/unwritten/Mobs/Import NinjaAdventure Monsters", priority = 1200)]
    static void Import()
    {
        EnsureFolder(ResourceDir);

        var mobFolders  = SpriteFolders(MonsterArtDir);
        var bossFolders = SpriteFolders(BossArtDir);

        if (mobFolders.Count == 0 && bossFolders.Count == 0)
        {
            EditorUtility.DisplayDialog("Import NinjaAdventure Monsters",
                $"No sprite folders found under\n{MonsterArtDir}\nor\n{BossArtDir}.", "OK");
            return;
        }

        var sb = new StringBuilder();
        int made = 0, skipped = 0;

        // ── MOBS: fill 20 cells (5 biomes × 4 tiers) with 3 each; the remainder become biome-agnostic commons.
        int cellCount = Biomes.Length * 4;          // 20
        int slotted   = Mathf.Min(mobFolders.Count, cellCount * 3);   // up to 60 go into cells
        for (int i = 0; i < mobFolders.Count; i++)
        {
            bool common = i >= slotted;
            Biome biome; int tier;
            if (common) { biome = Biome.Grassland; tier = 1; }
            else        { biome = Biomes[i % Biomes.Length]; tier = (i / Biomes.Length) % 4 + 1; }

            if (CreateDef(mobFolders[i], isBoss: false, biome, tier, common, sb)) made++; else skipped++;
        }

        // ── BOSSES: one per (biome, tier) cell.
        for (int j = 0; j < bossFolders.Count; j++)
        {
            Biome biome = Biomes[j % Biomes.Length];
            int tier = Mathf.Clamp(j / Biomes.Length + 1, 1, 4);
            if (CreateDef(bossFolders[j], isBoss: true, biome, tier, common: false, sb)) made++; else skipped++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        MonsterCatalog.Reload();

        Debug.Log($"[unwritten] Import NinjaAdventure Monsters: {made} created, {skipped} already existed.\n{sb}");
        EditorUtility.DisplayDialog("Import NinjaAdventure Monsters",
            $"{mobFolders.Count} mob folders + {bossFolders.Count} boss folders scanned.\n\n" +
            $"{made} MonsterDef asset(s) created in {ResourceDir}\n{skipped} already existed (left untouched).\n\n" +
            "Tagged with Biome + tier — the map generator now spreads a map's mobs from its recipe Biome. " +
            "Re-tag any in the Inspector; re-running this never clobbers your edits.", "OK");
    }

    /// <summary>Subfolders of <paramref name="artDir"/> that hold at least one usable sprite sheet, sorted by
    /// name. A "usable" sheet is any PNG that isn't a <c>Faceset</c> portrait or a <c>*Preview</c> gif/png —
    /// this catches both the <c>SpriteSheet.png</c> mobs and the ones whose single sheet is named after the
    /// folder (e.g. <c>Slime/Slime.png</c>), plus the split-file bosses.</summary>
    static List<string> SpriteFolders(string artDir)
    {
        if (!Directory.Exists(artDir)) return new List<string>();
        return Directory.GetDirectories(artDir)
            .Where(HasUsableSheet)
            .Select(Path.GetFileName)
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();
    }

    static bool HasUsableSheet(string dir)
    {
        foreach (var f in Directory.GetFiles(dir, "*.png"))
        {
            string fn = Path.GetFileNameWithoutExtension(f);
            if (fn.Equals("Faceset", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (fn.EndsWith("Preview", System.StringComparison.OrdinalIgnoreCase)) continue;
            return true;
        }
        return false;
    }

    static bool CreateDef(string folder, bool isBoss, Biome biome, int tier, bool common, StringBuilder log)
    {
        string path = $"{ResourceDir}/{Sanitize(folder)}.asset";
        if (AssetDatabase.LoadAssetAtPath<MonsterDef>(path) != null)
        { log.AppendLine($"  • {folder} — exists, skipped"); return false; }

        var def = ScriptableObject.CreateInstance<MonsterDef>();
        def.id = "mob." + Kebab(folder);
        def.displayName = folder;
        def.sheet = folder;
        def.isBoss = isBoss;
        def.biome = biome;
        def.tier = tier;
        def.biomeAgnostic = common;

        ApplyStats(def, isBoss, tier, common, biome);

        AssetDatabase.CreateAsset(def, path);
        string tag = isBoss ? $"BOSS {biome}/T{tier}" : common ? "COMMON" : $"{biome}/T{tier}";
        log.AppendLine($"  • {def.id} [{tag}] → {path}");
        return true;
    }

    /// <summary>Difficulty that actually ramps with tier (so a tier-4 zone bites). Arbitrary-but-sane starting
    /// numbers; balance later. Bosses get a wall of HP and never join the weighted field pick (weight 0).</summary>
    static void ApplyStats(MonsterDef def, bool isBoss, int tier, bool common, Biome biome)
    {
        def.element = BiomeElement(biome);
        def.attackElement = Element.Neutral;
        def.flash = Color.white;
        def.tint = Color.white;
        def.attackType = MonsterAI3D.AttackType.Physical;
        def.damageSpread = 0.25f;
        def.touchRange = 0.9f;
        def.touchInterval = 1.0f;
        def.aggroRadius = 6f;
        def.leashRadius = 11f;
        def.stopDistance = 0.7f;
        def.wanderRadius = 3f;

        if (isBoss)
        {
            def.weight = 0;   // never rolled by the field spawner; placed deliberately
            def.scale = 2.0f + tier * 0.15f;
            def.maxHealth = Mathf.RoundToInt(220f * Mathf.Pow(1.7f, tier - 1));
            def.defense = 4 + tier * 2;
            def.magicDefense = 2 + tier * 2;
            def.hardDefPercent = 20 + tier * 3;
            def.hardMagicDefPercent = 12 + tier * 2;
            def.luk = 8 + tier * 2;
            def.attack = 12 + tier * 4;
            def.hit = 12 + tier * 2;
            def.xpReward = 200 * tier;
            def.goldMin = 250 * tier;
            def.goldMax = 500 * tier;
            def.moveSpeed = 1.7f;
            return;
        }

        def.weight = common ? 4 : (tier <= 1 ? 3 : tier == 2 ? 3 : tier == 3 ? 2 : 1);
        def.scale = common ? 0.8f : 0.85f + tier * 0.07f;
        def.maxHealth = Mathf.RoundToInt(14f * Mathf.Pow(1.9f, tier - 1));
        def.defense = (tier - 1) * 2;
        def.magicDefense = (tier - 1);
        def.hardDefPercent = (tier - 1) * 5;
        def.luk = tier;
        def.attack = Mathf.RoundToInt(3f * Mathf.Pow(1.5f, tier - 1));
        def.hit = 8 + (tier - 1) * 3;
        def.xpReward = Mathf.RoundToInt(5f * Mathf.Pow(2f, tier - 1));
        def.goldMin = 2 * tier;
        def.goldMax = 6 * tier;
        def.moveSpeed = 2.4f - tier * 0.1f;
    }

    static Element BiomeElement(Biome b) => b switch
    {
        Biome.Snow      => Element.Water,
        Biome.Desert    => Element.Wind,
        Biome.Volcanic  => Element.Fire,
        Biome.Grassland => Element.Earth,
        _               => Element.Neutral,   // Corrupted
    };

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        AssetDatabase.CreateFolder("Assets/Resources", "Monsters");
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
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "Monster" : s.Trim();
    }
}
