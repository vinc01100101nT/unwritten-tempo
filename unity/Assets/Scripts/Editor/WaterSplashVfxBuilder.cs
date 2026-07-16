using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors a PLACEHOLDER puddle-splash particle prefab and registers it in the EffectLibrary as
/// <c>water.splash</c> — the droplet spray <see cref="WaterStep"/> kicks up when a foot lands in a puddle.
/// It's deliberately a starting point: a small upward burst of soft droplets under gravity, tinted watery
/// blue. Swap it any time by dropping a nicer splash prefab into <c>Assets/1_Authored/Art/VFX/water/</c> and running
/// <b>Rebuild Effect Library</b> — the id stays <c>water.&lt;name&gt;</c> and no runtime code changes.
///
/// <para>The flat expanding RIPPLE ring is NOT built here — the WetGround shader already draws that on the
/// puddle surface. This prefab only supplies the vertical kicked-up water, so the two never duplicate.</para>
///
/// Menu: <b>Tools ▸ unwritten ▸ Art ▸ Build Water Splash VFX (placeholder)</b>. Idempotent — re-running
/// overwrites the generated texture/material/prefab in place.
/// </summary>
public static class WaterSplashVfxBuilder
{
    const string Dir      = "Assets/1_Authored/Art/VFX/water";
    const string TexPath  = Dir + "/WaterSplashDot.png";
    const string MatPath  = Dir + "/WaterSplash.mat";
    const string PrefPath = Dir + "/Splash.prefab";

    [MenuItem("Tools/unwritten/Art/Build Water Splash VFX (placeholder)", priority = 625)]
    public static void Build()
    {
        EnsureFolder(Dir);
        var tex = BakeDropTexture();
        var mat = BuildMaterial(tex);
        BuildPrefab(mat);

        // Register it (id = water.splash via the "Assets/1_Authored/Art/VFX/<prefix>" convention root) so Vfx can resolve it.
        string summary = EffectLibraryBuilder.RebuildCore();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefPath);
        if (prefab != null) Selection.activeObject = prefab;
        EditorUtility.DisplayDialog("Build Water Splash VFX",
            "Built placeholder splash → " + PrefPath + " (id: water.splash)\n\n" + summary, "OK");
    }

    // A 64² soft radial dot (white RGB, smooth alpha falloff) — the droplet sprite. Baked to PNG so a saved
    // material/prefab can reference it as a real asset.
    static Texture2D BakeDropTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f;
                float dy = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a *= a;                                   // soften the edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        File.WriteAllBytes(TexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TexPath);

        var ti = (TextureImporter)AssetImporter.GetAtPath(TexPath);
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }

    // Sprites/Default: unlit, alpha-blended, tints by the particle's vertex (start) colour — works in URP without
    // any keyword juggling, so the placeholder can't render pink on a pipeline mismatch.
    static Material BuildMaterial(Texture2D tex)
    {
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, MatPath); }
        else mat.shader = sh;
        mat.mainTexture = tex;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void BuildPrefab(Material mat)
    {
        var go = new GameObject("Splash");
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.startLifetime = Range(0.28f, 0.5f);
        main.startSpeed = Range(1.4f, 3.2f);
        main.startSize = Range(0.04f, 0.10f);
        main.gravityModifier = 3.5f;                                  // arc back down like real droplets
        main.startColor = new Color(0.82f, 0.90f, 1f, 0.9f);          // watery blue-white
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 64;
        main.playOnAwake = true;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;                                         // burst only, no steady stream
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)12) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;                                            // narrow upward fan
        shape.radius = 0.04f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        PrefabUtility.SaveAsPrefabAsset(go, PrefPath);
        Object.DestroyImmediate(go);
    }

    // Random-between-two-constants curve (MinMaxCurve has no two-float constructor — that binds to multiplier+curve).
    static ParticleSystem.MinMaxCurve Range(float min, float max) => new ParticleSystem.MinMaxCurve
    {
        mode = ParticleSystemCurveMode.TwoConstants,
        constantMin = min,
        constantMax = max,
    };

    // Create each missing segment of an Assets/-relative folder path (AssetDatabase.CreateFolder needs one level).
    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string cur = parts[0];   // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
