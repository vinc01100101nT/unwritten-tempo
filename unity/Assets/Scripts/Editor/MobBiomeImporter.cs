using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click bulk creator: turns every Ninja Adventure sprite folder under <c>Actor/Monster</c> (mobs) and
/// <c>Actor/Boss</c> (bosses) into an editable <see cref="MonsterDef"/> asset in <c>Resources/Monsters</c>,
/// pre-tagged with a <see cref="Biome"/> + a difficulty LEVEL so the map generator can spread them by
/// biome (see <see cref="MonsterCatalog.WeightedPick(Biome,int,int,bool)"/>).
///
/// Division (functionality-first — the actual biome/level choice is ARBITRARY for now, re-tag freely later):
/// <list type="bullet">
/// <item>Mobs: 5 biomes × 4 level bands × <b>3</b> = 60 placed evenly into the cells; any remainder (the 6 of 66)
///   become biome-agnostic <b>commons</b> (eligible in every biome's low level band — the universal trash layer).</item>
/// <item>Bosses: one per (biome, level band) cell in folder order, made <see cref="CreatureKind.Elite"/> — 18 today,
///   because the two split-part dragon folders have no art a creature can be built from.</item>
/// </list>
/// Idempotent: an asset that already exists is left untouched, so hand edits + re-runs are safe.
///
/// A mob folder's <c>SpriteSheet.png</c> renders directly as a grid; a boss folder has one picture per animation
/// (Idle.png, Walk.png…), which RigBuilder25D.TryStripArt resolves from the folder linked in
/// <see cref="MonsterDef.artFolder"/>. A folder with neither is skipped (see RigBuilder25D.HasCreatureArt).
/// Boss art is a deferred pass.
/// </summary>
public static class MobBiomeImporter
{
    // The two folders this importer turns into creatures. A creature made here links to its own folder
    // (MonsterDef.artFolder), so nothing after this point looks art up by these paths.
    public const string MonsterArtDir = "Assets/1_Authored/Art/Billboards/Actor/Monster";
    public const string BossArtDir     = "Assets/1_Authored/Art/Billboards/Actor/Boss";
    const string ResourceDir    = "Assets/3_Data/Resources/Monsters";

    // The 5 biomes mob/boss cells cycle through (same enum the map ground uses).
    static readonly Biome[] Biomes = { Biome.Grassland, Biome.Snow, Biome.Desert, Biome.Volcanic, Biome.Corrupted };

    [MenuItem("Tools/unwritten/Mobs & NPCs/Import NinjaAdventure Monsters", priority = 1200)]
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

        // ── MOBS: fill 20 cells (5 biomes × 4 level bands) with 3 each; the remainder become biome-agnostic commons.
        int cellCount = Biomes.Length * 4;          // 20
        int slotted   = Mathf.Min(mobFolders.Count, cellCount * 3);   // up to 60 go into cells
        for (int i = 0; i < mobFolders.Count; i++)
        {
            bool common = i >= slotted;
            Biome biome; int band;
            if (common) { biome = Biome.None; band = 1; }
            else        { biome = Biomes[i % Biomes.Length]; band = (i / Biomes.Length) % 4 + 1; }

            if (CreateDef(mobFolders[i], isBoss: false, biome, band, common, sb)) made++; else skipped++;
        }

        // ── BOSSES: one per (biome, level band) cell.
        for (int j = 0; j < bossFolders.Count; j++)
        {
            Biome biome = Biomes[j % Biomes.Length];
            int band = Mathf.Clamp(j / Biomes.Length + 1, 1, 4);
            if (CreateDef(bossFolders[j], isBoss: true, biome, band, common: false, sb)) made++; else skipped++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        MonsterCatalog.Reload();

        Debug.Log($"[unwritten] Import NinjaAdventure Monsters: {made} created, {skipped} already existed.\n{sb}");
        EditorUtility.DisplayDialog("Import NinjaAdventure Monsters",
            $"{mobFolders.Count} mob folders + {bossFolders.Count} boss folders scanned.\n\n" +
            $"{made} MonsterDef asset(s) created in {ResourceDir}\n{skipped} already existed (left untouched).\n\n" +
            "Tagged with Biome + level — the map generator now spreads a map's mobs from its recipe Biome. " +
            "Re-tag any in the Inspector; re-running this never clobbers your edits.", "OK");
    }

    /// <summary>Subfolders of <paramref name="artDir"/> holding art a creature can be built from, sorted by name:
    /// a grid sheet (<c>SpriteSheet.png</c>) or an Idle
    /// strip (the bosses). Folders with neither — the split-part dragons — are skipped.</summary>
    static List<string> SpriteFolders(string artDir)
    {
        if (!Directory.Exists(artDir)) return new List<string>();
        return Directory.GetDirectories(artDir)
            .Where(HasUsableSheet)
            .Select(Path.GetFileName)
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();
    }

