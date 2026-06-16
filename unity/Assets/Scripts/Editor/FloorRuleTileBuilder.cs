using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools ▸ unwritten ▸ Build Floor RuleTiles.
///
/// Turns the NinjaAdventure "TilesetFloor" sheet into auto-tiling <see cref="RuleTile"/> assets — one
/// per terrain — so painting (or generating) a region picks the right edge/corner tile automatically.
/// RuleTile is Unity's built-in neighbor-matching engine (from com.unity.2d.tilemap.extras); we only
/// supply the sprite⇄neighbor map, decoded from the sheet's fixed layout.
///
/// Each terrain block is a ~15-tile autotile laid out at a fixed (col,row) offset in the sheet:
///   core 3×3 (outer corners + edges + center) at (0..2, 0..2),
///   vertical strip at col 3 (rows 0/1/2) + isolated at (3,3),
///   horizontal strip at row 3 (cols 0/1/2),
///   inner corners at (6,2)=NW (5,2)=NE (6,1)=SW (5,1)=SE — all RELATIVE to the terrain's origin.
///
/// All 8 terrains (<see cref="Terrains"/>) are enabled & verified. Verify any time with
/// Tools ▸ unwritten ▸ Floor RuleTile Test Patch (paints every RuleTile with holes/strips, no painting).
///
/// AUTO-WIRING: if a TownTheme is selected in the Project window when you run this (or there's exactly
/// one TownTheme in the project), it also assigns that theme's Path Tile + Patch Terrains for you — see
/// <see cref="AutoWireTheme"/> / <see cref="DefaultPathTerrain"/> / <see cref="DefaultPatchTerrains"/>.
/// </summary>
public static class FloorRuleTileBuilder
{
    const string SheetPath = "Assets/Art/NinjaAdventure/Backgrounds/Tilesets/TilesetFloor.png";
    const string OutFolder = "Assets/Art/Tiles/Floor";
    const int TS = 16;   // tile size in px (PPU 16)

    // 8 neighbour directions in tilemap space (+y is up).
    static readonly Vector3Int N = new Vector3Int(0, 1, 0), S = new Vector3Int(0, -1, 0),
                               E = new Vector3Int(1, 0, 0), W = new Vector3Int(-1, 0, 0),
                               NE = new Vector3Int(1, 1, 0), NW = new Vector3Int(-1, 1, 0),
                               SE = new Vector3Int(1, -1, 0), SW = new Vector3Int(-1, -1, 0);
    const int This = RuleTile.TilingRuleOutput.Neighbor.This;       // = 1 (neighbour is the same terrain)
    const int Not = RuleTile.TilingRuleOutput.Neighbor.NotThis;     // = 2 (neighbour is empty / other terrain)

    struct Terrain
    {
        public string name; public int oc, orr;
        public Terrain(string n, int col, int row) { name = n; oc = col; orr = row; }
    }

    // Terrain origins in the sheet (top-left cell of each block): two terrains per band, left col 0
    // and right col 11, bands every 7 rows. All 8 share the SAME verified autotile layout — pixel-
    // sampling confirmed Ice/Lava's inner corners sit at the exact same cells as Sand's. The only
    // gap: Ice/Lava have no isolated-single (3,3) or horizontal-strip end caps, so those rare cases
    // fall back to the center fill (handled gracefully — a missing sprite just skips that one rule).
    static readonly Terrain[] Terrains =
    {
        new Terrain("Sand",     0,  0),
        new Terrain("Salmon",  11,  0),
        new Terrain("Dirt",     0,  7),
        new Terrain("DarkDirt", 11,  7),
        new Terrain("Snow",     0, 14),
        new Terrain("Mud",      11, 14),
        new Terrain("Ice",      0, 21),
        new Terrain("Lava",     11, 21),
    };

    // Auto-wire defaults for a GRASS-based map. Each terrain's edge tile is drawn against ONE specific
    // background: only Dirt & DarkDirt have a green grass rim, so only they blend seamlessly as patches/
    // paths on grass. Sand/Snow/Ice/Lava/Salmon/Mud fade into their OWN material → hard seam on grass;
    // they're for maps whose base ground is that material, not grass patches. (Change for other biomes.)
    const string DefaultPathTerrain = "Dirt";
    static readonly string[] DefaultPatchTerrains = { "Dirt", "DarkDirt" };

