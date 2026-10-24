using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE PAINTED INTERFACE PICTURES, by name — the window buttons, the townsfolk's services, the quest marks, the
/// weather and the kinds of map — read from <c>Resources/Icons/UI/&lt;name&gt;</c>, and the ailments' from
/// <c>Resources/Icons/Ailments/&lt;name&gt;</c>. Each picture is loaded the first time it is asked for and kept, so every
/// later ask is a dictionary look-up. Null for a name with no picture: the caller keeps its words then.
/// </summary>
public static class UiIcons
{
    const string UiFolder = "Icons/UI/";
    const string AilmentFolder = "Icons/Ailments/";

    // One table per folder, keyed by the bare name, so a look-up builds no string; only the first ask of a name does.
    static readonly Dictionary<string, Sprite> uiLoaded = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, Sprite> ailmentLoaded = new Dictionary<string, Sprite>();
    static readonly string[] ailmentNames = System.Enum.GetNames(typeof(global::Ailment));

    /// <summary>The interface picture called <paramref name="name"/>, or null.</summary>
    public static Sprite Get(string name) => Load(uiLoaded, UiFolder, name);

    /// <summary>The picture of an ailment, or null for a state that is not one. The six ailments head
    /// <see cref="StateId"/> in the order of <see cref="Ailment"/>.</summary>
    public static Sprite Ailment(StateId id)
    {
        int i = (int)id;
        if (i < 0 || i > (int)global::Ailment.Silence) return null;
        return Load(ailmentLoaded, AilmentFolder, ailmentNames[i]);
    }

    /// <summary>The picture of what a townsperson offers — their station, their shop, the Keeper's window or the boss
    /// they summon — or null for somebody who offers none.</summary>
    public static Sprite ServiceOf(WorldTownsfolk.Row person)
    {
        if (person == null) return null;
        if (person.keeper) return Get("Keeper");
        if (person.station == CraftStation.Smith) return Get("Smith");
        if (!string.IsNullOrEmpty(person.shop)) return Get("Shop");
        if (!string.IsNullOrEmpty(person.summons)) return Get("Summoner");
        return null;
    }

    /// <summary>The picture of a kind of weather.</summary>
    public static Sprite WeatherOf(WeatherDirector.Preset weather) => Get(WeatherNames[(int)weather]);

    static readonly string[] WeatherNames = System.Enum.GetNames(typeof(WeatherDirector.Preset));

    /// <summary>The picture of a kind of map (the world layout's "town", "field", "dungeon"), or null for one with none.</summary>
    public static Sprite KindOf(string mapType)
    {
        switch (MapKinds.Of(mapType))
        {
            case MapKind.Town:    return Get("Town");
            case MapKind.Field:   return Get("Field");
            case MapKind.Dungeon: return Get("Dungeon");
            default:              return null;
        }
    }

    static Sprite Load(Dictionary<string, Sprite> table, string folder, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (table.TryGetValue(name, out var s)) return s;
        s = Resources.Load<Sprite>(folder + name);
        table[name] = s;
        return s;
    }
}
