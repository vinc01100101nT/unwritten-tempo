using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A CARPET OF GRASS OR FLOWERS over part of a chunk, drawn in bulk: thousands of copies of one small plant mesh in a
/// few draw calls, on this screen only. No objects, no colliders — nothing to walk on or bump into, the ground below is
/// what is walked on (RULE ONEGROUND).
///
/// <para><b>What is saved is small.</b> Each patch keeps its plant, its disc, how close the plants stand, a seed and the
/// ground's heights on a quarter-metre grid (a missing height means "no plant here": a road, water). The plants
/// themselves are worked out when the chunk loads, the same every time: the world is cut into spots, and each spot rolls
/// from its place and the seed alone (StableRandom) how many plants it holds — none, one, or a clump — by the chances the
/// pack's own demo ground shows, and which of the carpet's plants each one is, so the carpet has gaps and clumps, never
/// an even sheet.
/// It runs on across a chunk's edge with no seam, and nothing rolled anywhere else can move it.</para>
///
/// <para><b>Display only.</b> Nothing here touches a game number. The server drops it.</para>
///
/// <para><b>Cost.</b> Laying the plants out once when the chunk loads, spread over frames (LayPerFrame). Each frame, one bulk draw
/// per cell of at most <see cref="MaxPerDraw"/> plants that the camera can see; cells out of view are skipped by their
/// bounds. No shadows are cast: tried 2026-10-27, they nearly doubled the GPU's time in one meadow view (3.9 to 7.4 ms a
/// frame) and barely changed the picture. Nothing is allocated per frame.</para>
/// </summary>
public class FlowerCarpet : MonoBehaviour
{
    [System.Serializable]
    public class Patch
    {
        [Tooltip("The plant mesh drawn, and the material it is drawn with.")]
        public Mesh mesh;
        public Material material;
        [Tooltip("The middle of the patch's disc and its radius, world X and Z.")]
        public Vector2 centre;
        public float radius;
        [Tooltip("The box of this chunk the patch covers, world X and Z.")]
        public Rect clip;
        [Tooltip("The side of one spot, in world units: each spot holds none, one or several plants.")]
        public float spot;
        [Tooltip("The chance a spot holds at most 0, 1, 2 … plants, each one the running total (the last is 1).")]
        public float[] chances;
        [Tooltip("How many plants share this carpet, and which of them this patch draws: each plant in a spot is one of them.")]
        public int kinds = 1;
        public int kind;
        [Tooltip("How wide one single-plant patch is, in world units: the ground is cut into wavy patches this wide and each " +
                 "grows one of the plants. 0 = the plants mix one by one.")]
        public float patchWidth;
        [Tooltip("Each plant's size, and how much it varies either way (0.2 = 20%).")]
        public float scale = 1f;
        public float scaleJitter = 0.2f;
        [Tooltip("How much each plant's height alone varies either way on top of its size (0.15 = 15%), so a patch of " +
                 "flowers is not all one height. 0 = no extra spread.")]
        public float heightJitter;
        [Tooltip("The share of this carpet's plants left out, each by its own roll (0.5 = every other plant, at random). " +
                 "0 = all of them, as the demo ground grows them.")]
        [Range(0f, 1f)] public float thinOut;
        [Tooltip("How much taller again than its size each plant stands (1 = twice as tall). Its width is unchanged. 0 = none.")]
        public float extraHeight;
        [Tooltip("Which carpet this is, as a number: every chunk's patch of the same carpet, and every plant in it, carries the same seed.")]
        public int seed;
        [Tooltip("The ground's height on a grid starting at gridMin, gridStep apart, gridW by gridH points; NaN = no plant.")]
        public Vector2 gridMin;
        public float gridStep = 0.25f;
        public int gridW, gridH;
        public float[] heights;
    }

    public List<Patch> patches = new List<Patch>();

    const int MaxPerDraw = 1023;
    const float CellSize = 4f;
    // A plant's height roll is its spot's number HeightRoll + k: far past the five numbers each plant k already uses.
    const int HeightRoll = 1000;
    // And its roll to be left out (thinOut), HeightRoll's distance further on.
    const int ThinRoll = 2000;
    // Plants laid per frame while a chunk is new. MEASURED 2026-10-26 in the editor: one theme's 274,842 plants in one go
    // held a frame for 385 ms; 20,000 a frame cost up to 12.8 ms a frame; at this many, up to 8.7 ms, and the carpet
    // fills in over 44 frames.
    const int LayPerFrame = 8000;

    struct Cell
    {
        public Mesh mesh;
        public RenderParams rp;
        public Matrix4x4[] matrices;
        public int count;
    }

    readonly List<Cell> cells = new List<Cell>();

    // Where laying has got to: the patch, the row of squares in it (int.MinValue = not started), and that patch's
    // plants so far, by cell.
    int layPatch;
    int layRow = int.MinValue;
    readonly Dictionary<(int, int), List<Matrix4x4>> layCells = new Dictionary<(int, int), List<Matrix4x4>>();

    void Awake()
    {
        GameServer.RemovesPresentation(this);
    }

