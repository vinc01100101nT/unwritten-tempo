using UnityEngine;

/// <summary>
/// The live registry that resolves a <see cref="VfxCueId"/> to its authored <see cref="VfxCueBinding"/>. Loads the
/// single <see cref="VfxCueLibrary"/> asset from <c>Resources/VfxCueLibrary.asset</c> ONCE and indexes it into a
/// flat array keyed by the enum's int value — so a lookup on the per-hit / per-tick path is an array index, not a
/// dictionary hash or any allocation. Mirrors <see cref="EffectCatalog"/>.
///
/// Play cues through <see cref="VfxCue"/> (the front door); this class only resolves bindings. Loading is lazy +
/// cached; call <see cref="Reload"/> after editing the library in the Vfx Cue Composer.
/// </summary>
public static class VfxCueCatalog
{
    const string ResourcePath = "VfxCueLibrary";

    static VfxCueBinding[] _byId;   // indexed by (int)VfxCueId; null slot = unbound cue

    static void EnsureLoaded()
    {
        if (_byId != null) return;
        int count = System.Enum.GetValues(typeof(VfxCueId)).Length;
        _byId = new VfxCueBinding[count];
        var lib = Resources.Load<VfxCueLibrary>(ResourcePath);
        if (lib == null || lib.cues == null) return;
        for (int i = 0; i < lib.cues.Count; i++)
        {
            var b = lib.cues[i];
            if (b == null) continue;
            int idx = (int)b.id;
            if (idx >= 0 && idx < _byId.Length) _byId[idx] = b;   // last binding for an id wins
        }
    }

    /// <summary>Drop the cache so the next access reloads from Resources (use after editing the library).</summary>
    public static void Reload() { _byId = null; }

    /// <summary>Resolve a cue's binding, or null if it isn't bound (a silent no-op cue). Zero-alloc array index.</summary>
    public static VfxCueBinding Get(VfxCueId id)
    {
        EnsureLoaded();
        int idx = (int)id;
        return (idx >= 0 && idx < _byId.Length) ? _byId[idx] : null;
    }
}
