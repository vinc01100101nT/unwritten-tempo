using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Repairs the Pxltgr "Fantasy Assets" materials after an FBX <b>Extract Materials</b>, which (in a URP
/// project) writes them onto the FBX's embedded <i>Autodesk Interactive</i> shader — a shader URP can't
/// render, so every prop turns magenta in the Scene while the Project thumbnails still preview gray. This
/// re-points every material in the extracted folder to <b>Universal Render Pipeline/Lit</b>, re-binds its
/// Base Map texture (matched by name, since the pack names materials 1:1 with their textures), and turns on
/// <b>alpha clipping</b> for the leaf/plant materials so the transparent leaf cards stop showing their gray
/// quad. Idempotent — safe to re-run after any future re-extract/re-import.
///
/// Background: see the chat thread that built this. Foliage uses CUTOUT (alpha clip), not Transparent, so it
/// writes depth, sorts correctly with the rest of the 3D world, and casts clean leaf-shaped shadows.
/// </summary>
public static class PxltgrMaterialFixer
{
    const string MatDir = "Assets/0_Raw/Pxltgr/fantasy-assets-extracted";
    static readonly string[] TexDirs =
    {
        // Prefer the model's own textures (mipmapped, 3D import) over the Sprite-imported copy.
        "Assets/0_Raw/Pxltgr/fantasy-assets/source/Fantasy_Assets/Textures",
        "Assets/0_Raw/Pxltgr/fantasy-assets/textures",
    };

    [MenuItem("Tools/unwritten/Props/Fix Pxltgr Materials (URP + leaf alpha)", priority = 510)]
    static void Fix()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        { EditorUtility.DisplayDialog("Fix Pxltgr Materials", "URP/Lit shader not found — is this a URP project?", "OK"); return; }

        if (!AssetDatabase.IsValidFolder(MatDir))
        { EditorUtility.DisplayDialog("Fix Pxltgr Materials", $"No materials folder at:\n{MatDir}\n\nExtract the FBX materials there first, then re-run.", "OK"); return; }

        var byName = BuildTextureLookup();
        var mats = AssetDatabase.FindAssets("t:Material", new[] { MatDir });
        int fixedCount = 0, leaves = 0, noTex = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var guid in mats)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                mat.shader = lit;   // reset onto a shader URP can actually render

                // Re-bind the base texture (the Autodesk→URP shader swap drops the old _BASE_COLOR_MAP binding).
                if (byName.TryGetValue(mat.name, out var tex) && tex != null)
                {
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetColor("_BaseColor", Color.white);
                }
                else noTex++;   // e.g. UCX_Collision has no texture — left as a plain lit material

                // Flat, stylized look — kill the plastic sheen the PBR import left on.
                mat.SetFloat("_Smoothness", 0f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Surface", 0f);   // Opaque (cutout still lives under Opaque in URP)

                if (IsFoliage(mat.name))
                {
                    // Alpha CLIP: the shader reads the _ALPHATEST_ON keyword; the float + queue drive the Inspector.
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.SetFloat("_AlphaToMask", 1f);     // smoother cutout edges (MSAA coverage)
                    mat.SetFloat("_Cull", 0f);            // double-sided so leaf-card backs show top-down
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;   // 2450
                    leaves++;
                }
                else
                {
                    mat.SetFloat("_AlphaClip", 0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", 2f);            // back-face cull solid props
                    mat.renderQueue = -1;                 // from shader (Geometry)
                }

                EditorUtility.SetDirty(mat);
                fixedCount++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[unwritten] Fixed {fixedCount} Pxltgr materials → URP/Lit ({leaves} leaf materials alpha-clipped, " +
                  $"{noTex} had no matching texture). Scene should un-pink immediately.");
        EditorUtility.DisplayDialog("Fix Pxltgr Materials — done",
            $"Re-pointed {fixedCount} materials to URP/Lit.\n" +
            $"• {leaves} leaf/plant materials → alpha clipping (transparent leaves)\n" +
            $"• {noTex} had no name-matched texture (e.g. UCX_Collision) — left as plain lit\n\n" +
            "The magenta should be gone and the leaf cards cut out. Re-run anytime after re-importing the FBX.", "OK");
    }

    // leaf / leaves / plant → cutout foliage.
    static bool IsFoliage(string n)
    {
        n = n.ToLowerInvariant();
        return n.Contains("leaf") || n.Contains("leaves") || n.Contains("plant");
    }

    // Map texture file-name (no extension, case-insensitive) → Texture2D, preferring the first TexDir.
    static Dictionary<string, Texture2D> BuildTextureLookup()
    {
        var map = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var dir in TexDirs)
        {
            if (!AssetDatabase.IsValidFolder(dir)) continue;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var key = Path.GetFileNameWithoutExtension(p);
                if (map.ContainsKey(key)) continue;   // first dir wins (the mipmapped model textures)
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) map[key] = t;
            }
        }
        return map;
    }
}
