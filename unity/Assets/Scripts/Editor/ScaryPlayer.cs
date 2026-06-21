using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the <b>Sscary/Male</b> pack the player's avatar — a 48×64 billboard with idle / walk / jump / dash /
/// death animations plus Shadow and Dust layers. Wires the per-direction strips straight onto
/// <see cref="Player3DController"/>'s Scary fields (sliced at runtime by <see cref="SpriteSheetCache.Row"/>);
/// no bake step, since the pack already ships clean per-direction strips.
///
/// <para>The art has 6 visual angles (front, back, the four diagonals); the LEFT diagonals are exact mirrors
/// of the RIGHT, so we only load 4 drawn clips per action (down / up / right-down / right-up) and the
/// controller resolves 8-direction facing onto them with a horizontal flip. The foot offset is auto-measured
/// from the idle-down strip so the character grounds correctly at the chosen scale.</para>
///
/// Reused by <see cref="Build25DSpike.BuildPlayer"/> (full replacement: every rebuild stays the Sscary player,
/// falling back to NinjaGreen only if this art is absent) and runnable on its own via the Tools menu.
/// </summary>
public static class ScaryPlayer
{
    const string Root = "Assets/Art/Sscary/Male";
    const int CellW = 48, CellH = 64, Frames = 8, Ppu = 48;
    // On-screen size relative to the 3D map (mobs use Build25DSpike.BillboardScale = 0.7; the player reads a
    // touch larger so it stays prominent). Tune here if the character feels too big/small.
    const float Scale = 0.85f;
    // Walk speed for the small Sscary character (the controller default 4 reads too fast at this size;
    // mobs run ~1.6–2.7 after the 0.75× size coupling). Fine-tune live in P → DevSettings ▸ Player ▸ Move speed.
    const float MoveSpeed = 2.6f;
    // Fallback foot offset (local units, pre-scale) used only if the idle strip can't be measured.
    const float DefaultFeetBelowCenter = 0.42f;

