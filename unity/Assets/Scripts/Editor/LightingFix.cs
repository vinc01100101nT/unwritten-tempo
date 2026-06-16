using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Factory for the persistent <c>Systems</c> scene's single global 2D light. URP's 2D renderer
/// allows only ONE global light at a time, so the game keeps exactly one in Systems (created by
/// <see cref="SystemsSceneBuilder"/>) and none per-map.
///
/// This used to also expose a "Fix 2D Lighting" menu tool that migrated existing maps to that
/// layout. That was a one-time migration and has been retired; only the reusable
/// <see cref="CreateGlobalLight"/> factory remains.
/// </summary>
public static class LightingFix
{
    /// <summary>Create a Global Light 2D in the active scene, configured via serialized fields so
    /// it's robust across URP versions: type = Global (the enum value IS the serialized int) and
    /// targeting every sorting layer so all sprites are lit. Default intensity (1) / colour (white)
    /// give a neutral look. Used by SystemsSceneBuilder.</summary>
    public static void CreateGlobalLight(string name)
    {
        var go = new GameObject(name);
        var light = go.AddComponent<Light2D>();

        var so = new SerializedObject(light);
        var type = so.FindProperty("m_LightType");
        if (type != null) type.intValue = (int)Light2D.LightType.Global;

        var applyTo = so.FindProperty("m_ApplyToSortingLayers");
        if (applyTo != null)
        {
            var all = SortingLayer.layers;
            applyTo.arraySize = all.Length;
            for (int i = 0; i < all.Length; i++)
                applyTo.GetArrayElementAtIndex(i).intValue = all[i].id;
        }
        so.ApplyModifiedProperties();
    }
}