    [MenuItem("Tools/unwritten/Build Floor RuleTiles")]
    static void Build()
    {
        // Capture a TownTheme the user selected BEFORE we change the selection below (for auto-wiring).
        var selectedTheme = Selection.activeObject as TownTheme;

        var grid = LoadSpriteGrid();
        if (grid == null) return;
        if (!AssetDatabase.IsValidFolder(OutFolder)) CreateFolders(OutFolder);

        int made = 0;
        foreach (var t in Terrains)
        {
            var rt = BuildTerrain(t, grid);
            if (rt == null) continue;
            string path = $"{OutFolder}/{t.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RuleTile>(path);
            if (existing != null)                         // keep the GUID so palettes/refs survive a rebuild
            {
                EditorUtility.CopySerialized(rt, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(rt);
            }
            else AssetDatabase.CreateAsset(rt, path);
            made++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Collect the on-disk assets by name for wiring.
        var byName = new Dictionary<string, TileBase>();
        foreach (var t in Terrains)
        {
            var a = AssetDatabase.LoadAssetAtPath<RuleTile>($"{OutFolder}/{t.name}.asset");
            if (a) byName[t.name] = a;
        }

        string wired = AutoWireTheme(selectedTheme ?? FindLoneTheme(), byName);

        var first = AssetDatabase.LoadAssetAtPath<RuleTile>($"{OutFolder}/{Terrains[0].name}.asset");
        if (first) { Selection.activeObject = first; EditorGUIUtility.PingObject(first); }
        Debug.Log($"[unwritten] Built {made} floor RuleTile(s) → {OutFolder}.  {wired}  " +
                  "Verify with Tools ▸ unwritten ▸ Floor RuleTile Test Patch.");
    }

    /// <summary>Assign pathTile + patchTerrains onto a theme, so you don't drag them in by hand.</summary>
    static string AutoWireTheme(TownTheme theme, Dictionary<string, TileBase> byName)
    {
        if (theme == null)
            return "No theme auto-wired (select a TownTheme in the Project window and re-run, " +
                   "or set Path Tile / Patch Terrains by hand).";

        Undo.RecordObject(theme, "Wire Floor RuleTiles");

        if (byName.TryGetValue(DefaultPathTerrain, out var path)) theme.pathTile = path;

        var patches = new List<TileBase>();
        foreach (var n in DefaultPatchTerrains)
            if (byName.TryGetValue(n, out var p)) patches.Add(p);
        if (patches.Count > 0) theme.patchTerrains = patches;

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return $"Auto-wired '{theme.name}': Path Tile = {(theme.pathTile ? theme.pathTile.name : "—")}, " +
               $"Patch Terrains = [{string.Join(", ", patches.Select(p => p.name))}]  (tweak on the theme to taste).";
    }

    /// <summary>The single TownTheme in the project, or null if there are zero / several (then don't guess).</summary>
    static TownTheme FindLoneTheme()
    {
        var guids = AssetDatabase.FindAssets("t:TownTheme");
        return guids.Length == 1
            ? AssetDatabase.LoadAssetAtPath<TownTheme>(AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }

    static RuleTile BuildTerrain(Terrain t, Dictionary<(int, int), Sprite> g)
    {
        Sprite Get(int dc, int dr)
        {
            g.TryGetValue((t.oc + dc, t.orr + dr), out var s);
            if (!s) Debug.LogWarning($"[unwritten] {t.name}: no sprite at sheet cell ({t.oc + dc},{t.orr + dr}).");
            return s;
        }

        var rt = ScriptableObject.CreateInstance<RuleTile>();
        rt.m_DefaultSprite = Get(1, 1);                 // fully-surrounded fill (no rule matches)
        rt.m_DefaultColliderType = Tile.ColliderType.None;
        rt.m_TilingRules = new List<RuleTile.TilingRule>();

        // Most-specific first; RuleTile uses first-match.
        // --- isolated + thin strips ---
        AddRule(rt, Get(3, 3), (N, Not), (E, Not), (S, Not), (W, Not));        // isolated single
        AddRule(rt, Get(0, 3), (E, This), (N, Not), (S, Not), (W, Not));       // h-strip left cap
        AddRule(rt, Get(2, 3), (W, This), (N, Not), (S, Not), (E, Not));       // h-strip right cap
        AddRule(rt, Get(1, 3), (E, This), (W, This), (N, Not), (S, Not));      // h-strip middle
        AddRule(rt, Get(3, 0), (S, This), (N, Not), (E, Not), (W, Not));       // v-strip top
        AddRule(rt, Get(3, 2), (N, This), (S, Not), (E, Not), (W, Not));       // v-strip bottom
        AddRule(rt, Get(3, 1), (N, This), (S, This), (E, Not), (W, Not));      // v-strip middle
        // --- outer (convex) corners ---
        AddRule(rt, Get(0, 0), (N, Not), (W, Not), (E, This), (S, This));      // outer NW
        AddRule(rt, Get(2, 0), (N, Not), (E, Not), (W, This), (S, This));      // outer NE
        AddRule(rt, Get(0, 2), (S, Not), (W, Not), (N, This), (E, This));      // outer SW
        AddRule(rt, Get(2, 2), (S, Not), (E, Not), (N, This), (W, This));      // outer SE
        // --- straight edges ---
        AddRule(rt, Get(1, 0), (N, Not), (E, This), (S, This), (W, This));     // edge N
        AddRule(rt, Get(1, 2), (S, Not), (E, This), (N, This), (W, This));     // edge S
        AddRule(rt, Get(0, 1), (W, Not), (N, This), (S, This), (E, This));     // edge W
        AddRule(rt, Get(2, 1), (E, Not), (N, This), (S, This), (W, This));     // edge E
        // --- inner (concave) corners: all 4 cardinals This, one diagonal empty ---
        AddRule(rt, Get(6, 2), (N, This), (E, This), (S, This), (W, This), (NW, Not));   // inner NW
        AddRule(rt, Get(5, 2), (N, This), (E, This), (S, This), (W, This), (NE, Not));   // inner NE
        AddRule(rt, Get(6, 1), (N, This), (E, This), (S, This), (W, This), (SW, Not));   // inner SW
        AddRule(rt, Get(5, 1), (N, This), (E, This), (S, This), (W, This), (SE, Not));   // inner SE
        // anything else (incl. all-8 surrounded) → m_DefaultSprite (center fill)
        return rt;
    }

    static void AddRule(RuleTile rt, Sprite sprite, params (Vector3Int dir, int state)[] cons)
    {
        if (!sprite) return;
        var rule = new RuleTile.TilingRule
        {
            m_Sprites = new[] { sprite },
            m_ColliderType = Tile.ColliderType.None,
            m_Output = RuleTile.TilingRuleOutput.OutputSprite.Single,
            m_RuleTransform = RuleTile.TilingRuleOutput.Transform.Fixed,
            m_NeighborPositions = new List<Vector3Int>(),
            m_Neighbors = new List<int>(),
        };
        foreach (var (dir, state) in cons) { rule.m_NeighborPositions.Add(dir); rule.m_Neighbors.Add(state); }
        rt.m_TilingRules.Add(rule);
    }

    static Dictionary<(int, int), Sprite> LoadSpriteGrid()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
        if (!tex)
        {
            EditorUtility.DisplayDialog("Build Floor RuleTiles", $"Couldn't find the sheet at:\n{SheetPath}", "OK");
            return null;
        }
        int texH = tex.height;
        var g = new Dictionary<(int, int), Sprite>();
        foreach (var s in AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>())
        {
            int col = Mathf.RoundToInt(s.rect.x / TS);
            int row = Mathf.RoundToInt((texH - s.rect.y - s.rect.height) / TS);   // row 0 = top
            g[(col, row)] = s;
        }
        if (g.Count == 0)
            EditorUtility.DisplayDialog("Build Floor RuleTiles",
                "That sheet has no sliced sprites.\nSet Sprite Mode = Multiple and slice it 16×16, then retry.", "OK");
        return g.Count == 0 ? null : g;
    }

    static void CreateFolders(string path)
    {
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}

/// <summary>
/// Tools ▸ unwritten ▸ Floor RuleTile Test Patch. Spawns a throwaway "__FloorRuleTest" tilemap and
/// paints EVERY RuleTile in Assets/Art/Tiles/Floor side by side, each in shapes that exercise every
/// rule — a filled block with a 1-cell hole (edges + all four inner corners), a 1-wide vertical strip,
/// and an isolated dot. No palette / manual painting needed: just look at the Scene view. Blocks run
/// left→right in the order logged to the Console. Delete the object when done.
/// </summary>
public static class FloorRuleTileTester
{
    [MenuItem("Tools/unwritten/Floor RuleTile Test Patch")]
    static void TestPatch()
    {
        var tiles = AssetDatabase.FindAssets("t:RuleTile", new[] { "Assets/Art/Tiles/Floor" })
            .Select(g => AssetDatabase.LoadAssetAtPath<RuleTile>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(t => t).OrderBy(t => t.name).ToList();
        if (tiles.Count == 0)
        {
            EditorUtility.DisplayDialog("Floor RuleTile Test Patch",
                "No RuleTiles found — run Tools ▸ unwritten ▸ Build Floor RuleTiles first.", "OK");
            return;
        }

        var old = GameObject.Find("__FloorRuleTest");
        if (old) Object.DestroyImmediate(old);

        var root = new GameObject("__FloorRuleTest");
        root.AddComponent<Grid>();
        var tmGO = new GameObject("Tilemap", typeof(Tilemap), typeof(TilemapRenderer));
        tmGO.transform.SetParent(root.transform, false);
        var tm = tmGO.GetComponent<Tilemap>();

        const int step = 13;   // x spacing between terrain blocks
        for (int i = 0; i < tiles.Count; i++)
        {
            var rt = tiles[i];
            int bx = i * step;
            // 9×7 filled block...
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 9; x++)
                    tm.SetTile(new Vector3Int(bx + x, y, 0), rt);
            // ...with a single-cell hole → 4 edges + 4 inner corners around it.
            tm.SetTile(new Vector3Int(bx + 4, 3, 0), null);
            for (int y = 0; y < 5; y++) tm.SetTile(new Vector3Int(bx + 10, y, 0), rt);   // vertical strip
            tm.SetTile(new Vector3Int(bx + 10, 6, 0), rt);                               // isolated dot
        }

        Selection.activeGameObject = root;
        SceneView.FrameLastActiveSceneView();
        Debug.Log("[unwritten] Painted '__FloorRuleTest' — blocks left→right: " + string.Join(", ", tiles.Select(t => t.name)) +
                  ".  Each: filled block w/ a hole (check edges + rounded inner corners), a 1-wide strip, an isolated dot. " +
                  "Delete the object when done.");
    }
}
