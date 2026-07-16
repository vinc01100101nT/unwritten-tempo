using UnityEditor;
using UnityEngine;

/// <summary>
/// Re-points every material in the Blink weapon pack (<c>Assets/0_Raw/Blink</c>) from the built-in <b>Standard</b> shader
/// (which a URP project renders MAGENTA) onto <b>Universal Render Pipeline/Lit</b>, carrying the albedo/normal/
/// metallic/emission maps across the Standard→URP property rename. One click for the whole pack instead of Unity's
/// select-a-folder converter. Idempotent — re-running on already-URP materials is a no-op. Source-named because it's
/// scoped to this pack's folder layout.
/// </summary>
public static class BlinkWeaponMaterialFixer
{
    const string Root = "Assets/0_Raw/Blink";

    [MenuItem("Tools/unwritten/Weapons/Fix Blink Weapon Materials (URP)", priority = 661)]
    static void Fix()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        { EditorUtility.DisplayDialog("Fix Blink Materials", "URP/Lit shader not found — is this a URP project?", "OK"); return; }
        if (!AssetDatabase.IsValidFolder(Root))
        { EditorUtility.DisplayDialog("Fix Blink Materials", $"No folder at {Root}.", "OK"); return; }

        var guids = AssetDatabase.FindAssets("t:Material", new[] { Root });
        int fixedCount = 0, skipped = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                if (mat.shader == lit) { skipped++; continue; }

                // Read the built-in Standard bindings BEFORE the shader swap (they survive as raw properties).
                var albedo   = mat.HasProperty("_MainTex")          ? mat.GetTexture("_MainTex")          : null;
                var color    = mat.HasProperty("_Color")            ? mat.GetColor("_Color")              : Color.white;
                var normal   = mat.HasProperty("_BumpMap")          ? mat.GetTexture("_BumpMap")          : null;
                var metallic = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;
                var emisMap  = mat.HasProperty("_EmissionMap")      ? mat.GetTexture("_EmissionMap")      : null;
                var emisCol  = mat.HasProperty("_EmissionColor")    ? mat.GetColor("_EmissionColor")      : Color.black;
                bool emissive = mat.IsKeywordEnabled("_EMISSION");

                mat.shader = lit;

                if (albedo   != null) mat.SetTexture("_BaseMap", albedo);
                mat.SetColor("_BaseColor", color);
                if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
                if (metallic != null) { mat.SetTexture("_MetallicGlossMap", metallic); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    if (emisMap != null) mat.SetTexture("_EmissionMap", emisMap);
                    mat.SetColor("_EmissionColor", emisCol);
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

        Debug.Log($"[unwritten] Blink materials → URP/Lit: converted {fixedCount}, {skipped} already URP.");
        EditorUtility.DisplayDialog("Fix Blink Materials — done",
            $"Converted {fixedCount} materials to URP/Lit ({skipped} already URP).\nThe magenta weapons should be gone.", "OK");
    }
}
