using UnityEditor;
using UnityEngine;

/// <summary>
/// Destroys leftover runtime pool hosts when Play mode ends. Every runtime-spawned object in this project
/// is tagged DontSaveInEditor so it can never bake into a scene — but that same flag makes Unity SKIP the
/// object in the automatic teardown when exiting Play mode, so pools (DamagePopupSystem, VfxSystem,
/// CastBarSystem…) linger in the edit-mode hierarchy as clutter. This sweeps them the moment the editor
/// returns to edit mode; the pools lazily rebuild on the next Play.
/// Editor-window previews are untouched: those use HideAndDontSave (hidden), which the sweep skips.
/// </summary>
[InitializeOnLoad]
public static class RuntimeLeftoverSweeper
{
    static RuntimeLeftoverSweeper()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;

        int swept = 0;
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go == null || go.transform.parent != null) continue;               // roots only; children die with them
            if ((go.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;        // savable = real scene content, leave it
            if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;         // hidden preview owned by an editor window
            Object.DestroyImmediate(go);
            swept++;
        }
        // Always log the outcome (even 0) so "no log" can never be mistaken for "sweeper didn't run":
        // 0 means Unity's own teardown already cleared the pools; N means this sweep did.
        Debug.Log($"[unwritten] RuntimeLeftoverSweeper: swept {swept} leftover runtime object(s) on exiting Play (pools rebuild next Play).");
    }
}
