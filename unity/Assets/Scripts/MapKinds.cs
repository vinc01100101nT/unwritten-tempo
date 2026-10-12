using UnityEngine;

/// <summary>The four kinds of map the world layout gives every map.</summary>
public enum MapKind { Town, Field, Dungeon, Special }

/// <summary>
/// THE ONE COLOUR OF EACH KIND OF MAP — a town is yellow, a field green, a dungeon purple, a special place red. A door's
/// ring is chosen by the colour of the map it leads to, a door on the zone map is drawn in it, and a map's name flashes
/// in it on arrival, so all three always agree.
///
/// <para>The kind comes from the map's <c>type</c> in the world layout (town | field | dungeon | special), never from
/// its name: a map's id says nothing reliable about what it is. Anything unlisted counts as a field.</para>
/// </summary>
public static class MapKinds
{
    public static MapKind Of(string type) => type switch
    {
        "town"    => MapKind.Town,
        "dungeon" => MapKind.Dungeon,
        "special" => MapKind.Special,
        _         => MapKind.Field,
    };

    public static Color ColorOf(MapKind kind) => kind switch
    {
        MapKind.Town    => new Color(1f,   0.85f, 0.4f),
        MapKind.Dungeon => new Color(0.6f, 0.35f, 0.9f),
        MapKind.Special => new Color(0.9f, 0.3f,  0.3f),
        _               => new Color(0.4f, 0.9f,  0.4f),
    };

    public static Color ColorOfType(string type) => ColorOf(Of(type));
}
