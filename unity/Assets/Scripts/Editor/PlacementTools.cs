using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Quick hand-authoring commands that act on the CURRENT scene selection — the keyboard-friendly companions
/// to the <see cref="PropPalette"/>. All bounds-aware via <see cref="PropPlacement"/>, all Undo-able.
/// Select one or more objects you dragged/placed, then run a command (or use the shortcut).
/// </summary>
public static class PlacementTools
{
    // Re-seat selection on the ground after nudging it around.
    [MenuItem("Tools/unwritten/Authoring/Drop to Ground", priority = 410)]
    static void DropToGround() => Apply("Drop to Ground", go => PropPlacement.DropToGround(go, PropPlacement.GroundMask()));

    [MenuItem("Tools/unwritten/Authoring/Scale ×0.3 (pack scale)", priority = 412)]
    static void Scale03() => Apply("Scale ×0.3", go => go.transform.localScale = Vector3.one * 0.3f);

    [MenuItem("Tools/unwritten/Authoring/Snap to Grid (0.5)", priority = 413)]
    static void SnapGrid() => Apply("Snap to Grid", go => go.transform.position = PropPlacement.SnapToGrid(go.transform.position, 0.5f));

    // Rotate selection 90° about Y (face it a different way).
    [MenuItem("Tools/unwritten/Authoring/Rotate 90°", priority = 414)]
    static void Rotate90() => Apply("Rotate 90°", go => go.transform.Rotate(0f, 90f, 0f, Space.World));

    // Strip colliders off the SELECTED props (any prop) so you can walk through them.
    [MenuItem("Tools/unwritten/Authoring/Remove Colliders (selected)", priority = 415)]
    static void RemoveCollidersSelected()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        { EditorUtility.DisplayDialog("Authoring", "Select one or more props in the scene first.", "OK"); return; }

        int n = 0;
        foreach (var g in sel)
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) { Undo.DestroyObjectImmediate(c); n++; }

        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Authoring", $"Removed {n} collider(s) from {sel.Length} selected object(s).", "OK");
    }

    // Scene-wide: strip colliders from ALL walk-through decor (grass, flowers, ferns, mushrooms…) at once.
    [MenuItem("Tools/unwritten/Authoring/Strip Decor Colliders (grass, flowers…)", priority = 416)]
    static void StripDecorColliders()
    {
        var scene = SceneManager.GetActiveScene();
        int groundLayer = PropPlacement.GroundLayer;
        int props = 0, cols = 0;

        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (go.layer == groundLayer) continue;                 // never strip the walkable floor
                if (!PropPlacement.IsWalkThroughDecor(go.name)) continue;
                var here = go.GetComponents<Collider>();
                if (here.Length == 0) continue;
                foreach (var c in here) { Undo.DestroyObjectImmediate(c); cols++; }
                props++;
            }

        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.DisplayDialog("Strip Decor Colliders",
            $"Removed {cols} collider(s) from {props} decor prop(s) (grass, flowers, ferns, mushrooms…).\n" +
            "You can now walk through them.", "OK");
    }

    static void Apply(string label, Action<GameObject> act)
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        { EditorUtility.DisplayDialog("Authoring", "Select one or more objects in the scene first.", "OK"); return; }

        var transforms = Array.ConvertAll(sel, g => (UnityEngine.Object)g.transform);
        Undo.RecordObjects(transforms, label);
        foreach (var g in sel) act(g);
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }
}
