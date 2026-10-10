using UnityEngine;

/// <summary>
/// One piece worn on a character's head — a hairstyle, or the model of a Head item — cut out of the character
/// models by <c>HeadPieceBaker</c>. It sits on the root of the piece's prefab and says the three things a body needs
/// to wear it; <see cref="HeadSocket"/> is what puts it on.
///
/// <para>Most pieces sit entirely on the head and are FIXED to the head bone, which costs nothing measurable. A piece
/// that hangs past the head onto the neck or shoulders (the hood) bends with the body instead, and names the bones
/// it follows. Measured 2026-10-10 with 200 people: a hair that bends with the body cost 22.6 ms a frame, the same
/// hair fixed to the head bone cost nothing that could be measured.</para>
/// </summary>
public class HeadPiece : MonoBehaviour
{
    [Tooltip("The piece's own colours, authored sRGB. On a hairstyle the first is the hair colour, the one a chosen " +
             "hair colour replaces.")]
    public Color[] colours = System.Array.Empty<Color>();

    [Tooltip("Headgear only: wearing it hides the hair under it — the full helmets and the hood.")]
    public bool hidesHair;

    [Tooltip("Headgear only: it covers the head, so the hair is cut away above where it sits on the head and only " +
             "the hair hanging below it shows. Off for a headband, which sits on the head without covering the hair.")]
    public bool coversHair;

    [Tooltip("Where a covering piece cuts the hair, all in the models' shared mesh space and measured by the bake: " +
             "the middle of the head, which way is up, and which way the first direction around the head points.")]
    public Vector3 cutCentre, cutUp, cutAcross;

    [Tooltip("How high the piece comes down, above the middle of the head, in each direction around it (evenly " +
             "spaced, starting at Cut Across). Hair above that height in that direction is cut away.")]
    public float[] cutRim = System.Array.Empty<float>();

    [Tooltip("A piece that bends with the body: the names of the bones its skin follows, in the order its weights " +
             "use. Empty for a piece fixed to the head bone.")]
    public string[] bones = System.Array.Empty<string>();

    public bool BendsWithBody => bones != null && bones.Length > 0;

    /// <summary>True for a renderer that belongs to a worn head piece rather than to the body under it. Whatever
    /// measures a body or reads its colours asks this, so a hat never makes a person shorter and a hair's texture
    /// never becomes part of which body somebody is wearing.</summary>
    public static bool IsPiece(Renderer r) => r != null && r.GetComponentInParent<HeadPiece>(true) != null;
}
