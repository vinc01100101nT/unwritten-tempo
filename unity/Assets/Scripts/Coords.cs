using System.Globalization;
using UnityEngine;

/// <summary>
/// HOW A POSITION IS WRITTEN FOR A PERSON: x runs west to east, y south to north, z is standing height.
///
/// <para>The engine counts the other way round — its second number is height and its third is north — and it has to:
/// physics, cameras and rendering are all built on it. So the engine's order is never shown to anybody, and nobody
/// types it. Every place a position is shown to someone, or read from what someone typed, goes through here, so the
/// two orders meet in exactly one file.</para>
/// </summary>
public static class Coords
{
    /// <summary>An engine position in the order a person reads it: (x, north, height).</summary>
    public static Vector3 ToShown(Vector3 engine) => new Vector3(engine.x, engine.z, engine.y);

    /// <summary>A position a person wrote, (x, north, height), in the engine's order.</summary>
    public static Vector3 FromShown(Vector3 shown) => new Vector3(shown.x, shown.z, shown.y);

    /// <summary>A spot on the ground a person named by x and y, at the given height.</summary>
    public static Vector3 FromShown(float x, float y, float height) => new Vector3(x, height, y);

    /// <summary>"x 1.2, y 3.4, z 0" — an engine position written for a person.</summary>
    public static string Text(Vector3 engine, string format = "0.#")
    {
        var c = CultureInfo.InvariantCulture;
        return "x " + engine.x.ToString(format, c) + ", y " + engine.z.ToString(format, c)
             + ", z " + engine.y.ToString(format, c);
    }
}

/// <summary>Draws a position field in the order a person reads it (x, y north, z height) while the engine's order is
/// what is saved — so numbers copied from <c>/where</c> go straight in.</summary>
public class ShownCoordsAttribute : PropertyAttribute { }
