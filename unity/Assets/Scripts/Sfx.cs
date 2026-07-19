using System;
using UnityEngine;

/// <summary>
/// One logical sound effect made of N interchangeable clip variations (e.g. Sword Attack 1/2/3).
/// <see cref="Pick"/> returns a random clip avoiding an immediate repeat, and the player applies a
/// little random pitch jitter — so rapid repeats (footsteps, swings) never sound copy-pasted.
/// Serializable, so it sits inline on components and <see cref="Item"/> assets.
/// </summary>
[Serializable]
public class SfxSet
{
    public AudioClip[] clips;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Random pitch range (x..y). 1 = unchanged.")]
    public Vector2 pitch = new Vector2(0.95f, 1.05f);

    [NonSerialized] int last = -1;

    public bool Empty => clips == null || clips.Length == 0;

    public AudioClip Pick()
    {
        if (Empty) return null;
        if (clips.Length == 1) return clips[0];
        int i = UnityEngine.Random.Range(0, clips.Length);
        if (i == last) i = (i + 1) % clips.Length;   // never the same one twice in a row
        last = i;
        return clips[i];
    }
}

/// <summary>
/// Tiny pooled one-shot SFX player living on the persistent <c>Systems</c> layer. Two fixed rings of
/// reused AudioSources (no per-shot GameObject alloc), so firing hundreds of hits/footsteps stays cheap:
///   • <see cref="Play"/>   — <b>2D</b> (no position): consistent volume, no panning. Use for UI and
///     player-self sounds (your own footsteps, your own cast/grunt) where direction is meaningless.
///   • <see cref="PlayAt"/> — <b>3D positional</b> at a world point: pans + attenuates by distance from the
///     listener (the player — see <see cref="AudioListenerRig"/>), so you can hear WHERE a mob died, a
///     spell landed, etc. Use for anything happening away from the player.
/// </summary>
public class Sfx : MonoBehaviour
{
    public static Sfx Instance { get; private set; }

    [Tooltip("How many 2D sounds may overlap at once. Older voices are recycled past this.")]
    public int voices = 16;
    [Range(0f, 1f)] public float masterVolume = 1f;

    [Header("3D positional one-shots (Sfx.PlayAt)")]
    [Tooltip("How many positional sounds may overlap at once.")]
    public int spatialVoices = 12;
    [Tooltip("Fully loud within this distance of the listener (world units).")]
    public float minDistance = 0f;
    [Tooltip("Inaudible past this distance from the listener.")]
    public float maxDistance = 5.19f;

    AudioSource[] pool;     // 2D ring
    AudioSource[] pool3d;   // 3D positional ring
    int next, next3d;

