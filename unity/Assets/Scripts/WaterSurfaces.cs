using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The loaded map's water, read off its real ground: a thin sheet on every level of flat ground, and a lake in every
/// hollow that rises as the rain goes on — each one a still water surface with a mirror of its own (see
/// <see cref="PlanarReflection"/>) — where on the map each lies (so only the ones on screen are drawn), and how much
/// rain runs to each spot (so puddles gather at the foot of slopes). The rules are <see cref="WaterPooling"/>; this
/// only feeds them the map and hands the answers to the wet-ground shader and to <see cref="WetGround.IsPuddleAt"/>,
/// so what is drawn and what is asked agree.
///
/// <para>The ground is read once per map, the first time wet ground shows after a map loads or unloads; the lakes are
/// worked out again only when the rain standing on the map moves by <see cref="RainQuantum"/>. Every player works the
/// lakes out from the same ground and the same rain (<see cref="WaterPooling.Rainfall"/>, on the shared world clock),
/// so everyone sees the same water. A player's machine only: nothing is drawn on a server.</para>
/// </summary>
public static class WaterSurfaces
{
    /// <summary>The most bodies of water a map keeps — ids up to this fit the shader's arrays and its 8-bit body map.</summary>
    public const int MaxHeights = 64;
    /// <summary>How many squares past a body's own squares its edge may run: a square is marked with a body when its
    /// centre is under water, and the water's edge crosses the squares beside those — two deep, so an edge that bends
    /// back across a square's corner is still drawn by the height of the ground rather than cut by the square.</summary>
    const int EdgeReach = 2;

    /// <summary>The lakes are refilled only when the rain standing on the map has moved this far, in world units.</summary>
    public const float RainQuantum = 0.0002f;

    /// <summary>How many bodies of water the map has now: first <see cref="FilmCount"/> sheets on level ground, most
    /// ground first, then the lakes. <see cref="Heights"/> holds each one's surface; a pixel is water when its ground
    /// lies at or below that and at or above <see cref="Floors"/>.</summary>
    public static int Count { get; private set; }
    public static int FilmCount { get; private set; }
    public static readonly float[] Heights = new float[MaxHeights];
    public static readonly float[] Floors = new float[MaxHeights];

    static readonly Bounds[][] chunks = new Bounds[MaxHeights][];
    static readonly float[] filmLevels = new float[MaxHeights];
    static float[] grid = new float[0];
    static WaterPooling.Watershed watershed;
    static float[] runoff = new float[0];
    static byte[] filmIds = new byte[0];
    static byte[] bodyIds = new byte[0];
    static int nx, nz;
    static float originX, originZ;
    static float rainNow = -1f, rainLogged = -1f;
    static Texture2D runoffTex, bodyTex;
    static bool dirty = true;
    static int groundLayer = -2;

    static readonly int HeightsID    = Shader.PropertyToID("_WaterLevels");
    static readonly int FloorsID     = Shader.PropertyToID("_WaterFloors");
    static readonly int CountID      = Shader.PropertyToID("_WaterHeightCount");
    static readonly int FilmCountID  = Shader.PropertyToID("_WaterFilmCount");
    static readonly int ToleranceID  = Shader.PropertyToID("_WaterTolerance");
    static readonly int ClarityID    = Shader.PropertyToID("_WaterClarity");
    static readonly int DrawnID      = Shader.PropertyToID("_WaterDrawn");
    static bool drawnShown;
    static bool drawnSet;

    /// <summary>Tell the screen passes whether standing water is drawn this frame (the wet ground is showing): the ink
    /// outlines fade under water only while it is. Written only when it changes.</summary>
    public static void SetDrawn(bool drawn)
    {
        if (drawnSet && drawn == drawnShown) return;
        drawnSet = true;
        drawnShown = drawn;
        Shader.SetGlobalFloat(DrawnID, drawn ? 1f : 0f);
    }
    static readonly int BodyTexID    = Shader.PropertyToID("_WaterBodyTex");
    static readonly int GridSizeID   = Shader.PropertyToID("_WaterGridSize");
    static readonly int RunoffTexID  = Shader.PropertyToID("_WaterRunoffTex");
    static readonly int RunoffRectID = Shader.PropertyToID("_WaterRunoffRect");
    static readonly int RunoffPullID = Shader.PropertyToID("_WaterRunoffPull");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        SceneManager.sceneUnloaded -= OnUnloaded;
        SceneManager.sceneUnloaded += OnUnloaded;
        dirty = true;
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => dirty = true;
    static void OnUnloaded(Scene s) => dirty = true;

