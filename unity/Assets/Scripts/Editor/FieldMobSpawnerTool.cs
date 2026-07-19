using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drops a <see cref="FieldMobSpawner"/> into the open scene — an RO-style, data-driven, pooled mob spawner
/// (keeps a population alive, respawns random catalog mobs on a timer). Placed at the Scene-view focus (or the
/// player, or the origin) and selected so you can position it and tune its radius/population/biome in the
/// Inspector. Contrast with the map generator's BAKED mobs (fixed, recycled in place) — use this to A/B the
/// pooled spawner against them.
/// </summary>
public static class FieldMobSpawnerTool
{
    [MenuItem("Tools/unwritten/Mobs & NPCs/Add Field Mob Spawner", priority = 313)]
    static void Add()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Field Mob Spawner", "Exit Play mode first, then re-run.", "OK"); return; }

        var go = new GameObject("FieldMobSpawner");
        go.AddComponent<FieldMobSpawner>();
        go.transform.position = ChooseSpot();
        Undo.RegisterCreatedObjectUndo(go, "Add Field Mob Spawner");

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorUtility.DisplayDialog("Field Mob Spawner — added",
            "Placed a 'FieldMobSpawner'. Move it where you want the pack to live, then set its " +
            "Population / Radius / Biome / Tier in the Inspector.\n\n" +
            "Press Play to see it keep a random mob population alive (respawning from the pool). Bake Mob " +
            "Prefabs first so it has Addressable mobs to spawn.", "OK");
    }

    // Scene-view pivot if there's a Scene view, else the player, else origin — snapped to the Ground when we can.
    static Vector3 ChooseSpot()
    {
        Vector3 guess = Vector3.zero;
        var sv = SceneView.lastActiveSceneView;
        if (sv != null) guess = sv.pivot;
        else { var pl = Object.FindFirstObjectByType<Player3DController>(); if (pl != null) guess = pl.transform.position; }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
        {
            Physics.SyncTransforms();
            if (Physics.Raycast(new Vector3(guess.x, 200f, guess.z), Vector3.down, out var hit, 400f,
                                1 << groundLayer, QueryTriggerInteraction.Ignore))
                return hit.point;
        }
        guess.y = 0f;
        return guess;
    }
}
