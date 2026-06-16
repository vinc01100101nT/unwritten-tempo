using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click player builder (beginner-friendly). After a character SpriteSheet
/// has been sliced into 16×16 frames, select that texture in the Project window
/// and run <b>Tools ▸ unwritten ▸ Build Player from selected SpriteSheet</b>.
///
/// Ninja Adventure character sheets are laid out as COLUMNS = facing direction,
/// ROWS = walk frames (4 wide × 7 tall). So each walk clip is one column. Default
/// column order is Down, Up, Left, Right (col 0,1,2,3) — if a direction looks
/// wrong in play, that's just the column order; ping me and it's a one-line swap.
///
/// Re-running this rebuilds the Player from scratch (it deletes any existing one),
/// so it's safe to run again after a fix.
/// </summary>
public static class PlayerBuilder
{
    const int CellSize = 16;   // Ninja Adventure art is 16 px per cell.
    const int WalkFrames = 4;  // walk-cycle frames per direction (top rows of each column).

    [MenuItem("Tools/unwritten/Build Player from selected SpriteSheet")]
    static void BuildPlayer()
    {
        var tex = Selection.activeObject as Texture2D;
        if (tex == null)
        {
            EditorUtility.DisplayDialog("Build Player",
                "First click the sliced character SpriteSheet texture in the Project window, then run this menu again.",
                "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(tex);
        var frames = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(FrameIndex)
            .ToArray();

        int cols = Mathf.Max(1, tex.width / CellSize);              // 4 for these sheets
        int walk = Mathf.Min(WalkFrames, frames.Length / cols);     // frames per direction

        if (frames.Length < cols * 1 || cols < 4 || walk < 1)
        {
            EditorUtility.DisplayDialog("Build Player",
                $"'{tex.name}' isn't sliced into a usable grid (found {frames.Length} frames, {cols} columns).\n\n" +
                "Slice it first: Inspector → Sprite Mode = Multiple → Open Sprite Editor → " +
                "Slice ▸ Type = Grid By Cell Size ▸ 16 × 16 ▸ Slice ▸ Apply. Then run this again.",
                "OK");
            return;
        }

        // Replace any previous Player so re-running is clean.
        var old = GameObject.Find("Player");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var go = new GameObject("Player");

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 0;     // shared "units" band — depth comes from the Y transparency axis, not a fixed order
        sr.sprite = frames[0];   // set the sprite BEFORE adding the collider (auto-size reads it)

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        var box = go.AddComponent<BoxCollider2D>();
        box.size = Vector2.one * 0.8f;   // explicit, slightly inset — never rely on auto-size being non-zero
        go.AddComponent<PlayerController2D>();
        go.AddComponent<PathAgent>();          // smart pathfinding (routes around walls)
        go.AddComponent<PlayerInteractor>();   // lets the player talk to NPCs / use portals
        go.AddComponent<YDepthSorter>();       // per-object draw order by feet (occlude / be occluded)

        // One column = one facing. Take the top `walk` rows of each column.
        Sprite[] Column(int c) =>
            Enumerable.Range(0, walk).Select(r => frames[c + r * cols]).ToArray();

        var anim = go.AddComponent<CharacterAnimator2D>();
        anim.down  = Column(0);
        anim.up    = Column(1);
        anim.left  = Column(2);
        anim.right = Column(3);

        Undo.RegisterCreatedObjectUndo(go, "Build Player");

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;

        Debug.Log($"[unwritten] Built Player from '{tex.name}' — {cols} columns, {walk} walk frames/direction. " +
                  "Next: Tools ▸ unwritten ▸ Setup Global Systems to make this the persistent player " +
                  "(it'll survive portals, carrying movement + the global cursor).");
    }

    // Unity names sliced sprites "<sheet>_0" … "_27"; sort by that trailing number.
    static int FrameIndex(Sprite s)
    {
        int u = s.name.LastIndexOf('_');
        return (u >= 0 && int.TryParse(s.name.Substring(u + 1), out int n)) ? n : 0;
    }
}
