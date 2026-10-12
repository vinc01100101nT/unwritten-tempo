using UnityEngine;
using UnityMeshSimplifier;

/// <summary>
/// THE ONE WAY A MESH IS MADE SIMPLER here: quadric-error decimation (UnityMeshSimplifier) steered toward a target
/// triangle count. The decimate window (<see cref="MeshDecimateWindow"/>) and the far-prop level-of-detail baker
/// (<see cref="PropLodBaker"/>) both call it, so a mesh reduced by hand and one reduced in bulk come out the same way.
/// </summary>
public static class MeshDecimation
{
    public struct Options
    {
        public bool preserveBorders, preserveUvSeams, preserveUvFoldover, smartLink;

        /// <summary>What the decimate window starts with: borders and UV seams kept, so a prop keeps its outline and its
        /// texture does not tear.</summary>
        public static Options Default => new Options { preserveBorders = true, preserveUvSeams = true, smartLink = true };
    }

    /// <summary>A simpler copy of <paramref name="src"/> with about <paramref name="target"/> triangles, or null when it
    /// already has that few. The simplifier's ratio does not land exactly on a count, so the ratio is corrected up to
    /// twice toward the target and stops within 7% of it.</summary>
    public static Mesh Decimate(Mesh src, int target, Options o)
    {
        int srcTris = Tris(src);
        if (srcTris <= target) return null;

        Mesh best = null;
        float quality = Mathf.Clamp01((float)target / srcTris);
        for (int pass = 0; pass < 3; pass++)
        {
            var ms = new MeshSimplifier
            {
                PreserveBorderEdges = o.preserveBorders,
                PreserveUVSeamEdges = o.preserveUvSeams,
                PreserveUVFoldoverEdges = o.preserveUvFoldover,
                EnableSmartLink = o.smartLink,
                MaxIterationCount = 100,
            };
            ms.Initialize(src);
            ms.SimplifyMesh(Mathf.Clamp01(quality));
            best = ms.ToMesh();
            best.name = src.name;

            int got = Tris(best);
            if (got <= 0) break;
            if (Mathf.Abs(got - target) <= target * 0.07f) break;
            quality *= (float)target / got;
        }
        return best;
    }

    public static int Tris(Mesh m)
    {
        if (m == null) return 0;
        long n = 0;
        for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
        return (int)n;
    }
}
