using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click enemy builders (beginner-friendly), mirroring <c>Build Player</c>.
/// Select a sliced monster SpriteSheet in the Project window, then run either:
///
///   • <b>Create Monster from selected SpriteSheet</b> — drops one enemy in the scene.
///   • <b>Create Monster Spawner from selected SpriteSheet</b> — saves the enemy as a
///     reusable prefab (Assets/Prefabs/Monsters/) and drops a <see cref="MonsterSpawner"/>
///     that keeps a pack of them alive.
///
/// Both build a Monster with a SpriteRenderer, physics body + collider,
/// <see cref="Health"/>, <see cref="MonsterAI"/>, and (for a 4-column walk sheet) a
/// <see cref="CharacterAnimator2D"/>, and make sure the scene's Player has a
/// <see cref="PlayerAttacker"/> so you can fight back.
/// </summary>
public static class MonsterBuilder
{
    const int CellSize = 16;   // Ninja Adventure art is 16 px per cell.
    const int WalkFrames = 4;  // walk-cycle frames per direction.
    const string PrefabDir = "Assets/Prefabs/Monsters";

    [MenuItem("Tools/unwritten/Create Monster from selected SpriteSheet")]
    static void CreateMonster()
    {
        var tex = Selection.activeObject as Texture2D;
        var go = BuildMonster(tex);
        if (go == null) return;

        var pc = Object.FindFirstObjectByType<PlayerController2D>();
        go.transform.position = NearPlayer(pc, 4f);

        Undo.RegisterCreatedObjectUndo(go, "Create Monster");
        string combat = EnsurePlayerCombat(pc);

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log($"[unwritten] Created Monster from '{tex.name}'.{combat} " +
                  "Press Play, then RIGHT-CLICK the monster to attack it (or press A then click). A down-arrow marks your target.");
    }

