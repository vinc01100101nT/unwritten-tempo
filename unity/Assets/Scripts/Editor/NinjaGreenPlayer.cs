using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the Ninja Adventure <b>CharacterAnimated/NinjaGreen</b> set the player's avatar — the only
/// character in the pack that ships full animations (idle / walk / 4-frame attack swing / hit / death).
///
/// <para>The pack is COLUMN-MAJOR (direction = column, frame = row). We BAKE a clean, normalized sheet so
/// the runtime slices it trivially: each frame is copied from its master cell into a uniform grid (matches
/// the project's "pre-bake over on-Play" rule — zero runtime cost).</para>
///
/// <para>The chosen weapon is composited onto the ATTACK frames only (nowhere else — idle/walk/hit/death
/// are body-only). The weapon sheet is its own 4×4 grid of 64px cells (column = direction, row = swing
/// frame); each 64px cell is composited, centred, over the matching 32px body attack frame — oversized so
/// the slash arc extends past the character. The on-hit <see cref="SlashEffect"/> (spawned by
/// <see cref="Player3DAttacker"/>) still plays at the target too.</para>
///
/// <para>Baked layout (64px cells, the 32px body centred in each): 4 direction rows (Down, Up, Left, Right)
/// × [idle f0-3 | walk f0-3 | attack f0-3 | hit f0-1], plus a 5th row holding the 2 death frames. We slice
/// at ppu 16 (the ~16px character ≈ 1 world unit) and align the CharacterController capsule bottom to the
/// sprite's feet so <see cref="PersistentPlayer3D"/> grounds it correctly.</para>
///
/// Reused by <see cref="Build25DSpike.BuildPlayer"/> (every rebuild stays animated) and runnable on its
/// own against the player in the open scene via the Tools menu.
/// </summary>
public static class NinjaGreenPlayer
{
    const string Root = "Assets/Art/NinjaAdventure/Actor/CharacterAnimated";
    const string BakeDir = Root + "/NinjaGreen/_Baked";
    public const string DefaultWeapon = "Katana";

    public const int BodyCell = 32;   // character (master) frame size (px)
    public const int Cell = 64;       // baked/render cell — matches the weapon sheet, big enough for the arc
    public const int Inset = (Cell - BodyCell) / 2;   // where the 32px body sits inside the 64px cell (16)
    public const int Ppu  = 16;       // slice ppu — renders the ~16px character ≈ 1 world unit tall
    // The body is centred in the cell; its feet sit ~8px below cell-centre, so at ppu 16 they're 0.5 units
    // below the transform (independent of cell size, since the character is centred and ppu is fixed).
    public const float FeetBelowCenterLocal = 0.5f;

    // ---- source layout --------------------------------------------------------------------------------
    // Body (master sheet) is COLUMN-MAJOR: direction = column (0=Down 1=Up 2=Left 3=Right), frame = row.
    //   Idle = rows 0-3, cols 0-3       Walk = rows 4-7, cols 0-3       Attack body = rows 0-3, cols 4-7
    //   Hit  = rows 4-5, cols 4-7       Dead = col 4, rows 13-14 (collapse → lying)
    // Weapon sheet is its OWN 4×4 grid of 64px cells: column = direction (0=Down 1=Up 2=Left 3=Right),
    // row = swing frame 0-3. Each 64px weapon cell overlays the matching 32px body attack frame, centred —
    // the cell is oversized so the slash arc can extend past the character. Weapon shows ONLY on attack.
    // We BAKE into a row-major normalized sheet (one direction per ROW) so the runtime slices simply.
    public const int Frames = 4;       // idle / walk / attack frames per direction
    public const int HitFrames = 2;    // hit/flinch frames per direction
    public const int WalkRowBase = 4;  // master row where the walk block starts
    public const int AtkBodyCol = 4;   // master column where the attack-body block starts
    public const int DeadCol = 4, DeadRow0 = 13;   // master cells: (13,4) collapse, (14,4) lying

    // Baked (normalized) layout: direction = ROW; column groups below; death on a trailing row.
    public const int IdleCol = 0, WalkCol = 4, AtkCol = 8, HitCol = 12;
    public const int OutCols = 16, OutRows = 5;       // 4 dir rows + 1 death row
    public const int OutDeadRow = 4, OutDeadCol = 0;  // 2 death frames at (4,0) collapse and (4,1) lying

    // ---- menu ----------------------------------------------------------------
    [MenuItem("Tools/unwritten/Player/Use NinjaGreen Player", priority = 40)]
    static void ApplyToOpenScene()
    {
        var baked = BakeIfNeeded(DefaultWeapon);
        if (baked == null) return;

        var player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("NinjaGreen Player",
                "No player found in the open scene. Open the player scene (or run a Build that creates the " +
                "player — e.g. Tools ▸ unwritten ▸ Setup Systems Scene), then re-run this.", "OK");
            return;
        }

