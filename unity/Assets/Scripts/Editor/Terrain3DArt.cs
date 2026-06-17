using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared builder for the world-projected terrain material (shader <c>Unwritten/TerrainProjected</c>)
/// that paints the merged 3D terrain with the 2D pixel-art grass/dirt tiles. Used by TerrainSandbox,
/// the town's scenic border, and the flat town ground so they all match. Caches one material asset at
/// <c>Assets/Art/Terrain3D/Terrain3D.mat</c>, and makes sure the tile textures import pixel-perfect
/// (Point filter, Repeat wrap) so world-space tiling is seamless.
/// </summary>
public static class Terrain3DArt
{
    const string Dir      = "Assets/Art/Terrain3D";
    const string MatPath  = Dir + "/Terrain3D.mat";
    const string GrassPng = Dir + "/grass.png";
    const string DirtPng  = Dir + "/dirt.png";

    // NOTE: the custom Unwritten/TerrainProjected shader renders in the GAME (3D renderer / Play mode),
    // but NOT in the Scene view (which uses the project's 2D renderer) — so the terrain looks invisible
    // while editing and shows correctly on Play. That's expected; verify by pressing Play.

    static Material _cached;

    public static Material Material()
    {
        if (_cached != null) return _cached;

        var grass = LoadTile(GrassPng);
        var dirt  = LoadTile(DirtPng);

        var shader = Shader.Find("Unwritten/TerrainProjected");
        if (shader == null) { Debug.LogError("[unwritten] Shader 'Unwritten/TerrainProjected' not found (still importing?)."); return null; }

        // If the shader didn't compile, DON'T apply it (terrain keeps its visible Kenney material) and
        // surface the exact errors so we can fix them — instead of silently rendering invisible.
        if (ShaderUtil.ShaderHasError(shader))
        {
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    Debug.LogError($"[unwritten] TerrainProjected error (line {m.line}): {m.message}");
            Debug.LogWarning("[unwritten] Terrain texture skipped — shader has errors (logged above). Terrain stays Kenney-green.");
            return null;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        bool created = mat == null;
        if (created)
        {
            mat = new Material(shader);
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Art", "Terrain3D");
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        else if (mat.shader != shader) mat.shader = shader;   // repair a material cached with a broken shader
        if (grass != null) mat.SetTexture("_GrassTex", grass);
        if (dirt != null)  mat.SetTexture("_DirtTex", dirt);
        // Defaults only on first creation — so your Inspector tuning of these survives a rebuild.
        if (created)
        {
            mat.SetFloat("_Tiles", 4f);      // tiles per world unit (PPU 16); tune in the Inspector
            mat.SetFloat("_GrassMin", 0.5f);
            mat.SetFloat("_GrassMax", 0.8f);
        }
        EditorUtility.SetDirty(mat);
        _cached = mat;
        return mat;
    }

    /// <summary>Re-skin every renderer under <paramref name="go"/> with the terrain material.</summary>
    public static void Apply(GameObject go)
    {
        var mat = Material();
        if (mat == null) return;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>()) mr.sharedMaterial = mat;
    }

    // Pixel-perfect + tileable: Point filtering, Repeat wrap, no compression.
    static Texture2D LoadTile(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            bool dirty = false;
            if (imp.filterMode != FilterMode.Point) { imp.filterMode = FilterMode.Point; dirty = true; }
            if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
            if (imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (dirty) { imp.SaveAndReimport(); }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
