using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot migration: converts EXISTING baked map scenes from scene-baked mobs to a runtime, pooled
/// <see cref="FieldMobSpawner"/> (what new generations now use). Works SCENE-driven, not recipe-driven, because
/// the World Creator emits many scenes (field_0…, dungeon_0…) from a few template recipes — there is no 1:1
/// recipe per scene. For each scene under <c>Assets/Scenes</c> that has a baked <c>Mobs</c> root, it:
///   1. reads the baked mobs (population = their count; each is named <c>Monster_&lt;def&gt;</c>, so the mob TYPE —
///      and thus its biome/tier from <see cref="MonsterCatalog"/> — is recoverable),
///   2. derives the spawn area from how far the baked mobs spread from the map centre,
///   3. deletes the baked <c>Mobs</c> root and drops one configured spawner (modal biome, observed tier band).
/// Hand-placed bosses live OUTSIDE the <c>Mobs</c> root (<c>RegionBuilder</c>) so they survive. Needs
/// <b>Bake Mob Prefabs</b> run first. Re-runnable.
/// </summary>
public static class MobSpawnerMigration
{
    const string ScenesDir = "Assets/Scenes";

    [MenuItem("Tools/unwritten/Mobs & NPCs/Convert Baked Mobs → Spawners (all maps)", priority = 314)]
    static void ConvertAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Convert Baked Mobs", "Exit Play mode first, then re-run.", "OK"); return; }

        var scenePaths = Directory.GetFiles(ScenesDir, "*.unity", SearchOption.TopDirectoryOnly);
        if (scenePaths.Length == 0)
        { EditorUtility.DisplayDialog("Convert Baked Mobs", $"No scenes found under {ScenesDir}.", "OK"); return; }

        if (!EditorUtility.DisplayDialog("Convert Baked Mobs → Spawners",
                $"This scans {scenePaths.Length} scene(s) and REWRITES any with baked mobs: it deletes the baked " +
                "'Mobs' root and replaces it with one runtime FieldMobSpawner (population + biome + tier recovered " +
                "from the baked mobs).\n\nSave any unsaved scene first (this switches scenes). Continue?",
                "Convert", "Cancel"))
            return;

        int changed = 0, skippedNoMobs = 0;
        var log = new List<string>();

        try
        {
            for (int i = 0; i < scenePaths.Length; i++)
            {
                string path = scenePaths[i].Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                if (name == "Systems") continue;   // the persistent HUD/player scene has no map mobs

                EditorUtility.DisplayProgressBar("Convert Baked Mobs → Spawners", name, (float)i / scenePaths.Length);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                GameObject mobsRoot = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == "Mobs") { mobsRoot = root; break; }
                if (mobsRoot == null) { skippedNoMobs++; continue; }

                var baked = mobsRoot.GetComponentsInChildren<MonsterAI3D>(true);
                if (baked.Length == 0) { Object.DestroyImmediate(mobsRoot); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); skippedNoMobs++; continue; }

                // Recover biome/tier from the baked mob TYPES (name = "Monster_<def>"), and the spawn radius from
                // how far they spread from the map centre (maps are centred at origin).
                var biomeVotes = new Dictionary<Biome, int>();
                int minTier = 4, maxTier = 1;
                float maxDist = 0f;
                foreach (var ai in baked)
                {
                    Vector3 p = ai.transform.position;
                    maxDist = Mathf.Max(maxDist, Mathf.Sqrt(p.x * p.x + p.z * p.z));

                    string n = ai.name.StartsWith("Monster_") ? ai.name.Substring("Monster_".Length) : ai.name;
                    var def = MonsterCatalog.Get(n);
                    if (def == null) continue;
                    if (def.biome != Biome.None) biomeVotes[def.biome] = biomeVotes.GetValueOrDefault(def.biome) + 1;
                    minTier = Mathf.Min(minTier, def.tier);
                    maxTier = Mathf.Max(maxTier, def.tier);
                }
                if (minTier > maxTier) { minTier = 1; maxTier = 4; }   // no types resolved → full band

                Biome biome = Biome.Grassland; int best = -1;
                foreach (var kv in biomeVotes) if (kv.Value > best) { best = kv.Value; biome = kv.Key; }

                int population = baked.Length;
                float radius = Mathf.Max(10f, maxDist * 1.1f);

                Object.DestroyImmediate(mobsRoot);
                var newRoot = new GameObject("Mobs");
                SceneManager.MoveGameObjectToScene(newRoot, scene);
                var s = newRoot.AddComponent<FieldMobSpawner>();
                s.population = population;
                s.radius = radius;
                s.biome = biome;
                s.minTier = minTier;
                s.maxTier = maxTier;
                s.includeCommons = true;

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                changed++;
                log.Add($"• {name}: {population} mobs → spawner (biome {biome}, tier {minTier}-{maxTier}, r={radius:0})");
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        Debug.Log($"[unwritten] Baked-mob → spawner migration: {changed} scene(s) converted, {skippedNoMobs} without map mobs.\n"
                  + string.Join("\n", log));
        EditorUtility.DisplayDialog("Convert Baked Mobs → Spawners",
            $"Done. {changed} scene(s) converted, {skippedNoMobs} had no map mobs.\n\n" +
            "Make sure Bake Mob Prefabs has been run so the spawners have Addressable mobs to spawn.", "OK");
    }
}
