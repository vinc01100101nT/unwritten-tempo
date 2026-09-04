using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// THE runtime roster of rigged character models — the one place that knows a model by NAME and can hand back the
/// asset for it in a build. Every live-3D character is drawn from here: the player, the townsfolk
/// (<see cref="AmbientLife"/>) and the 3D shadow mobs (<see cref="MobShadow3DVisual"/>).
///
/// <para><b>Why by name and not by reference.</b> The cast used to be direct object links on
/// <see cref="CharacterSpec"/>, which meant loading that one settings asset dragged EVERY model in the cast into
/// memory whether or not a single one was worn. Names cost nothing until something asks for one, and a name is also
/// what a save file, a chat command and (later) a server can store — an object link is none of those things.</para>
///
/// <para>Models are marked Addressable under <c>Characters/&lt;model name&gt;</c> and share the label
/// <see cref="Label"/>. The label is what makes the roster LISTABLE without loading it: asking for the locations of
/// a label returns addresses only, no meshes. A newly added model must be given that address + label before the
/// game can see it.</para>
/// </summary>
public static class CharacterModels
{
    /// <summary>Address prefix every character model is registered under.</summary>
    public const string AddressPrefix = "Characters/";

    /// <summary>The Addressable label every character model carries — the roster's membership test.</summary>
    public const string Label = "CharacterModel";

    static List<string> names;          // cached roster (short names, sorted) — addresses only, no assets loaded
    static bool namesLoading;
    static readonly List<Action<IReadOnlyList<string>>> namesWaiting = new List<Action<IReadOnlyList<string>>>();

    /// <summary>The Addressable address for a model's short name ("BlueSoldier_Male" → "Characters/BlueSoldier_Male").</summary>
    public static string AddressOf(string modelName) => AddressPrefix + modelName;

    /// <summary>Deliver the model asset for <paramref name="modelName"/>, or null when the roster has no such name.
    /// Fires this frame if that model is already loaded, a few frames later the first time.</summary>
    public static void Load(string modelName, Action<GameObject> onModel)
    {
        if (string.IsNullOrWhiteSpace(modelName)) { onModel?.Invoke(null); return; }
        AddressablePrefabs.Load(AddressOf(modelName), onModel);
    }

    /// <summary>The model asset for <paramref name="modelName"/> if it is already loaded, else null.</summary>
    public static GameObject Cached(string modelName)
        => string.IsNullOrWhiteSpace(modelName) ? null : AddressablePrefabs.Cached(AddressOf(modelName));

    /// <summary>Every model name in the roster, sorted. Listing does NOT load any model — only their addresses.
    /// The list is fetched once and cached; every caller after that is answered immediately.</summary>
    public static void Names(Action<IReadOnlyList<string>> onNames)
    {
        if (names != null) { onNames?.Invoke(names); return; }
        if (onNames != null) namesWaiting.Add(onNames);
        if (namesLoading) return;

        namesLoading = true;
        Addressables.LoadResourceLocationsAsync(Label).Completed += h =>
        {
            names = new List<string>();
            if (h.Status == AsyncOperationStatus.Succeeded && h.Result != null)
                foreach (IResourceLocation loc in h.Result)
                {
                    string key = loc.PrimaryKey;
                    if (string.IsNullOrEmpty(key) || !key.StartsWith(AddressPrefix, StringComparison.Ordinal)) continue;
                    string shortName = key.Substring(AddressPrefix.Length);
                    if (!names.Contains(shortName)) names.Add(shortName);
                }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0)
                GameLog.Warn(LogCat.Unwritten, "No character models carry the '" + Label + "' Addressable label — the " +
                                 "roster is empty, so no character can be dressed. Register the models and build " +
                                 "Addressables content.");
            namesLoading = false;
            var waiting = namesWaiting.ToArray();
            namesWaiting.Clear();
            foreach (var cb in waiting) cb(names);
        };
    }

    /// <summary>Match <paramref name="typed"/> against the roster: an exact name (any casing) wins, otherwise the
    /// first name that starts with it. Null when nothing matches. Callers must have the roster already (see
    /// <see cref="Names"/>) — this is a pure lookup over the cached list.</summary>
    public static string Resolve(string typed)
    {
        if (names == null || string.IsNullOrWhiteSpace(typed)) return null;
        foreach (var n in names) if (string.Equals(n, typed, StringComparison.OrdinalIgnoreCase)) return n;
        foreach (var n in names) if (n.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return n;
        return null;
    }
}
