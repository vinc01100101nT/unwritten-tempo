using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WEATHER LOOKS A BIOME ALWAYS HAS, WHATEVER THE SKY IS DOING — a swamp's standing water, a snowfield's blanket, a
/// mist's fog, a sacred peak's clouds at treetop height. The one list of them, read by <see cref="WetGround"/>,
/// <see cref="SnowGround"/>, <see cref="FogOverlay"/> and <see cref="CloudLayerOverlay"/> for the map loaded right now
/// (<see cref="MapBiome.Current"/>). Each one only ever RAISES the look: the day's weather can still add more on top.
///
/// <para>Lives at <c>Resources/BiomeWeather</c>. A biome with no row holds nothing. How a biome's ground and props
/// look is the editor's Biome Table, which the running game cannot read; this list is only what must hold at runtime.</para>
/// </summary>
[CreateAssetMenu(fileName = "BiomeWeather", menuName = "unwritten/Biome Weather", order = 3)]
public class BiomeWeather : ScriptableObject
{
    public const string ResourcePath = "BiomeWeather";

    [System.Serializable]
    public class Row
    {
        public Biome biome;
        [Range(0f, 1f)]
        [Tooltip("Puddles this biome's maps always show, whatever the weather: 0 = only after rain, 1 = the full " +
                 "puddle coverage a soaking rain leaves.")]
        public float alwaysPuddles;
        [Range(0f, 1f)]
        [Tooltip("Snow blanket this biome's maps always show over the ground and every prop, whatever the weather: " +
                 "0 = only while it snows, 1 = fully covered.")]
        public float alwaysSnowCover;
        [Range(0f, 1f)]
        [Tooltip("Fog this biome's maps always show, whatever the weather: 0 = only in foggy weather, 1 = the full " +
                 "fog of the Fog weather.")]
        public float alwaysFog;
        [Range(0f, 1f)]
        [Tooltip("Clouds floating over the map at treetop height, which this biome's maps always show: 0 = none, " +
                 "1 = full.")]
        [UnityEngine.Serialization.FormerlySerializedAs("alwaysCloudSea")]
        public float alwaysClouds;
    }

    public List<Row> rows = new List<Row>();

    static BiomeWeather loaded;
    static bool looked;

    /// <summary>The list, loaded once. Null when it has not been made — then no biome holds anything.</summary>
    public static BiomeWeather Instance
    {
        get
        {
            if (!looked) { looked = true; loaded = Resources.Load<BiomeWeather>(ResourcePath); }
            return loaded;
        }
    }

    /// <summary>The row for the map loaded right now, or null (no marker on the map, no list, or no row).</summary>
    static Row Here
    {
        get
        {
            var map = MapBiome.Current;
            var list = Instance;
            if (map == null || list == null) return null;
            for (int i = 0; i < list.rows.Count; i++)
                if (list.rows[i] != null && list.rows[i].biome == map.biome) return list.rows[i];
            return null;
        }
    }

    /// <summary>Puddles the map loaded right now always shows (0..1 of the full coverage).</summary>
    public static float AlwaysPuddlesHere { get { var r = Here; return r != null ? r.alwaysPuddles : 0f; } }

    /// <summary>Snow blanket the map loaded right now always shows (0..1).</summary>
    public static float AlwaysSnowCoverHere { get { var r = Here; return r != null ? r.alwaysSnowCover : 0f; } }

    /// <summary>Fog the map loaded right now always shows (0..1).</summary>
    public static float AlwaysFogHere { get { var r = Here; return r != null ? r.alwaysFog : 0f; } }

    /// <summary>Clouds at treetop height the map loaded right now always shows (0..1).</summary>
    public static float AlwaysCloudsHere { get { var r = Here; return r != null ? r.alwaysClouds : 0f; } }

    /// <summary>What a weather look shows: the day's weather, never below what the biome always holds.</summary>
    public static float Hold(float fromWeather, float always) => Mathf.Max(fromWeather, always);
}
