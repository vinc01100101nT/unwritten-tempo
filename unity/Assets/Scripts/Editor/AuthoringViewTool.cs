using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click reset of the <b>Scene view</b> camera to a known-good 2.5D authoring angle: a high
/// 3/4 view looking DOWN at the origin, roughly matching the in-game camera. Handy when scene
/// navigation flips you under the ground (you end up seeing the brown underside of grass blocks).
/// Pure editor-camera move — changes nothing in the scene.
/// </summary>
public static class AuthoringViewTool
{
    [MenuItem("Tools/unwritten/Authoring/Reset 2.5D Authoring View", priority = 401)]
    static void ResetView()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) { Debug.LogWarning("[unwritten] No active Scene view to reset."); return; }

        sv.orthographic = false;
        sv.pivot = Vector3.zero;                       // look at the origin (where the stage is built)
        sv.rotation = Quaternion.Euler(50f, 45f, 0f);  // pitched 50° DOWN, 45° around — RO-style 3/4
        sv.size = 12f;                                 // zoom: ~stage-sized
        sv.Repaint();
    }
}