    /// <summary>Re-read the ground if a map came or went since the last read. Cheap when nothing changed.</summary>
    public static void Refresh()
    {
        if (!dirty || GameServer.IsServer) return;
        dirty = false;
        Rebuild();
    }

    static int GroundLayer
    {
        get
        {
            if (groundLayer == -2) groundLayer = LayerMask.NameToLayer("Ground");
            return groundLayer;
        }
    }

    // perf-ok: runs once per map load or unload (the dirty gate in Refresh), never per frame.
    static void Rebuild()
    {
        var meshes = new List<(Vector3[] verts, int[] tris, Matrix4x4 toWorld)>();
        bool any = false;
        var area = new Bounds();
        if (GroundLayer >= 0)
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.gameObject.layer != GroundLayer) continue;
                var f = r.GetComponent<MeshFilter>();
                var mesh = f != null ? f.sharedMesh : null;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                {
                    GameLog.Warn(LogCat.Unwritten, "WaterSurfaces: ground mesh '" + mesh.name + "' is not readable — no water on it.", r);
                    continue;
                }
                meshes.Add((mesh.vertices, mesh.triangles, r.transform.localToWorldMatrix));
                if (any) area.Encapsulate(r.bounds); else { area = r.bounds; any = true; }
            }

        var found = new List<WaterPooling.FlatHeight>();
        foreach (var m in meshes) WaterPooling.AddFlatTriangles(m.verts, m.tris, m.toWorld, found);
        WaterPooling.KeepWaterHeights(found, MaxHeights);
        var films = WaterPooling.JoinFilms(found);
        FilmCount = Mathf.Min(films.Count, MaxHeights);

        float cell = WaterPooling.RunoffCell;
        originX = area.min.x; originZ = area.min.z;
        nx = any ? Mathf.Max(1, Mathf.CeilToInt(area.size.x / cell)) : 1;
        nz = any ? Mathf.Max(1, Mathf.CeilToInt(area.size.z / cell)) : 1;
        grid = new float[nx * nz];
        for (int k = 0; k < grid.Length; k++) grid[k] = float.NegativeInfinity;
        foreach (var m in meshes) WaterPooling.RaiseToGround(m.verts, m.tris, m.toWorld, originX, originZ, cell, nx, nz, grid);

        // The sheets: each over its level ground, its edge where the ground beside it has risen FilmDepth.
        filmIds = new byte[nx * nz];
        for (int i = 0; i < FilmCount; i++)
        {
            var film = films[i];
            filmLevels[i] = film.y;
            Heights[i] = film.top + WaterPooling.FilmDepth;
            Floors[i] = film.y - WaterPooling.HeightTolerance;
            // Padded by a square each way: the sheet's edge runs a little past the level triangles, onto the rising ground.
            chunks[i] = new List<Bounds>(film.chunks.Values).ToArray();
            for (int c = 0; c < chunks[i].Length; c++)
                chunks[i][c].Expand(new Vector3(cell * 2f, WaterPooling.FilmDepth * 2f, cell * 2f));
            MarkFilm(Floors[i], Heights[i], (byte)(i + 1));
        }

        // Rain on level ground stays there as its sheet; rain on every other square runs downhill into the hollows.
        var sheds = new bool[nx * nz];
        for (int k = 0; k < grid.Length; k++)
        {
            if (float.IsNegativeInfinity(grid[k])) continue;
            bool level = false;
            for (int i = 0; i < FilmCount && !level; i++) level = grid[k] >= Floors[i] && grid[k] <= Heights[i];
            sheds[k] = !level;
        }
        watershed = WaterPooling.BuildWatershed(grid, nx, nz, sheds);

        runoff = new float[nx * nz];
        WaterPooling.Runoff(grid, nx, nz, cell, filmLevels, FilmCount, runoff);
        var bytes = new byte[nx * nz];
        for (int k = 0; k < runoff.Length; k++)
        {
            runoff[k] = WaterPooling.AsStored(runoff[k]);
            bytes[k] = (byte)Mathf.RoundToInt(runoff[k] * 255f);
        }
        runoffTex = Upload(runoffTex, "WaterRunoff", bytes, FilterMode.Bilinear);

        Shader.SetGlobalFloat(ToleranceID, WaterPooling.HeightTolerance);
        Shader.SetGlobalFloat(ClarityID, WaterPooling.Clarity);
        Shader.SetGlobalVector(RunoffRectID, new Vector4(originX, originZ, 1f / (nx * cell), 1f / (nz * cell)));
        Shader.SetGlobalVector(GridSizeID, new Vector4(nx, nz, 0f, 0f));
        Shader.SetGlobalFloat(RunoffPullID, WaterPooling.RunoffPull);
        Shader.SetGlobalFloat(FilmCountID, FilmCount);

        rainNow = -1f;
        Fill(0f);
        GameLog.Info(LogCat.Unwritten, "WaterSurfaces: " + FilmCount + " level sheet(s) on " + meshes.Count +
                     " ground mesh(es), ground map " + nx + "x" + nz + ".");
    }

    static Texture2D Upload(Texture2D tex, string name, byte[] bytes, FilterMode filter)
    {
        if (tex == null || tex.width != nx || tex.height != nz)
        {
            if (tex != null) Object.Destroy(tex);
            tex = new Texture2D(nx, nz, TextureFormat.R8, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = filter, hideFlags = HideFlags.DontSave,
            };
        }
        tex.SetPixelData(bytes, 0);
        tex.Apply(false, false);
        return tex;
    }

    // Mark the squares whose ground lies between a sheet's floor and its surface, and the squares within EdgeReach of
    // one (the sheet's edge crosses those), with its id; which pixels in them are water is then decided by their own height.
    static void MarkFilm(float floor, float top, byte id)
    {
        var inside = new List<int>();
        for (int k = 0; k < grid.Length; k++) if (grid[k] >= floor && grid[k] <= top) inside.Add(k);
        foreach (int k in inside) filmIds[k] = id;
        foreach (int k in inside)
        {
            int ci = k % nx, cj = k / nx;
            for (int dj = -EdgeReach; dj <= EdgeReach; dj++)
                for (int di = -EdgeReach; di <= EdgeReach; di++)
                {
                    int ni = ci + di, nj = cj + dj;
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                    int n = nj * nx + ni;
                    if (filmIds[n] == 0) filmIds[n] = id;
                }
        }
    }

    /// <summary>How much rain stands on the map now (<see cref="WaterPooling.Rainfall"/>, world units of water): the
    /// lakes are filled again only when it moved by <see cref="RainQuantum"/>.</summary>
    public static void SetRain(float rain)
    {
        if (grid.Length == 0) return;
        rain = Mathf.Round(Mathf.Max(0f, rain) / RainQuantum) * RainQuantum;
        if (rain == rainNow) return;
        Fill(rain);
    }

    // perf-ok: runs when the standing rain moves by RainQuantum — a few times in each 3-second rise, never per frame.
    static void Fill(float rain)
    {
        rainNow = rain;
        float cell = WaterPooling.RunoffCell;
        var lakes = rain > 0f && watershed != null
            ? WaterPooling.Fill(watershed, grid, cell, rain)
            : new List<WaterPooling.Lake>();
        int lakeCount = Mathf.Min(lakes.Count, MaxHeights - FilmCount);
        Count = FilmCount + lakeCount;

        if (bodyIds.Length != filmIds.Length) bodyIds = new byte[filmIds.Length];
        System.Array.Copy(filmIds, bodyIds, filmIds.Length);
        for (int i = FilmCount; i < MaxHeights; i++)
        {
            chunks[i] = null;
            Heights[i] = 0f;
            Floors[i] = 0f;
            if (i >= Count) continue;
            var lake = lakes[i - FilmCount];
            Heights[i] = lake.level;
            Floors[i] = float.MinValue;
            chunks[i] = LakeChunks(lake, cell);
            byte id = (byte)(i + 1);
            foreach (int c in lake.cells) bodyIds[c] = id;
            // The shore squares round the lake, whose centre stands above it: the water's edge crosses them.
            foreach (int c in lake.cells)
            {
                int ci = c % nx, cj = c / nx;
                for (int dj = -EdgeReach; dj <= EdgeReach; dj++)
                    for (int di = -EdgeReach; di <= EdgeReach; di++)
                    {
                        int ni = ci + di, nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                        int n = nj * nx + ni;
                        if (grid[n] >= lake.level && bodyIds[n] <= FilmCount) bodyIds[n] = id;
                    }
            }
        }
        bodyTex = Upload(bodyTex, "WaterBodies", bodyIds, FilterMode.Point);
        Shader.SetGlobalTexture(BodyTexID, bodyTex);
        Shader.SetGlobalTexture(RunoffTexID, runoffTex);
        Shader.SetGlobalFloatArray(HeightsID, Heights);
        Shader.SetGlobalFloatArray(FloorsID, Floors);
        Shader.SetGlobalFloat(CountID, Count);

        // Said out loud once per 2 mm of rain, so a lake that does not rise can be told from one that rises unseen.
        if (Mathf.Abs(rain - rainLogged) >= 0.002f || (rain == 0f && rainLogged > 0f))
        {
            rainLogged = rain;
            float deepestLake = 0f;
            foreach (var l in lakes) deepestLake = Mathf.Max(deepestLake, l.level - l.bottom);
            GameLog.Info(LogCat.Unwritten, "WaterSurfaces: " + rain.ToString("0.0000") + " of rain standing — " + lakeCount +
                         " lake(s) of " + lakes.Count + ", the deepest " + deepestLake.ToString("0.000") + " deep, " + Count +
                         " bodies of water.");
        }
    }

    // The lake's squares, gathered into the on-screen test's chunks, from its lowest ground up to its level.
    static Bounds[] LakeChunks(WaterPooling.Lake lake, float cell)
    {
        var byChunk = new Dictionary<long, Bounds>();
        foreach (int c in lake.cells)
        {
            float x = originX + (c % nx + 0.5f) * cell, z = originZ + (c / nx + 0.5f) * cell;
            var box = new Bounds(new Vector3(x, (lake.bottom + lake.level) * 0.5f, z),
                                 new Vector3(cell * 2f, lake.level - lake.bottom + WaterPooling.HeightTolerance * 2f, cell * 2f));
            long key = ((long)Mathf.FloorToInt(x / WaterPooling.ChunkSize) << 32) ^ (Mathf.FloorToInt(z / WaterPooling.ChunkSize) & 0xffffffffL);
            if (byChunk.TryGetValue(key, out var have)) { have.Encapsulate(box); byChunk[key] = have; }
            else byChunk[key] = box;
        }
        return new List<Bounds>(byChunk.Values).ToArray();
    }

    /// <summary>Fill <paramref name="into"/> with the index of every body of water that has ground inside
    /// <paramref name="frustum"/>, sheets first, most ground first.</summary>
    public static void OnScreen(Plane[] frustum, List<int> into)
    {
        into.Clear();
        for (int i = 0; i < Count; i++)
        {
            var list = chunks[i];
            if (list == null) continue;
            for (int c = 0; c < list.Length; c++)
                if (GeometryUtility.TestPlanesAABB(frustum, list[c])) { into.Add(i); break; }
        }
    }

    /// <summary>The body of water standing over <paramref name="world"/>, or -1 — the same test the shader makes per
    /// pixel, made on the ground straight below (within a quarter unit either way): the body marked on that square,
    /// with the ground between its floor and its surface. <paramref name="lake"/> says whether it is a lake (water all
    /// over) or a sheet (puddles, where the pattern allows); <paramref name="runoffHere"/> is how much rain runs to a
    /// sheet's spot.</summary>
    public static int WaterUnder(Vector3 world, out bool lake, out float runoffHere)
    {
        lake = false;
        runoffHere = 0f;
        if (Count == 0 || GroundLayer < 0) return -1;
        int ci = Mathf.FloorToInt((world.x - originX) / WaterPooling.RunoffCell);
        int cj = Mathf.FloorToInt((world.z - originZ) / WaterPooling.RunoffCell);
        if (ci < 0 || cj < 0 || ci >= nx || cj >= nz) return -1;
        int id = bodyIds[cj * nx + ci] - 1;
        if (id < 0 || id >= Count) return -1;
        if (!Physics.Raycast(world + Vector3.up * 0.25f, Vector3.down, out var hit, 0.5f, 1 << GroundLayer,
                             QueryTriggerInteraction.Ignore)) return -1;
        float y = hit.point.y;
        if (y > Heights[id] + WaterPooling.HeightTolerance || y < Floors[id]) return -1;
        lake = id >= FilmCount;
        if (!lake) runoffHere = WaterPooling.SampleSmooth(runoff, nx, nz, originX, originZ, WaterPooling.RunoffCell, world.x, world.z);
        return id;
    }
}
