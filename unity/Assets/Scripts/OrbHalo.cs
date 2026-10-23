using UnityEngine;

/// <summary>
/// THE STAFF'S HELD ORBS, drawn circling the person who holds them — on every screen that draws that person. Each
/// shell holds one of these and hands it, every frame, the orbs the server sent and the body this machine draws.
/// Nothing here decides anything: the set arrives packed (<see cref="OrbMath.Pack"/>) and is only drawn.
///
/// <para>Each orb is a level-3 weapon-enchant effect (Fire, Nature for Earth, Ice for Water, Dark tinted grey for
/// Wind). Those are made to glow along a weapon, so each copy is told to emit from a small sphere instead. A copy is
/// made only when that slot's orb changes; each frame only moves it along the circle.</para>
/// </summary>
public sealed class OrbHalo
{
    /// <summary>How far from the body's centre the orbs circle, at shoulder height, in world units.</summary>
    const float Radius = 0.2f, Height = 0.25f;
    /// <summary>Seconds for one full turn — slow, so the orbs read rather than blur.</summary>
    const float TurnSeconds = 3f;
    /// <summary>The size of one orb's effect in the world (a library effect is made at about a person's size).</summary>
    const float OrbScale = 0.06f;
    /// <summary>The sphere, in the effect's own units, its particles leave from.</summary>
    const float EmitRadius = 0.35f;
    static readonly Color WindGrey = new Color(0.5f, 0.52f, 0.55f, 1f);

    readonly Transform[] orbs = new Transform[OrbMath.Slots];
    readonly GameObject[] looks = new GameObject[OrbMath.Slots];
    readonly OrbElement[] lookOf = new OrbElement[OrbMath.Slots];
    Transform root, on;
    int shown = -1, count;

    /// <summary>Called every frame by the shell that draws <paramref name="body"/>.</summary>
    public void Show(int packed, Transform body)
    {
        bool want = body != null && OrbMath.StaffInHand(packed) && OrbMath.Unpack(packed, 0) != OrbElement.None;
        if (!want) { if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false); shown = -1; return; }
        if (on != body) { Stop(); Build(body); }
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        if (packed != shown) Paint(packed);

        // The circle turns in world space; the root follows the body so the orbs leave with it.
        float turn = Time.time / TurnSeconds * Mathf.PI * 2f;
        Vector3 centre = body.position + Vector3.up * Height;
        for (int i = 0; i < count; i++)
        {
            float a = turn + i * Mathf.PI * 2f / count;
            orbs[i].position = centre + new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(turn * 2f + i) * 0.02f, Mathf.Sin(a) * Radius);
        }
    }

    /// <summary>Take the orbs away — the body is going, or being banked for reuse.</summary>
    public void Stop()
    {
        if (root != null) Object.Destroy(root.gameObject);
        root = null; on = null; shown = -1; count = 0;
        for (int i = 0; i < looks.Length; i++) { looks[i] = null; lookOf[i] = OrbElement.None; }
    }

    void Build(Transform body)
    {
        on = body;
        root = new GameObject("Held Orbs").transform;
        root.gameObject.hideFlags = HideFlags.DontSave;
        root.SetParent(body, false);
        float s = Mathf.Max(0.0001f, body.lossyScale.x);
        for (int i = 0; i < OrbMath.Slots; i++)
        {
            var orb = new GameObject("Orb " + i).transform;
            orb.SetParent(root, false);
            orb.localScale = Vector3.one / s;
            orbs[i] = orb;
        }
    }

    void Paint(int packed)
    {
        shown = packed;
        count = 0;
        for (int i = 0; i < OrbMath.Slots; i++)
        {
            var orb = OrbMath.Unpack(packed, i);
            bool has = orb != OrbElement.None;
            orbs[i].gameObject.SetActive(has);
            if (!has) continue;
            count++;
            if (lookOf[i] == orb && looks[i] != null) continue;
            if (looks[i] != null) Object.Destroy(looks[i]);
            looks[i] = Make(orb, orbs[i]);
            lookOf[i] = orb;
        }
    }

    /// <summary>The effect an orb wears: Fire, Nature (Earth), Ice (Water), Dark tinted grey (Wind).</summary>
    public static string EffectOf(OrbElement orb)
    {
        switch (orb)
        {
            case OrbElement.Fire:  return "weaponenchant.fire-lvl-3";
            case OrbElement.Earth: return "weaponenchant.nature-lvl-3";
            case OrbElement.Water: return "weaponenchant.ice-lvl-3";
            case OrbElement.Wind:  return "weaponenchant.dark-lvl-3";
            default:               return "";
        }
    }

    static GameObject Make(OrbElement orb, Transform parent)
    {
        var def = EffectCatalog.Get(EffectOf(orb));
        if (def == null || def.prefab == null) return null;
        // Made under a switched-off parent, so nothing plays before it is told where to emit from — playing first
        // would ask for the weapon mesh it was built for and warn about having none.
        bool wasOn = parent.gameObject.activeSelf;
        parent.gameObject.SetActive(false);
        var go = Object.Instantiate(def.prefab, parent, false);   // perf-ok: once per orb change, never per frame
        go.hideFlags = HideFlags.DontSave;
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one * OrbScale;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))   // perf-ok: once per orb change
        {
            // The orb's size comes from its parent, which only a Hierarchy-scaled system obeys.
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            // Made to leave from a weapon's mesh: with none to read, emit from a small sphere instead.
            var shape = ps.shape;
            if (shape.enabled && shape.shapeType == ParticleSystemShapeType.MeshRenderer)
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = EmitRadius;
            }
            if (orb == OrbElement.Wind) Grey(ps);
        }
        parent.gameObject.SetActive(wasOn);
        return go;
    }

    // Wind wears the Dark effect turned grey: every colour it starts or fades through keeps its alpha, loses its hue.
    static void Grey(ParticleSystem ps)
    {
        var main = ps.main;
        var start = main.startColor;
        float alpha = start.mode == ParticleSystemGradientMode.Color ? start.color.a : 1f;
        main.startColor = new Color(WindGrey.r, WindGrey.g, WindGrey.b, alpha);
        var col = ps.colorOverLifetime;
        if (col.enabled)
        {
            var g = col.color.gradient;
            if (g != null)
            {
                var keys = g.colorKeys;
                for (int k = 0; k < keys.Length; k++) keys[k].color = Color.white;
                var grey = new Gradient();
                grey.SetKeys(keys, g.alphaKeys);
                col.color = new ParticleSystem.MinMaxGradient(grey);
            }
        }
    }
}
