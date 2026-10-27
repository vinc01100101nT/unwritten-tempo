using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ONE SHEET OF STANDING OR RUNNING WATER in a chunk of a seamless world — a river's or a pond's surface, at
/// <see cref="level"/>, over the box <see cref="min"/>..<see cref="max"/> (world X and Z, in this chunk's own
/// coordinates). The sheet is drawn by the <c>UberStylizedWater</c> shader; this only lets the rest of the game ask where
/// the water is, and hands the shaders the rings that bodies wading through it leave behind (read only by
/// <c>Unwritten/SeamlessWater</c>, which no sheet uses now).
///
/// <para><b>Display only.</b> Nothing here touches a game number: a foot in the water makes a splash and a ring on the
/// screen of the machine that sees it, and nothing else. Nobody is slowed, wetted or hurt by it.</para>
///
/// <para><b>Cost.</b> A sheet registers itself when its chunk is built and leaves when its chunk goes; asking whether a
/// spot is under water walks the sheets loaded right now (a handful). A wading step writes one entry into a ring of
/// eight and hands the eight to the shaders — once per step, never per frame.</para>
/// </summary>
public class WaterSheet : MonoBehaviour
{
    [Tooltip("The height of the water's surface, in this chunk's own coordinates.")]
    public float level;
    [Tooltip("The south-west corner of the box the sheet covers, x and z, in this chunk's own coordinates.")]
    public Vector2 min;
    [Tooltip("The north-east corner of the box the sheet covers, x and z, in this chunk's own coordinates.")]
    public Vector2 max;

    static readonly List<WaterSheet> sheets = new List<WaterSheet>();

    // Footsteps are listened to only while any water is loaded, so a map with none pays nothing for them.
    void OnEnable()
    {
        if (sheets.Contains(this)) return;
        sheets.Add(this);
        if (sheets.Count == 1) FootstepTracker.Stepped += OnStep;
    }

    void OnDisable()
    {
        if (!sheets.Remove(this)) return;
        if (sheets.Count == 0) FootstepTracker.Stepped -= OnStep;
    }

    // A foot coming down in the water leaves a ring on it. The footstep splash particles stay off, as they are on
    // rain puddles (WaterStep): the ring is the whole of it.
    static void OnStep(Vector3 foot, float size)
    {
        if (Wading(foot, out float surface)) Ripple(new Vector3(foot.x, surface, foot.z), size);
    }

    /// <summary>TRUE when <paramref name="foot"/> (world position) stands in water: inside a loaded sheet's box and below
    /// its surface. Ground above the surface is never water, so a sheet reaching in under its banks answers false there.</summary>
    public static bool Wading(Vector3 foot, out float surface)
    {
        for (int i = 0; i < sheets.Count; i++)
        {
            var s = sheets[i];
            if (s == null) continue;
            // The chunk may be set down somewhere other than where it was built, so its box moves with it.
            Vector3 at = s.transform.position;
            float x = foot.x - at.x, z = foot.z - at.z, top = s.level + at.y; // server-math: where water is drawn, for a ripple on screen — never a game number
            if (x < s.min.x || x > s.max.x || z < s.min.y || z > s.max.y) continue;
            if (foot.y > top + 0.02f) continue;
            surface = top;
            return true;
        }
        surface = 0f;
        return false;
    }

    // ---- rings on the water -------------------------------------------------------------------------------------

    const int MaxRipples = 8;
    static readonly Vector4[] ripples = new Vector4[MaxRipples];
    static int nextRipple;
    static readonly int RipplesID = Shader.PropertyToID("_WaterRipples");

    /// <summary>A ring spreading from <paramref name="at"/> on the water, starting now, as big as <paramref name="size"/>
    /// (a step is 1, a landing more). The oldest of the last eight gives way.</summary>
    public static void Ripple(Vector3 at, float size)
    {
        ripples[nextRipple] = new Vector4(at.x, at.z, Time.timeSinceLevelLoad, size);
        nextRipple = (nextRipple + 1) % MaxRipples;
        Shader.SetGlobalVectorArray(RipplesID, ripples);
    }
}
