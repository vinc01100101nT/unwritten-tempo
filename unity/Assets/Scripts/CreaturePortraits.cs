using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A CREATURE'S PORTRAIT — the small face drawn beside its name on the hover plate, the target panel and the map
/// window's creature card. A saved
/// character shows its own; any other creature shows its creature's (<see cref="MonsterDef.faceset"/>). A creature with
/// none (a shadow, drawn as a 3D character) shows nothing.
/// </summary>
public static class CreaturePortraits
{
    // One sprite per portrait picture, made the first time it is asked for and shared after — Sprite.Create is not free.
    static readonly Dictionary<Texture2D, Sprite> sprites = new Dictionary<Texture2D, Sprite>();

    /// <summary>The portrait for <paramref name="unit"/>, whose shell (if it has one) is <paramref name="shell"/>; null
    /// when it has none.</summary>
    public static Sprite For(Health unit, NetMob shell)
    {
        if (unit == null) return null;
        Texture2D face = null;
        if (unit.savedAs != null) face = unit.savedAs.faceset;
        else if (shell != null)
        {
            var def = MonsterCatalog.Get(shell.defId);
            if (def != null) face = def.faceset;
        }
        return SpriteOf(face);
    }

    /// <summary>The portrait of the creature <paramref name="def"/> describes; null when it has none.</summary>
    public static Sprite Of(MonsterDef def) => SpriteOf(def != null ? def.faceset : null);

    static Sprite SpriteOf(Texture2D face)
    {
        if (face == null) return null;
        if (sprites.TryGetValue(face, out var s)) return s;
        s = Sprite.Create(face, new Rect(0f, 0f, face.width, face.height), new Vector2(0.5f, 0.5f), face.height);
        sprites[face] = s;
        return s;
    }
}