        ApplyTo(player.gameObject, baked);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorUtility.DisplayDialog("NinjaGreen Player",
            "Player wired to the animated NinjaGreen sheet.\n\n" +
            "Press Play — animated idle, walk, a 4-frame attack swing with the katana (drawn only during " +
            "the swing), a hit flinch and the death pose.\n\n" +
            "Save the scene to keep it. The baked sheet lives under " + BakeDir + ".", "OK");
    }

    // ---- wiring (shared with Build25DSpike.BuildPlayer) -----------------------
    /// <summary>Bake the default weapon (if needed) and point the player at it. Safe no-op when the
    /// CharacterAnimated art is missing — the player keeps its legacy single-pose look.</summary>
    public static void TryApply(GameObject playerGO)
    {
        if (playerGO == null || playerGO.GetComponent<Player3DController>() == null) return;
        var baked = BakeIfNeeded(DefaultWeapon);
        if (baked != null) ApplyTo(playerGO, baked);
    }

    /// <summary>Point <paramref name="playerGO"/>'s controller at the baked animated sheet and align its
    /// scale / grounding / collider to the 32px centred frames.</summary>
    public static void ApplyTo(GameObject playerGO, Texture2D baked)
    {
        var ctrl = playerGO.GetComponent<Player3DController>();
        ctrl.animSheet = baked;
        ctrl.animCell = Cell;
        ctrl.animPpu = Ppu;

        float scale = Build25DSpike.BillboardScale;
        playerGO.transform.localScale = Vector3.one * scale;
        var pos = playerGO.transform.position;
        pos.y = FeetBelowCenterLocal * scale;   // lift so the feet rest on ground y=0 (initial; maps re-ground)
        playerGO.transform.position = pos;

        // Tuned SHORT capsule (clears overhead eaves/leaves; bumps/climbs ground geometry). Single source
        // of truth in Player3DController; PersistentPlayer3D.MoveTo re-grounds across maps from its bottom.
        var cc = playerGO.GetComponent<CharacterController>();
        if (cc != null)
            Player3DController.ApplyDefaultCapsule(cc);

        EditorUtility.SetDirty(ctrl);
        EditorUtility.SetDirty(playerGO);
    }

    // ---- baking --------------------------------------------------------------
    /// <summary>Bakes (or re-bakes) the normalized NinjaGreen player sheet and returns it.
    /// Null (with a warning) if the source art is missing.</summary>
    public static Texture2D BakeIfNeeded(string weapon)
    {
        string masterPath = Root + "/NinjaGreen/SpriteSheet.png";
        if (!File.Exists(masterPath))
        { Debug.LogWarning("[unwritten] NinjaGreen master not found: " + masterPath); return null; }

        // Always re-bake (overwriting any existing file in place, preserving its .meta/GUID) so a stale
        // cached sheet from an earlier version of this baker can never mask a fix. The bake is cheap and
        // only runs on explicit player builds.
        string outPath = BakeDir + "/NinjaGreen_" + weapon + ".png";

        // Weapon sheet drives the attack-swing blade; if it's missing, BakeNormalized warns and bakes the
        // attack body-only (the player still works, just without a visible weapon).
        string weaponPath = Root + "/Weapon/" + weapon + ".png";

        var baked = BakeNormalized(masterPath, weaponPath);
        if (baked == null) return null;

        Directory.CreateDirectory(BakeDir);
        File.WriteAllBytes(outPath, baked.EncodeToPNG());
        Object.DestroyImmediate(baked);
        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
        ConfigureImport(outPath);
        Debug.Log($"[unwritten] Baked animated player sheet: {outPath}");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    // Copy each frame into the normalized grid: the 32px body centred in a 64px output cell, with the 64px
    // weapon cell composited on top for attack frames only. Reads the raw PNGs so import settings don't
    // matter. Idle / walk / hit / death are body-only, so the katana shows only during the swing.
    static Texture2D BakeNormalized(string masterPath, string weaponPath)
    {
        var master = LoadRaw(masterPath);
        if (master == null) return null;
        var weapon = LoadRaw(weaponPath);   // null tolerated → attack bakes body-only
        if (weapon == null) Debug.LogWarning("[unwritten] Weapon sheet missing; attack baked without a blade: " + weaponPath);

        var mp = master.GetPixels32();
        var wp = weapon != null ? weapon.GetPixels32() : System.Array.Empty<Color32>();
        var outPx = new Color32[OutCols * Cell * OutRows * Cell];   // starts fully transparent

        int mw = master.width, mh = master.height;
        int ww = weapon != null ? weapon.width : 0, wh = weapon != null ? weapon.height : 0;

        for (int d = 0; d < 4; d++)        // d = direction (0=down 1=up 2=left 3=right)
        {
            for (int f = 0; f < Frames; f++)   // f = animation frame
            {
                BlitBody(mp, mw, mh, f,               d,              outPx, d, IdleCol + f);   // idle
                BlitBody(mp, mw, mh, WalkRowBase + f, d,              outPx, d, WalkCol + f);   // walk
                BlitBody(mp, mw, mh, f,               AtkBodyCol + d, outPx, d, AtkCol + f);    // attack body
                BlitWeapon(wp, ww, wh, f, d, outPx, d, AtkCol + f);                             // attack weapon (64px)
            }
            for (int f = 0; f < HitFrames; f++)
                BlitBody(mp, mw, mh, WalkRowBase + f, AtkBodyCol + d, outPx, d, HitCol + f);    // hit (master cols 4-7)
        }

        // Death — one shared pose (not per-direction): collapse then lying. Body only.
        BlitBody(mp, mw, mh, DeadRow0,     DeadCol, outPx, OutDeadRow, OutDeadCol);
        BlitBody(mp, mw, mh, DeadRow0 + 1, DeadCol, outPx, OutDeadRow, OutDeadCol + 1);

        var outTex = new Texture2D(OutCols * Cell, OutRows * Cell, TextureFormat.RGBA32, false);
        outTex.SetPixels32(outPx);
        outTex.Apply();
        Object.DestroyImmediate(master);
        if (weapon != null) Object.DestroyImmediate(weapon);
        return outTex;
    }

    // Copy a 32px body cell (master grid coords) into the CENTRE of a 64px output cell. All pixel arrays are
    // bottom-up (Texture2D origin is bottom-left).
    static void BlitBody(Color32[] mp, int mw, int mh, int srcRow, int srcCol, Color32[] outPx, int dstRow, int dstCol)
    {
        int outW = OutCols * Cell, outH = OutRows * Cell;
        for (int by = 0; by < BodyCell; by++)         // within body cell, bottom-up
        {
            int sy = mh - (srcRow + 1) * BodyCell + by;
            int dy = outH - (dstRow + 1) * Cell + Inset + by;
            if (sy < 0 || sy >= mh) continue;
            for (int bx = 0; bx < BodyCell; bx++)
            {
                int sx = srcCol * BodyCell + bx;
                if (sx < 0 || sx >= mw) continue;
                outPx[dy * outW + (dstCol * Cell + Inset + bx)] = mp[sy * mw + sx];
            }
        }
    }

    // Composite a 64px weapon cell (weapon grid: col = direction, row = frame) over a 64px output cell.
    static void BlitWeapon(Color32[] wp, int ww, int wh, int wRow, int wCol, Color32[] outPx, int dstRow, int dstCol)
    {
        if (ww == 0 || wh == 0) return;   // no weapon sheet → body-only attack
        int outW = OutCols * Cell, outH = OutRows * Cell;
        for (int wy = 0; wy < Cell; wy++)
        {
            int sy = wh - (wRow + 1) * Cell + wy;
            int dy = outH - (dstRow + 1) * Cell + wy;
            if (sy < 0 || sy >= wh) continue;
            for (int wx = 0; wx < Cell; wx++)
            {
                int sx = wCol * Cell + wx;
                if (sx < 0 || sx >= ww) continue;
                var w = wp[sy * ww + sx];
                if (w.a == 0) continue;
                int di = dy * outW + (dstCol * Cell + wx);
                outPx[di] = Over(w, outPx[di]);
            }
        }
    }

    static Texture2D LoadRaw(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); return null; }
        return tex;
    }

    // Standard "source over destination" alpha blend. For pixel art (alpha ~0/255) this is essentially
    // "draw the weapon where it has pixels", but the proper blend keeps any soft edges clean.
    static Color32 Over(Color32 s, Color32 d)
    {
        float sa = s.a / 255f, da = d.a / 255f;
        float oa = sa + da * (1f - sa);
        if (oa <= 0f) return new Color32(0, 0, 0, 0);
        byte C(byte cs, byte cd) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt((cs * sa + cd * da * (1f - sa)) / oa), 0, 255);
        return new Color32(C(s.r, d.r), C(s.g, d.g), C(s.b, d.b), (byte)Mathf.RoundToInt(oa * 255f));
    }

    static void ConfigureImport(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;   // we slice frames at runtime via SpriteSheetCache.Grid
        ti.filterMode = FilterMode.Point;                // crisp pixels
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.spritePixelsPerUnit = Ppu;
        ti.SaveAndReimport();
    }
}
