using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// KEEPS THE WIKI'S DATABASE PAGES CURRENT — the one place that decides when they are rewritten, so no page can be
/// left behind by a change nobody wired it to.
///
/// <para><b>Every change re-runs every export.</b> The pages lean on each other's sources: a creature's drops decide the
/// Materials page, a skill's publishing decides the trademark table, an item's numbers decide a creature's attack. So
/// rather than map which change feeds which page — a map that goes stale the first time a new link appears — any
/// change to any source rewrites all of them. Each export writes only when its bytes moved, and the whole pass takes
/// well under a second.</para>
///
/// <para><b>What counts as a change:</b> a creature, item, skill, the spawn list, the world map, the weapon colours
/// (saved, imported, moved or deleted); every recompile, because the element chart, the formulas and the wording all
/// live in code. A change made during Play is held until Play ends. Map previews are the exception: rendering them
/// costs seconds, so they re-render one map at a time when that map's scene is saved (<see cref="WorldMapExport"/>).</para>
///
/// <para>Everything written lives in the wiki folder (and the skill export's own files), outside the project's
/// assets, so nothing Unity saves can change because of it.</para>
/// </summary>
[InitializeOnLoad]
public static class WikiExport
{
    static bool queued, heldForPlay;

    static WikiExport()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && heldForPlay) { heldForPlay = false; Request(); }
        };
        EditorApplication.update += Tick;
        // A recompile is itself a change: the element chart, every formula and every sentence live in code.
        Request();
    }

    [MenuItem("Tools/unwritten/Wiki/Export Database Pages")]
    public static void ExportMenu()
    {
        RunAll();
        Debug.Log("[Wiki] Rewrote the Database pages' data from the game.");
    }

    /// <summary>Ask for one pass on the editor's next update. Any number of asks before it runs are one pass.</summary>
    public static void Request()
    {
        queued = true;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    // THE EDITOR'S UPDATE, not delayCall: measured 2026-09-26, an editor sitting in the background kept running its
    // update while its delayCall queue sat untouched for minutes, so a change saved by a tool (or pulled from git)
    // never reached the wiki until somebody clicked into Unity.
    static void Tick()
    {
        if (!queued) return;
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { heldForPlay = true; return; }
        RunAll();
    }

    /// <summary>Rewrite every Database page's data now, from what is on disk this moment.</summary>
    public static void RunAll()
    {
        // Forget every cached list first, so an item, creature or skill added since the last pass is seen.
        MonsterCatalog.Reload();
        SkillCatalog.Reload();
        ItemCatalog.Reload();
        MaterialCatalog.Forget();

        SkillExport.Write();
        MobExport.Write();
        ItemExport.Write();
        ElementExport.Write();
    }

    /// <summary>A creature, item, skill, the spawn list, the world map or the weapon colours was saved, imported,
    /// moved or deleted.</summary>
    class Watcher : AssetPostprocessor
    {
        static readonly string[] Folders =
        {
            "Assets/3_Data/Resources/Monsters/",
            "Assets/3_Data/Resources/Items/",
            "Assets/3_Data/Resources/Skills/",
            "Assets/3_Data/Resources/WorldMap/",
        };

        static readonly string[] Files =
        {
            "Assets/3_Data/Resources/WorldSpawns.asset",
            "Assets/3_Data/Resources/WorldAtlas.asset",
            "Assets/3_Data/Resources/WeaponColors.asset",
        };

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (Touches(imported) || Touches(deleted) || Touches(moved) || Touches(movedFrom)) Request();
        }

        static bool Touches(string[] paths)
        {
            if (paths == null) return false;
            foreach (var path in paths)
            {
                if (path == null || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (Array.IndexOf(Files, path) >= 0) return true;
                foreach (var folder in Folders)
                    if (path.StartsWith(folder, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
