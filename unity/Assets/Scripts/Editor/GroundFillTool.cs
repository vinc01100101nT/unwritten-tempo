using UnityEditor;
using UnityEngine;

/// <summary>
/// Flow A helper: tile the <b>selected</b> Kenney block across the whole ground in one click, so you
/// don't hand-place hundreds of grass tiles. Measures the selected block's XZ footprint, then fills
/// the "Ground" plane's area with copies snapped to that footprint, tops aligned to y=0.
///
/// Perf (hard project rule — keep the 2.5D game smooth): the tiles are made <b>visual-only</b>
/// (colliders stripped — the flat Ground plane keeps ONE collider for walking) and flagged
/// <b>static</b> so Unity batches them into few draw calls. Undoable (Ctrl/Cmd+Z).
/// </summary>
public static class GroundFillTool
{
    [MenuItem("Tools/unwritten/Fill Ground with Selected Tile", priority = 6)]
    static void Fill()
    {
        var sel = Selection.activeGameObject;
        if (sel == null || sel.scene.IsValid() == false)
        { Warn("Select the placed grass tile in the Scene/Hierarchy first, then re-run."); return; }

        var rends = sel.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { Warn("The selected object has no Renderer to measure/tile."); return; }

        // Footprint + top of the selected tile (world space).
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float footX = Mathf.Max(0.01f, b.size.x), footZ = Mathf.Max(0.01f, b.size.z);
        Vector3 pivotOff = sel.transform.position - b.center;   // pivot vs bbox center (XZ)
        float topToPivotY = sel.transform.position.y - b.max.y;  // keep tops flush at y=0

        // Source to clone (prefer the prefab so instances keep their link).
        var source = PrefabUtility.GetCorrespondingObjectFromSource(sel) as GameObject;

        // Area to cover = the Ground plane's footprint (fallback 40x40 at origin).
        var ground = GameObject.Find("Ground");
        Bounds area;
        if (ground != null && ground.TryGetComponent<Renderer>(out var gr)) area = gr.bounds;
        else area = new Bounds(Vector3.zero, new Vector3(40f, 1f, 40f));

        int nx = Mathf.Max(1, Mathf.RoundToInt(area.size.x / footX));
        int nz = Mathf.Max(1, Mathf.RoundToInt(area.size.z / footZ));
        int total = nx * nz;
        if (total > 2500 && !EditorUtility.DisplayDialog("Fill Ground",
                $"This will place {total} tiles ({nx}×{nz}). That's a lot — consider a larger block " +
                "(e.g. block-grass-large) or a smaller ground. Continue anyway?", "Fill", "Cancel"))
            return;

        var root = new GameObject("GrassFloor");
        Undo.RegisterCreatedObjectUndo(root, "Fill Ground");

        float startX = area.center.x - (nx - 1) * footX * 0.5f;
        float startZ = area.center.z - (nz - 1) * footZ * 0.5f;

        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                var go = source != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(source)
                    : Object.Instantiate(sel);
                go.transform.SetParent(root.transform, true);
                Vector3 cell = new Vector3(startX + ix * footX, 0f, startZ + iz * footZ);
                go.transform.position = new Vector3(cell.x + pivotOff.x, topToPivotY, cell.z + pivotOff.z);
                go.transform.rotation = Quaternion.identity;

                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
            }

        // The flat plane is now the (hidden) collision floor — hide its green so only grass shows.
        if (ground != null && ground.TryGetComponent<MeshRenderer>(out var gmr)) gmr.enabled = false;

        Object.DestroyImmediate(sel);          // remove the single hand-placed tile; the grid replaces it
        Selection.activeGameObject = root;
        EditorUtility.SetDirty(root);
        Debug.Log($"[unwritten] Filled ground with {total} tiles ({nx}×{nz}) under 'GrassFloor' " +
                  "(visual-only + static; Ground plane keeps the collider).");
    }

    static void Warn(string msg)
    {
        EditorUtility.DisplayDialog("Fill Ground", msg, "OK");
        Debug.LogWarning("[unwritten] Fill Ground: " + msg);
    }
}
