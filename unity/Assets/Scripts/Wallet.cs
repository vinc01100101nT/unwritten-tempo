using System;
using UnityEngine;

/// <summary>
/// Global Zeny (gold) store. A plain static class — no MonoBehaviour overhead, no
/// scene dependency. Any system can call <see cref="Add"/> or read <see cref="Gold"/>;
/// HUD components subscribe to <see cref="OnChanged"/> to refresh their display.
///
/// Persistence: <see cref="Capture"/> / <see cref="Restore"/> are called by SaveManager
/// so the wallet survives scene transitions and game restarts (M3 save slice).
/// </summary>
public static class Wallet
{
    /// <summary>Fires whenever the gold amount changes. Arg = new total.</summary>
    public static event Action<int> OnChanged;

    static int _gold;

    /// <summary>Current Zeny balance.</summary>
    public static int Gold => _gold;

    /// <summary>Add (or subtract) Zeny. Amount may be negative (purchase, fee).</summary>
    public static void Add(int amount)
    {
        _gold = Mathf.Max(0, _gold + amount);
        OnChanged?.Invoke(_gold);
    }

    /// <summary>Directly set the balance (e.g. after loading a save). Fires OnChanged.</summary>
    public static void Set(int amount)
    {
        _gold = Mathf.Max(0, amount);
        OnChanged?.Invoke(_gold);
    }

    /// <summary>Export current balance into a save snapshot.</summary>
    public static int Capture() => _gold;

    /// <summary>Restore balance from a save snapshot (silently, no OnChanged spam on load).</summary>
    public static void Restore(int saved) => _gold = Mathf.Max(0, saved);
}
