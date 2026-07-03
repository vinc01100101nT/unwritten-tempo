using UnityEngine;

/// <summary>
/// One authored piece of background music — the "score" a map plays. A reusable <see cref="ScriptableObject"/>
/// (author once via Assets ▸ Create ▸ unwritten ▸ Bgm Track, reference from many <see cref="MapRecipe"/>s), mirroring
/// how mobs/items/maps are data assets. Optionally a day/night pair, blended by <see cref="TimeOfDay"/> exactly like
/// the surroundings bed — leave <see cref="nightClip"/> null to play <see cref="dayClip"/> around the clock.
///
/// This asset only DESCRIBES the music; the persistent <see cref="MusicDirector"/> owns all playback (one choke point).
/// Long music should import as <c>Streaming</c> load type + Vorbis compression (don't decompress a 2-minute loop into RAM).
/// </summary>
[CreateAssetMenu(fileName = "BgmTrack", menuName = "unwritten/Bgm Track", order = 1)]
public class BgmTrack : ScriptableObject
{
    [Tooltip("Stable kebab id, e.g. 'bgm.town'. For quests/debug reference; the recipe holds a direct asset ref, not this.")]
    public string id = "bgm.new";

    [Tooltip("The actual background music loop for this map — plays around the clock.")]
    public AudioClip mapBgm;
    [Range(0f, 1f), Tooltip("Loudest the Map BGM ever gets.")]
    public float bgmVolume = 0.5f;

    [Header("Surroundings (day/night ambience bed)")]
    [Tooltip("Daytime surroundings loop (wind/birds/etc). Blended out as night falls.")]
    public AudioClip dayClip;
    [Tooltip("Nighttime surroundings loop (crickets/etc). Blended in as night falls; leave null to play Day around the clock.")]
    public AudioClip nightClip;
    [Range(0f, 1f), Tooltip("Loudest the surroundings bed ever gets.")]
    public float surroundVolume = 0.22f;

    [Tooltip("Seconds to crossfade FROM the previous track INTO this one when it takes over.")]
    public float fadeSeconds = 2f;

    public bool HasNight => nightClip != null;
    public bool Empty => mapBgm == null && dayClip == null && nightClip == null;
}
