/// <summary>
/// Where a person stands back up after dying, and with how much health. A character's own save point is set with
/// the GM's <c>/save</c> and stored on the character; one that has never been set comes back here instead — the
/// arrival point beside Lastline's Central Fountain, just south of its Keeper (moved there 2026-10-20).
///
/// <para>The DEFAULT is named by map and arrival-point id, never by a position: the scene owns where that point
/// stands, so moving it in the map moves where the defaultless come back, with nothing here to keep in step.</para>
/// </summary>
public static class SavePoint
{
    public const string DefaultMap = "world";
    public const string DefaultSpawnId = "center";

    /// <summary>The health a person stands back up with: a tenth of their maximum, never less than one point.
    /// Mana is not touched by standing up — it comes back exactly as it was.</summary>
    public static int RespawnHealth(int maxHP) => System.Math.Max(1, maxHP / 10);
}
