using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the ambient-life layer: drops an <see cref="AmbientLife"/> director into the open scene,
/// wires it to the project's <see cref="CharacterRoster"/>, and points its ground mask at the Ground layer. After
/// running it, just press Play — wandering townsfolk (drawn from the baked characters) appear and chatter.
///
/// Idempotent: re-running re-uses the existing director (re-wires the roster) instead of stacking duplicates.
/// </summary>
public static class AmbientLifeTools
{
    [MenuItem("Tools/unwritten/World/Add Ambient Townsfolk")]
    public static void AddAmbientTownsfolk()
    {
        var existing = Object.FindFirstObjectByType<AmbientLife>();
        var al = existing != null ? existing : new GameObject("AmbientLife").AddComponent<AmbientLife>();

        var roster = FindRoster();
        if (roster != null) al.roster = roster;
        else EditorUtility.DisplayDialog("Ambient Townsfolk",
            "No CharacterRoster asset found. Bake characters first (Tools ▸ unwritten ▸ Bake All Quaternius " +
            "Characters), then run this again.\n\nThe director was still added — assign its Roster by hand.", "OK");

        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) al.groundMask = 1 << ground;

        if (existing == null)
            al.transform.position = SceneCenter(ground);

        EditorUtility.SetDirty(al);
        EditorSceneManager.MarkSceneDirty(al.gameObject.scene);
        Selection.activeGameObject = al.gameObject;
        EditorGUIUtility.PingObject(al.gameObject);

        Debug.Log($"[unwritten] Ambient townsfolk ready — {(roster != null ? roster.characters.Count : 0)} characters " +
                  $"available, spawning {al.count} at Play. Tweak Count / Area Radius on the AmbientLife object.");
    }

    static CharacterRoster FindRoster()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:CharacterRoster"))
        {
            var r = AssetDatabase.LoadAssetAtPath<CharacterRoster>(AssetDatabase.GUIDToAssetPath(guid));
            if (r != null && r.characters != null && r.characters.Count > 0) return r;
        }
        // fall back to the first one even if empty, so the field is at least populated
        var any = AssetDatabase.FindAssets("t:CharacterRoster");
        return any.Length > 0 ? AssetDatabase.LoadAssetAtPath<CharacterRoster>(AssetDatabase.GUIDToAssetPath(any[0])) : null;
    }

    // Centre on the map: average of any SpawnPoints, else the world origin — ground-snapped so the director's
    // fallback height is sane.
    static Vector3 SceneCenter(int groundLayer)
    {
        Vector3 c = Vector3.zero;
        var spawns = Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
        if (spawns != null && spawns.Length > 0)
        {
            c = Vector3.zero;
            foreach (var s in spawns) c += s.transform.position;
            c /= spawns.Length;
        }

        int mask = groundLayer >= 0 ? (1 << groundLayer) : Physics.DefaultRaycastLayers;
        if (Physics.Raycast(c + Vector3.up * 50f, Vector3.down, out var hit, 100f, mask, QueryTriggerInteraction.Ignore))
            c.y = hit.point.y;
        return c;
    }
}
