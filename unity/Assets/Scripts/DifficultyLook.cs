using UnityEngine;

/// <summary>
/// THE LOOK A CREATURE'S MULTIPLIER GIVES IT — every kind, normal, elite or shadow, on every watching screen (decided
/// 2026-10-20): ×1 and ×2 nothing; ×3 the dark enchant at level 1, ×10 at level 2, ×30 at level 3 (the scythe and dagger
/// enchant, <see cref="GradeGlow.MakeThemeEnchant"/>); ×90 and up the rising smoke. Each creature shell holds one of
/// these and hands it, every frame, the multiplier its own copy of the creature list gives and the body it draws.
///
/// <para>Comparing a typed multiplier against four steps chooses a picture and produces no number, so it is showing, not
/// computing — exactly as a creature's size and element are read from the same list.</para>
///
/// <para><b>Cost.</b> Built once when a body is taken on or its look changes, never per frame. The engine then updates
/// 3 particle layers a creature at level 1, 4 at level 2, 6 at level 3 and 1 for the smoke, every frame, on every
/// watching machine that has the creature in view.</para>
/// </summary>
public sealed class DifficultyLook
{
    /// <summary>The smoke ×90 and up wears.</summary>
    public const string SmokeEffectId = "aura.smoke-01";

    /// <summary>How big the smoke is drawn on a creature, against the effect's own size: half (decided 2026-10-20).</summary>
    public const float SmokeScale = 0.5f;

    /// <summary>The multiplier each look starts from: dark level 1, 2, 3, then the smoke.</summary>
    public static readonly float[] LookFrom = { 3f, 10f, 30f, 90f };

    /// <summary>Which look a multiplier wears: 0 none, 1 to 3 the dark enchant's level, 4 the smoke.</summary>
    public static int LookOf(float multiplier)
    {
        int look = 0;
        for (int i = 0; i < LookFrom.Length; i++) if (multiplier >= LookFrom[i]) look = i + 1;
        return look;
    }

    int shown;
    Transform on;
    GameObject enchant;
    Vfx.EffectHandle smoke;
    int smokeGeneration;

    bool SmokeRunning => smoke != null && smoke.IsActive && smoke.Generation == smokeGeneration;

    /// <summary>Called every frame by the shell that draws <paramref name="body"/>. Two compares in the settled case.</summary>
    public void Show(float multiplier, Transform body)
    {
        int want = body != null ? LookOf(multiplier) : 0;
        if (want == shown && on == body && (want != 4 || SmokeRunning) && (want == 0 || want == 4 || enchant != null)) return;
        Stop();
        if (want == 0) return;
        on = body;
        shown = want;
        if (want == 4)
        {
            smoke = Vfx.Attach(SmokeEffectId, body, default, SmokeScale);
            if (smoke != null) smokeGeneration = smoke.Generation;
            return;
        }
        enchant = GradeGlow.MakeThemeEnchant(body, LocalBounds(body), 1f, GradeGlow.Enchant.Dark, want);
    }

    /// <summary>Take the look off — the body is going away or being banked for reuse.</summary>
    public void Stop()
    {
        if (SmokeRunning) smoke.Stop();
        smoke = null;
        if (enchant != null) Object.Destroy(enchant);
        enchant = null;
        on = null;
        shown = 0;
    }

    // The box the creature's pictures fill, in the body's own units: what the enchant streams from.
    static Bounds LocalBounds(Transform body)
    {
        var renderers = body.GetComponentsInChildren<Renderer>();   // perf-ok: once when a look is put on a body
        bool any = false;
        var box = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            var b = r.bounds;
            var min = body.InverseTransformPoint(b.min);
            var max = body.InverseTransformPoint(b.max);
            var local = new Bounds((min + max) * 0.5f, Vector3.zero);
            local.Encapsulate(min);
            local.Encapsulate(max);
            if (!any) { box = local; any = true; }
            else box.Encapsulate(local);
        }
        return any ? box : new Bounds(Vector3.up * 0.5f, Vector3.one);
    }
}
