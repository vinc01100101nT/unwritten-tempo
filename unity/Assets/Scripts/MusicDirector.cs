using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The ONE audio-bed playback service for maps — the single choke point for both the game's SCORE (a map's Map BGM
/// loop) and its day/night SURROUNDINGS bed. Lives on the persistent <c>GameSystems</c> layer (never unloaded), so
/// audio CONTINUES and CROSSFADES across additive map swaps instead of hard-cutting to silence at every portal.
///
/// It drives itself off map loads: each additive map load (anything but <c>Systems</c>) is looked up in the
/// <see cref="MapBgmTable"/> and the matching <see cref="BgmTrack"/> is crossfaded in. Requesting the SAME track that's
/// already playing is a no-op, so re-entering a same-theme map never restarts anything. The surroundings day/night pair
/// is blended by <see cref="TimeOfDay"/>; the Map BGM loop plays around the clock.
///
/// Playback is three <see cref="Xfade"/> channels (Map BGM + surroundings-day + surroundings-night), each a fixed pair
/// of 2D <see cref="AudioSource"/>s crossfaded in <see cref="Update"/> — no per-frame allocation, only float writes
/// (see PERFORMANCE.md).
/// </summary>
[DisallowMultipleComponent]
public class MusicDirector : MonoBehaviour
{
    public static MusicDirector Instance { get; private set; }

    [Range(0f, 1f), Tooltip("Global level on top of each track's own volumes (for a settings slider later).")]
    public float masterVolume = 1f;

    // One crossfading audio channel: an INCOMING source ramping up and an OUTGOING source (the previous clip) ramping
    // down. Reused across map swaps — HandOff swaps the two so we never allocate a source past construction.
    class Xfade
    {
        AudioSource inSrc, outSrc;
        float outVol;   // outgoing source's volume frozen at handoff; Update only scales it toward 0

        public void Init(GameObject host) { inSrc = Make(host); outSrc = Make(host); }

        static AudioSource Make(GameObject host)
        {
            var s = host.AddComponent<AudioSource>();
            s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
            return s;
        }

        // Promote the current clip to outgoing (freeze its level) and start the new one silent.
        public void HandOff(AudioClip clip)
        {
            var t = inSrc; inSrc = outSrc; outSrc = t;
            outVol = outSrc.volume;
            inSrc.clip = clip;
            inSrc.volume = 0f;
            if (clip != null) inSrc.Play(); else if (inSrc.isPlaying) inSrc.Stop();
        }

        // Ramp incoming toward inPeak and outgoing toward 0 by crossfade progress k∈[0,1]; stop a spent outgoing voice.
        public void Tick(float inPeak, float k)
        {
            inSrc.volume = inPeak * k;
            outSrc.volume = outVol * (1f - k);
            if (k >= 1f && outSrc.isPlaying) outSrc.Stop();
        }
    }

    BgmTrack current;                 // the track we're playing / fading toward
    readonly Xfade music = new Xfade();       // Map BGM (around the clock)
    readonly Xfade surrDay = new Xfade();     // surroundings — day half
    readonly Xfade surrNight = new Xfade();   // surroundings — night half
    float fadeT, fadeDur;             // crossfade progress / length (seconds)

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        music.Init(gameObject);
        surrDay.Init(gameObject);
        surrNight.Init(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        var active = SceneManager.GetActiveScene();   // a map may already be open when we boot
        if (active.IsValid() && active.isLoaded) PlayForMap(active.name);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == MapManager.SystemsScene) return;   // the persistent layer isn't a map
        PlayForMap(scene.name);
    }

    /// <summary>Crossfade to whatever track the <see cref="MapBgmTable"/> maps <paramref name="sceneName"/> to
    /// (no-op if there's no table / no entry — leaves the current track playing).</summary>
    public void PlayForMap(string sceneName)
    {
        var table = MapBgmTable.Load();
        if (table == null) return;
        var track = table.Lookup(sceneName);
        if (track != null) PlayTrack(track);
    }

    /// <summary>Crossfade to <paramref name="track"/> (no-op if it's already current, or null/empty). Static so callers
    /// can fire before an instance is guaranteed — no-ops until the director exists.</summary>
    public static void Play(BgmTrack track)
    {
        if (Instance != null) Instance.PlayTrack(track);
    }

    void PlayTrack(BgmTrack track)
    {
        if (track == null || track.Empty || track == current) return;
        current = track;
        fadeDur = Mathf.Max(0.01f, track.fadeSeconds);
        fadeT   = 0f;
        music.HandOff(track.mapBgm);
        surrDay.HandOff(track.dayClip);
        surrNight.HandOff(track.HasNight ? track.nightClip : null);
    }

    void Update()
    {
        if (current == null) return;

        if (fadeT < fadeDur) fadeT += Time.unscaledDeltaTime;   // audio runs in real time (unaffected by pauses)
        float k = Mathf.Clamp01(fadeT / fadeDur);               // 0 = all previous, 1 = all current

        float night = TimeOfDay.Instance != null ? TimeOfDay.Instance.Night01 : 0f;

        music.Tick(current.bgmVolume * masterVolume, k);        // Map BGM: no day/night split

        // Surroundings: blend day↔night. With no night clip, day plays around the clock.
        float surrPeak = current.surroundVolume * masterVolume;
        float dayW   = current.HasNight ? (1f - night) : 1f;
        float nightW = current.HasNight ? night : 0f;
        surrDay.Tick(surrPeak * dayW, k);
        surrNight.Tick(surrPeak * nightW, k);
    }
}
