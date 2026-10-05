using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// Sets up every material a character model makes on import (under <c>Assets/1_Authored/Art/Characters/</c>).
///
/// <para><b>Drawn with the character shader.</b> <c>Unwritten/CharacterLit</c> is URP Lit plus the outfit-colour
/// swap, so a person's chosen colours are applied while they are drawn rather than baked into a texture of their own
/// (<see cref="CharacterDye3D"/>). With no colours handed to it, it draws exactly what URP Lit draws. Done in
/// <see cref="OnPreprocessMaterialDescription"/>, which runs after URP's own FBX setup (order -980) — the character
/// models import their materials from the file's description, and for those that is the step that builds them.</para>
///
/// <para><b>No plastic sheen.</b> The default URP-Lit gloss (_Smoothness ~0.5) paints a bright highlight on every
/// low-poly facet — the "shiny plastic shell" look, worst on the dark bodies where the highlight is all you see — so
/// this forces the same flat/stylized finish the prop importers already use (<see cref="GlbPropImporter"/>,
/// <see cref="FbxKitPrefabBaker"/>): _Smoothness = 0, _Metallic = 0. Done in BOTH steps below: Unity never calls
/// <see cref="OnPostprocessMaterial"/> for a model that imports its materials from the file's description (measured
/// 2026-10-05 — 51 of the 52 models, which had silently kept _Smoothness 0.5), and URP's own description step sets the
/// gloss from the file first, so ours, running after it, has the last word. Only two numbers change; the shader and its
/// keywords do not, so it costs nothing extra to draw.</para>
///
/// <para>Runs on every import so it can't drift. Raising <see cref="GetVersion"/> makes Unity re-import every model
/// this touches, on every machine, the next time the project opens.</para>
/// </summary>
public class CharacterMaterialFixer : AssetPostprocessor
{
    public const string CharacterShaderPath = "Assets/Shaders/CharacterLit.shader";

    public override uint GetVersion() => 3;

    void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Art/Characters/")) return;

        // Declared so the shader is always imported before the model asks for it, whatever order a fresh import runs in.
        context.DependsOnArtifact(CharacterShaderPath);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(CharacterShaderPath);
        if (shader == null)
        {
            Debug.LogError("[CharacterMaterialFixer] " + CharacterShaderPath + " is missing — " + path
                           + " keeps URP Lit and its outfit colours will not show.");
            return;
        }
        UseCharacterShader(material, shader);
        Flatten(material);
    }

    /// <summary>Put a material on the character shader, set up exactly as URP sets up its own Lit materials.</summary>
    public static void UseCharacterShader(Material material, Shader shader)
    {
        if (material == null || shader == null) return;
        material.shader = shader;
        // URP switches the motion-vector pass off on every Lit material unless it adds precomputed velocity
        // (BaseShaderGUI.UpdateMotionVectorKeywordsAndPass). It only does that for shaders it recognises by name, so
        // the same rule is applied here — otherwise every character would draw an extra motion-vector pass.
        bool motionVectors = material.HasProperty("_AddPrecomputedVelocity") && material.GetFloat("_AddPrecomputedVelocity") != 0f;
        material.SetShaderPassEnabled("MotionVectors", motionVectors);
    }

    void OnPostprocessMaterial(Material material)
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Art/Characters/")) return;
        Flatten(material);
    }

    // The flat, stylized finish: no gloss highlight, no metal.
    static void Flatten(Material material)
    {
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);   // legacy name, harmless if absent
        if (material.HasProperty("_Metallic"))   material.SetFloat("_Metallic", 0f);
    }
}
