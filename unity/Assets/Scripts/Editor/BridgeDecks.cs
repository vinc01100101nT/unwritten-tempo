using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A STONE BRIDGE ONE WALKS ON: the bridge model fitted to a crossing, and its own triangles laid into the ground mesh it
/// joins at both ends, drawn with the model's own materials — so the bridge IS ground: walked on, solid, and seen as the
/// one body it is (RULE ONEGROUND). Shared by the world builder (a road over a river) and the town builder (a street
/// over a town's stream), so the two can never fit a bridge two ways.
/// </summary>
public static class BridgeDecks
{
    public const string PrefabPath = "Assets/1_Authored/Prefabs/Props/Bridges/BridgeRock.prefab";
    // The walkway's middle is sampled every half metre along the crossing, this far under the model's surface: the
    // heights the ground ramps up to at each end and lifts to under the bridge's footprint.
    public const float DeckAlong = 0.5f, DeckSink = 0.03f;
    // The farthest in from an end the walkway's first whole row is looked for.
    const float MaxInset = 4f;

    /// <summary>The bridge model fitted to the crossing, and the height of its walkway's middle along it.</summary>
    public sealed class Deck
    {
        public Vector3 position;
        public Quaternion rotation;
        public float scale, halfWidth;
        public Rect footprint;
        public Vector3 start, along;
        public int rows;
        public float[] top;
        /// <summary>How far in from each end, along the crossing, the walkway is first whole across its width — the model's
        /// rounded ends leave ground under the first metre or two that no stone covers.</summary>
        public float insetFrom, insetTo;
        public bool Has(int a) => !float.IsNaN(top[a]);
    }

