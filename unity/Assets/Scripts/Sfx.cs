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

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        pool = new AudioSource[Mathf.Max(1, voices)];
        for (int i = 0; i < pool.Length; i++)
        {
            var src = new GameObject("SfxVoice" + i).AddComponent<AudioSource>();
            src.transform.SetParent(transform, false);
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D
            pool[i] = src;
        }

        pool3d = new AudioSource[Mathf.Max(1, spatialVoices)];
        for (int i = 0; i < pool3d.Length; i++)
        {
            var src = new GameObject("SfxVoice3D" + i).AddComponent<AudioSource>();
            src.transform.SetParent(transform, false);
            src.playOnAwake = false;
            src.spatialBlend = 1f;                       // fully 3D positional
            src.rolloffMode = AudioRolloffMode.Linear;   // predictable: loud ≤ min, silent ≥ max
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;                        // no pitch wobble from fast emitters / a moving listener
            pool3d[i] = src;
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

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
        if (Instance != null) Instance.PlaySetAt(set, pos);
    }

    void PlaySet(SfxSet set)
    {
        if (set == null || set.Empty) return;
        var clip = set.Pick();
        if (clip == null) return;

        var src = NextFree(pool, ref next);
        src.clip = clip;
        src.volume = set.volume * masterVolume;
        src.pitch = UnityEngine.Random.Range(set.pitch.x, set.pitch.y);
        src.Play();
    }

    void PlaySetAt(SfxSet set, Vector3 pos)
    {
        if (set == null || set.Empty) return;
        var clip = set.Pick();
        if (clip == null) return;

        var src = NextFree(pool3d, ref next3d);
        src.transform.position = pos;   // the voice IS the emitter — engine pans/attenuates from here
        src.clip = clip;
        src.volume = set.volume * masterVolume;
        src.pitch = UnityEngine.Random.Range(set.pitch.x, set.pitch.y);
        src.Play();
    }

    // Prefer an idle voice in the ring; if all are busy, recycle the next one (steals the oldest).
    static AudioSource NextFree(AudioSource[] ring, ref int cursor)
    {
        for (int k = 0; k < ring.Length; k++)
        {
            var src = ring[(cursor + k) % ring.Length];
            if (!src.isPlaying) { cursor = (cursor + k + 1) % ring.Length; return src; }
        }
        var s = ring[cursor];
        cursor = (cursor + 1) % ring.Length;
        return s;
    }
}