    // Optional per-voice volume envelope (bound length + ease in/out). Parallel to the ring; only touched by
    // Update while at least one voice is enveloped (activeEnvelopes gate), so plain one-shots stay free.
    struct Voice { public bool active; public float start, dur, fadeIn, fadeOut, baseVol; }
    Voice[] env;    // parallel to pool
    Voice[] env3d;  // parallel to pool3d
    int activeEnvelopes;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        pool = new AudioSource[Mathf.Max(1, voices)];
        for (int i = 0; i < pool.Length; i++)
        {
            var src = new GameObject("SfxVoice" + i) { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<AudioSource>();
            src.transform.SetParent(transform, false);
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D
            pool[i] = src;
        }

        pool3d = new AudioSource[Mathf.Max(1, spatialVoices)];
        for (int i = 0; i < pool3d.Length; i++)
        {
            var src = new GameObject("SfxVoice3D" + i) { hideFlags = HideFlags.DontSaveInEditor }.AddComponent<AudioSource>();
            src.transform.SetParent(transform, false);
            src.playOnAwake = false;
            src.spatialBlend = 1f;                       // fully 3D positional
            src.rolloffMode = AudioRolloffMode.Linear;   // predictable: loud ≤ min, silent ≥ max
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;                        // no pitch wobble from fast emitters / a moving listener
            pool3d[i] = src;
        }

        env = new Voice[pool.Length];
        env3d = new Voice[pool3d.Length];
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Drive the volume envelopes of any bounded/faded voices. Early-out (no per-frame work) when none are active.
    void Update()
    {
        if (activeEnvelopes <= 0) return;
        float now = Time.unscaledTime;   // audio plays in real time, unaffected by timeScale/pauses
        StepEnvelopes(env, pool, now);
        StepEnvelopes(env3d, pool3d, now);
    }

    void StepEnvelopes(Voice[] envRing, AudioSource[] ring, float now)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            if (!envRing[i].active) continue;
            var src = ring[i];
            float t = now - envRing[i].start;
            if (t >= envRing[i].dur || !src.isPlaying)   // reached the bound (or the voice was stolen by a new shot)
            {
                if (src.isPlaying) src.Stop();
                envRing[i].active = false;
                activeEnvelopes--;
                continue;
            }
            float mult = 1f;
            if (envRing[i].fadeIn > 0f) mult = Mathf.SmoothStep(0f, 1f, t / envRing[i].fadeIn);            // ease in
            if (envRing[i].fadeOut > 0f)
                mult = Mathf.Min(mult, Mathf.SmoothStep(0f, 1f, (envRing[i].dur - t) / envRing[i].fadeOut)); // ease out
            src.volume = envRing[i].baseVol * mult * masterVolume;
        }
    }

    /// <summary>Live-retune the 3D positional range (how far from the listener a world sound can be heard).
    /// The pool copies <see cref="minDistance"/>/<see cref="maxDistance"/> once in <see cref="Awake"/>, so this
    /// pushes new values onto every already-created 3D voice — lets the DevSettings sliders work mid-Play.</summary>
    public void SetSpatialRange(float min, float max)
    {
        minDistance = Mathf.Max(0f, min);
        maxDistance = Mathf.Max(minDistance + 0.01f, max);
        if (pool3d == null) return;
        for (int i = 0; i < pool3d.Length; i++)
        {
            if (pool3d[i] == null) continue;
            pool3d[i].minDistance = minDistance;
            pool3d[i].maxDistance = maxDistance;
        }
    }

    /// <summary>Play a random variation of the set in <b>2D</b> (no-op if the set is empty or no Sfx exists
    /// yet). For UI and player-self sounds where direction doesn't matter.</summary>
    public static void Play(SfxSet set)
    {
        if (Instance != null) Instance.PlaySet(set);
    }

    /// <summary>Play a random variation of the set as a <b>3D positional</b> one-shot at <paramref name="pos"/>
    /// — pans + fades by distance from the player. For anything happening away from the player (a mob dying,
    /// a spell landing, a distant door). No-op if the set is empty or no Sfx exists yet.</summary>
    public static void PlayAt(SfxSet set, Vector3 pos)
    {
        if (Instance != null) Instance.PlaySetAt(set, pos, 0f, 0f, 0f);
    }

    /// <summary>3D positional variant with a volume envelope: bound the play length to <paramref name="duration"/>
    /// seconds (0 = full clip) and ease the volume in / out over <paramref name="fadeIn"/> / <paramref name="fadeOut"/>
    /// seconds (0 = no ramp). Bounding is how a long or looping clip is kept from bleeding past its beat.</summary>
    public static void PlayAt(SfxSet set, Vector3 pos, float fadeIn, float fadeOut, float duration)
    {
        if (Instance != null) Instance.PlaySetAt(set, pos, fadeIn, fadeOut, duration);
    }

    void PlaySet(SfxSet set)
    {
        if (set == null || set.Empty) return;
        var clip = set.Pick();
        if (clip == null) return;

        int idx = NextFree(pool, ref next);
        var src = pool[idx];
        src.clip = clip;
        src.pitch = UnityEngine.Random.Range(set.pitch.x, set.pitch.y);
        ArmVoice(env, idx, src, set.volume, 0f, 0f, 0f, clip.length);
        src.Play();
    }

    void PlaySetAt(SfxSet set, Vector3 pos, float fadeIn, float fadeOut, float duration)
    {
        if (set == null || set.Empty) return;
        var clip = set.Pick();
        if (clip == null) return;

        int idx = NextFree(pool3d, ref next3d);
        var src = pool3d[idx];
        src.transform.position = pos;   // the voice IS the emitter — engine pans/attenuates from here
        src.clip = clip;
        src.pitch = UnityEngine.Random.Range(set.pitch.x, set.pitch.y);
        ArmVoice(env3d, idx, src, set.volume, fadeIn, fadeOut, duration, clip.length);
        src.Play();
    }

    // Arm (or clear) voice idx's volume envelope. No envelope → plain one-shot at full volume, and any stale
    // envelope on this reused voice is cleared. With an envelope → Update ramps + stops it (see StepEnvelopes).
    void ArmVoice(Voice[] envRing, int idx, AudioSource src, float baseVol, float fadeIn, float fadeOut, float duration, float clipLen)
    {
        float effLen = clipLen / Mathf.Max(0.01f, src.pitch);   // a pitched clip finishes sooner
        bool enveloped = fadeIn > 0f || fadeOut > 0f || duration > 0f;
        if (enveloped)
        {
            if (!envRing[idx].active) activeEnvelopes++;
            envRing[idx].active = true;
            envRing[idx].start = Time.unscaledTime;
            envRing[idx].dur = duration > 0f ? Mathf.Min(duration, effLen) : effLen;
            envRing[idx].fadeIn = fadeIn;
            envRing[idx].fadeOut = fadeOut;
            envRing[idx].baseVol = baseVol;
            src.volume = (fadeIn > 0f ? 0f : baseVol) * masterVolume;
        }
        else
        {
            if (envRing[idx].active) { envRing[idx].active = false; activeEnvelopes--; }   // clear a stolen envelope
            src.volume = baseVol * masterVolume;
        }
    }

    // Prefer an idle voice in the ring; if all are busy, recycle the next one (steals the oldest). Returns its index.
    static int NextFree(AudioSource[] ring, ref int cursor)
    {
        for (int k = 0; k < ring.Length; k++)
        {
            int idx = (cursor + k) % ring.Length;
            if (!ring[idx].isPlaying) { cursor = (idx + 1) % ring.Length; return idx; }
        }
        int steal = cursor;
        cursor = (cursor + 1) % ring.Length;
        return steal;
    }
}
