#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EDITOR-PLAY dev tool: hot-swap the live-3D player character model while the game runs, for quick
/// visual testing across the rigged-character library. Cycled with the <b>[</b> / <b>]</b> keys or
/// picked from the list in the <see cref="DevSettings"/> panel (▸ 3D Character).
///
/// <para>Editor-only by nature — it loads the rigged FBXs by project path via <see cref="AssetDatabase"/>,
/// which doesn't exist in a build (the models aren't in a Resources/Addressables group, so there's nothing
/// to load at runtime). For real in-build character select, that's the later Addressables/catalog work.</para>
///
/// <para>SINGLE SOURCE OF TRUTH: the actual attach (instantiate model, build the shared Humanoid controller,
/// add the rig components) is <see cref="Player3DVisualBuilder"/>.Attach — the ONE place a player visual is
/// built. That class lives in the Editor assembly (unreachable from this runtime script), so it registers
/// itself here via a delegate on editor load (see its HookRuntimeModelSwap). This class never re-implements
/// the attach; it only chooses which model and calls back.</para>
/// </summary>
public static class PlayerModelSwap
{
    /// <summary>Set by <see cref="Player3DVisualBuilder"/> on editor load: (player, modelPath) → the new visual.</summary>
    public static System.Func<Player3DController, string, GameObject> Attacher;
    /// <summary>The rigged-model folder, mirrored from <see cref="Player3DVisualBuilder"/>.RiggedDir on editor load.</summary>
    public static string RiggedDir = "Assets/Art/Quat/Rigged";

    static List<string> paths;      // rigged FBX asset paths, sorted by name — built lazily once, then cached
    static int index = -1;          // index of the model last applied (−1 = unknown / matches whatever was baked)

    /// <summary>All rigged model paths (lazily enumerated once). Drives the DevSettings list.</summary>
    public static IReadOnlyList<string> Paths => Ensure();
    /// <summary>Index of the model last applied via this tool, or −1 if none yet.</summary>
    public static int Index => index;

    /// <summary>Display name (file stem) for the path at <paramref name="i"/>.</summary>
    public static string NameAt(int i)
    {
        var p = Ensure();
        return (i >= 0 && i < p.Count) ? Path.GetFileNameWithoutExtension(p[i]) : "(none)";
    }

    static List<string> Ensure()
    {
        if (paths != null) return paths;
        paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { RiggedDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) && IsPlayableHumanoid(p))
                paths.Add(p);
        }
        paths.Sort(System.StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    // Only offer models that can actually wear the shared HUMANOID controller: imported as Humanoid AND carrying a
    // valid biped avatar. This is the whole "why are some T-posing" story — a Generic-rigged or quadruped FBX (Cow/
    // Pug) can't retarget the Humanoid clips, so it stands in a T-pose (or has no Animator). Filtering here means the
    // dropdown + [ / ] only ever cycle working characters. Run Tools ▸ unwritten ▸ Rigs ▸ Bulk Set Humanoid to add more.
    static bool IsPlayableHumanoid(string fbxPath)
    {
        var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (imp == null || imp.animationType != ModelImporterAnimationType.Human) return false;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (a is Avatar av) return av.isValid && av.isHuman;
        return false;
    }

    /// <summary>Step to the next (+1) / previous (−1) rigged model and apply it — wraps around the list.</summary>
    public static void Cycle(int dir)
    {
        var p = Ensure();
        if (p.Count == 0) return;
        int start = index >= 0 ? index : 0;
        Apply(((start + dir) % p.Count + p.Count) % p.Count);
    }

    /// <summary>Swap the live player to the rigged model at <paramref name="i"/>. No-op off editor Play.</summary>
    public static void Apply(int i)
    {
        var p = Ensure();
        if (i < 0 || i >= p.Count) return;
        if (Attacher == null)
        {
            Debug.LogWarning("[unwritten] PlayerModelSwap has no attacher — the editor hook didn't run. Reimport scripts.");
            return;
        }
        var player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null)
        {
            Debug.LogWarning("[unwritten] PlayerModelSwap: no Player3DController in the scene to re-skin.");
            return;
        }
        if (Attacher(player, p[i]) != null) index = i;   // only advance the marker on a successful attach
    }
}
#endif