    // Fits the bridge model to the crossing — as long as the crossing, at its own proportions, centred on its middle at
    // the road's height — and samples its walkway's middle from above. Where a sample falls through a gap between stones
    // it takes the heights either side of it along the crossing.
    public static Deck Fit(Vector2 crossingFrom, Vector2 crossingTo, float crossingHeight, Scene preview, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new System.Exception("no bridge model at " + PrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var drawn = DrawnUpClose(go);
            var b = BoundsOf(drawn, go.transform);

            Vector2 line = crossingTo - crossingFrom;
            float length = line.magnitude;
            var u = new Vector3(line.x, 0f, line.y) / length;
            var mid = (crossingFrom + crossingTo) * 0.5f;
            var deck = new Deck { scale = length / b.size.z, rotation = Quaternion.LookRotation(u) };
            deck.halfWidth = b.extents.x * deck.scale;
            deck.position = new Vector3(mid.x, crossingHeight, mid.y) - deck.rotation * new Vector3(b.center.x, 0f, b.center.z) * deck.scale;
            go.transform.SetPositionAndRotation(deck.position, deck.rotation);
            go.transform.localScale = Vector3.one * deck.scale;
            foreach (var r in drawn)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.GetComponent<MeshCollider>();
                if (mc == null) mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            Physics.SyncTransforms();

            var world = BoundsOf(drawn, null);
            deck.footprint = Rect.MinMaxRect(world.min.x, world.min.z, world.max.x, world.max.z);
            deck.rows = Mathf.CeilToInt(length / DeckAlong) + 1;
            deck.along = u * (length / (deck.rows - 1));
            deck.start = new Vector3(crossingFrom.x, 0f, crossingFrom.y);
            deck.top = new float[deck.rows];

            var physics = preview.GetPhysicsScene();
            float from = world.max.y + 1f, reachDown = world.size.y + 2f;
            int found = 0;
            for (int a = 0; a < deck.rows; a++)
            {
                var at = deck.start + deck.along * a;
                deck.top[a] = physics.Raycast(new Vector3(at.x, from, at.z), Vector3.down, out var h, reachDown) ? h.point.y - DeckSink : float.NaN;
                if (deck.Has(a)) found++;
            }
            // A row whose middle falls through a gap between stones takes the rows either side of it.
            for (int a = 0; a < deck.rows; a++)
            {
                if (deck.Has(a)) continue;
                int lo = a - 1, hi = a + 1;
                while (lo >= 0 && !deck.Has(lo)) lo--;
                while (hi < deck.rows && !deck.Has(hi)) hi++;
                if (lo < 0 && hi >= deck.rows) continue;
                int from0 = lo >= 0 ? lo : hi, to0 = hi < deck.rows ? hi : lo;
                float w = from0 == to0 ? 0f : (a - from0) / (float)(to0 - from0);
                deck.top[a] = Mathf.Lerp(deck.top[from0], deck.top[to0], w);
            }
            // From each end inward, a quarter metre at a time, until five rays across the walkway's middle four fifths all
            // land on stone.
            var across = new Vector3(-u.z, 0f, u.x) * (deck.halfWidth * 0.8f);
            float Inset(Vector3 end, Vector3 inward)
            {
                for (float d = 0f; d <= MaxInset; d += 0.25f)
                {
                    bool whole = true;
                    for (int k = -2; k <= 2 && whole; k++)
                    {
                        var at = end + inward * d + across * (k * 0.5f);
                        whole = physics.Raycast(new Vector3(at.x, from, at.z), Vector3.down, out _, reachDown);
                    }
                    if (whole) return d;
                }
                return MaxInset;
            }
            deck.insetFrom = Inset(deck.start, u);
            deck.insetTo = Inset(new Vector3(crossingTo.x, 0f, crossingTo.y), -u);
            report.AppendLine($"Bridge: {prefab.name} at {deck.scale:0.00}x, {world.size.x:0.0} x {world.size.z:0.0} m, its walkway found under {found} of {deck.rows} rows, whole across from {deck.insetFrom:0.00} m and {deck.insetTo:0.00} m in from its ends.");
            return deck;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // The renderers drawn up close: level 0 of every switch, or all of them when the model does not switch.
    public static List<Renderer> DrawnUpClose(GameObject go)
    {
        var drawn = new List<Renderer>();
        var groups = go.GetComponentsInChildren<LODGroup>(true);
        foreach (var g in groups)
        {
            var lods = g.GetLODs();
            if (lods.Length > 0) foreach (var r in lods[0].renderers) if (r != null) drawn.Add(r);
        }
        if (groups.Length == 0) drawn.AddRange(go.GetComponentsInChildren<Renderer>(true));
        return drawn;
    }

    // Their bounds in world space, or in the space of `local` (its rotation and scale undone) when given.
    public static Bounds BoundsOf(List<Renderer> renderers, Transform local)
    {
        var b = new Bounds();
        bool any = false;
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            foreach (var corner in Corners(mf.sharedMesh.bounds))
            {
                var p = mf.transform.TransformPoint(corner);
                if (local != null) p = local.InverseTransformPoint(p);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
    }

    // The bridge model's own triangles — the shape drawn up close — added to the ground mesh's vertex lists where the
    // bridge is fitted, each material's triangles into its own list in `byMaterial`, for the caller's drawn submeshes.
    // The ground's points before them get a plain tangent so `tan` stays as long as the vertex list (the ground's own
    // materials read no tangents). `rk` (the world ground's rock layer) may be left out.
    public static void AddModel(Deck d, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> c, List<Vector2> rk,
                                List<Vector4> tan, Dictionary<Material, List<int>> byMaterial)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var place = Matrix4x4.TRS(d.position, d.rotation, Vector3.one * d.scale) * prefab.transform.worldToLocalMatrix;
        while (tan.Count < v.Count) tan.Add(new Vector4(1f, 0f, 0f, 1f));
        foreach (var r in DrawnUpClose(prefab))
        {
            var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            var m = place * r.transform.localToWorldMatrix;
            int first = v.Count;
            var mv = mesh.vertices; var mn = mesh.normals; var muv = mesh.uv; var mt = mesh.tangents;
            for (int k = 0; k < mv.Length; k++)
            {
                v.Add(m.MultiplyPoint3x4(mv[k]));
                n.Add(k < mn.Length ? m.MultiplyVector(mn[k]).normalized : Vector3.up);
                uv.Add(k < muv.Length ? muv[k] : Vector2.zero);
                c.Add(Color.white);
                rk?.Add(new Vector2(0f, 0.5f));
                if (k < mt.Length) { var t = m.MultiplyVector(mt[k]).normalized; tan.Add(new Vector4(t.x, t.y, t.z, mt[k].w)); }
                else tan.Add(new Vector4(1f, 0f, 0f, 1f));
            }
            var mats = r.sharedMaterials;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var mat = mats[Mathf.Min(sub, mats.Length - 1)];
                if (!byMaterial.TryGetValue(mat, out var tri)) byMaterial[mat] = tri = new List<int>();
                foreach (int i in mesh.GetTriangles(sub)) tri.Add(first + i);
            }

            // THE BRIDGE'S BELLY. The model is a shell open underneath: seen from the river it was not drawn, and a body
            // below it rose straight through its walkway. Every triangle is laid a second time facing the other way, so
            // the shell's inside is drawn and solid from below — whole wherever the model is whole from above.
            int back = v.Count;
            for (int k = first; k < back; k++)
            {
                v.Add(v[k]); n.Add(-n[k]); uv.Add(uv[k]); c.Add(c[k]);
                if (rk != null) rk.Add(rk[k]);
                tan.Add(new Vector4(tan[k].x, tan[k].y, tan[k].z, -tan[k].w));
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tri = byMaterial[mats[Mathf.Min(sub, mats.Length - 1)]];
                var t = mesh.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3) { tri.Add(back + t[i]); tri.Add(back + t[i + 2]); tri.Add(back + t[i + 1]); }
            }
        }
    }
}
