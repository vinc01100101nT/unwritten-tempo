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

    /// <summary>Must the machine that owns this body SAY OUT LOUD that it went into this stance, or can every
    /// other machine already work it out for itself?
    ///
    /// <para>Two kinds answer NO, for two different reasons, and everything else answers YES. A travel gear is
    /// worked out from how fast the body is moving and which gear it is in, both of which already cross the wire —
    /// saying it again would be a second owner of one answer. A weapon swing is chosen by the SERVER, from the
    /// weapon the server holds, and announced from there — saying it here as well would play one swing twice.</para>
    ///
    /// <para>Everything else — a jump, a dodge, a flinch, a death, a skill's release swing, a pose held while
    /// guarding or sitting — happens at a moment nothing else on the wire describes, so it is reported. A stance
    /// added later inherits that by default, which is the way round it should be: a new stance nobody remembers to
    /// list is one watchers still see.</para></summary>
    public static bool WatchersMustBeTold(StanceId id)
        => !IsLocomotion(id) && !WeaponStances.IsBasicAttack(id);

    /// <summary>The travel gears — the stances a body picks from its own measured speed. Named here, beside the
    /// registry that resolves every stance, so the one list lives in one place.</summary>
    public static bool IsLocomotion(StanceId id)
        => id == StanceId.Idle || id == StanceId.Walk || id == StanceId.Run || id == StanceId.Sprint;

    /// <summary>The three travel gears whose feet step over the ground — the ones whose foot-plants and walking speed
    /// are measured from their clip, and whose legs are locked to the ground.</summary>
    public static bool IsWalkCycle(StanceId id)
        => id == StanceId.Walk || id == StanceId.Run || id == StanceId.Sprint;

    /// <summary>Is this a real stance at all — a value inside the authored vocabulary? Asked of anything that
    /// arrived over the wire, where the number is whatever the sender wrote rather than something Unity picked.</summary>
    public static bool Exists(StanceId id)
    {
        EnsureLoaded();
        int idx = (int)id;
        return idx >= 0 && idx < _byId.Length;
    }

    /// <summary>Resolve a stance's binding, or null if it isn't bound. Zero-alloc array index.</summary>
    public static StanceBinding Get(StanceId id)
    {
        EnsureLoaded();
        int idx = (int)id;
        return (idx >= 0 && idx < _byId.Length) ? _byId[idx] : null;
    }
}
