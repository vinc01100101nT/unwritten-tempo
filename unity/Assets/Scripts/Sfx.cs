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
/// Tiny pooled one-shot SFX player living on the persistent <c>Systems</c> layer. A fixed ring of
/// <see cref="voices"/> AudioSources is reused for every sound (no per-shot GameObject alloc), so
/// firing hundreds of hits/footsteps stays cheap. Sounds are 2D (consistent volume, no odd panning
/// under the orbiting 2.5D camera). Call <see cref="Play"/> from anywhere.
/// </summary>
public class Sfx : MonoBehaviour
{
    public static Sfx Instance { get; private set; }

    [Tooltip("How many sounds may overlap at once. Older voices are recycled past this.")]
    public int voices = 16;
    [Range(0f, 1f)] public float masterVolume = 0.9f;

    AudioSource[] pool;
    int next;

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
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Play a random variation of the set (no-op if the set is empty or no Sfx exists yet).</summary>
    public static void Play(SfxSet set)
    {
        if (Instance != null) Instance.PlaySet(set);
    }

    void PlaySet(SfxSet set)
    {
        if (set == null || set.Empty) return;
        var clip = set.Pick();
        if (clip == null) return;

        var src = NextVoice();
        src.clip = clip;
        src.volume = set.volume * masterVolume;
        src.pitch = UnityEngine.Random.Range(set.pitch.x, set.pitch.y);
        src.Play();
    }

    // Prefer an idle voice; if all are busy, recycle the next in the ring (steals the oldest).
    AudioSource NextVoice()
    {
        for (int k = 0; k < pool.Length; k++)
        {
            var src = pool[(next + k) % pool.Length];
            if (!src.isPlaying) { next = (next + k + 1) % pool.Length; return src; }
        }
        var s = pool[next];
        next = (next + 1) % pool.Length;
        return s;
    }
}