    // THE PLAYER'S PLANT DENSITY (Settings ▸ Graphics): a new share lays every plant again, from the first patch —
    // once the slider has rested for RelayAfter seconds, so dragging it does not start the laying over every frame.
    const float RelayAfter = 0.3f;
    float relayAt = -1f;

    void OnEnable() => GameSettings.PlantDensityChanged += DensityChanged;
    void OnDisable() => GameSettings.PlantDensityChanged -= DensityChanged;
    void DensityChanged() => relayAt = Time.unscaledTime + RelayAfter;

    void LayAgain()
    {
        relayAt = -1f;
        cells.Clear();
        layCells.Clear();
        layPatch = 0;
        layRow = int.MinValue;
    }

    // The game camera's view, worked out once a frame for every carpet.
    static readonly Plane[] view = new Plane[6];
    static int viewFrame = -1;
    static bool haveView;
    static Vector3 viewFrom;
    // HOW FAR FROM THE GAME CAMERA CARPET PLANTS ARE DRAWN is the player's own choice (Settings ▸ Graphics ▸ Grass &
    // flower distance, GameSettings.PlantDistance): beyond it the ground's own painted grass shows instead. MEASURED in
    // the trial square with no limit: the graphics card drew about 2.3 million carpet triangles a frame; at 40 units,
    // about 1.9 million and about 15 ms a frame.
    // Its leave-out roll (the player's density), past the thin-out roll.
    const int DensityRoll = 3000;

