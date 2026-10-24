using System.Collections.Generic;

/// <summary>
/// THE INK CRATES — which crate a kill drops, how often, and what one opens into. Plain lookups and sums over the saved
/// items, so the kill's roll (<see cref="LootDropper"/>), what <c>/mobinfo</c> says (<see cref="CreatureDrops"/>) and the
/// opening (<c>NetPlayer.CmdOpenCrate</c>) all read one answer.
///
/// <para><b>Which crate:</b> the rarity of the creature's own material (<see cref="CreatureMaterials.TierOfMultiplier"/>).</para>
/// <para><b>What is inside, one piece:</b> <see cref="MaterialShare"/> a creature material of the crate's rarity — any
/// creature's, anywhere — <see cref="ForgingShare"/> a forging material of that rarity (Ink, Salt, Temper Dust, Quench
/// Oil), <see cref="ConsumableShare"/> one of the crate's own potions and foods. A share with nothing in it is spread
/// over the others.</para>
/// <para><b>How often:</b> a crate is worth, on average, <see cref="KillShare"/> of what the kill already pays
/// (<see cref="CreatureMaterials.GoldPerLevel"/> × level × multiplier, in what a shop pays). A weak creature drops a crate
/// rarely and a strong one often, so no creature is the cheap way to crates.</para>
/// </summary>
public static class CrateMath
{
    public const float MaterialShare = 0.55f;
    public const float ForgingShare = 0.20f;
    public const float ConsumableShare = 0.25f;
    public const float KillShare = 0.10f;

    const int Tiers = (int)Rarity.Legendary + 1;

    /// <summary>The crate's rarity a <paramref name="kind"/> of <paramref name="multiplier"/> drops.</summary>
    public static Rarity TierOf(CreatureKind kind, float multiplier)
        => CreatureMaterials.TierOfMultiplier(kind, multiplier, out var tier) ? tier : Rarity.Common;

    /// <summary>The chance (0–1) one kill drops its crate, from what the kill pays and the crate's average.</summary>
    public static float DropChance(int level, float multiplier, double crateAverage)
    {
        if (crateAverage <= 0) return 0f;
        double killWorth = (double)CreatureMaterials.GoldPerLevel * System.Math.Max(1, level) * CreaturePay.Clamp(multiplier);
        return (float)System.Math.Min(1.0, KillShare * killWorth / crateAverage);
    }

    /// <summary>The average a shop pays for what one crate holds, given each share's items' sale pays.</summary>
    public static double Average(IReadOnlyList<int> materialPays, IReadOnlyList<int> forgingPays, IReadOnlyList<int> consumablePays)
    {
        Shares(materialPays.Count, forgingPays.Count, consumablePays.Count, out float m, out float f, out float c);
        return m * Mean(materialPays) + f * Mean(forgingPays) + c * Mean(consumablePays);
    }

    /// <summary>The three shares, with any share that has no items spread over the others in proportion.</summary>
    public static void Shares(int materials, int forging, int consumables, out float m, out float f, out float c)
    {
        m = materials > 0 ? MaterialShare : 0f;
        f = forging > 0 ? ForgingShare : 0f;
        c = consumables > 0 ? ConsumableShare : 0f;
        float sum = m + f + c;
        if (sum <= 0f) { m = f = c = 0f; return; }
        m /= sum; f /= sum; c /= sum;
    }

    /// <summary>Which share a roll of <paramref name="r01"/> lands in: 0 material, 1 forging, 2 consumable, -1 none.</summary>
    public static int ShareOf(float r01, int materials, int forging, int consumables)
    {
        Shares(materials, forging, consumables, out float m, out float f, out float c);
        if (m + f + c <= 0f) return -1;
        if (r01 < m) return 0;
        if (r01 < m + f) return 1;
        return consumables > 0 ? 2 : forging > 0 ? 1 : 0;
    }

    static double Mean(IReadOnlyList<int> v)
    {
        if (v.Count == 0) return 0;
        double s = 0;
        for (int i = 0; i < v.Count; i++) s += v[i];
        return s / v.Count;
    }

