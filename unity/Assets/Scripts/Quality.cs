/// <summary>
/// What a crafted piece came out of its QUENCH as — the one grade every weapon and every piece of armour carries.
/// <see cref="Raw"/> is a freshly forged piece that has never been quenched; after that the ladder is F (poorest)
/// to A (highest), climbed ONE RUNG AT A TIME. There is no S, SS or anything above A.
///
/// <para>The order is the ladder, so a grade can be stepped with +1 / −1 and compared with &lt; and &gt;.
/// The integer values are what the save and the wire carry — never renumber them.</para>
/// </summary>
public enum Quality { Raw = 0, F = 1, E = 2, D = 3, C = 4, B = 5, A = 6 }

/// <summary>
/// The eighteen things one Ink can write onto a piece: TWELVE sub-stats, then the six primaries. A primary is far
/// rarer to roll and worth far more (see <see cref="CraftingMath.InkValue"/>).
///
/// <para>One Ink writes ONE sub-stat. Two of the ring's ten nodes move a pair between them — its Defense node moves
/// both defences and its Critical node moves both crit chance and crit damage — so those four are separate here:
/// a written stat must name exactly the one thing it moves.</para>
///
/// <para>The integer values are what the save and the wire carry — never renumber them.</para>
/// </summary>
public enum InkStat
{
    Defense = 0, HP = 1, Attack = 2, Accuracy = 3, CastSpeed = 4, MagicAttack = 5, MP = 6, Evasion = 7,
    CritChance = 8, AttackSpeed = 9,
    STR = 10, AGI = 11, VIT = 12, INT = 13, DEX = 14, LUK = 15,
    MagicDefense = 16, CritDamage = 17,
}
