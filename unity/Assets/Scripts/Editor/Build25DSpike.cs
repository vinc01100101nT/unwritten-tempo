using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click 2.5D spike (RO-style look test). Builds <b>Assets/Scenes/Spike2D5.unity</b>: your
/// Ninja Adventure player as a camera-facing <see cref="Billboard"/> walking a 3D Kenney town,
/// viewed from a tilted camera — so you can judge the 2.5D style before committing to it.
///
/// It adds a 3D (Universal) Renderer ALONGSIDE the existing 2D one and overrides it only on the
/// spike camera, so NewTown / Systems (2D renderer, index 0) are untouched. Safe to re-run.
///
/// After building: open Spike2D5.unity and press Play (WASD to move).
///
/// NOT throwaway despite its name: the spike's <c>[MenuItem]</c> was removed at the 2.5D pivot, but this
/// class is now the shared rig-builder library — its public helpers (BuildPlayer/Camera/Light/Character/
/// HUD/Cursor/Monster, BillboardScale, FootOffsetY, EnsureUniversalRenderer, MonsterSheet) are called by
/// SystemsSceneBuilder, RegionBuilder, FantasyTownBuilder, NatureStageBuilder, NatureMobs, TrainingDummyTool,
/// ScaryPlayer and NinjaGreenPlayer. Keep it; deleting it breaks the whole editor build pipeline.
/// </summary>
public static class Build25DSpike
{
    const string ScenePath      = "Assets/Scenes/Spike2D5.unity";
    const string UrpPath        = "Assets/Settings/UniversalRP.asset";
    const string Renderer3DPath = "Assets/Settings/Spike3DRenderer.asset";
    const string KitFbxDir      = "Assets/Art/KenneyFantasyTownKit/FBX format";
    const string ColormapPath   = KitFbxDir + "/Textures/colormap.png";
    const string SmokeSheetPath = "Assets/Art/NinjaAdventure/FX/Smoke/Smoke/SpriteSheet.png";
    const int CellSize = 16, WalkFrames = 4;
    // Transparent padding below the feet inside each (center-pivoted) sprite cell, in world units.
    // We drop the billboard by this and raise the CharacterController center to match, so the art's
    // feet sit near the ground instead of floating. Kept slightly UNDER the true padding on purpose:
    // sprites now write depth (alpha-clip), so any pixels below the ground plane get clipped by it —
    // a small residual float is safe, dropping too far hides the feet. Tune: lower if feet clip,
    // raise if they float. (Sheets are isReadable:0, so we can't auto-measure the padding.)
    public const float SpriteFoot = 0.1f;

    // Size of the 2D billboards (player / mobs / NPCs) RELATIVE to the 3D map. 1 = one world cell tall;
    // 0.7 = 30% smaller than that. One shared knob so every billboard builder matches. ONLY affects the
    // 2.5D scenes built here — the 2D game (PlayerController2D + Player.prefab) is untouched.
    public const float BillboardScale = 0.7f;
    // Y the billboard's transform sits at so its feet rest on ground y=0, at the current scale.
    public static float FootOffsetY => BillboardScale * (0.5f - SpriteFoot);

    // Curated props (each guarded — skipped if not present in the kit).
    static readonly string[] Props = {
        "tree", "tree-high", "tree-crooked", "fountain-round", "cart", "cart-high",
        "lantern", "hedge-large", "pillar-stone", "banner-red", "pillar-wood", "hedge",
    };

