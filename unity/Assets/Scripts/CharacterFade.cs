using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes a character's body see-through, and puts it back. Pooled and shared: ANY unit can fade (a dashing player, a
/// vanishing mob), so this is a general presentation service, not one effect's trick.
///
/// <para><b>Built for more than one reason to fade at once, on any number of bodies at once.</b> Each request is a
/// HOLD, tagged with who asked for it, so a dash's fade and (later) an invisibility's fade can overlap on the same body
/// without either erasing the other: while several holds are live the body wears the MOST see-through of them, and it
/// only returns to solid when the last one is released. Every body is dressed on its own, so a crowd of people dashing
/// at once each fade — a single dressed body would have looked fine until the day two of them met.</para>
///
/// <para>Three ways to hold, matching how long the reason lasts:
/// <list type="bullet">
/// <item><see cref="HoldFor"/> — a fixed number of seconds, then it lets go on its own.</item>
/// <item><see cref="HoldWhileRelocating"/> — for exactly as long as the unit is mid-hop.</item>
/// <item><see cref="Hold"/> + <see cref="Release"/> — held until the caller says otherwise (a status that lasts).</item>
/// </list></para>
///
/// <para><b>Why a material swap and not just an alpha.</b> A character is drawn OPAQUE, and an opaque URP surface
/// ignores alpha completely — turning the colour's alpha down does nothing at all. So the body's materials are swapped
/// for see-through clones (built once per source material and cached) and the alpha rides a
/// <see cref="MaterialPropertyBlock"/>. Letting go puts the real materials back.</para>
///
/// <para><b>It must not clobber the outfit dye.</b> <see cref="CharacterDye3D"/> pushes the recoloured atlas through
/// the SAME property block on these renderers, so this reads the block, changes only the colour, and writes it back —
/// and on release it restores the previous colour rather than clearing the block, which would erase the dye.</para>
///
/// <para>PERFORMANCE: the per-frame tick only runs while something is faded, and only walks the hold list (a handful
/// of entries) — no searching, no allocation. Swapping materials happens once when a body's fade level CHANGES, not
/// per frame, and the clones are cached across every fade of every body.</para>
/// </summary>
public static class CharacterFade
{
    // One reason a body is see-through. `until` < 0 = no timer (held until released or the hop ends).
    struct Hold_
    {
        public Transform body;
        public object owner;
        public float opacity;
        public float until;          // Time.time to expire at; < 0 = never on its own
        public ICombatUnit relocating; // non-null = let go the moment this unit stops relocating
    }

    // One renderer currently wearing a fade: what it had on before, so it goes back exactly as it was.
    struct Dressed
    {
        public Renderer renderer;
        public Material[] original;
        public bool hadColor;
        public Color prevColor;
    }

    // One body currently wearing a fade, at what level, and every renderer that was swapped for it. Pooled: a body
    // that goes back to solid returns its entry for the next one, so fading costs no garbage after the first few.
    sealed class DressedBody
    {
        public Transform body;
        public float opacity;
        public readonly List<Dressed> parts = new List<Dressed>(8);
    }

    static readonly List<Hold_> holds = new List<Hold_>(8);
    static readonly List<DressedBody> dressedBodies = new List<DressedBody>(4);
    static readonly Stack<DressedBody> spareBodies = new Stack<DressedBody>(4);
    static readonly Dictionary<Material, Material> transparentOf = new Dictionary<Material, Material>();
    static readonly List<Material> goneMaterials = new List<Material>();

    // A body's materials leave memory with its model once nobody has worn it for two maps (AddressablePrefabs); the
    // see-through copy of each would otherwise stay forever, and a fresh one be made beside it on the next visit.
    static CharacterFade() => AddressablePrefabs.MapLeft += DropCopiesOfGoneMaterials;