    // ---- the saved items, filed once ------------------------------------------------------------------------------

    /// <summary>What a crate of one rarity can hold, and the crate itself.</summary>
    public sealed class Pool
    {
        public Item crate;
        public readonly List<Item> materials = new List<Item>();
        public readonly List<Item> forging = new List<Item>();
        public readonly List<Item> consumables = new List<Item>();
        public double average;

        public bool Empty => materials.Count + forging.Count + consumables.Count == 0;

        /// <summary>Every item this crate can open into, each once.</summary>
        public IEnumerable<Item> All()
        {
            foreach (var i in materials) yield return i;
            foreach (var i in forging) yield return i;
            foreach (var i in consumables) yield return i;
        }
    }

    static Pool[] pools;

    /// <summary>The pool of crates of <paramref name="tier"/>, built from the saved items on first ask and kept.</summary>
    public static Pool PoolOf(Rarity tier)
    {
        if (pools == null) Build();
        int t = (int)tier;
        return t >= 0 && t < Tiers ? pools[t] : null;
    }

    /// <summary>The crate a kill of <paramref name="kind"/> of <paramref name="multiplier"/> drops, or null.</summary>
    public static Item CrateFor(CreatureKind kind, float multiplier) => PoolOf(TierOf(kind, multiplier))?.crate;

    /// <summary>The chance one kill of <paramref name="def"/> drops its crate.</summary>
    public static float DropChanceOf(MonsterDef def)
    {
        if (def == null) return 0f;
        var pool = PoolOf(TierOf(def.kind, def.multiplier));
        return pool == null || pool.crate == null ? 0f : DropChance(def.level, def.multiplier, pool.average);
    }

    /// <summary>What one crate of <paramref name="pool"/> opens into, from two rolls in 0–1.</summary>
    public static Item Roll(Pool pool, float shareRoll, float pickRoll)
    {
        if (pool == null) return null;
        List<Item> from;
        switch (ShareOf(shareRoll, pool.materials.Count, pool.forging.Count, pool.consumables.Count))
        {
            case 0: from = pool.materials; break;
            case 1: from = pool.forging; break;
            case 2: from = pool.consumables; break;
            default: return null;
        }
        int i = (int)(pickRoll * from.Count);
        return from[System.Math.Min(from.Count - 1, System.Math.Max(0, i))];
    }

    /// <summary>Forget the pools, so a newly authored item is picked up. Called with <see cref="ItemCatalog.Reload"/>.</summary>
    public static void Reload() => pools = null;

    static void Build()
    {
        pools = new Pool[Tiers];
        for (int t = 0; t < Tiers; t++) pools[t] = new Pool();
        foreach (var it in ItemCatalog.All)
        {
            if (it == null) continue;
            if (it.opensAsCrate)
            {
                int ct = (int)it.crateRarity;
                if (ct >= 0 && ct < Tiers && pools[ct].crate == null) pools[ct].crate = it;
                continue;
            }
            int r = (int)Rarities.Of(it);
            if (it.materialKind == MaterialKind.Creature && CreatureMaterials.Is(it)) pools[r].materials.Add(it);
            else if (IsForging(it.materialKind)) pools[r].forging.Add(it);
        }
        var mp = new List<int>(); var fp = new List<int>(); var cp = new List<int>();
        foreach (var pool in pools)
        {
            if (pool.crate != null && pool.crate.crateConsumables != null)
                foreach (var c in pool.crate.crateConsumables)
                    if (c != null && !pool.consumables.Contains(c)) pool.consumables.Add(c);
            Pays(pool.materials, mp); Pays(pool.forging, fp); Pays(pool.consumables, cp);
            pool.average = Average(mp, fp, cp);
        }
    }

    static bool IsForging(MaterialKind k) =>
        k == MaterialKind.Ink || k == MaterialKind.Salt || k == MaterialKind.TemperDust || k == MaterialKind.QuenchOil;

    static void Pays(List<Item> items, List<int> into)
    {
        into.Clear();
        foreach (var i in items) into.Add((int)ShopMath.SalePay(i.value, 1));
    }
}
