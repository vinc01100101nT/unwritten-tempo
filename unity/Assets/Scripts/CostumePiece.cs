using UnityEngine;

/// <summary>
/// A costume — the model of a Costume Body item — made by <c>CostumeBaker</c>. It sits on the root of the costume's
/// prefab and holds the costume ONCE, fitted in the space every body shares, and the bodies it was fitted to.
/// <see cref="CostumeModels"/> joins it with the wearer's whole body into the one model a wearer is drawn with.
///
/// <para><b>Why joined, and why at runtime.</b> A piece that bends with the body as a model of its own costs a whole
/// extra bending model per person: measured with 200 people, hair worn that way took 22.6 ms a frame. Joined, each
/// person stays one bending model. Stored joined, every body's copy loaded with the item at game start — 537,016 bytes
/// each, measured 2026-10-16 — so it is joined the first time a body wears it, and shared by everyone wearing that
/// pair.</para>
/// </summary>
public class CostumePiece : MonoBehaviour
{
    [System.Serializable]
    public class Fit
    {
        [Tooltip("The name of the plain body model this fit was made for.")]
        public string bodyMesh;
    }

    [Tooltip("The costume alone, in the space every body shares. Its bone weights point into `bones`.")]
    public Mesh costume;

    [Tooltip("The names of the bones the costume's bone weights point at, in that order. Each body lists its bones in " +
             "its own order, so they are matched by name.")]
    public string[] bones = System.Array.Empty<string>();

    [Tooltip("The bodies the costume was fitted to: only these can wear it.")]
    public Fit[] fits = System.Array.Empty<Fit>();

    [Tooltip("What the costume part is drawn with. It keeps its own colours: the wearer's clothes colours are not laid " +
             "on it.")]
    public Material material;

    /// <summary>The fit for the body whose own model is named <paramref name="bodyMesh"/>; null when the costume was
    /// never fitted to that body.</summary>
    public Fit FitFor(string bodyMesh)
    {
        for (int i = 0; i < fits.Length; i++)
            if (fits[i] != null && fits[i].bodyMesh == bodyMesh) return fits[i];
        return null;
    }
}