    void Update()
    {
        if (relayAt >= 0f && Time.unscaledTime >= relayAt) LayAgain();
        if (layPatch < patches.Count) Lay(LayPerFrame, GameSettings.PlantDensity);
        // ONLY THE CELLS THE GAME CAMERA CAN SEE ARE HANDED OVER. Unity drops an unseen cell itself, but only after
        // copying all its plants: MEASURED 2026-10-27 in the trial square, 594 cells (509,151 plants) were handed over
        // every frame for 263 (231,918) on screen, and this Update cost 5.3-6.1 ms a frame. A cell outside the game
        // camera's view is not drawn in any other camera either (a water reflection of plants behind the camera).
        if (viewFrame != Time.frameCount)
        {
            viewFrame = Time.frameCount;
            var cam = Camera.main;
            haveView = cam != null;
            if (haveView) { GeometryUtility.CalculateFrustumPlanes(cam, view); viewFrom = cam.transform.position; }
        }
        float range = GameSettings.PlantDistance;
        var shadows = GameSettings.PlantShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (haveView && (c.rp.worldBounds.SqrDistance(viewFrom) > range * range
                             || !GeometryUtility.TestPlanesAABB(view, c.rp.worldBounds))) continue;
            var rp = c.rp;
            rp.shadowCastingMode = shadows;
            Graphics.RenderMeshInstanced(rp, c.mesh, 0, c.matrices, c.count);
        }
    }

    // Lays up to `budget` more spots and plants, patch by patch, row by row of spots, each spot's plant and count worked
    // out from its place and its patch's seed; a finished patch is cut into cells of CellSize metres, each its own draw.
    void Lay(int budget, float density)
    {
        while (budget > 0 && layPatch < patches.Count)
        {
            var p = patches[layPatch];
            if (p.mesh == null || p.material == null || p.heights == null || p.spot <= 0f || p.chances == null || p.chances.Length == 0) { layPatch++; continue; }
            float s = p.spot, r2 = p.radius * p.radius;
            int rowEnd = Mathf.FloorToInt(p.clip.yMax / s), colStart = Mathf.FloorToInt(p.clip.xMin / s), colEnd = Mathf.FloorToInt(p.clip.xMax / s);
            if (layRow == int.MinValue) { layRow = Mathf.FloorToInt(p.clip.yMin / s); layCells.Clear(); }
            uint seed = unchecked((uint)p.seed);
            for (; layRow <= rowEnd && budget > 0; layRow++)
                for (int col = colStart; col <= colEnd; col++)
                {
                    budget--;
                    uint sq = StableRandom.Square(seed, col, layRow);
                    int n = CountAt(p.chances, StableRandom.Value(sq, 0));
                    for (int k = 0; k < n; k++)
                    {
                        int at = 1 + k * 5;
                        float px = (col + StableRandom.Value(sq, at)) * s, pz = (layRow + StableRandom.Value(sq, at + 1)) * s;
                        float pick = p.patchWidth > 0f ? PatchRoll(seed, px, pz, p.patchWidth) : StableRandom.Value(sq, at + 4);
                        if (KindAt(p.kinds, pick) != p.kind) continue;
                        if (p.thinOut > 0f && StableRandom.Value(sq, ThinRoll + k) < p.thinOut) continue;
                        if (density < 1f && StableRandom.Value(sq, DensityRoll + k) >= density) continue;
                        float yaw = StableRandom.Value(sq, at + 2) * 360f, size = p.scale * (1f + (StableRandom.Value(sq, at + 3) * 2f - 1f) * p.scaleJitter);
                        float dx = px - p.centre.x, dz = pz - p.centre.y;
                        if (dx * dx + dz * dz > r2 || !p.clip.Contains(new Vector2(px, pz))) continue;
                        if (!GroundAt(p, px, pz, out float y)) continue;
                        budget--;
                        var key = (Mathf.FloorToInt(px / CellSize), Mathf.FloorToInt(pz / CellSize));
                        if (!layCells.TryGetValue(key, out var list)) layCells[key] = list = new List<Matrix4x4>();
                        // Laid where the chunk was built, then carried to where it stands now (a world may be set down
                        // somewhere other than where it was built).
                        // Its height rolled apart from its size, from a number of its own so no other roll moves.
                        float tall = size * (1f + p.extraHeight) * (1f + (StableRandom.Value(sq, HeightRoll + k) * 2f - 1f) * p.heightJitter);
                        list.Add(transform.localToWorldMatrix * Matrix4x4.TRS(new Vector3(px, y, pz), Quaternion.Euler(0f, yaw, 0f), new Vector3(size, tall, size)));
                    }
                }
            if (layRow <= rowEnd) return;
            foreach (var all in layCells.Values) AddCells(p, all);
            layCells.Clear();
            layRow = int.MinValue;
            layPatch++;
        }
    }

    /// <summary>THE WHOLE CARPET IN ONE PICTURE (the map's, drawn in the editor where no game camera runs): every plant, at
    /// full density whatever the player's setting, handed to <paramref name="cam"/>'s next render only. Call
    /// <see cref="ForgetLaid"/> once it is drawn; a square's carpet is a few hundred thousand plants.</summary>
    public void DrawAllInto(Camera cam)
    {
        LayAgain();
        Lay(int.MaxValue, 1f);
        for (int i = 0; i < cells.Count; i++)
        {
            var rp = cells[i].rp;
            rp.camera = cam;
            rp.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Graphics.RenderMeshInstanced(rp, cells[i].mesh, 0, cells[i].matrices, cells[i].count);
        }
    }

    /// <summary>Drops every plant laid, so the next frame lays them again from the first patch.</summary>
    public void ForgetLaid() => LayAgain();

    /// <summary>Which of <paramref name="kinds"/> plants a plant is, from its roll <paramref name="u"/> (0 up to 1).</summary>
    public static int KindAt(int kinds, float u) => kinds <= 1 ? 0 : Mathf.Min((int)(u * kinds), kinds - 1);

    /// <summary>The roll of the wavy patch <paramref name="x"/>, <paramref name="z"/> lies in: one roll for the whole patch.</summary>
    public static float PatchRoll(uint seed, float x, float z, float width)
    {
        float wx = (Mathf.PerlinNoise(x / width * 1.3f + 17.1f, z / width * 1.3f) - 0.5f) * width;
        float wz = (Mathf.PerlinNoise(x / width * 1.3f, z / width * 1.3f + 41.7f) - 0.5f) * width;
        return StableRandom.Value(StableRandom.Square(seed ^ 0x5bd1e995u, Mathf.FloorToInt((x + wx) / width), Mathf.FloorToInt((z + wz) / width)), 0);
    }

    /// <summary>How many plants a spot holds: the first count whose running chance is above the roll <paramref name="u"/>.</summary>
    public static int CountAt(float[] chances, float u)
    {
        for (int i = 0; i < chances.Length; i++) if (u < chances[i]) return i;
        return chances.Length - 1;
    }

    void AddCells(Patch p, List<Matrix4x4> all)
    {
        for (int start = 0; start < all.Count; start += MaxPerDraw)
        {
            int n = Mathf.Min(MaxPerDraw, all.Count - start);
            var m = new Matrix4x4[n];   // perf-ok: once per cell, when its patch finishes laying; never again after
            var b = new Bounds(all[start].GetColumn(3), Vector3.zero);
            for (int i = 0; i < n; i++) { m[i] = all[start + i]; b.Encapsulate((Vector3)m[i].GetColumn(3)); }
            b.Expand(new Vector3(1f, 2f * (1f + p.extraHeight), 1f) * p.scale);
            var rp = new RenderParams(p.material)
            {
                worldBounds = b,
                // Whether plants cast shadows is the player's choice, set on every draw (GameSettings.PlantShadows).
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows = true,
                layer = gameObject.layer,
            };
            cells.Add(new Cell { mesh = p.mesh, rp = rp, matrices = m, count = n });
        }
    }

    // The ground's height at (x, z) from the patch's grid, or false where the grid marks no plant.
    static bool GroundAt(Patch p, float x, float z, out float y)
    {
        y = 0f;
        float gx = (x - p.gridMin.x) / p.gridStep, gz = (z - p.gridMin.y) / p.gridStep;
        int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
        if (i < 0 || j < 0 || i + 1 >= p.gridW || j + 1 >= p.gridH) return false;
        float a = p.heights[j * p.gridW + i], b = p.heights[j * p.gridW + i + 1];
        float c = p.heights[(j + 1) * p.gridW + i], d = p.heights[(j + 1) * p.gridW + i + 1];
        if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c) || float.IsNaN(d)) return false;
        float u = gx - i, v = gz - j;
        y = Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        return true;
    }
}