    // ---- menu ----------------------------------------------------------------
    [MenuItem("Tools/unwritten/Player/Use Sscary Player", priority = 600)]
    static void ApplyToOpenScene()
    {
        var player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Sscary Player",
                "No player found in the open scene. Open the player scene (or run Tools ▸ unwritten ▸ " +
                "Setup Systems Scene to create one), then re-run this.", "OK");
            return;
        }
        if (!TryApply(player.gameObject))
        {
            EditorUtility.DisplayDialog("Sscary Player",
                "Couldn't find the Sscary art under " + Root + " (expected Idle/idle_down.png …).", "OK");
            return;
        }
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorUtility.DisplayDialog("Sscary Player",
            "Player wired to the Sscary Male sprite (48×64, 8-direction).\n\n" +
            "Press Play — WASD to move, SPACE to hop, E to dash. Idle / walk / death animate; the Shadow and " +
            "Dust layers are auto-built at runtime.\n\nSave the scene to keep it.", "OK");
    }

    // ---- wiring (shared with Build25DSpike.BuildPlayer) -----------------------
    /// <summary>Point <paramref name="playerGO"/>'s controller at the Sscary strips, grounded + scaled.
    /// Returns false (a no-op) when the Sscary art is missing, so callers can fall back.</summary>
    public static bool TryApply(GameObject playerGO)
    {
        if (playerGO == null) return false;
        var ctrl = playerGO.GetComponent<Player3DController>();
        if (ctrl == null) return false;
        if (!File.Exists(Root + "/Idle/idle_down.png")) return false;   // art absent → let the caller fall back

        ctrl.scaryCellW = CellW; ctrl.scaryCellH = CellH; ctrl.scaryFrames = Frames; ctrl.scaryPpu = Ppu;

        ctrl.scaryIdle  = Set("/Idle/idle_down.png",  "/Idle/idle_up.png",  "/Idle/idle_right_down.png",  "/Idle/idle_right_up.png");
        ctrl.scaryWalk  = Set("/Walk/walk_down.png",  "/Walk/walk_up.png",  "/Walk/walk_right_down.png",  "/Walk/walk_right_up.png");
        ctrl.scaryJump  = Set("/Jump/Normal/Jump_Down.png", "/Jump/Normal/Jump_Up.png", "/Jump/Normal/Jump_Right_Down.png", "/Jump/Normal/Jump_Right_Up.png");
        ctrl.scaryDash  = Set("/Dash/Dash_Down.png",  "/Dash/Dash_up.png",  "/Dash/Dash_right_Down.png",  "/Dash/Dash_right_Up.png");
        ctrl.scaryDeath = Set("/Death/death_normal_down.png", "/Death/death_normal_up.png", "/Death/death_normal_right_down.png", "/Death/death_normal_right_up.png");

        // Jump dust intentionally NOT wired — it followed the body up the arc and looked wrong (removed).
        ctrl.scaryDashDust = Set("/Dash/Dust/Dash_Dust_Down.png", "/Dash/Dust/Dash_Dust_Up.png", "/Dash/Dust/Dash_Dust_right_Down.png", "/Dash/Dust/Dash_Dust_right_Up.png");
        // Shadows intentionally NOT wired — the engine renders the character's real URP cast shadow.

        // Ground + size: lift so the measured feet rest on y=0 at the chosen scale, and align the capsule
        // bottom to the feet (PersistentPlayer3D re-grounds on map load from cc.center/height).
        float fbcl = MeasureFeetBelowCenterLocal();
        ctrl.scaryFeetBelowCenter = 0.13f;   // user-preferred grounding (sits the feet slightly into the ground); measured feet ≈0.23
        playerGO.transform.localScale = Vector3.one * Scale;
        var pos = playerGO.transform.position; pos.y = fbcl * Scale; playerGO.transform.position = pos;
        // Tuned SHORT capsule (clears overhead eaves/leaves; bumps/climbs ground geometry). Single source
        // of truth in Player3DController; the transform lift above grounds the sprite, gravity settles it.
        var cc = playerGO.GetComponent<CharacterController>();
        if (cc != null)
            Player3DController.ApplyDefaultCapsule(cc);

        ctrl.moveSpeed = MoveSpeed;   // slower walk to suit the small character (was the 4 default)
        ctrl.animSheet = null;   // disable the NinjaGreen path so the Scary path takes over
        EditorUtility.SetDirty(ctrl);
        EditorUtility.SetDirty(playerGO);
        Debug.Log("[unwritten] Player wired to the Sscary Male sprite (feetBelowCenter " + fbcl.ToString("0.000") + ").");
        return true;
    }

    static Player3DController.ScaryClipSet Set(string down, string up, string rd, string ru)
        => new Player3DController.ScaryClipSet
        {
            down = Tex(Root + down), up = Tex(Root + up), rightDown = Tex(Root + rd), rightUp = Tex(Root + ru),
        };

    // Import as a runtime-sliceable pixel-art sprite (we cut frames ourselves via SpriteSheetCache.Row, so
    // Single mode is fine). Only re-imports when a setting actually changes — this touches ~35 textures.
    static Texture2D Tex(string path)
    {
        if (!File.Exists(path)) { Debug.LogWarning("[unwritten] Sscary strip missing: " + path); return null; }
        ConfigureImport(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void ConfigureImport(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool dirty = false;

        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; dirty = true; }
        if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
        if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
        if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        if (!Mathf.Approximately(ti.spritePixelsPerUnit, Ppu)) { ti.spritePixelsPerUnit = Ppu; dirty = true; }
        if (ti.maxTextureSize < 512) { ti.maxTextureSize = 512; dirty = true; }
        if (dirty) ti.SaveAndReimport();
    }

    // Auto-measure how far the character's feet sit BELOW the cell centre (local units, pre-scale), by
    // finding the lowest SOLID (a>200, so a faint baked shadow is ignored) pixel of the first idle-down frame.
    static float MeasureFeetBelowCenterLocal()
    {
        var tex = LoadRaw(Root + "/Idle/idle_down.png");
        if (tex == null) return DefaultFeetBelowCenter;
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height, maxX = Mathf.Min(CellW, w);
        int lowest = h;
        for (int y = 0; y < h && lowest == h; y++)
            for (int x = 0; x < maxX; x++)
                if (px[y * w + x].a > 200) { lowest = y; break; }
        Object.DestroyImmediate(tex);
        if (lowest >= h) return DefaultFeetBelowCenter;
        float belowCenterPx = (CellH * 0.5f) - lowest;   // centre is CellH/2 px above the bottom
        return Mathf.Max(0f, belowCenterPx) / Ppu;
    }

    static Texture2D LoadRaw(string path)
    {
        if (!File.Exists(path)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); return null; }
        return tex;
    }
}