    // Once per map left, never per frame.
    static void DropCopiesOfGoneMaterials()
    {
        goneMaterials.Clear();
        foreach (var pair in transparentOf) if (pair.Key == null) goneMaterials.Add(pair.Key);
        foreach (var k in goneMaterials)
        {
            if (transparentOf[k] != null) Object.Destroy(transparentOf[k]);
            transparentOf.Remove(k);
        }
    }
    static readonly List<Renderer> scratch = new List<Renderer>(8);
    static MaterialPropertyBlock mpb;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>Fade <paramref name="body"/> to <paramref name="opacity"/> (0 = invisible, 1 = solid) until
    /// <see cref="Release"/> is called with the same <paramref name="owner"/>. Re-holding with the same owner just
    /// updates the level, so a caller can drive it live without leaking holds.</summary>
    public static void Hold(Transform body, object owner, float opacity) => Add(body, owner, opacity, -1f, null);

    /// <summary>Fade for a fixed <paramref name="seconds"/>, then let go automatically.</summary>
    public static void HoldFor(Transform body, object owner, float opacity, float seconds)
        => Add(body, owner, opacity, Time.time + Mathf.Max(0f, seconds), null);

    /// <summary>Fade for exactly as long as <paramref name="unit"/> is mid-relocation — the hop's own travel, however
    /// long that turns out to be. Call it AFTER starting the hop, or the unit isn't relocating yet and it ends at once.</summary>
    public static void HoldWhileRelocating(ICombatUnit unit, Transform body, float opacity)
        => Add(body, unit, opacity, -1f, unit);

    /// <summary>Let go of one hold. Safe to call when it was never held.</summary>
    public static void Release(Transform body, object owner)
    {
        for (int i = holds.Count - 1; i >= 0; i--)
            if (holds[i].owner == owner && holds[i].body == body) holds.RemoveAt(i);
        Resolve();
    }

    /// <summary>Drop every hold and put every body back to solid — a scene change, or a hard reset.</summary>
    public static void Clear()
    {
        holds.Clear();
        Resolve();
    }

    static void Add(Transform body, object owner, float opacity, float until, ICombatUnit relocating)
    {
        if (body == null) return;
        if (opacity >= 0.999f) { Release(body, owner); return; }   // "solid" is not a fade — it's the absence of one

        var h = new Hold_ { body = body, owner = owner, opacity = opacity, until = until, relocating = relocating };
        for (int i = 0; i < holds.Count; i++)
            if (holds[i].owner == owner && holds[i].body == body) { holds[i] = h; Resolve(); Ticker.Ensure(); return; }
        holds.Add(h);
        Resolve();
        Ticker.Ensure();
    }

    // Drop expired / finished holds, then dress whichever body the surviving holds point at, at the MOST see-through
    // level any of them asks for. Called on every change and once per frame while anything is held.
    static void Step()
    {
        float now = Time.time;
        for (int i = holds.Count - 1; i >= 0; i--)
        {
            var h = holds[i];
            bool done = h.body == null
                     || (h.until >= 0f && now >= h.until)
                     || (h.relocating != null && !h.relocating.IsRelocating);
            if (done) holds.RemoveAt(i);
        }
        Resolve();
    }

    // Bring every body to the level its holds ask for: undress the ones nothing holds any more, re-level the ones whose
    // level moved, dress the ones newly held. Bodies already at their level are left alone — no churn.
    static void Resolve()
    {
        for (int i = dressedBodies.Count - 1; i >= 0; i--)
        {
            var d = dressedBodies[i];
            float wanted = d.body != null ? WantedOpacity(d.body) : 1f;
            if (wanted >= 0.999f)
            {
                Undress(d);
                dressedBodies.RemoveAt(i);
                spareBodies.Push(d);
            }
            else if (!Mathf.Approximately(wanted, d.opacity)) SetLevel(d, wanted);
        }

        for (int i = 0; i < holds.Count; i++)
        {
            var body = holds[i].body;
            if (body == null || IsDressed(body)) continue;
            float wanted = WantedOpacity(body);
            if (wanted < 0.999f) Dress(body, wanted);
        }
    }

    // The MOST see-through level any live hold asks of this body; 1 = none.
    static float WantedOpacity(Transform body)
    {
        float opacity = 1f;
        for (int i = 0; i < holds.Count; i++)
            if (holds[i].body == body) opacity = Mathf.Min(opacity, holds[i].opacity);
        return opacity;
    }

