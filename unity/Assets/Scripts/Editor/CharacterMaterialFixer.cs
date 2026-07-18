using UnityEditor;
using UnityEngine;

/// <summary>
/// Kills the plastic sheen on character models. The character FBXs (under
/// <c>Assets/1_Authored/Art/Characters/</c>) let Unity auto-generate their materials on import, and the
/// default URP-Lit gloss (_Smoothness ~0.5) paints a bright highlight on every low-poly facet — that's the
/// "shiny plastic shell" look, worst on the dark bodies where the highlight is all you see.
///
/// This forces the same flat/stylized finish the prop importers already use
/// (<see cref="GlbPropImporter"/>, <see cref="FbxKitPrefabBaker"/>): _Smoothness = 0, _Metallic = 0. Runs on
/// every import so it can't drift; existing models pick it up on the next reimport (right-click
/// <c>Assets/1_Authored/Art/Characters</c> → Reimport).
/// </summary>
public class CharacterMaterialFixer : AssetPostprocessor
{
    void OnPostprocessMaterial(Material material)
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Art/Characters/")) return;

        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);   // legacy name, harmless if absent
        if (material.HasProperty("_Metallic"))   material.SetFloat("_Metallic", 0f);
    }
}
