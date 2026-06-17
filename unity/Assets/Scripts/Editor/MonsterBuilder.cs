using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared player-combat + hardware-cursor setup helpers used by the persistent Systems layer
/// (<c>Setup Global Systems</c> / <c>Run All</c>). The old 2D sprite-sheet monster/spawner creation
/// menus were retired with the 2.5D pivot — mobs are now billboards built by <see cref="Build25DSpike"/>.
/// (Kept under the original type name so existing callers — RunAll, GlobalSystemsBuilder — keep working.)
/// </summary>
public static class MonsterBuilder
{
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
    /// (the persistent GameSystems) using the Outline Kenney cursors. Force-reimports the three
    /// PNGs so they're readable Cursor textures (see PixelArtImportPostprocessor).</summary>
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
}
