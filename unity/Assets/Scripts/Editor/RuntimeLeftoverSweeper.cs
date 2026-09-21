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
        // Always log the outcome (even 0) so "no log" can never be mistaken for "sweeper didn't run":
        // 0 means Unity's own teardown already cleared the pools; N means this sweep did. If this line NEVER
        // appears on Stop, the editor callbacks aren't firing (usually a compile error) — use the manual menu item.
        Debug.Log($"[unwritten] RuntimeLeftoverSweeper: swept {Sweep()} leftover runtime object(s) on exiting Play (pools rebuild next Play).");
    }

    // On-demand recovery for when the auto-sweep above never ran (e.g. Play was exited while the project had a
    // compile error, so [InitializeOnLoad] never registered the callback and the ghosts linger in the scene view).
    [MenuItem("Tools/unwritten/Sweep Runtime Leftovers")]
    static void SweepMenu()
    {
        Debug.Log($"[unwritten] RuntimeLeftoverSweeper: manual sweep removed {Sweep()} leftover runtime object(s).");
    }

    /// <summary>Destroys every root GameObject tagged DontSaveInEditor still hanging in the editor (runtime pool
    /// hosts, projectiles, bursts) and returns how many. Editor-window previews use HideAndDontSave
    /// (HideInHierarchy) and are skipped, so an open composer's preview is never touched.</summary>
    static int Sweep()
    {
        int swept = 0;
        // Must use Resources.FindObjectsOfTypeAll, NOT FindObjectsByType: Unity's FindObjectsByType is
        // documented to SKIP objects with HideFlags.DontSave set — which is exactly the DontSaveInEditor tag on
        // every runtime object here. After Play-exit those leaked objects also go sceneless (gone from the
        // Hierarchy, still rendered); only FindObjectsOfTypeAll enumerates sceneless/hidden/DontSave objects.
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || go.transform.parent != null) continue;               // roots only; children die with them
            if (EditorUtility.IsPersistent(go)) continue;                          // asset/prefab on disk, not a live object
            if ((go.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;        // savable = real scene content, leave it
            if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;         // hidden preview owned by an editor window
            // editor-write-ok: destroys only DontSaveInEditor objects leaked by Play — never a byte that could be saved
            Object.DestroyImmediate(go);
            swept++;
        }
        return swept;
    }
}
