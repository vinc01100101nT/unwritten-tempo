using UnityEngine;

/// <summary>
/// The live registry that resolves a <see cref="VfxCueId"/> to its authored <see cref="VfxCueBinding"/>. Loads the
/// single <see cref="VfxCueLibrary"/> asset from <c>Resources/VfxCueLibrary.asset</c> ONCE and indexes it into a
/// flat array keyed by the enum's int value — so a lookup on the per-hit / per-tick path is an array index, not a
/// dictionary hash or any allocation. Mirrors <see cref="EffectCatalog"/>.
///
/// <para>A cue may also have a WEAPON'S OWN version (<see cref="VfxCueBinding.weapon"/>) — the Bow's cast — kept in
/// a second flat array keyed by cue and weapon, and used instead of the every-weapon row when one exists.</para>
///
/// Play cues through <see cref="VfxCue"/> (the front door); this class only resolves bindings. Loading is lazy +
/// cached; call <see cref="Reload"/> after editing the library in the Vfx Cue Composer.
/// </summary>
public static class VfxCueCatalog
{
    const string ResourcePath = "VfxCueLibrary";

    static VfxCueBinding[] _byId;       // indexed by (int)VfxCueId; null slot = unbound cue
    static VfxCueBinding[] _byWeapon;   // indexed by (int)VfxCueId * weapons + (int)WeaponType; null = no own version
    static int weapons;

    static void EnsureLoaded()
    {
        if (_byId != null) return;
        int count = System.Enum.GetValues(typeof(VfxCueId)).Length;
        weapons = 0;
        foreach (int w in System.Enum.GetValues(typeof(WeaponType))) if (w + 1 > weapons) weapons = w + 1;
        _byId = new VfxCueBinding[count];
        _byWeapon = new VfxCueBinding[count * weapons];
        var lib = Resources.Load<VfxCueLibrary>(ResourcePath);
        if (lib == null || lib.cues == null) return;
        for (int i = 0; i < lib.cues.Count; i++)
        {
            var b = lib.cues[i];
            if (b == null) continue;
            int idx = (int)b.id;
            if (idx < 0 || idx >= _byId.Length) continue;
            int w = (int)b.weapon;
            if (w == (int)WeaponType.None) _byId[idx] = b;   // last binding for an id wins
            else if (w > 0 && w < weapons) _byWeapon[idx * weapons + w] = b;
        }
    }

    /// <summary>Drop the cache so the next access reloads from Resources (use after editing the library).</summary>
    public static void Reload() { _byId = null; _byWeapon = null; }

    /// <summary>Resolve a cue's binding, or null if it isn't bound (a silent no-op cue). Zero-alloc array index.</summary>
    public static VfxCueBinding Get(VfxCueId id) => Get(id, WeaponType.None);

    /// <summary>The cue's look for <paramref name="weapon"/>: that weapon's own version when it has one, else the
    /// every-weapon row. Zero-alloc array indexes.</summary>
    public static VfxCueBinding Get(VfxCueId id, WeaponType weapon)
    {
        EnsureLoaded();
        int idx = (int)id;
        if (idx < 0 || idx >= _byId.Length) return null;
        int w = (int)weapon;
        if (w > 0 && w < weapons)
        {
            var own = _byWeapon[idx * weapons + w];
            if (own != null) return own;
        }
        return _byId[idx];
    }
}
