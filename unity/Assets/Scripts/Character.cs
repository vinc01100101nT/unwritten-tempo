using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's character data — the hub for Phase F's numbers. It lives in the persistent
/// <c>Systems</c> scene (which is never unloaded), so the player avatar, monsters, and the UI
/// all just reference <see cref="Instance"/> and it stays alive across map swaps with no
/// <c>DontDestroyOnLoad</c>. Holds level/XP, base stats + per-level growth, current HP, and
/// equipped items; effective stats = base + growth + equipment bonuses.
///
/// (No save-to-disk yet — a fresh session starts at level 1.)
/// </summary>
public class Character : MonoBehaviour
{
    public static Character Instance { get; private set; }

    [Header("Base stats (level 1)")]
    public int baseMaxHP = 20;
    public int baseAttack = 4;
    public int baseDefense = 1;

    [Header("Growth per level")]
    public int hpPerLevel = 6;
    public int attackPerLevel = 1;
    public int defensePerLevel = 1;

    public int Level { get; private set; } = 1;
    public int XP { get; private set; }
    public int CurrentHP { get; private set; }

    readonly Dictionary<EquipSlotType, Item> equipped = new Dictionary<EquipSlotType, Item>();

    static readonly Color HealColor = new Color(0.45f, 1f, 0.45f);
    Transform avatar;   // the in-scene player, located lazily for heal popups

    public event Action OnStatsChanged;    // level / equipment changed
    public event Action OnHealthChanged;   // current or max HP changed
    public event Action<int> OnLeveledUp;  // passes the new level
    public event Action OnDied;

    public int MaxHP   => baseMaxHP   + hpPerLevel      * (Level - 1) + Bonus(i => i.bonusHP);
    public int Attack  => baseAttack  + attackPerLevel  * (Level - 1) + Bonus(i => i.bonusAttack);
    public int Defense => baseDefense + defensePerLevel * (Level - 1) + Bonus(i => i.bonusDefense);
    public int XPToNext => 5 + Level * 5;
    public bool IsDead => CurrentHP <= 0;

    int Bonus(Func<Item, int> select)
    {
        int total = 0;
        foreach (var item in equipped.Values) if (item != null) total += select(item);
        return total;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        CurrentHP = MaxHP;
    }

    public Item GetEquipped(EquipSlotType slot)
        => equipped.TryGetValue(slot, out var item) ? item : null;

    public void SetEquipped(EquipSlotType slot, Item item)
    {
        if (slot == EquipSlotType.None) return;
        equipped[slot] = item;
        CurrentHP = Mathf.Min(CurrentHP, MaxHP);   // clamp if max dropped
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
    }

    /// <summary>Apply an incoming hit: damage = max(1, attackerAttack − Defense). Returns the damage dealt.</summary>
    public int TakeDamage(int attackerAttack)
    {
        if (IsDead) return 0;
        int dmg = Mathf.Max(1, attackerAttack - Defense);
        CurrentHP = Mathf.Max(0, CurrentHP - dmg);
        OnHealthChanged?.Invoke();
        if (CurrentHP == 0)
        {
            OnDied?.Invoke();
            CurrentHP = MaxHP;            // Phase F: instant revive at full HP (no death screen yet)
            OnHealthChanged?.Invoke();
        }
        return dmg;
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        int before = CurrentHP;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        int healed = CurrentHP - before;
        OnHealthChanged?.Invoke();
        if (healed > 0)
        {
            var a = Avatar();
            if (a != null) DamagePopup.SpawnText(a.position, "+" + healed, HealColor);  // green = heal
        }
    }

    // The in-scene player avatar (re-found after scene changes destroy the old one).
    Transform Avatar()
    {
        if (avatar == null)
        {
            var pc = FindFirstObjectByType<PlayerController2D>();
            avatar = pc != null ? pc.transform : null;
        }
        return avatar;
    }

    public void AddXP(int amount)
    {
        if (amount <= 0) return;
        XP += amount;
        while (XP >= XPToNext)
        {
            XP -= XPToNext;
            Level++;
            CurrentHP = MaxHP;           // level-up fully heals
            OnLeveledUp?.Invoke(Level);  // Phase G will hang the skill-draft off this
        }
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
    }
}
