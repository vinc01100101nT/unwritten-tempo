using UnityEngine;

/// <summary>
/// HOW LONG ONE PARTICLE EFFECT PLAYS, measured from the effect itself — the single formula, in one place,
/// because two things now need the answer and they must never disagree.
///
/// <para><b>Why this had to come out of the pool.</b> A skill's gameplay beats are timed by its effect: a beat
/// authored to land "at the end" lands when the burst finishes, and that length was only ever known by the
/// POOLED INSTANCE, measured the first time one was built. A server draws nothing and builds no instances, so it
/// could not know when its own skills were supposed to land — and a second formula written for it is exactly how
/// the two halves of the same skill come to fire at different moments.</para>
///
/// <para>So the measurement lives here, is run against the prefab at author time (the baker writes the numbers
/// into the effect table), and is run again against the live instance on a machine that has one. Both call THIS,
/// so a baked number equals the measured one by construction rather than by hope.</para>
///
/// <para>PERFORMANCE: cold on both sides. The pool measures once per instance it ever builds, from an array it
/// already holds; the baker runs in the editor. Nothing here is on a frame or per-hit path.</para>
/// </summary>
public static class EffectTiming
{
    /// <summary>Safety cap so a mis-read duration can't leak a pooled instance forever.</summary>
    public const float MaxSeconds = 30f;

    // Slack on a measured window, so an effect is never cut off exactly on its own last frame.
    const float Slack = 0.15f;

    /// <summary>What an effect's own particles say about how long it runs.</summary>
    public readonly struct Measured
    {
        /// <summary>Seconds one play takes: the longest system's emission plus the life of the last particle it
        /// emits, at that system's own playback rate.</summary>
        public readonly float playSeconds;

        /// <summary>Seconds the tail takes to fade once emission stops — the longest particle lifetime.</summary>
        public readonly float fadeSeconds;

        /// <summary>True when ANY of its systems loops, so it never ends on its own.</summary>
        public readonly bool loops;

        public Measured(float playSeconds, float fadeSeconds, bool loops)
        {
            this.playSeconds = playSeconds;
            this.fadeSeconds = fadeSeconds;
            this.loops = loops;
        }

        /// <summary>Nothing has measured this yet — what an entry the baker never reached reads as.</summary>
        public bool IsUnmeasured => playSeconds <= 0f;
    }

    /// <summary>Measure every particle system under <paramref name="root"/>. Works on a prefab asset and on a live
    /// instance alike, because it reads only authored values — nothing that exists solely while playing.
    ///
    /// <para>Gathers the systems itself, so it ALLOCATES. Author-time and boot-time callers only; anything holding
    /// its own array must call the overload below instead.</para></summary>
    public static Measured Of(GameObject root)
    {
        if (root == null) return default;
        return Of(root.GetComponentsInChildren<ParticleSystem>(true));
    }

    /// <summary>The formula itself, over systems the caller already gathered — the effect pool holds its own array
    /// and must not build a second one.</summary>
    public static Measured Of(ParticleSystem[] systems)
    {
        if (systems == null) return default;
        float play = 0f, fade = 0f;
        bool loops = false;
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            var m = systems[i].main;
            if (m.loop) loops = true;
            float speed = Mathf.Max(0.01f, m.simulationSpeed);
            float emitted = (m.duration + m.startLifetime.constantMax) / speed;
            float tail = m.startLifetime.constantMax / speed;
            if (emitted > play) play = emitted;
            if (tail > fade) fade = tail;
        }
        return new Measured(Mathf.Min(play + Slack, MaxSeconds), Mathf.Min(fade + Slack, MaxSeconds), loops);
    }

    /// <summary>Nothing may be shorter than this once it has started, so an effect asked for a silly window still
    /// gets a frame or two rather than ending on the instant it began.</summary>
    const float MinWindow = 0.05f;

    /// <summary>Returned when a play never ends on its own — an attached loop, which lives until it is stopped.</summary>
    public const float NeverEnds = 0f;

    /// <summary>HOW LONG THIS PLAY LASTS, in seconds from the moment it starts — the one arithmetic, so the pool
    /// that draws the effect and the machine that only needs to know WHEN cannot answer differently.
    ///
    /// <para>Reads exactly what the spawn call reads: a forced window wins over the effect's own authored one, and
    /// both are real seconds that playback speed does not stretch; only a measured length is divided by speed. A
    /// looping effect that was not asked for as a one-shot never ends at all.</para></summary>
    public static float PlayEndsInSeconds(in Measured measured, float authoredLifetime, float requestedLifetime,
                                          bool oneShot, float speed, float tailTrim, float clipStart)
    {
        float forced = requestedLifetime > 0f ? requestedLifetime : authoredLifetime;
        if (forced > 0f) return Mathf.Max(MinWindow, forced - tailTrim - clipStart);
        if (measured.loops && !oneShot) return NeverEnds;
        return Mathf.Max(MinWindow, measured.playSeconds / Mathf.Max(0.01f, speed) - tailTrim - clipStart);
    }
}