    [MenuItem("Tools/unwritten/Create Monster Spawner from selected SpriteSheet")]
    static void CreateMonsterSpawner()
    {
        var tex = Selection.activeObject as Texture2D;
        var monster = BuildMonster(tex);
        if (monster == null) return;

        // Author the prefab from the freshly-built monster, then discard the temp
        // scene instance — the spawner will instantiate copies at runtime.
        EnsureFolder(PrefabDir);
        string prefabPath = $"{PrefabDir}/Monster_{tex.name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(monster, prefabPath);
        Object.DestroyImmediate(monster);

        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Create Monster Spawner",
                $"Couldn't save the prefab to '{prefabPath}'.", "OK");
            return;
        }

        var go = new GameObject("MonsterSpawner_" + tex.name);
        var spawner = go.AddComponent<MonsterSpawner>();
        spawner.monsterPrefab = prefab;

        var pc = Object.FindFirstObjectByType<PlayerController2D>();
        go.transform.position = NearPlayer(pc, 8f);

        Undo.RegisterCreatedObjectUndo(go, "Create Monster Spawner");
        string combat = EnsurePlayerCombat(pc);

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log($"[unwritten] Saved prefab '{prefabPath}' and dropped MonsterSpawner_{tex.name} " +
                  $"(keeps up to {spawner.maxAlive} alive within {spawner.spawnRadius} units).{combat} " +
                  "Move the spawner where you want the pack, then press Play.");
    }

    /// <summary>
    /// Builds a Monster GameObject from a sliced sheet (no positioning / Undo /
    /// player wiring — the callers handle that). Returns null and shows a dialog
    /// if the sheet isn't usable.
    /// </summary>
    public static GameObject BuildMonster(Texture2D tex)
    {
        if (tex == null)
        {
            EditorUtility.DisplayDialog("Create Monster",
                "First click a sliced monster SpriteSheet texture in the Project window " +
                "(e.g. a Slime / Skull / Bear sheet), then run this menu again.",
                "OK");
            return null;
        }

        string path = AssetDatabase.GetAssetPath(tex);
        var frames = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(FrameIndex)
            .ToArray();

        if (frames.Length == 0)
        {
            EditorUtility.DisplayDialog("Create Monster",
                $"'{tex.name}' has no sprites. Set Sprite Mode = Multiple and slice it " +
                "(Sprite Editor ▸ Slice ▸ Grid By Cell Size ▸ 16 × 16), then run this again.",
                "OK");
            return null;
        }

        int cols = Mathf.Max(1, tex.width / CellSize);
        int walk = Mathf.Min(WalkFrames, frames.Length / cols);

        var go = new GameObject("Monster_" + tex.name);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 0;          // shared "units" band — depth is by Y (transparency axis), not a fixed order
        sr.sprite = frames[0];

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        var box = go.AddComponent<BoxCollider2D>();
        box.size = Vector2.one * 0.7f;

        // Phase F: give monsters enough HP that a few swings are needed (player base ATK 4).
        go.AddComponent<Health>().maxHealth = 12;
        go.AddComponent<MonsterAI>();
        // MonsterAI's [RequireComponent(PathAgent)] already added the agent — fetch it, don't add a 2nd.
        var agent = go.GetComponent<PathAgent>() ?? go.AddComponent<PathAgent>();
        agent.agentRadius = 0.35f;                   // ≈ half the 0.7 body — keep clear of walls
        go.AddComponent<YDepthSorter>();             // per-object draw order by feet (occlude / be occluded)

        if (cols >= 4 && walk >= 1)
        {
            Sprite[] Column(int c) =>
                Enumerable.Range(0, walk).Select(r => frames[c + r * cols]).ToArray();

            var anim = go.AddComponent<CharacterAnimator2D>();
            anim.down  = Column(0);
            anim.up    = Column(1);
            anim.left  = Column(2);
            anim.right = Column(3);
        }
        return go;
    }

    static Vector3 NearPlayer(PlayerController2D pc, float dx) =>
        (pc != null ? pc.transform.position : Vector3.zero) + new Vector3(dx, 0f, 0f);

    const string SlashFxPath = "Assets/Art/NinjaAdventure/FX/Attack/SlashCurved/SpriteSheet.png";

    /// <summary>Ensure the player has mouse-target combat (range/cooldown) plus the
    /// attack-pose + slash-FX feedback, so swings don't look static.</summary>
    public static string EnsurePlayerCombat(PlayerController2D pc)
    {
        if (pc == null) return "";
        var atk = pc.GetComponent<PlayerAttacker>();
        bool added = atk == null;
        if (added) atk = Undo.AddComponent<PlayerAttacker>(pc.gameObject);
        atk.range = 1.4f;
        atk.cooldown = 0.6f;
        atk.windupTime = 0.18f;   // damage lands at the end of this window, so S can cancel it

        // Smart pathfinding: route the player around walls on right-click move / attack / follow.
        var agent = pc.GetComponent<PathAgent>();
        if (agent == null) agent = Undo.AddComponent<PathAgent>(pc.gameObject);
        agent.agentRadius = 0.4f;
        EditorUtility.SetDirty(agent);

        // Slash FX strip shown on each swing.
        var slash = AssetDatabase.LoadAssetAtPath<Texture2D>(SlashFxPath);
        if (slash != null) atk.slashSheet = slash;
        EditorUtility.SetDirty(atk);

        // Dota-style order controller (right-click move/attack/follow, A attack-click, S stop).
        // The Kenney hardware cursor is no longer added here — it's a global system on
        // GameSystems now (Tools ▸ unwritten ▸ Setup Global Systems), so it survives portals.
        if (pc.GetComponent<PlayerCommander>() == null)
            Undo.AddComponent<PlayerCommander>(pc.gameObject);

        // Attack pose, pulled from this character's own SeparateAnim/Attack.png.
        bool pose = false;
        var anim = pc.GetComponent<CharacterAnimator2D>();
        var sr = pc.GetComponent<SpriteRenderer>();
        if (anim != null && sr != null && sr.sprite != null)
        {
            string sheetPath = AssetDatabase.GetAssetPath(sr.sprite.texture);
            if (!string.IsNullOrEmpty(sheetPath))
            {
                string folder = Path.GetDirectoryName(sheetPath).Replace('\\', '/');
                var attackTex = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/SeparateAnim/Attack.png");
                if (attackTex != null) { anim.attackSheet = attackTex; EditorUtility.SetDirty(anim); pose = true; }
            }
        }

        string fx = (slash != null ? " slash FX" : "") + (pose ? (slash != null ? " +" : "") + " attack pose" : "");
        return (added ? " Added Dota-style combat to the Player" : " Player combat ready") +
               " (right-click move/attack/follow, A+click attack-move, S stop; smart pathfinding" +
               (fx.Length > 0 ? ";" + fx : "") + " wired).";
    }

    const string CursorDir = "Assets/Art/KenneyCursorPack/PNG/Outline/Default";

    /// <summary>Attach + configure the hardware <see cref="GameCursor"/> on a host object
    /// (now the persistent GameSystems, not the player) using the Outline Kenney cursors.
    /// Force-reimports the three PNGs so they're readable Cursor textures (see
    /// PixelArtImportPostprocessor) — no manual reimport needed.</summary>
    public static void ConfigureCursor(GameObject host)
    {
        foreach (var n in new[] { "pointer_b", "tool_sword_a", "target_b" })
            AssetDatabase.ImportAsset($"{CursorDir}/{n}.png", ImportAssetOptions.ForceUpdate);

        var cursor = host.GetComponent<GameCursor>();
        if (cursor == null) cursor = Undo.AddComponent<GameCursor>(host);

        cursor.defaultCursor     = LoadCursorTex("pointer_b");     // normal pointer
        cursor.attackCursor      = LoadCursorTex("tool_sword_a");  // hovering an enemy
        cursor.attackMoveCursor  = LoadCursorTex("target_b");      // A-armed (attack-click)
        cursor.defaultHotspot    = new Vector2(3, 3);              // pointer tip (top-left)
        cursor.attackHotspot     = Vector2.zero;                   // (0,0) => auto-centered
        cursor.attackMoveHotspot = Vector2.zero;
        EditorUtility.SetDirty(cursor);

        if (cursor.defaultCursor == null)
            Debug.LogWarning("[unwritten] Cursor textures not found under " + CursorDir +
                             " — the default OS cursor will be used. Check the KenneyCursorPack import.");
    }

    static Texture2D LoadCursorTex(string name)
        => AssetDatabase.LoadAssetAtPath<Texture2D>($"{CursorDir}/{name}.png");

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // Unity names sliced sprites "<sheet>_0" … "_27"; sort by that trailing number.
    static int FrameIndex(Sprite s)
    {
        int u = s.name.LastIndexOf('_');
        return (u >= 0 && int.TryParse(s.name.Substring(u + 1), out int n)) ? n : 0;
    }
}