    static bool HasUsableSheet(string dir) => RigBuilder25D.HasCreatureArt(dir.Replace('\\', '/'));

    static bool CreateDef(string folder, bool isBoss, Biome biome, int band, bool common, StringBuilder log)
    {
        string path = $"{ResourceDir}/{Sanitize(folder)}.asset";
        if (AssetDatabase.LoadAssetAtPath<MonsterDef>(path) != null)
        { log.AppendLine($"  • {folder} — exists, skipped"); return false; }

        var def = ScriptableObject.CreateInstance<MonsterDef>();
        def.id = "mob." + Kebab(folder);
        def.displayName = folder;
        def.artFolder = AssetDatabase.AssetPathToGUID($"{(isBoss ? BossArtDir : MonsterArtDir)}/{folder}");
        def.faceset = AssetDatabase.LoadAssetAtPath<Texture2D>($"{(isBoss ? BossArtDir : MonsterArtDir)}/{folder}/Faceset.png");
        def.kind = isBoss ? CreatureKind.Elite : CreatureKind.Normal;
        def.biome = biome;

        ApplyStats(def, isBoss, band, common, biome);

        AssetDatabase.CreateAsset(def, path);
        string tag = isBoss ? $"BOSS {biome}/Lv{def.level}" : common ? $"COMMON Lv{def.level}" : $"{biome}/Lv{def.level}";
        log.AppendLine($"  • {def.id} [{tag}] → {path}");
        return true;
    }

    // Difficulty is LEVEL. The four old "tiers" become four LEVEL BANDS; everything that used to be hand-scaled
    // per tier (HP, ATK, DEF, FLEE) now falls out of the level + stat spread through UnitStatMath, exactly as it
    // does for the player. Starting numbers to tune, not gospel.
    static readonly int[] BandLevel     = { 1, 10, 20, 32 };   // normal mobs, by band
    static readonly int[] BossBandLevel = { 8, 18, 30, 45 };   // a boss is simply a much higher-level unit

    /// <summary>Author one mob: its level comes from the band, its primaries are the FULL point budget that level
    /// earns, spent in a role-appropriate spread. A boss differs by level and bulk, not by a special stat rule.</summary>
    static void ApplyStats(MonsterDef def, bool isBoss, int band, bool common, Biome biome)
    {
        def.element = BiomeElement(biome);
        def.tint = Color.white;
        def.touchRange = 0.9f;
        def.touchInterval = 1.0f;
        def.aggroRadius = 6f;
        def.leashRadius = 11f;
        def.stopDistance = 0.7f;
        def.wanderRadius = 3f;

        int b = Mathf.Clamp(band, 1, 4) - 1;

        if (isBoss)
        {
            def.level = BossBandLevel[b];
            // Bulk-first spread: heavy VIT/STR, enough AGI/DEX to stay accurate.
            var u = UnitStatMath.Allocate(def.level, wStr: 3f, wAgi: 1f, wVit: 4f, wInt: 1f, wDex: 2f, wLuk: 1f);
            WriteStats(def, u);
            def.weight = 0;   // never rolled by the field spawner; placed deliberately
            def.hardDefPercent = 20 + b * 3;          // armour-sourced, still authored
            def.hardMagicDefPercent = 12 + b * 2;
            def.goldMin = 250 * (b + 1);
            def.goldMax = 500 * (b + 1);
            def.moveSpeed = 1.7f;
            return;
        }

        def.level = common ? BandLevel[0] : BandLevel[b];
        // Balanced field-mob spread, tilted to STR/VIT so early mobs hit and survive believably.
        var n = UnitStatMath.Allocate(def.level, wStr: 3f, wAgi: 2f, wVit: 3f, wInt: 1f, wDex: 2f, wLuk: 1f);
        WriteStats(def, n);
        def.weight = common ? 4 : (b <= 1 ? 3 : b == 2 ? 2 : 1);
        def.hardDefPercent = b * 5;
        def.goldMin = 2 * (b + 1);
        def.goldMax = 6 * (b + 1);
        def.moveSpeed = 2.4f - b * 0.1f;
    }

    static void WriteStats(MonsterDef def, in UnitStatInputs u)
    {
        def.str = u.str; def.agi = u.agi; def.vit = u.vit;
        def.intel = u.intel; def.dex = u.dex; def.luk = u.luk;
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
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "Monster" : s.Trim();
    }
}
