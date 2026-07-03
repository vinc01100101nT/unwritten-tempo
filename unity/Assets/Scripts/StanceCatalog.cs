using UnityEngine;

/// <summary>
/// The live registry that resolves a <see cref="StanceId"/> to its authored <see cref="StanceBinding"/>. Loads the
/// single <see cref="StanceLibrary"/> asset from <c>Resources/StanceLibrary.asset</c> ONCE and indexes it into a
/// flat array keyed by the enum's int value — so a per-frame lookup in <see cref="StanceController"/> is an array
/// index, not a dictionary hash or any allocation. The stance sibling of <see cref="VfxCueCatalog"/>.
///
/// Loading is lazy + cached; call <see cref="Reload"/> after editing the library in the Stance Composer.
/// </summary>
public static class StanceCatalog
{
    const string ResourcePath = "StanceLibrary";

    static StanceBinding[] _byId;   // indexed by (int)StanceId; null slot = unbound stance

    static void EnsureLoaded()
    {
        if (_byId != null) return;
        int count = System.Enum.GetValues(typeof(StanceId)).Length;
        _byId = new StanceBinding[count];
        var lib = Resources.Load<StanceLibrary>(ResourcePath);
        if (lib != null && lib.stances != null)
        {
            for (int i = 0; i < lib.stances.Count; i++)
            {
                var b = lib.stances[i];
                if (b == null) continue;
                int idx = (int)b.id;
                if (idx >= 0 && idx < _byId.Length) _byId[idx] = b;   // last binding for an id wins
            }
        }
        // Fill any id the asset hasn't authored yet with its built-in default, so the game animates correctly even
        // before the library is opened + saved (StanceDefaults is the single owner of these).
        for (int i = 0; i < _byId.Length; i++)
            if (_byId[i] == null) _byId[i] = StanceDefaults.For((StanceId)i);
    }

    /// <summary>Drop the cache so the next access reloads from Resources (use after editing the library).</summary>
    public static void Reload() { _byId = null; }

    /// <summary>Resolve a stance's binding, or null if it isn't bound. Zero-alloc array index.</summary>
    public static StanceBinding Get(StanceId id)
    {
        EnsureLoaded();
        int idx = (int)id;
        return (idx >= 0 && idx < _byId.Length) ? _byId[idx] : null;
    }
}