    static bool IsDressed(Transform body)
    {
        for (int i = 0; i < dressedBodies.Count; i++)
            if (dressedBodies[i].body == body) return true;
        return false;
    }

    static void Dress(Transform body, float opacity)
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        var d = spareBodies.Count > 0 ? spareBodies.Pop() : new DressedBody();
        d.body = body; d.opacity = opacity; d.parts.Clear();
        body.GetComponentsInChildren(false, scratch);   // false = skip hidden parts; a hidden piece shouldn't fade IN
        for (int i = 0; i < scratch.Count; i++)
        {
            var r = scratch[i];
            if (r == null) continue;

            var src = r.sharedMaterials;
            var clone = new Material[src.Length];
            for (int m = 0; m < src.Length; m++) clone[m] = Transparent(src[m]);

            r.GetPropertyBlock(mpb);
            bool had = mpb.HasColor(BaseColorId);
            Color prev = had ? mpb.GetColor(BaseColorId) : Color.white;
            d.parts.Add(new Dressed { renderer = r, original = src, hadColor = had, prevColor = prev });

            r.sharedMaterials = clone;
            var c = prev; c.a = opacity;
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
        scratch.Clear();
        dressedBodies.Add(d);
    }

    // Already wearing the see-through materials: only the colour's alpha moves.
    static void SetLevel(DressedBody d, float opacity)
    {
        for (int i = 0; i < d.parts.Count; i++)
        {
            var p = d.parts[i];
            if (p.renderer == null) continue;
            p.renderer.GetPropertyBlock(mpb);
            var c = p.prevColor; c.a = opacity;
            mpb.SetColor(BaseColorId, c);
            p.renderer.SetPropertyBlock(mpb);
        }
        d.opacity = opacity;
    }

    static void Undress(DressedBody d)
    {
        for (int i = 0; i < d.parts.Count; i++)
        {
            var p = d.parts[i];
            if (p.renderer == null) continue;      // the body was destroyed mid-fade (death, model swap)
            p.renderer.sharedMaterials = p.original;
            // NOT SetPropertyBlock(null): the dye lives in this same block, so the colour goes back by value.
            p.renderer.GetPropertyBlock(mpb);
            if (p.hadColor) mpb.SetColor(BaseColorId, p.prevColor);
            else mpb.SetColor(BaseColorId, OpaqueColorOf(p.original));
            p.renderer.SetPropertyBlock(mpb);
        }
        d.parts.Clear();
        d.body = null;
    }

    // The colour to hand back when the block held none of its own: the material's own base colour, so a body whose
    // material isn't plain white doesn't come back the wrong shade. White only if the material has no such property.
    static Color OpaqueColorOf(Material[] mats)
    {
        if (mats != null && mats.Length > 0 && mats[0] != null && mats[0].HasProperty(BaseColorId))
        {
            var c = mats[0].GetColor(BaseColorId); c.a = 1f;
            return c;
        }
        return Color.white;
    }

    // A cached see-through clone of a source material (built once, shared by every body using that material).
    // Kept single-sided, unlike the occluding-prop fader: a character is a closed shell, and showing its inside faces
    // through itself reads as a hole, not as transparency.
    static Material Transparent(Material src)
    {
        if (src == null) return null;
        if (transparentOf.TryGetValue(src, out var m) && m != null) return m;

        m = new Material(src) { name = src.name + " (Fade)", hideFlags = HideFlags.HideAndDontSave };
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_Surface", 1f);            // URP: 0 = Opaque, 1 = Transparent
        m.SetFloat("_Blend", 0f);              // alpha blend
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_AlphaClip", 0f);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.renderQueue = (int)RenderQueue.Transparent;

        transparentOf[src] = m;
        return m;
    }

    // Timed and hop-length holds need a per-frame beat, and a static class has none. One hidden driver, created with
    // the first hold — the same shape Afterimage uses for its fade-out.
    class Ticker : MonoBehaviour
    {
        static Ticker instance;
        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("CharacterFadeTicker") { hideFlags = HideFlags.DontSaveInEditor };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Ticker>();
        }
        void LateUpdate() { if (holds.Count > 0) Step(); }
    }
}
