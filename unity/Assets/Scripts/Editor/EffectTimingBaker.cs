using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps every effect's measured length in the effect table CURRENT — measuring by itself, writing only on a click.
///
/// <para><b>Why anything has to.</b> A skill's gameplay beats are timed by the effect it plays — a beat authored
/// to land "at the end" lands when that burst finishes. That length was only ever knowable by building the
/// particle prefab and reading it, which a server never does: it draws nothing. So the number is measured here,
/// at author time, and written into the table the server already loads.</para>
///
/// <para><b>Nobody has to remember to look.</b> It measures when the editor loads and again whenever an effect
/// prefab or the table is imported, moved or deleted, and says out loud when a number has moved. <b>It never
/// writes on its own:</b> nothing may change a saved asset while the user did nothing (the project's
/// scene-dirtying rule), so the write is the menu item below — one click, after the warning names what drifted.
/// Composing a skill is untouched: the length belongs to the EFFECT, so every skill that names one already has it.</para>
///
/// <para>Idempotent and quiet: a project where nothing changed costs one pass and prints nothing. Not a quick fix —
/// this is standing maintenance, like the material fixer.</para>
/// </summary>
public static class EffectTimingBaker
{
    // The measurement is only ever wrong by this little because of float noise in the read itself; anything
    // smaller is not a change worth dirtying an asset for.
    const float Epsilon = 0.0005f;

    const string MenuPath = "Tools/unwritten/Art/Re-measure Effect Timings";

    [InitializeOnLoadMethod]
    static void SweepWhenTheEditorLoads()
    {
        // Deferred by one editor tick: at load time the asset database may still be importing, and a table read
        // during that window comes back with prefabs that are not resolved yet.
        EditorApplication.delayCall += () => Sweep(write: false, silent: true);
    }

    /// <summary>The one place the table is WRITTEN. The automatic sweeps only warn.</summary>
    [MenuItem(MenuPath, priority = 460)]
    static void ReMeasureAndWrite() => Sweep(write: true, silent: false);

    /// <summary>Measure every entry. With <paramref name="write"/> the changed numbers are saved into the table;
    /// without it they are only counted and named, and the count is what this returns either way.</summary>
    public static int Sweep(bool write, bool silent)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return 0;

        var lib = Load();
        if (lib == null || lib.effects == null) return 0;

        int changed = 0, unresolved = 0;
        for (int i = 0; i < lib.effects.Count; i++)
        {
            var e = lib.effects[i];
            if (e == null) continue;
            if (e.prefab == null) { unresolved++; continue; }

            var m = EffectTiming.Of(e.prefab);
            if (Mathf.Abs(e.measuredPlaySeconds - m.playSeconds) < Epsilon
                && Mathf.Abs(e.measuredFadeSeconds - m.fadeSeconds) < Epsilon
                && e.measuredLoops == m.loops) continue;

            changed++;
            if (!write) continue;
            e.measuredPlaySeconds = m.playSeconds;
            e.measuredFadeSeconds = m.fadeSeconds;
            e.measuredLoops = m.loops;
        }

        if (write && changed > 0)
        {
            // editor-write-ok: reached only with write:true, which the menu item alone passes; every automatic sweep passes false
            EditorUtility.SetDirty(lib);
            // editor-write-ok: same — the automatic sweeps never get here
            AssetDatabase.SaveAssetIfDirty(lib);
        }

        // SAID OUT LOUD when something moved, because a retimed effect silently retimes a skill's beats, and the
        // person who nudged a particle duration has no reason to connect the two.
        string tail = unresolved > 0 ? ", " + unresolved + " with no prefab (skipped)" : "";
        if (!write && changed > 0)
            Debug.LogWarning("[effects] " + changed + " of " + lib.effects.Count + " effect timings have drifted from "
                             + "the table" + tail + " — " + MenuPath.Replace("/", " \u25B8 ") + " writes them.");
        else if (!silent)
            Debug.Log("[effects] measured " + lib.effects.Count + " effects: " + changed + " updated" + tail + ".");
        return changed;
    }

    static EffectLibrary Load()
    {
        var guids = AssetDatabase.FindAssets("t:EffectLibrary");
        for (int i = 0; i < guids.Length; i++)
        {
            var lib = AssetDatabase.LoadAssetAtPath<EffectLibrary>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (lib != null) return lib;
        }
        return null;
    }

    /// <summary>Re-measure the moment anything the table is made of changes — an effect prefab being retimed, and
    /// the table itself gaining a row (which is how a NEWLY dropped-in effect first appears). Both matter: without
    /// the second, an effect added today would go unmeasured until the editor was next restarted, and a skill
    /// composed against it would fire its beats at the half-second an unknown effect falls back to.
    ///
    /// <para>The whole table is re-read rather than only the asset that moved: it is a hundred-odd entries whose
    /// numbers are already right, so the pass costs one walk and writes nothing, and matching imported paths against
    /// entries would be a second way to decide which effect an asset is — one that goes stale the first time a
    /// prefab is renamed.</para></summary>
    class Watcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Interesting(imported) && !Interesting(deleted) && !Interesting(moved)) return;
            EditorApplication.delayCall += () => Sweep(write: false, silent: true);
        }

        // Only the effect prefabs and the table itself. Before this filter, importing ANY prefab or data asset
        // anywhere in the project re-measured all hundred-odd effects.
        const string EffectFolder = "Assets/1_Authored/Prefabs/VFX/";

        static bool Interesting(string[] paths)
        {
            if (paths == null) return false;
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (path == null) continue;
                bool prefab = path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase);
                bool table = path.EndsWith("EffectLibrary.asset", System.StringComparison.OrdinalIgnoreCase);
                if (table) return true;
                if (prefab && path.StartsWith(EffectFolder, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
