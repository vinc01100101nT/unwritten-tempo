using UnityEngine;

/// <summary>
/// SOMEBODY ELSE'S CAST, DRAWN ON A MACHINE THAT ONLY WATCHES — the effect, the cast bar over their head and the ring
/// on the ground under a wind-up. Held by the shell of anything that casts: a person's, and a creature's.
///
/// <para><b>Why it is shared.</b> A person and a creature casting the same skill must look the same from every seat,
/// so the picture is written once. Only the body it is drawn on and the map it stands in differ, and each shell hands
/// those over.</para>
///
/// <para>Nothing here decides anything: the server says a cast played, a wind-up began or a wind-up ended, and this
/// draws it. The wind-up's bar is the one thing drawn over time, from the clock both machines share.</para>
/// </summary>
public class WatchedCast
{
    SkillDef windingUp;
    double windupStartedAt;
    float windupSeconds;
    Vector3 windupAimPointInMap;
    Vfx.EffectHandle windupRing;

    // The skill currently being drawn, and the two resolvers that read its per-step answers. Fields rather than
    // fresh lambdas per cast, so drawing somebody's flurry allocates nothing per beat.
    SkillDef showing;
    readonly System.Func<string, float> stayForShown;
    readonly System.Func<string, ProjectileFan> fanForShown;

    public WatchedCast()
    {
        stayForShown = tid => showing != null ? showing.StayForTick(tid) : 0f;
        fanForShown = tid => showing != null ? showing.FanForTick(tid) : ProjectileFan.Single;
    }

    /// <summary>Play a cast's effect on <paramref name="on"/>, aimed at <paramref name="aimedAt"/> or, with none, at
    /// <paramref name="pointInWorld"/> — with the SERVER'S seed, so a scattered shot lands where the server measured
    /// its damage from.</summary>
    public void Play(SkillDef def, Transform on, Transform aimedAt, Vector3 pointInWorld, int seed)
    {
        if (def == null || def.vfx == null || on == null) return;
        Vector3 target = aimedAt != null ? aimedAt.position : pointInWorld;
        showing = def;
        VfxDirector.Play(def.vfx, new VfxContext
        {
            origin = on.position,
            target = target,
            effectPoint = pointInWorld,
            follow = on,
            targetUnit = aimedAt,
            scaleMult = 1f,
            seed = seed,
            // …and the two answers that decide how much there is to look at: how long a looping step stays, and how
            // many copies of a carrier fly. Read off the SAME authored skill the server read them off, so a watcher
            // cannot draw one bolt where five were fired.
            stayForTick = stayForShown,
            fanForTick = fanForShown,
        });
    }

    /// <summary>Start showing a wind-up. A second one replaces the first; never two rings on one body.</summary>
    public void BeginWindup(SkillDef def, double startedAt, float seconds, Vector3 aimPointInMap, Transform body, string map)
    {
        if (def == null || seconds <= 0f) return;
        StopWindup(body);
        windingUp = def;
        windupStartedAt = startedAt;
        windupSeconds = seconds;
        windupAimPointInMap = aimPointInMap;
        ShowRing(body, map);
    }

    /// <summary>Stop showing it — the cast finished, was broken, or the body is going away. The bar and the ring go
    /// together, so a broken cast cannot leave a circle glowing in an empty field.</summary>
    public void StopWindup(Transform body)
    {
        windingUp = null;
        if (body != null) CastBar.Hide(body);
        if (windupRing != null) { windupRing.Stop(); windupRing = null; }
    }

    /// <summary>Draw this frame of a wind-up. Its own clock running out ends it too, not only the server saying so — the
    /// message that says so crosses a network, and the bar would otherwise sit full until it arrived.</summary>
    public void Tick(Transform body, string map)
    {
        if (windingUp == null) return;
        float elapsed = (float)(Mirror.NetworkTime.time - windupStartedAt);
        if (elapsed >= windupSeconds) { StopWindup(body); return; }
        if (body == null) return;

        CastBar.Set(body, elapsed / windupSeconds, windingUp.displayName, windingUp.tint);

        // THE RING, IF THE BODY ONLY JUST ARRIVED. Something can walk into view part-way through a long spell, and the
        // message that started this had no body to hang anything on at the time.
        if (windupRing == null) ShowRing(body, map);
    }

    // The ring that opens on the ground under a wind-up — the same one the caster sees, read off the same authored
    // skill, sized by the same authored radius. Attached to the BODY, exactly as the caster's own is, so a caster
    // shoved mid-cast takes their ring with them.
    void ShowRing(Transform body, string map)
    {
        if (body == null || windingUp == null || string.IsNullOrEmpty(windingUp.castCircleId)) return;
        Vector3 at = MapManager.InWorld(windupAimPointInMap, map);
        windupRing = Vfx.AttachGroundCircle(windingUp.castCircleId, body, at - body.position,
                                            windingUp.LargestAreaRadius, windingUp.castCircleTint);
    }
}
