using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The SINGLE OWNER of "what colour is this weapon". One colour per <see cref="WeaponType"/>, chosen in the Skill
/// Composer (the pencil beside a weapon group) and read by everything that shows that weapon's skills — the
/// composer's own list today, and whatever draws a skill tile later — so the colour is never typed twice and can
/// never drift between the tool and the game.
///
/// <para>A weapon with no row here has NO chosen colour, and the lookup says so by returning null rather than a
/// stand-in: an unauthored weapon must stay distinguishable from one somebody deliberately painted black. Each
/// caller decides what to show in the meantime.</para>
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Weapon Colors", fileName = "WeaponColors")]
public class WeaponColors : ScriptableObject
{
    /// <summary>Where the game loads the table from — <c>Resources/</c> plus this name.</summary>
    public const string ResourceName = "WeaponColors";

    [Serializable]
    public struct Entry
    {
        public WeaponType weapon;
        public Color color;
    }

    public List<Entry> entries = new List<Entry>();

    /// <summary>The colour chosen for <paramref name="w"/>, or null if nobody has chosen one. Linear scan — the list
    /// holds one row per weapon and this runs on a repaint or a tile build, never per frame.</summary>
    public Color? Lookup(WeaponType w)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].weapon == w) return entries[i].color;
        return null;
    }

    /// <summary>Point <paramref name="w"/> at <paramref name="c"/>, replacing any colour it already had. The caller
    /// marks the asset dirty and saves — this only edits the list.</summary>
    public void Set(WeaponType w, Color c)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].weapon != w) continue;
            var row = entries[i];
            row.color = c;
            entries[i] = row;
            return;
        }
        entries.Add(new Entry { weapon = w, color = c });
    }

    /// <summary>The swatches the composer's palette offers, in the weapon roster's order. Twelve colours spread as
    /// far apart as twelve can be, because at tile size the colour is what says WHICH weapon.</summary>
    public static readonly Color[] Palette =
    {
        new Color32(0xD6, 0x3A, 0x3A, 0xFF),   // crimson
        new Color32(0xE2, 0x70, 0x1C, 0xFF),   // ember orange
        new Color32(0xE5, 0xB0, 0x1C, 0xFF),   // gold
        new Color32(0x8A, 0x61, 0x34, 0xFF),   // bronze
        new Color32(0x96, 0xAD, 0x22, 0xFF),   // olive lime
        new Color32(0x2F, 0x7F, 0xD1, 0xFF),   // azure
        new Color32(0x2E, 0x9E, 0x5B, 0xFF),   // forest green
        new Color32(0x14, 0xA2, 0xA2, 0xFF),   // teal
        new Color32(0x78, 0x83, 0x8F, 0xFF),   // gunmetal
        new Color32(0x7B, 0x54, 0xD8, 0xFF),   // violet
        new Color32(0xED, 0xED, 0xED, 0xFF),   // white
        new Color32(0xA0, 0x3C, 0xA8, 0xFF),   // plum
    };

    static WeaponColors cached;

    /// <summary>The project's table, or null until somebody picks a first colour. Re-reads only while it is still
    /// missing, so the one lookup that can fail is the only one that ever repeats.</summary>
    public static WeaponColors Load()
    {
        if (cached == null) cached = Resources.Load<WeaponColors>(ResourceName);
        return cached;
    }

    /// <summary>Adopt a table the moment it is created at author time, so readers see it immediately instead of
    /// waiting for the asset database to hand it back on the next import.</summary>
    public static void Adopt(WeaponColors table)
    {
        if (table != null) cached = table;
    }
}
