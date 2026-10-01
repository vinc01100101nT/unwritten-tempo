using UnityEngine;

/// <summary>
/// THE SMOKE A UNIT AT THE LEVEL CAP WEARS — every person, creature (normal, epic, shadow), rescued character and
/// townsperson at level 99 or above, on every screen that draws them. Each shell holds one of these and hands it,
/// every frame, the level the server sent and the body this machine draws; nothing else decides whether it shows.
///
/// <para>Comparing a level the server SENT against the cap chooses a picture and produces no number, so it is
/// showing, not computing. The cap itself is read from <see cref="UnitStatMath.MaxBaseLevel"/>, never typed here.</para>
/// </summary>
public sealed class LevelCapAura
{
    public const string EffectId = "aura.smoke-01";

    public static bool Shows(int level) => level >= UnitStatMath.MaxBaseLevel;

    Vfx.EffectHandle handle;
    int generation;
    Transform on;
    // The body an attach was refused for (the effect missing from the table), so a missing effect is asked for once
    // per body rather than every frame.
    Transform refusedFor;

    // The handle is pooled: once its play ends it can be showing somebody else's effect, so it counts as ours only
    // while its play number still matches the one we started.
    bool Running => handle != null && handle.IsActive && handle.Generation == generation;

    /// <summary>Called every frame by the shell that draws <paramref name="body"/>. Two compares in the settled case.</summary>
    public void Show(int level, Transform body)
    {
        bool want = body != null && Shows(level);
        if (want ? (Running && on == body) : handle == null) return;
        Stop();
        if (!want || body == refusedFor) return;
        handle = Vfx.Attach(EffectId, body);
        if (handle == null) { refusedFor = body; return; }
        generation = handle.Generation;
        on = body;
    }

    /// <summary>Take the smoke off — the body is going away, being banked for reuse, or dropped below the cap.</summary>
    public void Stop()
    {
        if (Running) handle.Stop();
        handle = null;
        on = null;
    }
}
