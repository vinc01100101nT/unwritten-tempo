using UnityEngine;

/// <summary>
/// Shared <b>placement engine</b> for hand-authoring 2.5D maps — the reusable core under the Prop Palette,
/// the Authoring menu commands, and the town baker. All placement is BOUNDS-DRIVEN: props are positioned by
/// their measured world renderer bounds (footprint centre + bottom-of-bounds), never by their pivot, because
/// the extracted Pxltgr meshes have their origin offset from the geometry (pivot-placement floats them).
/// </summary>
public static class PropPlacement
{
    public static int GroundLayer => LayerMask.NameToLayer("Ground");
    public static int GroundMask() { int g = GroundLayer; return g >= 0 ? 1 << g : ~0; }

    /// <summary>Combined world-space renderer bounds of a live instance (true visual footprint, any pivot).</summary>
    public static bool TryWorldBounds(GameObject go, out Bounds b)
    {
        b = new Bounds(go.transform.position, Vector3.zero);
        bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return has;
    }

    /// <summary>Ground surface Y under (x,z) — raycast down on the ground layer(s). Falls back to 0.</summary>
    public static float GroundY(float x, float z, int mask)
    {
        if (Physics.Raycast(new Vector3(x, 500f, z), Vector3.down, out var hit, 2000f, mask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return 0f;
    }

    /// <summary>Move <paramref name="go"/> so its footprint CENTRE sits at <paramref name="at"/>'s XZ and the
    /// BOTTOM of its bounds rests on the ground under that XZ. Rotation/scale should already be set.</summary>
    public static void PlaceOnGround(GameObject go, Vector3 at, int mask)
    {
        go.transform.position = Vector3.zero;
        float gy = GroundY(at.x, at.z, mask);
        if (TryWorldBounds(go, out var b))
            go.transform.position = new Vector3(at.x - b.center.x, gy - b.min.y, at.z - b.center.z);
        else
            go.transform.position = new Vector3(at.x, gy, at.z);
    }

    /// <summary>Keep XZ where it is; drop the bottom of the bounds onto the ground (re-seat after a manual nudge).</summary>
    public static void DropToGround(GameObject go, int mask)
    {
        if (!TryWorldBounds(go, out var b)) return;
        float gy = GroundY(b.center.x, b.center.z, mask);
        go.transform.position += new Vector3(0f, gy - b.min.y, 0f);
    }

    /// <summary>Small ground flora the player should walk THROUGH (no collider) — grass, flowers, ferns,
    /// mushrooms, etc. Matched by name so it works on any instance of the Pxltgr decor prefabs. Trees,
    /// bushes, houses, carts are NOT decor (they stay solid blockers).</summary>
    public static bool IsWalkThroughDecor(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        name = name.ToLowerInvariant();
        return name.Contains("grass") || name.Contains("flower") || name.Contains("fern")
            || name.Contains("mushroom") || name.Contains("plant") || name.Contains("clover")
            || name.Contains("reed")  || name.Contains("ivy")    || name.Contains("leaf")
            || name.Contains("weed");
    }

    /// <summary>Remove every collider on a prop (and its children). Returns how many were removed.
    /// Editor-time only — used to make decor walk-through so it never blocks the player or pathfinder
    /// (the pathfinder treats anything with a collider off the Ground layer as a wall).</summary>
    public static int StripColliders(GameObject go)
    {
        var cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) Object.DestroyImmediate(cols[i]);
        return cols.Length;
    }

    /// <summary>Round XZ to a grid (Y untouched).</summary>
    public static Vector3 SnapToGrid(Vector3 p, float grid)
    {
        if (grid <= 0f) return p;
        p.x = Mathf.Round(p.x / grid) * grid;
        p.z = Mathf.Round(p.z / grid) * grid;
        return p;
    }
}
