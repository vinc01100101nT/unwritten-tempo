using UnityEngine;

/// <summary>
/// Allegiance marker for friendly units (allies, mercenaries / companions, and — later —
/// other players). It's the reusable seam for right-click ordering of friendlies:
///   • the player CANNOT attack a unit with this marker — <see cref="PlayerAttacker.MonsterUnderCursor"/>
///     ignores it (friendlies have no <see cref="MonsterAI"/>); and
///   • right-clicking one routes to FOLLOW, via <see cref="PlayerAttacker.FriendlyUnitUnderCursor"/>
///     → <see cref="PlayerCommander"/>.
/// Add this to any friendly character; monsters must NOT have it.
///
/// REUSE NOTE (for a future mercenary): this marker is the stable contract — tag a unit with
/// it and it's automatically non-attackable + right-click-followable. But it is ONLY a marker.
/// "Follow" here means the PLAYER follows the unit; a real companion that follows YOU and fights
/// needs its own brain — most cheaply a CompanionAI sibling of <see cref="MonsterAI"/> with its
/// target flipped from the player to the nearest monster — plus <see cref="Health"/> and a faction
/// so monsters aggro it.
/// </summary>
public class FriendlyUnit : MonoBehaviour { }
