using UnityEngine;

/// <summary>
/// Per-map minimap backdrop: a pre-baked top-down image of THIS map plus the world rectangle it
/// covers, so the HUD <see cref="Minimap"/> can show a clean 2D chart instead of running a live
/// second camera. Baked at author-time by <c>Tools ▸ unwritten ▸ Bake Minimap (active map)</c>
/// (<see cref="MinimapBaker"/>), which fills these fields and drops this component on a map-scene root.
///
/// One map is loaded under <c>Systems</c> at a time, so the active map's component registers itself as
/// <see cref="Current"/> on enable; the HUD reads <see cref="Current"/> every frame and falls back to
/// its live render-texture camera when a map has no bake yet.
///
/// <para>Coordinate convention: the image is a straight-down render, so image-right = world +X and
/// image-up = world +Z (fixed-north). <see cref="worldMin"/> is the (x,z) of the image's lower-left
/// corner and <see cref="worldSize"/> is the square's edge length in world units — together they map any
/// world (x,z) to a 0..1 UV on the image.</para>
/// </summary>
public class MinimapData : MonoBehaviour
{
    /// <summary>The map whose backdrop the HUD should currently show (null → use the live camera).</summary>
    public static MinimapData Current { get; private set; }

    [Tooltip("Baked top-down snapshot of this map (set by the Bake Minimap tool).")]
    public Texture2D image;

    [Tooltip("World-space (x, z) of the image's lower-left corner.")]
    public Vector2 worldMin;

    [Tooltip("Edge length (world units) of the square the image covers.")]
    public float worldSize = 100f;

    void OnEnable()  { Current = this; }
    void OnDisable() { if (Current == this) Current = null; }
}
