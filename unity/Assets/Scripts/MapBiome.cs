using UnityEngine;

/// <summary>
/// Which biome this map is. The map generator stamps one into every map it builds, so the running game can tell a
/// swamp from a snowfield — a map's scene carries no other record of its biome. Read <see cref="Current"/> for looks
/// that belong to the ground rather than the sky (see <see cref="BiomeWeather"/>).
/// </summary>
[DisallowMultipleComponent]
public class MapBiome : MonoBehaviour
{
    public Biome biome = Biome.Grassland;

    /// <summary>The biome marker of the map loaded right now, or null when that map carries none (every map built
    /// before the generator stamped one).</summary>
    public static MapBiome Current { get; private set; }

    void OnEnable() { Current = this; }
    void OnDisable() { if (Current == this) Current = null; }
}
