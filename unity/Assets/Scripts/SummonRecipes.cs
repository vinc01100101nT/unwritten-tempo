using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT EACH SUMMON-ONLY BOSS COSTS at an Elite Summoner: a list of creature materials and how many of each. Being named
/// here is what makes a creature summon-only — the world never spawns it on its own (<see cref="FieldMobSpawner"/>
/// refuses it). Loaded from Resources/SummonRecipes.
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Summon Recipes", fileName = "SummonRecipes")]
public class SummonRecipes : ScriptableObject
{
    [System.Serializable]
    public class Need
    {
        [Tooltip("The material's item id, e.g. 'item.abyss-maw-heart'.")]
        public string itemId = "";
        [Min(1)] public int count = 1;
    }

    [System.Serializable]
    public class Recipe
    {
        [Tooltip("The boss's creature id, e.g. 'mob.blood-ronin'.")]
        public string bossId = "";
        public List<Need> needs = new List<Need>();
    }

    public List<Recipe> recipes = new List<Recipe>();

    static SummonRecipes instance;
    static bool looked;

    public static SummonRecipes Instance
    {
        get
        {
            if (!looked) { instance = Resources.Load<SummonRecipes>("SummonRecipes"); looked = true; }
            return instance;
        }
    }

    /// <summary>The recipe for <paramref name="bossId"/>, or null when it is not summoned.</summary>
    public static Recipe Find(string bossId)
    {
        var list = Instance;
        if (list == null || string.IsNullOrEmpty(bossId)) return null;
        for (int i = 0; i < list.recipes.Count; i++)
            if (list.recipes[i] != null && list.recipes[i].bossId == bossId) return list.recipes[i];
        return null;
    }

    /// <summary>True when <paramref name="bossId"/> only ever appears by summoning.</summary>
    public static bool IsSummonOnly(string bossId) => Find(bossId) != null;
}

/// <summary>The arithmetic of a summon, as plain functions over plain numbers.</summary>
public static class SummonMath
{
    /// <summary>The longest a summoned boss stays with nobody hitting it: 10 minutes.</summary>
    public const float IdleSecondsBeforeLeaving = 600f;

    /// <summary>How far a summoned boss may be led from where it appeared before it walks back and heals: 15 m.</summary>
    public const float TetherMetres = 15f;

    /// <summary>True when every need is held: <paramref name="held"/>[i] of <paramref name="needed"/>[i].</summary>
    public static bool CanPay(IReadOnlyList<int> needed, IReadOnlyList<int> held)
    {
        if (needed == null || held == null || held.Count < needed.Count) return false;
        for (int i = 0; i < needed.Count; i++)
            if (held[i] < needed[i]) return false;
        return true;
    }

    /// <summary>True when a boss that appeared at <paramref name="appearedAt"/> and was last hit at
    /// <paramref name="lastHitAt"/> (a time before it appeared means never) should leave at <paramref name="now"/>.</summary>
    public static bool ShouldLeave(float appearedAt, float lastHitAt, float now, float idleSeconds = IdleSecondsBeforeLeaving) =>
        now - Mathf.Max(appearedAt, lastHitAt) >= idleSeconds;
}