    // Retired from the menu (Kenney-prop demo). The public Build* helpers below are the live 2.5D rig,
    // reused by NatureStageBuilder. This method is kept only as an internal reference for that rig.
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("2.5D Spike", "Exit Play mode first, then re-run.", "OK"); return; }

        int rendererIndex = EnsureUniversalRenderer();
        if (rendererIndex < 0) return;   // EnsureUniversalRenderer already explained why

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        BuildLight();
        BuildGround();
        int placed = ScatterProps();
        var player = BuildPlayer();              // returns the player GameObject (or null)
        BuildCamera(rendererIndex, player);      // 3D follow-camera tracks the player
        int monsters = BuildMonsters();          // billboard mobs that wander + chase (Phase 3D-D)
        BuildCharacter();                        // player HP/ATK/XP hub — turns on two-way combat (A)
        BuildHUD();                              // screen-space HP / Lv / XP bar (A)
        if (player != null) BuildCursor(player); // Dota hardware cursor (pointer / sword / crosshair)

        if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

        // If Spike2D5 is already open, close it first — Unity won't SaveScene onto a path another
        // open scene occupies ("Overwriting the same path as another open scene is not allowed").
        var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded && alreadyOpen != scene)
            EditorSceneManager.CloseScene(alreadyOpen, true);

        EditorSceneManager.SaveScene(scene, ScenePath);

        // The spike is standalone — Play must use the OPEN scene, not boot Systems.
        EditorSceneManager.playModeStartScene = null;

        // Re-open the freshly-built scene by itself so you can just press Play — no delete dance.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("2.5D Spike — built",
            $"Built {ScenePath}\n\n" +
            $"• {placed} Kenney props placed\n" +
            $"• Player billboard: {(player != null ? "yes" : "MISSING — see Console")}\n" +
            $"• Monsters: {monsters} (wander/chase; now bite back)\n" +
            "• Player HP + HUD bar (two-way combat)\n\n" +
            "NEXT: Spike2D5 is now open — just press Play.\n" +
            "Controls: LEFT-click ground = move, LEFT-click a mob = attack;  A then LEFT-click = attack-move;  S = stop.\n" +
            "Camera: scroll = zoom;  Q/E or RIGHT-drag = orbit (X/Y).\n" +
            "The hardware cursor changes: pointer / sword (over a mob) / crosshair (after A).\n\n" +
            "NOTE: I set Play-Mode Start Scene to None so the spike plays standalone. To go back " +
            "to the real game, open NewTown and re-run Tools ▸ unwritten ▸ Setup Systems Scene.",
            "OK");
        Debug.Log($"[unwritten] 2.5D Spike built ({placed} props, renderer index {rendererIndex}). Spike2D5 is open — press Play.");
    }

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
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.70f, 0.85f);
        cam.fieldOfView = 35f;
        cam.farClipPlane = 200f;
        go.transform.SetPositionAndRotation(new Vector3(0f, 10f, -8f), Quaternion.Euler(52f, 0f, 0f));
        go.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);   // 3D renderer, this camera only

        var follow = go.AddComponent<CameraFollow3D>();    // orbit (Q/E + right-drag) + zoom follow rig
        follow.distance = 9f;
        follow.height = 10f;
        follow.yaw = 0f;
        if (player != null) follow.target = player.transform;

        var fader = go.AddComponent<CameraOccluderFader>();   // fade props that block the view of the player
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

    static void BuildGround()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Ground";
        go.transform.localScale = new Vector3(4f, 1f, 4f);   // 40 x 40 units
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.36f, 0.55f, 0.27f) };
        m.SetFloat("_Smoothness", 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static Material _kitMat;
    static Material KitMaterial()
    {
        if (_kitMat != null) return _kitMat;
        _kitMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ColormapPath);
        if (tex != null) { _kitMat.SetTexture("_BaseMap", tex); _kitMat.mainTexture = tex; }
        else Debug.LogWarning("[unwritten] colormap not found at " + ColormapPath + " — props will be untextured.");
        _kitMat.SetFloat("_Smoothness", 0f);
        return _kitMat;
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

    static int ScatterProps()
    {
        var mat = KitMaterial();
        int placed = 0;
        float step = 360f / Props.Length;
        for (int i = 0; i < Props.Length; i++)
        {
            string path = FindFbx(Props[i]);
            if (path == null) continue;
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (fbx == null) continue;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            float a = (i * step) * Mathf.Deg2Rad;
            float r = 5f + (i % 3);
            inst.transform.position = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            inst.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = mr.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                mr.sharedMaterials = mats;
            }
            // Solid collision so the player can't walk through props (Phase 3D-B). Hollow props
            // (fountain/well) get a CONVEX hull so you can't stand inside the ring — keeps the
            // walkable footprint consistent between physical collision and the A* (3D-D fix).
            bool hollow = inst.name.ToLowerInvariant().Contains("fountain") || inst.name.ToLowerInvariant().Contains("well");
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    if (hollow) mc.convex = true;
                }
            placed++;
        }
        return placed;
    }

    static string FindFbx(string baseName)
    {
        string exact = $"{KitFbxDir}/{baseName}.fbx";
        if (File.Exists(exact)) return exact;
        foreach (var g in AssetDatabase.FindAssets(baseName + " t:GameObject", new[] { KitFbxDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (p.ToLowerInvariant().EndsWith(".fbx")) return p;
        }
        return null;
    }

    public static void BuildCursor(GameObject host)
    {
        const string dir = "Assets/Art/KenneyCursorPack/PNG/Outline/Default";
        const string dirBig = "Assets/Art/KenneyCursorPack/PNG/Outline/Double";   // 64×64 variants
        foreach (var n in new[] { "pointer_b", "tool_sword_a", "target_b" })
            AssetDatabase.ImportAsset($"{dir}/{n}.png", ImportAssetOptions.ForceUpdate);   // ensure readable Cursor textures
        AssetDatabase.ImportAsset($"{dirBig}/target_round_b.png", ImportAssetOptions.ForceUpdate);

        var cur = host.AddComponent<GameCursor3D>();
        cur.defaultCursor    = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/pointer_b.png");          // normal
        cur.attackCursor     = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/tool_sword_a.png");       // hovering a mob
        cur.attackMoveCursor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/target_b.png");           // A-armed
        cur.targetCursor     = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dirBig}/target_round_b.png");  // aiming a skill — large 64×64 reticle, drawn full-size (software cursor)
        cur.defaultHotspot   = new Vector2(3, 3);
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
        canvasGO.AddComponent<GraphicRaycaster>();

        // The GraphicRaycaster only reports "pointer over HUD" if an EventSystem drives it. The real
        // game gets one from the Systems scene; the standalone spike needs its own, or clicking the
        // HP bar would fall through to the world and move the player (Player3DController.PointerOverUI).
        if (Object.FindFirstObjectByType<EventSystem>() == null)
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
        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(bar.transform, false);
        var lRT = (RectTransform)labelGO.transform;
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one; lRT.offsetMin = lRT.offsetMax = Vector2.zero;
        var label = labelGO.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
        label.fontSize = 16; label.fontStyle = FontStyle.Bold;

        var hud = bar.AddComponent<HealthBarHUD>();
        hud.fill = fill; hud.label = label;
    }

    static int BuildMonsters() => BuildMonsters(Vector3.zero, 8f);

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
        arc.sheet = null;                   // use the caller's texture, not the archetype's sheet
        arc.tint  = Color.white;
        if (mystic)
        {
            arc.name       = "Mystic";
            arc.attackType = MonsterAI3D.AttackType.Magic;
            arc.tint       = new Color(0.62f, 0.72f, 1f);   // arcane blue marks a caster
        }
        return BuildMonster(tex, foot, arc);
    }

    /// <summary>Build a mob from a <see cref="MonsterArchetype"/>, resolving its sprite sheet by name
    /// (falls back to the default monster sheet if that art folder is missing).</summary>
    public static GameObject BuildMonster(in MonsterArchetype arc, Vector3 foot)
        => BuildMonster(MonsterSheet(arc.sheet) ?? MonsterSheet(), foot, arc);

    /// <summary>Core builder: stamp <paramref name="arc"/>'s stats / behavior / look onto the shared
    /// billboard-mob template using <paramref name="tex"/> as the sprite sheet.</summary>
    static GameObject BuildMonster(Texture2D tex, Vector3 foot, in MonsterArchetype arc)
    {
        float scale = arc.scale <= 0f ? 1f : arc.scale;
        var go = new GameObject("Monster_" + (string.IsNullOrEmpty(arc.name) ? tex.name : arc.name));
        go.transform.localScale = Vector3.one * BillboardScale * scale;
        go.transform.position = new Vector3(foot.x, foot.y + FootOffsetY * scale, foot.z);   // scale the foot offset too

        var msr = go.AddComponent<SpriteRenderer>();
        ApplyBillboardLighting(msr);   // flat art + receive/cast shadows
        go.AddComponent<Billboard>();

        var agent = go.AddComponent<PathAgent3D>();         // auto-adds CharacterController
        agent.moveSpeed = arc.moveSpeed; agent.agentRadius = 0.3f;
        var cc = go.GetComponent<CharacterController>();
        cc.radius = 0.3f; cc.height = 1f; cc.center = new Vector3(0f, SpriteFoot, 0f); cc.stepOffset = 0.3f; cc.slopeLimit = 45f;   // walk the low-poly hills
        int crowd = NatureStageBuilder.EnsureLayer(UnitCrowd.LayerName);   // mobs pass through each other (no stacking/hopping)
        if (crowd >= 0) go.layer = crowd;

        var mh = go.AddComponent<Health>();
        mh.maxHealth    = arc.maxHealth;
        mh.defense      = arc.defense;
        mh.magicDefense = arc.magicDefense;
        mh.flee         = arc.flee;                          // RO FLEE: dodges some of the player's swings (red MISS)
        mh.flashColor   = arc.flash.a > 0f ? arc.flash : Color.white;
        mh.element      = arc.element;                        // armor PROPERTY — what the player's spells/melee hit

        var ai = go.AddComponent<MonsterAI3D>();
        ai.attackType     = arc.attackType;
        ai.attack         = arc.attack;
        ai.hit            = arc.hit;
        ai.xpReward       = arc.xpReward;
        ai.damageSpread   = arc.damageSpread;
        ai.touchRange     = arc.touchRange;
        ai.touchInterval  = arc.touchInterval;
        ai.moveSpeed      = arc.moveSpeed;
        ai.aggroRadius    = arc.aggroRadius;
        ai.leashRadius    = arc.leashRadius;
        ai.stopDistance   = arc.stopDistance;
        if (arc.wanderRadius > 0f) ai.wanderRadius = arc.wanderRadius;
        ai.preferredRange = arc.preferredRange;             // >0 = caster/archer kites at range
        ai.fleeHpFraction = arc.fleeHpFraction;             // >0 = skittish: flees when wounded
        ai.attackElement          = arc.attackElement;       // outgoing PROPERTY (checked vs player's armor)
        ai.onHitAilment           = arc.onHitAilment;        // status inflicted on a landed hit (chance 0 = none)
        ai.onHitAilmentChance     = arc.onHitAilmentChance;
        ai.onHitAilmentSeconds    = arc.onHitAilmentSeconds;
        ai.onHitAilmentMagnitude  = arc.onHitAilmentMagnitude;
        // Magic mobs (e.g. the Flam caster) now lob a VISIBLE fireball — the hit lands on arrival.
        // Defaults (16px × 4 frames) match this sheet; null sheet = the old instant magic touch.
        if (ai.attackType == MonsterAI3D.AttackType.Magic)
            ai.projectileSheet = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Projectile/Fireball.png");
        // Stun swirl shown over the head when a status (e.g. Meteor's per-hit stun) procs (32px × 5 frames).
        ai.stunSheet = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Magic/Spirit/SpriteSheet.png");

        go.AddComponent<BillboardAnimator3D>().sheet = tex;
        if (arc.tint.a > 0f && arc.tint != Color.white) msr.color = arc.tint;   // tint AFTER the animator sets frames

        go.AddComponent<AttackLunge>();              // squash-stretch "chomp" on each bite
        var death = go.AddComponent<DeathDissolve>();   // smoke puff → remains linger → fade (no death sprite)
        death.smokeSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeSheetPath);
        death.deathSfx = SfxAssets.Set(0.7f, "Spells", "Spell Impact 1", "Spell Impact 2", "Spell Impact 3");  // poof (TomMusic has no creature cries)
        go.AddComponent<MobRespawn>();               // recycle the mob back into the world after it dies
        go.AddComponent<CameraDepthSorter>();        // camera-relative depth sorting (occlude / be occluded)

        // Loot: drop Zeny and/or items on death.
        bool hasGold  = arc.goldMax > 0;
        bool hasItems = arc.itemDropNames != null && arc.itemDropNames.Length > 0;
        if (hasGold || hasItems)
        {
            var dropper = go.AddComponent<LootDropper>();
            dropper.goldMin = arc.goldMin;
            dropper.goldMax = arc.goldMax;
            // GoldCoin.png is a Multiple-sprite sheet — LoadAllAssetsAtPath picks the first Sprite.
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/NinjaAdventure/Items/Treasure/GoldCoin.png"))
                if (obj is Sprite s) { dropper.coinSprite = s; break; }

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

        // The floating HP bar + name (MobNameplate) is auto-added at runtime by MonsterAI3D.Awake, so it
        // works on mobs baked before it existed too — no re-bake needed.
        return go;
    }

    /// <summary>The Ninja Adventure monster sprite sheet used for billboard mobs (or null).</summary>
    public static Texture2D MonsterSheet()
    {
        string sheetPath = FindMonsterSheet();
        return sheetPath == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
    }

    /// <summary>Load a specific monster sheet by its folder name under Actor/Monster (e.g. "Slime"),
    /// or null if that art folder doesn't exist — callers fall back to <see cref="MonsterSheet()"/>.</summary>
    public static Texture2D MonsterSheet(string name)
    {
        if (string.IsNullOrEmpty(name)) return MonsterSheet();
        string p = $"Assets/Art/NinjaAdventure/Actor/Monster/{name}/SpriteSheet.png";
        return File.Exists(p) ? AssetDatabase.LoadAssetAtPath<Texture2D>(p) : null;
    }

    static string FindMonsterSheet()
    {
        const string dir = "Assets/Art/NinjaAdventure/Actor/Monster";
        foreach (var n in new[] { "Slime", "KappaGreen", "Cyclope", "BlueBat", "Flam" })
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

    static GameObject BuildPlayer() => BuildPlayer(Vector3.zero);

    /// <summary>Build the billboard player standing on the ground at <paramref name="foot"/> (XZ;
    /// Y is overridden to the sprite-foot height). Reused by the town converter to drop the player
    /// on the map's spawn point.</summary>
    public static GameObject BuildPlayer(Vector3 foot)
    {
        string sheetPath = PlayerSheetPath();
        if (sheetPath == null) { Debug.LogWarning("[unwritten] Spike: no player sprite sheet found."); return null; }

        var frames = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().OrderBy(FrameIndex).ToArray();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
        if (frames.Length == 0 || tex == null)
        { Debug.LogWarning("[unwritten] Spike: player sheet not sliced (Sprite Mode = Multiple?): " + sheetPath); return null; }

        int cols = Mathf.Max(1, tex.width / CellSize);
        int walk = Mathf.Min(WalkFrames, frames.Length / cols);
        if (cols < 4 || walk < 1)
        { Debug.LogWarning("[unwritten] Spike: player sheet isn't a 4-column grid: " + sheetPath); return null; }

        var go = new GameObject("Player3D");
        go.transform.localScale = Vector3.one * BillboardScale;
        go.transform.position = new Vector3(foot.x, FootOffsetY, foot.z);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        ApplyBillboardLighting(sr);                  // flat art + receive/cast environment shadows
        go.AddComponent<Billboard>();
        var ctrl = go.AddComponent<Player3DController>();
        go.AddComponent<PlayerHurtFlash>();          // red flash when the Character takes damage
        var atk = go.AddComponent<Player3DAttacker>();
        atk.slashSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Art/NinjaAdventure/FX/Attack/SlashCurved/SpriteSheet.png");   // slash FX on each hit
        atk.defaultAttackSfx = SfxAssets.Set(0.8f, SfxAssets.SwordDir, "Sword Attack 1", "Sword Attack 2", "Sword Attack 3");
        atk.defaultHitSfx    = SfxAssets.Set(0.9f, SfxAssets.SwordDir, "Sword Impact Hit 1", "Sword Impact Hit 2", "Sword Impact Hit 3");

        // Basic magic bolt (Q) — the first thing that uses MAtk / MP / MDef (Phase G magic foundation).
        var caster = go.AddComponent<Player3DCaster>();
        var spark = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Magic/Spark/SpriteSheet.png");
        caster.boltSheet = spark;
        if (spark != null) { caster.boltFrameSize = spark.height; caster.boltFrames = Mathf.Max(1, spark.width / spark.height); }
        caster.castSfx = SfxAssets.Set(0.8f, "Spells", "Spell Impact 1", "Spell Impact 2", "Spell Impact 3");
        caster.icon = LoadSkillIcon("Spell/OrbLight");        // hotbar icon (generic magic bolt; distinct from Mage Fireball)

        // Swordsman signature active — Bash (slot 1): a heavy, always-landing strike that staggers the target.
        var bash = go.AddComponent<Player3DBash>();
        bash.bashSfx = SfxAssets.Set(0.95f, SfxAssets.SwordDir, "Sword Attack 1", "Sword Attack 2", "Sword Attack 3");
        bash.hitSfx  = SfxAssets.Set(1f,    SfxAssets.SwordDir, "Sword Impact Hit 1", "Sword Impact Hit 2", "Sword Impact Hit 3");
        bash.icon = LoadSkillIcon("Spell/Cut");               // hotbar icon (heavy slash)

        // Swordsman active 2 — Charge (slot 2): gap-closer dash to a target, then a staggering hit.
        var charge = go.AddComponent<Player3DCharge>();
        charge.chargeSfx = SfxAssets.Set(0.8f, SfxAssets.SwordDir, "Sword Attack 1", "Sword Attack 2", "Sword Attack 3");
        charge.hitSfx    = SfxAssets.Set(1f,   SfxAssets.SwordDir, "Sword Impact Hit 1", "Sword Impact Hit 2", "Sword Impact Hit 3");
        charge.icon = LoadSkillIcon("Spell/Counter");         // hotbar icon (forceful lunge)

        // Swordsman defensive active — Guard (slot 3): a brief damage-reduction stance + shield bubble.
        var guard = go.AddComponent<Player3DGuard>();
        guard.shieldSheet = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Magic/Shield/SpriteSheetBlue.png");
        guard.guardSfx = SfxAssets.Set(0.9f, SfxAssets.SwordDir, "Sword Blocked 1", "Sword Blocked 2", "Sword Blocked 3");
        guard.icon = LoadSkillIcon("Spell/DefenseUpgrade");   // hotbar icon (defensive buff)

        // Swordsman AoE active — Warcry (slot 4): damages + knocks back every nearby mob.
        var warcry = go.AddComponent<Player3DWarcry>();
        warcry.shockSheet = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Magic/Aura/SpriteSheet.png");
        warcry.warcrySfx = SfxAssets.Set(1f, "Spells", "Spell Impact 1", "Spell Impact 2", "Spell Impact 3");
        warcry.icon = LoadSkillIcon("Spell/Explosion");       // hotbar icon (AoE burst)

        // ---- Mage kit (class #2): enabled only when ClassState.Current == Mage (Player3DController.ApplyClass).
        var fireballTex  = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Projectile/Fireball.png");
        var explosionTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Elemental/Explosion/SpriteSheet.png");
        var iceTex       = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Elemental/Ice/SpriteSheet.png");
        var spellSfx     = SfxAssets.Set(0.85f, "Spells", "Spell Impact 1", "Spell Impact 2", "Spell Impact 3");

        var fireball = go.AddComponent<Player3DFireball>();   // 1: single-target projectile nuke
        fireball.projectileSheet = fireballTex;
        fireball.impactSheet = explosionTex;
        fireball.castSfx = spellSfx;
        fireball.icon = LoadSkillIcon("Spell/Fireball");

        var frost = go.AddComponent<Player3DFrostNova>();     // 2: PBAoE magic + slow
        frost.burstSheet = iceTex;
        frost.castSfx = spellSfx;
        frost.icon = LoadSkillIcon("Spell/BookIce");

        var meteor = go.AddComponent<Player3DMeteor>();       // 3: ground-targeted delayed AoE
        meteor.meteorSheet = fireballTex;
        meteor.impactSheet = explosionTex;
        meteor.castSfx = spellSfx;
        meteor.hitSfx = SfxAssets.Custom(0.8f, "Assets/Audio/Customs/hit.wav");   // custom per-hit sound, every hit of the multi-hit
        meteor.meteorSfx = SfxAssets.Set(0.9f, "Spells", "Fireball 1", "Fireball 2", "Fireball 3");   // randomized fireball boom per meteor impact
        meteor.icon = LoadSkillIcon("Spell/RockSpike");

        var soul = go.AddComponent<Player3DSoulStrike>();     // 4: aim-first SINGLE-target nuke (pick the enemy, then fire)
        var sparkTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NinjaAdventure/FX/Magic/Spark/SpriteSheet.png");
        soul.projectileSheet = sparkTex != null ? sparkTex : fireballTex;   // focused arcane bolt (falls back to fireball art)
        if (sparkTex != null) { soul.projFrameSize = sparkTex.height; soul.projFrames = Mathf.Max(1, sparkTex.width / sparkTex.height); }
        soul.impactSheet = explosionTex;
        soul.castSfx = spellSfx;
        soul.icon = LoadSkillIcon("Spell/OrbDarkness");      // hotbar icon (focused single-target bolt; distinct from caster's OrbLight)

        go.AddComponent<CameraDepthSorter>();        // camera-relative depth sorting (occlude / be occluded)
        // go.AddComponent<CharacterDye>();          // live HSV palette control — DISABLED for now (no color-palette dye on the character)

        var cc = go.GetComponent<CharacterController>();     // auto-added; size to the sprite (Phase 3D-B)
        // Tuned SHORT capsule (radius 0.15, height 0.5, centre offset to the feet) — its top clears
        // overhead prop geometry (roof eaves, tree leaves) while the bottom still bumps/climbs walls,
        // stairs and roots. Single source of truth so all player builders + runtime agree.
        Player3DController.ApplyDefaultCapsule(cc);
        int crowd = NatureStageBuilder.EnsureLayer(UnitCrowd.LayerName);   // player passes through mobs (can't hop onto them)
        if (crowd >= 0) go.layer = crowd;

        Sprite[] Col(int c) => Enumerable.Range(0, walk).Select(r => frames[c + r * cols]).ToArray();
        ctrl.down = Col(0); ctrl.up = Col(1); ctrl.left = Col(2); ctrl.right = Col(3);
        ctrl.attackSheet = LoadAttackSheet(sheetPath);   // SeparateAnim/Attack.png — sliced at runtime per facing
        ctrl.deadSheet = LoadDeadSheet(sheetPath);       // SeparateAnim/Dead.png — defeated pose for Phase F death
        ctrl.footstepSfx = SfxAssets.Set(0.55f, "Footsteps/Dirt",
            "Dirt Walk 1", "Dirt Walk 2", "Dirt Walk 3", "Dirt Walk 4", "Dirt Walk 5");
        ctrl.waterFootstepSfx = SfxAssets.Set(0.55f, "Footsteps/Water",   // splash when walking through rain puddles
            "Water Walk 1", "Water Walk 2", "Water Walk 3", "Water Walk 4", "Water Walk 5");
        ctrl.waterJumpSfx = SfxAssets.Set(0.65f, "Footsteps/Water", "Water Jump");   // splash when a hop lands in a puddle

        // Make the player the Sscary Male avatar (48×64 idle/walk/jump/dash/death + Shadow/Dust). Full
        // replacement of the older looks wired above; falls back to the animated NinjaGreen avatar (then the
        // legacy single-pose Boy sheet) if the Sscary art is absent, so older art setups still work.
        if (!ScaryPlayer.TryApply(go))
            NinjaGreenPlayer.TryApply(go);
        return go;
    }

    /// <summary>The SeparateAnim/Attack.png texture beside the player sheet (sliced into 4 directional
    /// poses at runtime by Player3DController), or null if there's no attack strip.</summary>
    static Texture2D LoadAttackSheet(string sheetPath)
    {
        string folder = Path.GetDirectoryName(sheetPath).Replace('\\', '/');
        string atkPath = folder + "/SeparateAnim/Attack.png";
        return File.Exists(atkPath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(atkPath) : null;
    }

    /// <summary>Loads a hotbar icon from the Ninja Adventure "Skill Icon" pack (e.g. "Spell/Cut"),
    /// ensuring it's imported as a Sprite first. Returns null (→ the hotbar shows the text label) if the
    /// file is missing. Assigned to the skill components so the icon serializes / is swappable in the
    /// Inspector.</summary>
    static Sprite LoadSkillIcon(string relativeNoExt)
    {
        string path = $"Assets/Art/NinjaAdventure/UI/Skill Icon/{relativeNoExt}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { Debug.LogWarning($"[unwritten] Hotbar icon not found: {path}"); return null; }
        // The pack ships these icons as Sprite/Multiple (one 24×24 sub-sprite each), which
        // LoadAssetAtPath<Sprite> can't return directly — force Single so the whole image is one
        // addressable sprite. Nothing references the sub-sprites yet, so this is non-destructive.
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[unwritten] Hotbar icon failed to load as Sprite: {path}");
        return sprite;
    }

    /// <summary>The SeparateAnim/Dead.png texture beside the player sheet (Ninja Adventure ships one
    /// 16×16 defeated-pose frame), sliced at runtime by Player3DController, or null.</summary>
    static Texture2D LoadDeadSheet(string sheetPath)
    {
        string folder = Path.GetDirectoryName(sheetPath).Replace('\\', '/');
        string deadPath = folder + "/SeparateAnim/Dead.png";
        return File.Exists(deadPath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(deadPath) : null;
    }

    static string PlayerSheetPath()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        var sr = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>() : null;
        if (sr != null && sr.sprite != null)
        {
            string p = AssetDatabase.GetAssetPath(sr.sprite.texture);
            if (!string.IsNullOrEmpty(p)) return p;
        }
        const string fallback = "Assets/Art/NinjaAdventure/Actor/Character/Boy/SpriteSheet.png";
        return File.Exists(fallback) ? fallback : null;
    }

    static int FrameIndex(Sprite s)
    {
        int u = s.name.LastIndexOf('_');
        return (u >= 0 && int.TryParse(s.name.Substring(u + 1), out int n)) ? n : 0;
    }

    static int Fail(string msg)
    {
        EditorUtility.DisplayDialog("2.5D Spike", msg, "OK");
        Debug.LogError("[unwritten] 2.5D Spike: " + msg);
        return -1;
    }
}
