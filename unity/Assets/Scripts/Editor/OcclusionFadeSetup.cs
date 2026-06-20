using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click: drop a <see cref="CameraOccluderFader"/> onto the existing follow camera so props between the
/// camera and the player fade out — without rebuilding the whole Systems scene. (New cameras built by
/// <see cref="Build25DSpike.BuildCamera"/> already include it.)
/// </summary>
public static class OcclusionFadeSetup
{
    [MenuItem("Tools/unwritten/Setup/Add Camera Occlusion Fade", priority = 28)]
    static void Add()
    {
        var follow = Object.FindObjectOfType<CameraFollow3D>();
        Camera cam = follow != null ? follow.GetComponent<Camera>() : Camera.main;
        if (cam == null) cam = Object.FindObjectOfType<Camera>();
        if (cam == null)
        { EditorUtility.DisplayDialog("Occlusion Fade",
            "No camera in the open scene(s). Open the scene that has the follow camera (Systems.unity) and re-run.", "OK"); return; }

        var fader = cam.GetComponent<CameraOccluderFader>();
        bool added = fader == null;
        if (added) fader = cam.gameObject.AddComponent<CameraOccluderFader>();   // Reset() sets a sane default mask
        if (fader.target == null && follow != null) fader.target = follow.target;

        EditorUtility.SetDirty(cam.gameObject);
        EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        EditorUtility.DisplayDialog("Occlusion Fade",
            $"{(added ? "Added" : "Found existing")} Camera Occlusion Fade on '{cam.name}'.\n\n" +
            "Press Play — props between the camera and the player now fade to semi-transparent.\n" +
            "Save the scene (Ctrl+S) to keep it.", "OK");
    }
}
