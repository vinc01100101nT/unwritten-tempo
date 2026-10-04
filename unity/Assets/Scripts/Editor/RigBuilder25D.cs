using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The shared 2.5D rig-builder library (RO-style look). Its public helpers (BuildPlayer/Camera/Light/
/// Character/HUD/Cursor/Monster, EnsureUniversalRenderer, MonsterSheet) build
/// the LIVE-3D rigged player + camera-facing <see cref="Billboard"/> mobs/NPCs on a 3D map viewed from a tilted camera,
/// and are called by SystemsSceneBuilder, RegionBuilder and RecipeGenerator.
///
/// It adds a 3D (Universal) Renderer ALONGSIDE the existing 2D one; index 0 (Systems) is untouched. Safe to re-run.
/// Load-bearing: deleting this class breaks the whole editor build pipeline.
/// </summary>
public static class RigBuilder25D
{
    const string UrpPath        = "Assets/Settings/UniversalRP.asset";
    const string Renderer3DPath = "Assets/Settings/Spike3DRenderer.asset";
    // Normalized foot-padding at the bottom of a mob cell (how far up the cell the feet sit). The mob
    // sprite is now FOOT-PIVOTED here (SpriteSheetCache.Walk4), so the transform sits on the ground and
    // the sprite renders up from it — the padding hangs below and is alpha-clipped by the ground plane.
    // Single owner is SpriteSheetCache.MobFootPivotY; mirror it, don't re-type it. Tune there if feet
    // clip (raise) or float (lower). (Sheets are isReadable:0, so we can't auto-measure the padding.)
    public const float SpriteFoot = SpriteSheetCache.MobFootPivotY;

    // The live-3D player's root scale. A mob's size is its own (MobSizes), so tuning mob size never resizes the
    // player. NOTE: this
    // multiplies the child avatar's own TargetHeight normalization AND its CharacterController capsule, so
    // changing it is a gameplay tweak (height + collision) — playtest before touching.
    public const float PlayerScale = 0.7f;

    /// <summary>Create a 3D Universal Renderer (once) and add it to the URP asset's renderer
    /// list; returns its index, or -1 (with a dialog) if URP's internals don't match.</summary>
    public static int EnsureUniversalRenderer()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath)
                  ?? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return Fail("Couldn't find the URP pipeline asset (expected " + UrpPath + ").");

        var so = new SerializedObject(urp);
        var list = so.FindProperty("m_RendererDataList");
        if (list == null)
            return Fail("URP asset has no 'm_RendererDataList' (version mismatch). Manually add a " +
                        "Universal Renderer to " + UrpPath + " then re-run.");

        var existing = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer3DPath);
        if (existing != null)
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == existing) return i;

        ScriptableRendererData data = existing;
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<UniversalRendererData>();
            if (data == null)
                return Fail("Couldn't create a UniversalRendererData. Manually: Assets/Settings ▸ " +
                            "Create ▸ Rendering ▸ URP Universal Renderer, name it 'Spike3DRenderer', then re-run.");
            AssetDatabase.CreateAsset(data, Renderer3DPath);
        }

        int idx = list.arraySize;
        list.arraySize = idx + 1;
        list.GetArrayElementAtIndex(idx).objectReferenceValue = data;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(urp);
        AssetDatabase.SaveAssets();
        Debug.Log($"[unwritten] Added 3D Universal Renderer to {UrpPath} at index {idx} (2D renderer stays index 0).");
        return idx;
    }

    public static void BuildCamera(int rendererIndex, GameObject player)
    {
        var go = new GameObject("Spike Camera") { tag = "MainCamera" };
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;   // SkyboxSky drives RenderSettings.skybox (procedural sky)
        cam.backgroundColor = new Color(0.55f, 0.70f, 0.85f);   // fallback if no skybox material is set
        cam.fieldOfView = 35f;
        cam.farClipPlane = 200f;
        go.transform.SetPositionAndRotation(new Vector3(0f, 10f, -8f), Quaternion.Euler(52f, 0f, 0f));
        // The listener lives on the PLAYER (AudioListenerRig), not the orbiting camera, so 3D sounds
        // attenuate from the player. Fallback: if this camera is built without a player, keep one here.
        if (player == null) go.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);   // 3D renderer, this camera only

        var follow = go.AddComponent<CameraFollow3D>();    // orbit (Q/E + right-drag) + zoom follow rig
        follow.distance = 4f;
        follow.height = 10f;
        follow.yaw = 0f;
        follow.minHeight = 0.1f;   // allow right-drag down to a near-horizontal, ground-level view
        if (player != null) follow.target = player.transform;

        var fader = go.AddComponent<CameraOccluderFader>();   // fade props that block the view of the player
        fader.enabled = false;                                // disabled by default; opt in via Dev Settings ▸ Occlusion Fade
        if (player != null) fader.target = player.transform;
    }

    public static Light BuildLight()
    {
        var go = new GameObject("Sun");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(1f, 0.96f, 0.90f);
        l.intensity = 1.1f;
        l.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        return l;
    }

    static Material _billboardMat;
    // Flat-shaded, shadow-RECEIVING material for the sprite billboards (Unwritten/BillboardLit).
    // One shared instance — the sprite texture is fed per-renderer by the SpriteRenderer.
    static Material BillboardMaterial()
    {
        if (_billboardMat != null) return _billboardMat;
        var shader = Shader.Find("Unwritten/BillboardLit");
        if (shader == null)
        {
            Debug.LogWarning("[unwritten] Shader 'Unwritten/BillboardLit' not found — sprites won't " +
                             "receive shadows. Make sure Assets/Shaders/BillboardLit.shader imported.");
            return null;
        }
        _billboardMat = new Material(shader) { name = "BillboardLit (spike)" };
        return _billboardMat;
    }

    // Make a sprite billboard light up like the rest of the 3D world: flat art that darkens in
    // shadow (receive) and throws a clean cutout shadow (cast). No-op material if the shader is missing.
    public static void ApplyBillboardLighting(SpriteRenderer sr)
    {
        var mat = BillboardMaterial();
        if (mat != null) sr.sharedMaterial = mat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;  // thin quad: cast from both faces
        sr.receiveShadows = true;
    }

    public static void BuildCursor(GameObject host)
    {
        const string dir = "Assets/0_Raw/KenneyCursorPack/PNG/Outline/Default";
        const string dirBig = "Assets/0_Raw/KenneyCursorPack/PNG/Outline/Double";   // 64×64 variants
        foreach (var n in new[] { "pointer_b", "tool_sword_a", "target_b" })
            AssetDatabase.ImportAsset($"{dir}/{n}.png", ImportAssetOptions.ForceUpdate);   // ensure readable Cursor textures
        AssetDatabase.ImportAsset($"{dirBig}/target_round_b.png", ImportAssetOptions.ForceUpdate);

        var cur = host.AddComponent<GameCursor3D>();
        cur.defaultCursor    = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/pointer_b.png");          // normal
        cur.attackCursor     = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/tool_sword_a.png");       // hovering a mob
        cur.attackMoveCursor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/target_b.png");           // A-armed
        cur.targetCursor     = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dirBig}/target_round_b.png");  // aiming a skill — large 64×64 reticle, drawn full-size (software cursor)
        // defaultHotspot is left at GameCursor3D's field default (the tip of pointer_b.png) — that field
        // is the single source of truth for the hotspot, so it isn't re-typed here.
        if (cur.defaultCursor == null)
            Debug.LogWarning("[unwritten] Kenney cursors not found under " + dir + " — using the OS cursor.");
    }

    public static void BuildCharacter()
    {
        // Always give the spike its OWN Character. The scene is rebuilt fresh each run and plays
        // standalone, so there's no duplicate — and a FindObjectByType guard wrongly skipped this
        // when another open scene (e.g. Systems) already had a Character, leaving Instance null.
        var ch = new GameObject("Character").AddComponent<Character>();   // sets Character.Instance on Play
        ch.blockSfx = SfxAssets.Set(0.9f, SfxAssets.SwordDir, "Sword Blocked 1", "Sword Blocked 2", "Sword Blocked 3");
        ch.levelUpSfx = SfxAssets.Set(0.8f, "Spells", "Firebuff 1", "Firebuff 2");   // level-up sting
        // Hurt grunt: TomMusic has NO vocals, so use a low-pitched "chop" as a meaty body-hit
        // PLACEHOLDER (swap in a CC0 human grunt later, same as the deferred monster cries).
        ch.hurtSfx = SfxAssets.Set(0.65f, "Chopping and Mining", "chop 1", "chop 2", "chop 3", "chop 4");
        ch.hurtSfx.pitch = new Vector2(0.7f, 0.85f);
    }

    public static void BuildHUD()
    {
        var canvasGO = new GameObject("HUD Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        UiKit.ApplyTextCrispness(scaler);
        canvasGO.AddComponent<GraphicRaycaster>();

        // The GraphicRaycaster only reports "pointer over HUD" if an EventSystem drives it. The real
        // game gets one from the Systems scene; the standalone spike needs its own, or clicking the
        // HP bar would fall through to the world and move the player (Player3DController.PointerOverUI).
        if (Object.FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        // Bar root, top-left.
        var bar = new GameObject("HealthBar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(canvasGO.transform, false);
        var barRT = (RectTransform)bar.transform;
        barRT.anchorMin = barRT.anchorMax = barRT.pivot = new Vector2(0f, 1f);
        barRT.anchoredPosition = new Vector2(20f, -20f);
        barRT.sizeDelta = new Vector2(340f, 30f);
        var bg = bar.GetComponent<Image>();
        bg.sprite = uiSprite; bg.type = Image.Type.Sliced; bg.color = new Color(0f, 0f, 0f, 0.6f);

        // Fill.
        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(bar.transform, false);
        var fillRT = (RectTransform)fillGO.transform;
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(3f, 3f); fillRT.offsetMax = new Vector2(-3f, -3f);
        var fill = fillGO.GetComponent<Image>();
        fill.sprite = uiSprite; fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
        fill.color = new Color(0.85f, 0.25f, 0.25f); fill.fillAmount = 1f;

        // Label.
        var label = UiKit.MakeText("Label", bar.transform, "", 16, FontStyle.Bold, TextAnchor.MiddleCenter);
        var lRT = label.rectTransform;
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one; lRT.offsetMin = lRT.offsetMax = Vector2.zero;
        label.color = Color.white;

        var hud = bar.AddComponent<HealthBarHUD>();
        hud.fill = fill; hud.label = label;
    }


    /// <summary>Spawn the wander/chase billboard mobs in a ring of <paramref name="radius"/> around
    /// <paramref name="center"/> (XZ). Reused by the town converter to drop mobs near the spawn.</summary>
    public static int BuildMonsters(Vector3 center, float radius)
    {
        var tex = MonsterSheet();
        if (tex == null) { Debug.LogWarning("[unwritten] Spike: no Ninja Adventure monster sheet found."); return 0; }

        const int count = 3;
        for (int i = 0; i < count; i++)
        {
            float a = (i * (360f / count) + 25f) * Mathf.Deg2Rad;
            BuildMonster(tex, new Vector3(center.x + Mathf.Cos(a) * radius, 0f, center.z + Mathf.Sin(a) * radius), i == 0);   // one mystic; start outside aggro
        }
        return count;
    }

    /// <summary>Build ONE wander/chase billboard mob standing at <paramref name="foot"/> (XZ; Y is the
    /// ground height there — the sprite-foot offset is added on top). Returns the GameObject so callers
    /// can parent/position it (e.g. surface-snapped scatter on terrain).</summary>
    public static GameObject BuildMonster(Texture2D tex, Vector3 foot) => BuildMonster(tex, foot, false);

    /// <summary>Back-compat overload: a default melee grunt (or a magic "mystic" if <paramref name="mystic"/>)
    /// from an explicit sheet. Used by the spike ring + training dummy. Varied spawns use the archetype
    /// overload below (see <see cref="MobRoster"/>).</summary>
    public static GameObject BuildMonster(Texture2D tex, Vector3 foot, bool mystic)
    {
        var arc = MobRoster.All[0];          // the Slime grunt as the baseline
        arc.artFolder = null;               // use the caller's texture, not the archetype's art
        arc.tint  = Color.white;
        if (mystic)
        {
            arc.name       = "Mystic";
            arc.tint       = new Color(0.62f, 0.72f, 1f);   // arcane blue marks a caster
        }
        return FitBody(BuildMonster(tex, foot, arc), arc);
    }

    /// <summary>Build a mob from a <see cref="MonsterArchetype"/>, resolving its sprite sheet from its linked art
    /// folder (falls back to the default monster sheet if that folder is missing).</summary>
    public static GameObject BuildMonster(in MonsterArchetype arc, Vector3 foot)
    {
        // A kind drawn as a 3D character (MobShadow3DVisual gives it its body at spawn) carries no flat picture.
        if (CreatureKinds.Is3D(arc.kind)) return FitBody(BuildMonster(null, foot, arc), arc);
        string dir = ArtFolderPath(arc.artFolder);
        if (TryStripArt(dir, out var idle, out var walk, out int ppu))
        {
            var go = BuildMonster(null, foot, arc);
            var anim = go.GetComponent<BillboardAnimator3D>();
            anim.idleStrip = idle;
            anim.walkStrip = walk;
            anim.stripPixelsPerUnit = ppu;
            return FitBody(go, arc);
        }
        return FitBody(BuildMonster(MonsterSheet(dir) ?? MonsterSheet(), foot, arc), arc);
    }

    /// <summary>Measure the art this mob plays, then put its size on its body through the one door the spawner uses
    /// too (<see cref="MobBody.Fit"/>). Last, because strip art is only attached after the core builder returns.</summary>
    static GameObject FitBody(GameObject go, in MonsterArchetype arc)
    {
        var anim = go.GetComponent<BillboardAnimator3D>();
        if (anim != null) MeasureArt(anim);
        MobBody.Fit(go, arc.kind, arc.size);
        return go;
    }

    /// <summary>How far the tallest played frame's crown sits above its feet line, and how wide the widest frame's
    /// painted part is, in world units at scale one — read from the picture's own pixels, so padding in the art never
    /// counts toward a creature's height. Grid art: the walk frames at the top of the four direction columns, feet
    /// line at <see cref="SpriteFoot"/> of a cell. Strip art: every frame of the idle and walk rows, feet line at the
    /// strip's own foot row.</summary>
    static void MeasureArt(BillboardAnimator3D anim)
    {
        float crown = 0f, width = 0f;
        if (anim.idleStrip.sheet != null)
        {
            MeasureStrip(anim.idleStrip, anim.stripPixelsPerUnit, ref crown, ref width);
            MeasureStrip(anim.walkStrip, anim.stripPixelsPerUnit, ref crown, ref width);
        }
        else MeasureGridSheet(anim.sheet, anim.cell, anim.walkFrames, ref crown, ref width);
        if (crown <= 0f) Debug.LogWarning($"[unwritten] {anim.name}: no painted pixels measured — its size can't be fitted.");
        anim.crownAtScaleOne = crown;
        anim.paintedWidthAtScaleOne = width;
    }

    /// <summary>How far the tallest walk frame of a GRID sheet reaches above its feet line, and how wide the widest one is
    /// painted, in world units at scale one — the walk frames at the top of the four direction columns, feet line at
    /// <see cref="SpriteFoot"/> of a cell. What a mob's body is fitted against, and what a saved character's asset
    /// records for its sheet.</summary>
    public static void MeasureGridSheet(Texture2D sheet, int cell, int walkFrames, ref float crown, ref float width,
                                        float footPx = -1f)
    {
        if (sheet == null || cell <= 0 || !ReadPixels(sheet, out var px, out int w, out int h)) return;
        int cols = Mathf.Max(1, w / cell), rows = Mathf.Max(1, h / cell);
        int frames = Mathf.Clamp(walkFrames, 1, rows);
        float feet = footPx >= 0f ? footPx : SpriteFoot * cell;
        for (int c = 0; c < 4; c++)
            for (int r = 0; r < frames; r++)
                MeasureFrame(px, w, Mathf.Min(c, cols - 1) * cell, h - (r + 1) * cell, cell, cell,
                             feet, cell, ref crown, ref width);
    }

    /// <summary>The lowest painted row across a GRID sheet's walk frames, in pixels up from the bottom of a cell — where
    /// its feet stand. -1 when nothing is painted or the picture cannot be read.</summary>
    public static float GridSheetFootPx(Texture2D sheet, int cell, int walkFrames)
    {
        if (sheet == null || cell <= 0 || !ReadPixels(sheet, out var px, out int w, out int h)) return -1f;
        int cols = Mathf.Max(1, w / cell), rows = Mathf.Max(1, h / cell);
        int frames = Mathf.Clamp(walkFrames, 1, rows);
        int lowest = int.MaxValue;
        for (int c = 0; c < Mathf.Min(4, cols); c++)
            for (int r = 0; r < frames; r++)
            {
                int x0 = c * cell, y0 = h - (r + 1) * cell;
                for (int y = y0; y < y0 + cell && y - y0 < lowest; y++)
                    for (int x = x0; x < x0 + cell; x++)
                        if (px[y * w + x].a != 0) { lowest = Mathf.Min(lowest, y - y0); break; }
            }
        return lowest == int.MaxValue ? -1f : lowest;
    }

    static void MeasureStrip(in BillboardAnimator3D.StripClip clip, int ppu, ref float crown, ref float width)
    {
        if (clip.sheet == null || clip.frames <= 0 || ppu <= 0) return;
        if (!ReadPixels(clip.sheet, out var px, out int w, out int h)) return;
        int fw = w / clip.frames;
        for (int i = 0; i < clip.frames; i++)
            MeasureFrame(px, w, i * fw, 0, fw, h, clip.footPx, ppu, ref crown, ref width);
    }

    // One frame (x, y = its bottom-left corner in pixels, Unity's bottom-up rows): the highest painted row above the
    // feet line, and the painted span across.
    static void MeasureFrame(Color32[] px, int texW, int x0, int y0, int fw, int fh, float footPx, int ppu,
                             ref float crown, ref float width)
    {
        int top = -1, minX = int.MaxValue, maxX = -1;
        for (int y = y0; y < y0 + fh; y++)
            for (int x = x0; x < x0 + fw; x++)
            {
                if (px[y * texW + x].a == 0) continue;
                if (y > top) top = y;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
        if (top < 0) return;
        crown = Mathf.Max(crown, (top - y0 + 1 - footPx) / ppu);
        width = Mathf.Max(width, (float)(maxX - minX + 1) / ppu);
    }

    // The picture's own pixels, read from its file — the imported texture is not readable, and switching its import
    // setting to make it so would be a saved change nobody asked for.
    static bool ReadPixels(Texture2D tex, out Color32[] px, out int w, out int h)
    {
        px = null; w = h = 0;
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        var copy = new Texture2D(2, 2);
        try
        {
            if (!copy.LoadImage(File.ReadAllBytes(path))) return false;
            if (copy.width != tex.width || copy.height != tex.height)
            {
                Debug.LogWarning($"[unwritten] {path}: imported at {tex.width}x{tex.height} but the file is {copy.width}x{copy.height} — not measured.");
                return false;
            }
            px = copy.GetPixels32(); w = copy.width; h = copy.height;
            return true;
        }
        finally { Object.DestroyImmediate(copy); }
    }

    /// <summary>Core builder: stamp <paramref name="arc"/>'s stats / behavior / look onto the shared
    /// billboard-mob template using <paramref name="tex"/> as the sprite sheet.</summary>
    static GameObject BuildMonster(Texture2D tex, Vector3 foot, in MonsterArchetype arc)
    {
        var go = new GameObject("Monster_" + (!string.IsNullOrEmpty(arc.name) ? arc.name : tex != null ? tex.name : "Unnamed"));
        go.transform.position = foot;   // pivot = feet on the ground (foot-pivoted sprite renders up from here)

        var msr = go.AddComponent<SpriteRenderer>();
        ApplyBillboardLighting(msr);   // flat art + receive/cast shadows
        go.AddComponent<Billboard>();

        var agent = go.AddComponent<PathAgent3D>();         // auto-adds CharacterController
        agent.agentRadius = MobBody.RoutingRadius;
        var cc = go.GetComponent<CharacterController>();
        cc.radius = 0.3f; cc.height = 1f; cc.center = new Vector3(0f, 0.5f, 0f);   // capsule STACKS UP from the feet pivot (bottom at y=0)
        GroundElevation.ApplyWalkRules(cc);   // the one step + slope every body walks by
        int crowd = WorldBuildUtil.EnsureLayer(UnitCrowd.LayerName);   // mobs pass through each other (no stacking/hopping)
        if (crowd >= 0) go.layer = crowd;

        var mh = go.AddComponent<Health>();
        // NOTE: no combat number is stamped here — UnitStatBlock below is the single writer of every one, deriving
        // them from level + primaries + gear (and again on every level-up). Only presentation and the element stay.
        mh.element      = arc.element;                        // armor PROPERTY — what the player's spells/melee hit

        var ai = go.AddComponent<MonsterAI3D>();
        // attack / hit / damageSpread: same story — derived, written by UnitStatBlock. Speed, reach and swing timing
        // are the shared character spec's, read live, so none of them is stamped either.
        ai.aggroRadius    = arc.aggroRadius;
        ai.leashRadius    = arc.leashRadius;
        if (arc.wanderRadius > 0f) ai.wanderRadius = arc.wanderRadius;
        ai.preferredRange = arc.preferredRange;             // >0 = caster/archer kites at range
        ai.fleeHpFraction = arc.fleeHpFraction;             // >0 = skittish: flees when wounded
        // The LIVE stat block + the worn set — the SINGLE writer of every derived combat number on this mob. It fills
        // Health.maxHealth/defense/magicDefense/flee/luk and MonsterAI3D's attack, hit, spread and crit numbers on Awake,
        // and again whenever something feeding them changes (the list is on UnitStatBlock), so a mob can never drift
        // onto a curve of its own.
        var equip = go.AddComponent<UnitEquipment>();
        if (arc.startingGear != null) equip.startingGear = arc.startingGear;
        // A MIRROR of the creature's catalog entry, which owns these three. MobSpawner re-reads them off the entry as
        // each creature is made, so a prefab baked before an edit corrects itself rather than serving a stale grade.
        equip.gearQuality = arc.gearQuality;
        equip.gearFolds = arc.gearFolds;
        equip.gearInkTier = arc.gearInkTier;
        var block = go.AddComponent<UnitStatBlock>();
        block.inputs = arc.stats.level > 0 ? arc.stats : UnitStatInputs.Default(1);
        block.ringRankStats  = arc.ringRankStats  ?? System.Array.Empty<StatId>();
        block.ringRankValues = arc.ringRankValues ?? System.Array.Empty<int>();

        ai.onHitAilment           = arc.onHitAilment;        // status inflicted on a landed hit (chance 0 = none)
        ai.onHitAilmentChance     = arc.onHitAilmentChance;
        ai.onHitAilmentSeconds    = arc.onHitAilmentSeconds;
        ai.onHitAilmentMagnitude  = arc.onHitAilmentMagnitude;
        // No NinjaAdventure VFX sheets: a creature holding a ranged weapon lands its blow instantly (null projectileSheet)
        // and the stun indicator stays off (null stunSheet) until a non-Ninja effect is wired in.

        if (!CreatureKinds.Is3D(arc.kind)) go.AddComponent<BillboardAnimator3D>().sheet = tex;
        if (arc.tint.a > 0f && arc.tint != Color.white) msr.color = arc.tint;   // tint AFTER the animator sets frames

        go.AddComponent<AttackLunge>();              // squash-stretch "chomp" on each bite
        var death = go.AddComponent<DeathDissolve>();   // remains linger → fade (no death sprite)
        death.deathSfx = SfxAssets.Set(0.7f, "Spells", "Spell Impact 1", "Spell Impact 2", "Spell Impact 3");  // poof (TomMusic has no creature cries)
        go.AddComponent<MobRespawn>();               // recycle the mob back into the world after it dies
        go.AddComponent<CameraDepthSorter>();        // camera-relative depth sorting (occlude / be occluded)

        // Loot: every creature carries its kind's materials, plus any items of its own, paid to whoever earned the kill.
        bool hasItems = arc.itemDropNames != null && arc.itemDropNames.Length > 0;
        {
            var dropper = go.AddComponent<LootDropper>();

            if (hasItems)
            {
                int count = arc.itemDropNames.Length;
                var table = new ItemDropEntry[count];
                for (int i = 0; i < count; i++)
                {
                    var item = Resources.Load<Item>($"Items/{arc.itemDropNames[i]}");
                    if (item == null)
                        Debug.LogWarning($"[unwritten] LootDropper: item '{arc.itemDropNames[i]}' not found in Resources/Items/");
                    table[i] = new ItemDropEntry
                    {
                        item   = item,
                        chance = (arc.itemDropChances != null && i < arc.itemDropChances.Length)
                                 ? arc.itemDropChances[i] : 0.1f,
                    };
                }
                dropper.lootTable = table;
            }
        }

        // The nameplate (MobNameplate — the display name) is auto-added at
        // runtime by MonsterAI3D.Awake, so it
        // works on mobs baked before it existed too — no re-bake needed.
        return go;
    }

    /// <summary>The Ninja Adventure monster sprite sheet used for billboard mobs (or null).</summary>
    public static Texture2D MonsterSheet()
    {
        string sheetPath = FindMonsterSheet();
        return sheetPath == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
    }

    /// <summary>The project path of the art folder a creature links to (<see cref="MonsterDef.artFolder"/>), or null
    /// when the link is empty or no longer points at a folder.</summary>
    public static string ArtFolderPath(string folderGuid)
    {
        if (string.IsNullOrEmpty(folderGuid)) return null;
        string path = AssetDatabase.GUIDToAssetPath(folderGuid);
        return AssetDatabase.IsValidFolder(path) ? path : null;
    }

    /// <summary>Load a specific creature's sheet from its art folder's path, or null if that folder has no usable
    /// sheet — callers fall back to <see cref="MonsterSheet()"/>.
    /// Handles both naming conventions in the pack: <c>SpriteSheet.png</c> and a single sheet named after the
    /// folder (e.g. <c>Slime/Slime.png</c>); else the first non-Faceset/non-Preview png.
    ///
    /// <para>Only a GRID sheet counts — a picture laid out in 16px cells, one column per direction. Boss art is one
    /// picture per animation instead and is resolved by <see cref="TryStripArt"/>; slicing one of those pictures as a
    /// grid is what showed every boss as a 16px corner of its Attack or Hit picture.</para></summary>
    public static Texture2D MonsterSheet(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return MonsterSheet();
        string sheetPath = ResolveMonsterSheetPath(dir, Path.GetFileName(dir));
        return sheetPath == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
    }

    static string ResolveMonsterSheetPath(string dir, string name)
    {
        string preferred = $"{dir}/SpriteSheet.png";
        if (File.Exists(preferred)) return preferred;
        if (!Directory.Exists(dir)) return null;

        foreach (var f in Directory.GetFiles(dir, "*.png"))
            if (Path.GetFileNameWithoutExtension(f).Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return f.Replace('\\', '/');
        return null;
    }

    /// <summary>True when <paramref name="dir"/> holds art a creature can be built from: a grid sheet, or an Idle
    /// strip. The importer reads this so it never makes a creature out of a folder nothing can draw (the split-part
    /// dragons: a head, body pieces and a wing, with no animation picture).</summary>
    public static bool HasCreatureArt(string dir)
        => ResolveMonsterSheetPath(dir, Path.GetFileName(dir)) != null || FindStrip(dir, "Idle") != null;

    /// <summary>Resolve a creature whose art is one picture per animation (Boss/&lt;Name&gt;/Idle.png, Walk.png…).
    /// Idle plays while standing; Walk plays while moving, or Jump for the creatures that move by jumping and have
    /// no Walk. Frame count and feet line come from the slicing Unity already saved on each picture.</summary>
    public static bool TryStripArt(string dir, out BillboardAnimator3D.StripClip idle,
                                   out BillboardAnimator3D.StripClip walk, out int pixelsPerUnit)
    {
        idle = default; walk = default; pixelsPerUnit = 0;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
        if (ResolveMonsterSheetPath(dir, Path.GetFileName(dir)) != null) return false;
        string idlePath = FindStrip(dir, "Idle");
        if (idlePath == null || !TryStripClip(idlePath, out idle)) return false;
        string walkPath = FindStrip(dir, "Walk") ?? FindStrip(dir, "Jump");
        if (walkPath == null || !TryStripClip(walkPath, out walk)) walk = default;
        pixelsPerUnit = idle.sheet.height;
        return true;
    }

    static string FindStrip(string dir, string animation)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (var f in Directory.GetFiles(dir, "*.png"))
            if (Path.GetFileNameWithoutExtension(f).StartsWith(animation, System.StringComparison.OrdinalIgnoreCase))
                return f.Replace('\\', '/');
        return null;
    }

    static bool TryStripClip(string path, out BillboardAnimator3D.StripClip clip)
    {
        clip = default;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) return false;
        var rects = new List<Rect>();
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a is Sprite sp) rects.Add(sp.rect);
        int frames = StripFrameCount(tex.width, rects);
        if (frames <= 0)
        {
            Debug.LogWarning($"[unwritten] {path}: couldn't work out its frames from the saved slicing ({rects.Count} pieces).");
            return false;
        }
        clip = new BillboardAnimator3D.StripClip { sheet = tex, frames = frames, footPx = StripFootPx(rects) };
        return true;
    }

    /// <summary>How many equal frames a single-row picture holds, from the pieces Unity sliced out of it: the LARGEST
    /// count that divides the width evenly, keeps every piece inside one frame, and leaves no frame empty. Loose
    /// flecks sliced as their own pieces sit inside a frame and don't add one. 0 = no count fits.</summary>
    public static int StripFrameCount(int sheetWidth, IReadOnlyList<Rect> pieces)
    {
        if (pieces == null || pieces.Count == 0 || sheetWidth <= 0) return 0;
        for (int n = sheetWidth; n >= 1; n--)
        {
            if (sheetWidth % n != 0) continue;
            int w = sheetWidth / n;
            var used = new bool[n];
            bool fits = true;
            foreach (var r in pieces)
            {
                int first = Mathf.FloorToInt(r.xMin / w), last = Mathf.FloorToInt((r.xMax - 1f) / w);
                if (first != last || first < 0 || first >= n) { fits = false; break; }
                used[first] = true;
            }
            if (!fits) continue;
            if (System.Array.TrueForAll(used, u => u)) return n;
        }
        return 0;
    }

    /// <summary>The feet line of a single-row picture, in pixels up from the bottom: the lowest piece's bottom edge.</summary>
    public static int StripFootPx(IReadOnlyList<Rect> pieces)
    {
        float min = float.MaxValue;
        foreach (var r in pieces) min = Mathf.Min(min, r.yMin);
        return min == float.MaxValue ? 0 : Mathf.FloorToInt(min);
    }

    static string FindMonsterSheet()
    {
        const string dir = "Assets/1_Authored/Art/Billboards/Actor/Monster";
        foreach (var n in new[] { "Plopling", "Kappi", "Cyclo", "Drizzle", "Cinder" })
        {
            string p = $"{dir}/{n}/SpriteSheet.png";
            if (File.Exists(p)) return p;
        }
        foreach (var g in AssetDatabase.FindAssets("SpriteSheet t:Texture2D", new[] { dir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (p.EndsWith("SpriteSheet.png")) return p;
        }
        return null;
    }


    /// <summary>Build the LIVE-3D rigged player standing on the ground at <paramref name="foot"/> (XZ;
    /// Y is overridden to the sprite-foot height). Reused by the town converter to drop the player
    /// on the map's spawn point.</summary>
    public static GameObject BuildPlayer(Vector3 foot)
    {
        var go = new GameObject("Player3D");
        go.transform.localScale = Vector3.one * PlayerScale;
        go.transform.position = foot;   // pivot = feet; the CharacterController rests here (capsule stacks up)
        var ctrl = go.AddComponent<Player3DController>();
        go.AddComponent<AudioListenerRig>();         // the "ears": listener on the player, oriented to the camera (3D sound)
        go.AddComponent<PlayerHurtFlash>();          // red flash when the Character takes damage
        var atk = go.AddComponent<Player3DAttacker>();   // slash/hit VFX default to Effect Library ids on the component
        atk.defaultAttackSfx = SfxAssets.Set(0.8f, SfxAssets.SwordDir, "Sword Attack 1", "Sword Attack 2", "Sword Attack 3");
        atk.defaultHitSfx    = SfxAssets.Set(0.9f, SfxAssets.SwordDir, "Sword Impact Hit 1", "Sword Impact Hit 2", "Sword Impact Hit 3");

        // Active skills are now DATA (composed SkillDef assets under Resources/Skills, interpreted by a
        // SkillRunner). Player3DController.Awake attaches one runner per authored skill, so Build Player no
        // longer bakes the retired hand-written Player3D* skill classes. Only the basic auto-attack
        // (Player3DAttacker, above) stays baked here.

        go.AddComponent<CameraDepthSorter>();        // camera-relative depth sorting (occlude / be occluded)
        // go.AddComponent<CharacterDye>();          // live HSV palette control — DISABLED for now (no color-palette dye on the character)

        var cc = go.GetComponent<CharacterController>();     // auto-added; size to the sprite (Phase 3D-B)
        // Tuned SHORT capsule (radius 0.15, height 0.5, centre offset to the feet) — its top clears
        // overhead prop geometry (roof eaves, tree leaves) while the bottom still bumps/climbs walls,
        // stairs and roots. Single source of truth so all player builders + runtime agree.
        Player3DController.ApplyDefaultCapsule(cc);
        int crowd = WorldBuildUtil.EnsureLayer(UnitCrowd.LayerName);   // player passes through mobs (can't hop onto them)
        if (crowd >= 0) go.layer = crowd;

        ctrl.footstepSfx = SfxAssets.Set(0.55f, "Footsteps/Dirt",
            "Dirt Walk 1", "Dirt Walk 2", "Dirt Walk 3", "Dirt Walk 4", "Dirt Walk 5");
        ctrl.waterFootstepSfx = SfxAssets.Set(0.55f, "Footsteps/Water",   // splash when walking through rain puddles
            "Water Walk 1", "Water Walk 2", "Water Walk 3", "Water Walk 4", "Water Walk 5");
        ctrl.waterJumpSfx = SfxAssets.Set(0.65f, "Footsteps/Water", "Water Jump");   // splash when a hop lands in a puddle

        // The project committed to LIVE-3D player characters (mobs stay billboards). A rigged Mixamo-Humanoid model
        // rides the controller via Character3DVisual — wired here so a fresh player IS the 3D avatar out of the box.
        // Swap models anytime via Tools ▸ Rigs ▸ Set 3D Player Model.
        ApplyDefault3DCharacter(go);
        // NOTE: the occlusion-silhouette marker goes on the VISUAL object (a separate object built by
        // Player3DVisualBuilder.Attach), NOT here on the player root — the root only holds the hidden sprite.
        return go;
    }

    /// <summary>Seat the committed live-3D player visual (rigged Humanoid model + shared clip controller) on the player.
    /// No-op with a warning if no rigged model exists yet — rig characters (tools/mixamo_batch_rig.py → Bulk Set
    /// Humanoid), then re-run Setup Systems Scene.</summary>
    static void ApplyDefault3DCharacter(GameObject playerGO)
    {
        var ctrl = playerGO.GetComponent<Player3DController>();
        Player3DVisualBuilder.Attach(ctrl, Player3DVisualBuilder.DefaultModel);
    }


    static int Fail(string msg)
    {
        EditorUtility.DisplayDialog("2.5D Spike", msg, "OK");
        Debug.LogError("[unwritten] 2.5D Spike: " + msg);
        return -1;
    }
}
