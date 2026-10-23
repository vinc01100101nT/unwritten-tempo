using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Plays the particles of a prefab opened in prefab mode while the game is in Play mode. The editor's own particle
/// preview does not run then (measured 2026-10-23: a playing system's clock stayed at 0 for 1.5 seconds), so every
/// effect prefab looked empty exactly when the save-to-refresh workflow needs it. A Scene view panel pauses it,
/// restarts it and sets its speed.
///
/// <para>Steps the particle state only, which is never saved: the prefab stays unmodified (measured, the same day).
/// Does nothing outside Play mode, where the editor's own preview and its Particles panel already work.</para>
/// </summary>
[InitializeOnLoad]
static class PrefabParticlePreview
{
    internal static bool paused;
    internal static float speed = 1f;
    static bool restart;
    static GameObject root;
    static ParticleSystem[] roots;
    static double lastTime;

    static PrefabParticlePreview()
    {
        EditorApplication.update += Step;
    }

    internal static bool Active =>
        EditorApplication.isPlaying && PrefabStageUtility.GetCurrentPrefabStage() != null;

    internal static void Restart() => restart = true;

    static void Step()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) { root = null; return; }
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        var contents = stage != null ? stage.prefabContentsRoot : null;
        if (contents == null) { root = null; return; }

        double now = EditorApplication.timeSinceStartup;
        if (contents != root)
        {
            root = contents;
            roots = TopSystems(contents);
            lastTime = now;
            return;
        }
        float dt = Mathf.Min(0.1f, (float)(now - lastTime)) * speed;
        lastTime = now;
        if (roots.Length == 0) return;

        bool again = restart;
        restart = false;
        if (!again && (paused || dt <= 0f)) return;
        for (int i = 0; i < roots.Length; i++)
        {
            var ps = roots[i];
            if (ps == null) continue;
            // A one-shot that has finished starts over, so the preview keeps showing it.
            if (again || (!ps.main.loop && !ps.IsAlive(true))) ps.Simulate(0f, true, true, false);
            else ps.Simulate(dt, true, false, false);
        }
        SceneView.RepaintAll();
    }

    // Only systems with no particle system above them: simulating a parent already moves its children.
    static ParticleSystem[] TopSystems(GameObject go)
    {
        var all = go.GetComponentsInChildren<ParticleSystem>(true);
        var top = new System.Collections.Generic.List<ParticleSystem>(all.Length);
        foreach (var ps in all)
        {
            bool nested = false;
            for (var t = ps.transform.parent; t != null; t = t.parent)
                if (t.GetComponent<ParticleSystem>() != null) { nested = true; break; }
            if (!nested) top.Add(ps);
        }
        return top.ToArray();
    }
}

/// <summary>The Scene view panel for <see cref="PrefabParticlePreview"/>: shown only in Play mode with a prefab open.</summary>
[Overlay(typeof(SceneView), "unwritten-prefab-particles", "Prefab Particles (Play mode)", true)]
class PrefabParticlePreviewOverlay : IMGUIOverlay, ITransientOverlay
{
    public bool visible => PrefabParticlePreview.Active;

    public override void OnGUI()
    {
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button(PrefabParticlePreview.paused ? "Play" : "Pause", GUILayout.Width(60)))
                PrefabParticlePreview.paused = !PrefabParticlePreview.paused;
            if (GUILayout.Button("Restart", GUILayout.Width(60))) PrefabParticlePreview.Restart();
        }
        PrefabParticlePreview.speed = EditorGUILayout.Slider("Speed", PrefabParticlePreview.speed, 0f, 2f);
    }
}
