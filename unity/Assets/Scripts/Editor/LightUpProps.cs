using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click: add a <see cref="NightLight"/> to the props that should glow after dark — houses (lit windows),
/// candlesticks and the tavern lamp — so once you hand-place them with the <b>Prop Palette</b> they light the
/// dark town at night and go dark by day, all off the shared <see cref="TimeOfDay"/> clock.
///
/// It edits the PREFAB ASSETS (via <see cref="PrefabUtility.LoadPrefabContents"/>), so every already-placed
/// instance inherits the glow too, and every future placement is lit automatically. Re-running is safe — props
/// that already have a <see cref="NightLight"/> are skipped. The light is centred in each prop's measured
/// bounds (glow-from-within) and its reach scales with the prop's size.
/// </summary>
public static class LightUpProps
{
    const string PropsDir = "Assets/1_Authored/Prefabs/Props";

    // Per-prop tuning: name (without extension) → warm colour, base intensity, range bonus on top of the
    // prop's own size. Houses get a big warm spill; candles/lamps a small focused flame.
    struct Tune { public Color color; public float intensity; public float extraRange; public float heightFrac; }

    static readonly Dictionary<string, Tune> Targets = new Dictionary<string, Tune>
    {
        { "House1",                 new Tune { color = Warm(1f, 0.70f, 0.34f), intensity = 2.6f, extraRange = 2f, heightFrac = 0.45f } },
        { "House2",                 new Tune { color = Warm(1f, 0.70f, 0.34f), intensity = 2.6f, extraRange = 2f, heightFrac = 0.45f } },
        { "House3",                 new Tune { color = Warm(1f, 0.70f, 0.34f), intensity = 2.6f, extraRange = 2f, heightFrac = 0.45f } },
        { "Tavern_interior_lamp",   new Tune { color = Warm(1f, 0.78f, 0.42f), intensity = 2.2f, extraRange = 1.5f, heightFrac = 0.6f } },
        { "Furniture_candlestick1", new Tune { color = Warm(1f, 0.82f, 0.45f), intensity = 1.3f, extraRange = 1f, heightFrac = 0.8f } },
        { "Furniture_candlestick3", new Tune { color = Warm(1f, 0.82f, 0.45f), intensity = 1.3f, extraRange = 1f, heightFrac = 0.8f } },
    };

    static Color Warm(float r, float g, float b) => new Color(r, g, b);

    [MenuItem("Tools/unwritten/Props/Light Up Props (night)", priority = 511)]
    static void Run()
    {
        int lit = 0, skipped = 0, missing = 0;

        foreach (var kv in Targets)
        {
            string path = PropResolver.PathFor(kv.Key) ?? $"{PropsDir}/{kv.Key}.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { missing++; Debug.LogWarning($"[LightUpProps] not found: {path}"); continue; }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<NightLight>() != null) { skipped++; continue; }

                var nl = root.AddComponent<NightLight>();
                var tune = kv.Value;

                // Measure the prop so the light sits inside it and reaches a sensible distance.
                if (TryLocalBounds(root, out var b))
                {
                    nl.localOffset = new Vector3(b.center.x, b.min.y + b.size.y * tune.heightFrac, b.center.z);
                    nl.range = Mathf.Max(2.5f, b.extents.magnitude + tune.extraRange);
                }
                else
                {
                    nl.localOffset = new Vector3(0f, 1f, 0f);
                    nl.range = 4f;
                }
                nl.color = tune.color;
                nl.intensity = tune.intensity;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                lit++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[LightUpProps] Lit {lit} prop prefab(s), skipped {skipped} (already lit), {missing} missing. " +
                  "Place them with the Prop Palette — they glow at night.");
        EditorUtility.DisplayDialog("Light Up Props",
            $"Added night lights to {lit} prop(s).\n{skipped} already had one. {missing} not found.\n\n" +
            "Place them with Tools ▸ unwritten ▸ Prop Palette — they light up at night automatically.\n" +
            "Tune the whole town live in Play from the P panel ▸ Environment (Lamp intensity / range).",
            "OK");
    }

    // Combined renderer bounds in the ROOT's local space (root loaded at the origin with identity transform,
    // so renderer.bounds is effectively local here). Returns false if the prop has no renderers.
    static bool TryLocalBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        var rends = root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        foreach (var r in rends)
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }
}
