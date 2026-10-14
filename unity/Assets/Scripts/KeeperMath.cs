using UnityEngine;

/// <summary>
/// THE KEEPER'S NUMBERS — what a trip costs, where it lands you, how much storage holds — as plain functions of plain
/// numbers, so every one of them can be checked without standing the game up (tools/game-math-tests/Checks.cs).
/// Read by the server only: a player's machine is sent the finished prices.
/// </summary>
public static class KeeperMath
{
    /// <summary>A TELEPORT IS PRICED IN KILLS OF INCOME (decided 2026-10-14): each door on the way costs this many kills
    /// of a creature at the traveller's own level, a kill being worth <see cref="CreatureMaterials.GoldPerLevel"/> gold a
    /// level — 40 gold a door at level 1, 3,960 at level 99.</summary>
    public const int KillsPerDoor = 4;

    /// <summary>How many separate things an account's storage holds: a stack of one item is one, each piece of gear is
    /// one. Sent with every move, so the character server refuses against the same number.</summary>
    public const int StorageLimit = 600;

    /// <summary>How far in front of the destination Keeper a trip sets you down, in metres along the way they face.</summary>
    public const float ArrivalAhead = 1.5f;

    /// <summary>How far from a door's centre a player may be and still be walking through it, in metres. The door's
    /// trigger is 1.3 m across; this leaves room for the player's own width and for a position that is a tenth of a
    /// second old.</summary>
    public const float DoorReach = 3f;

    /// <summary>Seconds between two reads of one player's storage — opening the window more often than this is shown
    /// the copy already held.</summary>
    public const float StorageReadGap = 1f;

    /// <summary>The price of a trip that takes <paramref name="doors"/> doors on foot, for a traveller of
    /// <paramref name="level"/>. Nothing for no doors (the same town), and never below nothing.</summary>
    public static long TeleportPrice(int doors, int level) =>
        doors <= 0 ? 0L : (long)KillsPerDoor * CreatureMaterials.GoldPerLevel * System.Math.Max(1, level) * doors;

    /// <summary>Where a trip to a Keeper standing at <paramref name="home"/>, facing <paramref name="facingDegrees"/>
    /// (clockwise from north), sets you down: <paramref name="ahead"/> metres in front of them, at their height.</summary>
    public static Vector3 ArrivalSpot(Vector3 home, float facingDegrees, float ahead)
    {
        float r = facingDegrees * Mathf.Deg2Rad;
        return new Vector3(home.x + Mathf.Sin(r) * ahead, home.y, home.z + Mathf.Cos(r) * ahead);
    }

    /// <summary>Is a point <paramref name="dx"/>, <paramref name="dz"/> metres from something within
    /// <paramref name="reach"/> of it — measured flat, on the ground.</summary>
    public static bool WithinReach(float dx, float dz, float reach) => dx * dx + dz * dz <= reach * reach;

    /// <summary>Would storing one more thing fit: always when it joins a stack already stored, otherwise only while
    /// fewer than <paramref name="limit"/> things are stored.</summary>
    public static bool Fits(int storedNow, int limit, bool needsNewRow) => !needsNewRow || storedNow < limit;

    /// <summary>Every position of <paramref name="labels"/> whose label is 1 to <paramref name="highest"/>, grouped by
    /// label in order (label 0, "not walkable", is left out): label L's positions are the returned array from
    /// <paramref name="firstOf"/>[L] up to <paramref name="firstOf"/>[L + 1]. Built once when a map's areas are labelled,
    /// so a random walkable cell of one area is one lookup: roll k below that area's count, read entry
    /// <paramref name="firstOf"/>[L] + k.</summary>
    public static int[] GroupByLabel(ushort[] labels, int highest, out int[] firstOf)
    {
        firstOf = new int[System.Math.Max(0, highest) + 2];
        if (labels == null) return new int[0];
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] > 0 && labels[i] <= highest) firstOf[labels[i] + 1]++;
        for (int l = 1; l < firstOf.Length; l++) firstOf[l] += firstOf[l - 1];
        var grouped = new int[firstOf[firstOf.Length - 1]];
        var next = (int[])firstOf.Clone();
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] > 0 && labels[i] <= highest) grouped[next[labels[i]]++] = i;
        return grouped;
    }
}
