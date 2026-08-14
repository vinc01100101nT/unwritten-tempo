using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Marks a public <see cref="SfxSet"/> field that is ALLOWED to be empty (a situational / fallback cue, e.g.
/// a "No MP" sting), so the wiring checks (<see cref="SfxWiringCheck"/> at runtime + the editor
/// "Verify Player Wiring" tool) don't flag it as a missing sound. The checks also auto-skip any field whose
/// name contains "fail" by convention (those are intentionally unassigned in the player builder).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class OptionalSfxAttribute : Attribute { }

/// <summary>
/// Shared reflection helper that finds public <see cref="SfxSet"/> fields on a GameObject's components that are
/// EMPTY (null or zero clips) and not opted out. One source of truth so the runtime guard and the editor
/// validator agree on what "missing audio" means.
///
/// Why this exists: skill audio is BAKED into the saved <c>Systems.unity</c> at author-time (the player builder
/// freezes clip references into the scene). A stale scene — re-baked before the wiring existed, or before the
/// clips were importable — silently ends up with empty clip arrays and NO error, just silence (this is exactly
/// how Meteor lost its sound). These checks turn that silent rot into an obvious, named warning.
/// </summary>
public static class SfxWiring
{
    /// <summary>Append a "Component.field" label for every empty, non-optional <see cref="SfxSet"/> on
    /// <paramref name="root"/> and its children.</summary>
    public static void ScanInto(GameObject root, List<string> issues)
    {
        if (root == null || issues == null) return;
        var comps = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var c in comps)
        {
            if (c == null) continue;   // missing script
            var t = c.GetType();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(SfxSet) || IsOptional(f)) continue;
                var set = f.GetValue(c) as SfxSet;
                if (set == null || set.Empty) issues.Add(t.Name + "." + f.Name);
            }
        }
    }

    static bool IsOptional(FieldInfo f)
    {
        if (f.IsDefined(typeof(OptionalSfxAttribute), true)) return true;
        // "fail" cues (No-MP / interrupt) are intentionally left empty by the player builder.
        return f.Name.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// DEV-ONLY runtime guard. Self-boots, waits for the player to load (it arrives under <c>Systems</c> async via
/// <c>GameRoot</c>), scans its skill SFX once, and logs a single clear warning if any are empty — so a stale
/// Systems scene is OBVIOUS the moment you hit Play instead of just making no sound. Compiled out of release
/// builds entirely (zero cost), like <see cref="DevSettings"/> / <c>PerfHud</c>.
/// </summary>
public class SfxWiringCheck : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SfxWiringCheck") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);   // dev tool only — intentionally exempt from the no-DontDestroyOnLoad rule
        go.AddComponent<SfxWiringCheck>();
    }

    // Unity runs an IEnumerator Start() as a coroutine automatically.
    IEnumerator Start()
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        Player3DController player = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            player = FindAnyObjectByType<Player3DController>();
            if (player != null) break;
            yield return new WaitForSeconds(0.5f);
        }

        if (player != null)
        {
            var issues = new List<string>();
            SfxWiring.ScanInto(player.gameObject, issues);
            if (issues.Count > 0)
                Debug.LogWarning($"[unwritten] {issues.Count} skill SFX unassigned on the player " +
                    $"(stale Systems scene?): {string.Join(", ", issues)}. Re-run " +
                    "Tools ▸ unwritten ▸ Scene Setup ▸ Setup Systems Scene to re-wire them.");
        }

        Destroy(gameObject);   // one-shot check
    }
}
#endif
