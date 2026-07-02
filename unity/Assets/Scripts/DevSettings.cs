using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// In-game DEV SETTINGS panel — one window to live-tune every gameplay knob without leaving Play or
/// hunting through inspectors. Toggle with <b>P</b>. Knobs are grouped by category (Player, Player
/// Combat, Player Stats, Mobs, Environment, Camera) and each slider/toggle is bound straight to the
/// live component, so dragging it changes the game *this frame* — speed, aggro range, day length,
/// camera framing, etc.
///
/// Like <see cref="PerfHud"/>, it self-boots with zero scene wiring and is compiled into the game in
/// all configs, but its <see cref="Bootstrap"/> only runs in the editor / development builds — a
/// release player never instantiates it, so it carries zero runtime cost (the static
/// <see cref="Blocking"/> just stays <c>false</c>).
///
/// IMGUI windows don't participate in the UI EventSystem, so a click on a slider would otherwise *also*
/// reach the world (ordering a move / orbiting the camera). <see cref="Blocking"/> is true while the
/// cursor is over the window; <see cref="Player3DController"/> and <see cref="CameraFollow3D"/> check it
/// and skip their input that frame.
/// </summary>
public class DevSettings : MonoBehaviour
{
    /// <summary>True while the cursor is over the open panel — gameplay input scripts skip their input
    /// this frame so panel clicks don't leak into the world. Always false in a release build.</summary>
    public static bool Blocking { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("DevSettings");
        DontDestroyOnLoad(go);   // dev tool only — intentionally exempt from the no-DontDestroyOnLoad rule
        go.AddComponent<DevSettings>();
    }
#endif

    // ---- binding model --------------------------------------------------------
    // A single knob: a labelled slider (float/int) or a toggle (bool), bound to the live component via
    // get/set closures. Defaults are captured the first time the knob draws (the panel is opened in
    // Play, so the targets exist by then) to power "Reset all".
    class Knob
    {
        public string label;
        public float min, max;
        public bool isInt, isBool, isAct;
        public Func<float> get; public Action<float> set;
        public Func<bool> getB; public Action<bool> setB;
        public Action act; public Func<string> dyn;   // isAct: a button (optionally with a live label)
        public bool isRaw; public Action draw;         // isRaw: escape hatch for custom IMGUI (DrawSwapSlots)
        public bool hasDefault; public float def; public bool defB;
    }

    // A category. `isSub` groups are sub-categories: they render indented under the most recent top-level (non-sub)
    // category and are hidden while that parent is collapsed — one level of nesting, e.g. Environment ▸ Snow.
    class Group { public string name; public bool open = false; public bool isSub; public readonly List<Knob> knobs = new List<Knob>(); }

    readonly List<Group> groups = new List<Group>();
    Group cur;   // group currently being filled by the builder helpers

    // ---- lazily-resolved targets (re-found after map swaps destroy/recreate them) ----
    Player3DController player;
    BakedBillboard8 bakedPlayer;   // the baked 8-dir visual riding the player (if "Use Baked Character" is active)
    BakedPlayerVisual bakedVisual; // the driver on top of bakedPlayer (owns the fall-pose phase)
    Transform groundRoot;          // the ground root moved by the "Ground height" knob (found via a Ground-layer ray)
    int groundLayerMask = -1;      // cached Ground layer mask (-1 = not resolved yet)
    Player3DAttacker attacker;
    CameraFollow3D camRig;
    CameraOccluderFader fader;
    TimeOfDay tod;
    GameCalendar cal;
    WeatherDirector wx;
    WeatherChannels wc;
    DayNightLight sun;
    CloudShadows cloud;
    SkyboxSky sky;
    WindGlobals wind;
    RainOverlay rain;
    WetGround wet;
    PlanarReflection refl;
    RainAudio rainAudio;
    SnowOverlay snow;
    SnowGround snowGround;
    FogOverlay fog;
    HandDrawnFX ink;
    FootstepTracker foot;
    CharacterDye dye;
    MobRespawn[] respawns;
    float nextRespawnRefresh;

    bool open;
    Vector2 scroll;
    Rect windowRect;
    bool built, rectInit;
    const int WinId = 920411;

    GUIStyle headerStyle, labelStyle, valueStyle, swatchStyle;

    void EnsureRefs()
    {
        if (player == null)   player   = FindFirstObjectByType<Player3DController>();
        if (bakedVisual == null) bakedVisual = FindFirstObjectByType<BakedPlayerVisual>();
        if (bakedPlayer == null && bakedVisual != null) bakedPlayer = bakedVisual.GetComponent<BakedBillboard8>();
        if (attacker == null) attacker = FindFirstObjectByType<Player3DAttacker>();
        if (camRig == null)   camRig   = FindFirstObjectByType<CameraFollow3D>();
        if (fader == null)    fader    = FindFirstObjectByType<CameraOccluderFader>();
        if (tod == null)      tod      = TimeOfDay.Instance;
        if (cal == null)      cal      = GameCalendar.Instance;
        if (wx == null)       wx       = WeatherDirector.Instance;
        if (wc == null)       wc       = WeatherChannels.Instance;
        if (sun == null)      sun      = FindFirstObjectByType<DayNightLight>();
        if (cloud == null)    cloud    = FindFirstObjectByType<CloudShadows>();
        if (sky == null)      sky      = FindFirstObjectByType<SkyboxSky>();
        if (wind == null)     wind     = FindFirstObjectByType<WindGlobals>();
        if (rain == null)     rain     = FindFirstObjectByType<RainOverlay>();
        if (wet == null)      wet      = FindFirstObjectByType<WetGround>();
        if (refl == null)     refl     = FindFirstObjectByType<PlanarReflection>();
        if (rainAudio == null) rainAudio = FindFirstObjectByType<RainAudio>();
        if (snow == null)     snow     = FindFirstObjectByType<SnowOverlay>();
        if (snowGround == null) snowGround = FindFirstObjectByType<SnowGround>();
        if (fog == null)      fog      = FindFirstObjectByType<FogOverlay>();
        if (ink == null)      ink      = FindFirstObjectByType<HandDrawnFX>();
        if (foot == null)     foot     = FindFirstObjectByType<FootstepTracker>();
        if (dye == null)      dye      = FindFirstObjectByType<CharacterDye>();
        EnsureGround();
    }

    // Resolve the ground root by raycasting straight down on the Ground layer under the player. The whole floor
    // (Ground_Visual + Ground_Floor) lives under one root, so moving that root's Y lifts/sinks the entire ground.
    void EnsureGround()
    {
        if (groundRoot != null) return;
        if (groundLayerMask == -1) { int l = LayerMask.NameToLayer("Ground"); groundLayerMask = l >= 0 ? (1 << l) : 0; }
        if (groundLayerMask == 0) return;
        Vector3 from = (player != null ? player.transform.position : Vector3.zero) + Vector3.up * 100f;
        if (Physics.Raycast(from, Vector3.down, out var hit, 500f, groundLayerMask, QueryTriggerInteraction.Ignore))
            groundRoot = hit.collider.transform.root;
    }

    // ---- builder helpers ------------------------------------------------------
    void Cat(string name) { cur = new Group { name = name }; groups.Add(cur); }
    // A sub-category under the current top-level Cat (renders indented + collapses with its parent).
    void Sub(string name) { cur = new Group { name = name, isSub = true }; groups.Add(cur); }
    void F(string label, float min, float max, Func<float> get, Action<float> set)
        => cur.knobs.Add(new Knob { label = label, min = min, max = max, get = get, set = set });
    void I(string label, float min, float max, Func<float> get, Action<float> set)
        => cur.knobs.Add(new Knob { label = label, min = min, max = max, isInt = true, get = get, set = set });
    void B(string label, Func<bool> get, Action<bool> set)
        => cur.knobs.Add(new Knob { label = label, isBool = true, getB = get, setB = set });
    // Sun knobs must nudge DayNightLight after writing: its Update early-outs until the clock moves, so a
    // colour/intensity slider otherwise wouldn't show until you also scrubbed Time of day.
    void FSun(string label, float min, float max, Func<float> get, Action<float> set)
        => F(label, min, max, get, v => { set(v); if (sun) sun.Refresh(); });
    void BSun(string label, Func<bool> get, Action<bool> set)
        => B(label, get, v => { set(v); if (sun) sun.Refresh(); });
    void Act(string label, Action act)
        => cur.knobs.Add(new Knob { label = label, isAct = true, act = act });
    void Act(Func<string> dyn, Action act)
        => cur.knobs.Add(new Knob { isAct = true, dyn = dyn, act = act });
    void Raw(Action draw)
        => cur.knobs.Add(new Knob { isRaw = true, draw = draw });

    // ---- mob fan-out: read the first live mob, write to ALL of them ----
    static void MobForEach(Action<MonsterAI3D> act)
    {
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++) if (list[i] != null) act(list[i]);
    }
    static float MobRead(Func<MonsterAI3D, float> read)
    {
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++) if (list[i] != null) return read(list[i]);
        return 0f;
    }

    MobRespawn[] Respawns()
    {
        if (respawns == null || respawns.Length == 0 || Time.unscaledTime >= nextRespawnRefresh)
        {
            respawns = FindObjectsByType<MobRespawn>(FindObjectsSortMode.None);
            nextRespawnRefresh = Time.unscaledTime + 1f;
        }
        return respawns;
    }
    float RespawnRead(Func<MobRespawn, float> read)
    {
        var r = Respawns();
        for (int i = 0; i < r.Length; i++) if (r[i] != null) return read(r[i]);
        return 0f;
    }
    void RespawnForEach(Action<MobRespawn> act)
    {
        var r = Respawns();
        for (int i = 0; i < r.Length; i++) if (r[i] != null) act(r[i]);
    }

    static Character Ch => Character.Instance;

    // ---- Combat Test helpers --------------------------------------------------
    MonsterAI3D NearestMob()
    {
        if (player == null) return null;
        Vector3 p = player.transform.position;
        MonsterAI3D best = null; float bestSqr = float.MaxValue;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null || m.Health == null || m.Health.IsDead) continue;
            float sqr = (m.transform.position - p).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = m; }
        }
        return best;
    }

    static Element NextElement(Element e) => (Element)(((int)e + 1) % Elements.Count);

    string MobElementLabel()
    {
        var m = NearestMob();
        string e = (m != null && m.Health != null) ? m.Health.element.ToString() : "(none)";
        return "Mob armor: " + e + "   ▶ cycle";
    }

    void CycleMobElement()
    {
        var m = NearestMob();
        if (m == null || m.Health == null) return;
        m.Health.element = NextElement(m.Health.element);
        DamagePopup.SpawnText(m.transform.position + Vector3.up * 1.1f, "Armor: " + m.Health.element, Elements.Tint(m.Health.element));
    }

    void CyclePlayerElement()
    {
        if (Ch == null) return;
        Ch.armorElement = NextElement(Ch.armorElement);
        if (player != null)
            DamagePopup.SpawnText(player.transform.position + Vector3.up * 1.1f, "Armor: " + Ch.armorElement, Elements.Tint(Ch.armorElement));
    }

    void CycleWeaponElement()
    {
        if (attacker == null) return;
        attacker.meleeElement = NextElement(attacker.meleeElement);
        if (player != null)
            DamagePopup.SpawnText(player.transform.position + Vector3.up * 1.1f, "Weapon: " + attacker.meleeElement, Elements.Tint(attacker.meleeElement));
    }

    void Afflict(Ailment a, float secs, float mag)
    {
        var m = NearestMob();
        if (m != null) Status.Apply(m.gameObject, a, secs, mag);
    }

    void AfflictMe(Ailment a, float secs, float mag)
    {
        if (player != null) Status.Apply(player.gameObject, a, secs, mag);
    }

    // ---- Calendar & Weather helpers -------------------------------------------
    // The weather-channels hub (source of truth for rain/cloud/snow/puddle), created on demand so the knobs work
    // even on a Systems scene baked before it joined the rig.
    WeatherChannels WC() => wc != null ? wc : (wc = WeatherChannels.Ensure());
    // A DevSettings master slider snaps its channel for instant feedback (manual tuning wants no lag).
    float ChGet(WeatherChannel c) => wc != null ? wc.Shown(c) : 0f;
    void  ChSnap(WeatherChannel c, float v) => WC().Snap(c, v);

    // Forcing a preset implies manual control, so flip autoFromCalendar off and step to the next weather.
    void CycleForcedWeather()
    {
        if (wx == null) return;
        wx.autoFromCalendar = false;
        int count = (int)WeatherDirector.Preset.Fog + 1;   // Fog is the last enum member
        wx.forcedPreset = (WeatherDirector.Preset)(((int)wx.forcedPreset + 1) % count);
    }

    void BuildKnobs()
    {
        // ---- Player (movement / feel) — Player3DController -----------------
        Cat("Player");
        F("Travel speed",     0f, 12f, () => player ? player.moveSpeed : 0f,       v => { if (player) player.moveSpeed = v; });   // base translation, no anim scaling
        F("Gait speed (×)",   0.1f, 5f, () => player ? player.gaitSpeed : 0f,      v => { if (player) player.gaitSpeed = v; });   // scales move + animation together
        F("Anim FPS",         1f, 20f, () => player ? player.framesPerSecond : 0f, v => { if (player) player.framesPerSecond = v; });
        F("Acquire range",    0f, 15f, () => player ? player.acquireRange : 0f,    v => { if (player) player.acquireRange = v; });
        F("Roam range",       0f, 30f, () => player ? player.roamRange : 0f,       v => { if (player) player.roamRange = v; });
        F("Snap radius (px)", 0f, 200f, () => player ? player.snapRadiusPixels : 0f, v => { if (player) { player.snapRadiusPixels = v; player.ApplySnapRadius(); } });
        F("Agent radius",     0.1f, 1f, () => player ? player.agentRadius : 0f,    v => { if (player) player.agentRadius = v; });
        F("Gravity",          0f, 30f, () => player ? player.gravity : 0f,         v => { if (player) player.gravity = v; });
        F("Fall pose @ speed", 0f, 10f, () => player ? player.fallAnimSpeed : 0f,  v => { if (player) player.fallAnimSpeed = v; });   // min downward speed before the falling pose plays
        F("Run step gap (s)",  0.1f, 1.5f, () => player ? player.stepInterval : 0f,     v => { if (player) player.stepInterval = v; });
        F("Walk step gap (s)", 0.1f, 1.5f, () => player ? player.walkStepInterval : 0f, v => { if (player) player.walkStepInterval = v; });
        B("Idle breathing",   () => player && player.breathe,                      v => { if (player) player.breathe = v; });
        F("Breathe amount",   0f, 0.2f, () => player ? player.breatheAmount : 0f,  v => { if (player) player.breatheAmount = v; });
        F("Breathe speed",    0f, 6f,  () => player ? player.breatheSpeed : 0f,    v => { if (player) player.breatheSpeed = v; });
        // Traversal abilities (SPACE = forward hop, E = dash burst).
        F("Jump distance",    0f, 6f,    () => player ? player.jumpDistance : 0f,  v => { if (player) player.jumpDistance = v; });
        F("Jump height",      0f, 2.5f,  () => player ? player.jumpHeight : 0f,    v => { if (player) player.jumpHeight = v; });
        F("Roll speed (×run)", 0f, 5f,   () => player ? player.eDashSpeedMult : 0f, v => { if (player) player.eDashSpeedMult = v; });
        F("Dash duration",    0.05f, 0.6f, () => player ? player.eDashDuration : 0f, v => { if (player) player.eDashDuration = v; });
        F("Dash cooldown",    0f, 3f,    () => player ? player.eDashCooldown : 0f, v => { if (player) player.eDashCooldown = v; });

        // ---- Baked Character (the 8-dir billboard riding the player via "Use Baked Character") ----
        // Live-seat the feet on the ground: higher = lift the sprite up, lower = sink it. ~0.42 grounds a
        // standard bake; once you like a value, write it into the prefab's BakedBillboard8.feetOffset (or set
        // the matching "Feet nudge" in the batch baker) so it sticks. Only does anything while a baked char is active.
        Cat("Baked Character");
        F("Feet offset", -0.5f, 1f, () => bakedPlayer ? bakedPlayer.FeetOffset : 0f,
            v => { if (bakedPlayer) bakedPlayer.SetFeetOffset(v); });
        // Where in the Jump clip the looped FALLING frames start (0 = whole clip, 0.5 = 2nd/descent half,
        // ~0.9 ≈ hold the last frame). Reuses Jump because there's no dedicated Fall bake.
        F("Fall pose phase", 0f, 1f, () => bakedVisual ? bakedVisual.fallPosePhase : 0f,
            v => { if (bakedVisual) bakedVisual.fallPosePhase = v; });

        // ---- Shadows — light-facing billboard cast shadows (player + mobs) -------
        // The proxy turns to fully face the sun so the silhouette shadow survives any light angle (side-on AND
        // overhead); far units are culled (no proxy) past the distance to keep crowded scenes cheap.
        Cat("Shadows");
        B("Billboard shadows", () => BillboardShadow.Enabled,     v => BillboardShadow.Enabled = v);
        F("Shadow cull dist",  5f, 80f, () => BillboardShadow.MaxDistance, v => BillboardShadow.MaxDistance = v);
        // Slide the cast shadow along its length to line its root up with the feet (+ away, − under).
        F("Shadow contact",   -2f, 2f,  () => BillboardShadow.ContactOffset, v => BillboardShadow.ContactOffset = v);
        // Slide the cast shadow left/right (perpendicular) to centre it under the feet.
        F("Shadow side",      -2f, 2f,  () => BillboardShadow.SideOffset,    v => BillboardShadow.SideOffset = v);
        // Only the N lamps nearest the player cast (one point-light cubemap each), so cost = casters.
        B("Lamp shadows",       () => NightLight.GlobalShadows,        v => NightLight.GlobalShadows = v);
        F("Lamp shadow casters", 1f, 8f, () => NightLight.MaxShadowCasters, v => NightLight.MaxShadowCasters = Mathf.RoundToInt(v));
        F("Lamp shadow range",  4f, 30f, () => NightLight.MaxShadowDist,       v => NightLight.MaxShadowDist = v);
        F("Lamp shadow dark",   0f, 1f,  () => NightLight.GlobalShadowStrength, v => NightLight.GlobalShadowStrength = v);
        // Push the sprite's shadow-receive sample toward the sun so it clears its own cast-shadow proxy
        // (the dark band that climbed the body). Higher = cleaner self-shade, coarser tree shade.
        F("Self-shadow bias",   0f, 3f,  () => SpriteAddLight.SelfShadowBias, v => { SpriteAddLight.SelfShadowBias = v; SpriteAddLight.Apply(); });

        // ---- Sprite Light — how billboards (player/mobs) pick up real-time lamps/torches ----
        // Global shader knobs (SpriteAddLight) — tune once, every billboard updates live.
        Cat("Sprite Light");
        F("Lamp pickup strength", 0f, 4f, () => SpriteAddLight.Strength,   v => { SpriteAddLight.Strength = v;   SpriteAddLight.Apply(); });
        F("Directional split",    0f, 1f, () => SpriteAddLight.Shape,      v => { SpriteAddLight.Shape = v;      SpriteAddLight.Apply(); });
        F("Bulge (round<->flat)", 0.05f, 2f, () => SpriteAddLight.Bulge,   v => { SpriteAddLight.Bulge = v;      SpriteAddLight.Apply(); });
        F("Albedo tint",          0f, 1f, () => SpriteAddLight.AlbedoTint, v => { SpriteAddLight.AlbedoTint = v; SpriteAddLight.Apply(); });

        // ---- World — move the whole ground floor (Ground_Visual + Ground_Floor) up/down ----
        // Lifts/sinks the entire ground plot. Props/units are seated separately so they DON'T follow — this is a
        // test/alignment dial (e.g. seat the ground exactly on Y0). SyncTransforms so ground rays update same-frame.
        Cat("World");
        F("Ground height (Y)", -1f, 1f, () => groundRoot ? groundRoot.position.y : 0f,
            v => { if (groundRoot) { var p = groundRoot.position; p.y = v; groundRoot.position = p; Physics.SyncTransforms(); } });

        // ---- Units (global size dial; each unit's MOVE SPEED scales with its size) ----
        Cat("Units");
        F("Unit size (global)", 0.3f, 2.5f, () => UnitScale.Global,      v => UnitScale.SetGlobal(v));
        F("Mob size (rel)",     0.3f, 2f,   () => UnitScale.MobRelative, v => UnitScale.SetMobRelative(v));

        // ---- Appearance — CharacterDye HSV knobs ---------------------------
        Cat("Appearance");
        F("Hue shift",   0f, 1f,  () => dye ? dye.HueShift   : 0f, v => { if (dye) dye.HueShift   = v; });
        F("Saturation",  0f, 2f,  () => dye ? dye.Saturation  : 1f, v => { if (dye) dye.Saturation  = v; });
        F("Brightness",  0f, 2f,  () => dye ? dye.Brightness  : 1f, v => { if (dye) dye.Brightness  = v; });
        Act("Reset dye", () => { if (dye) { dye.HueShift = 0f; dye.Saturation = 1f; dye.Brightness = 1f; } });
        Raw(DrawSwapSlots);

        // ---- Hand-Drawn — runtime "art filter" (ink linework + paper + lowered saturation), no re-bake ----
        // Master turns the INK on; Saturation is independent and CLAMPED so colour can mute but never go greyscale.
        // Quad/post are off until raised, so this costs nothing when the look is off.
        Cat("Hand-Drawn");
        F("Ink amount",     0f, 1f,    () => ink ? ink.intensity : 0f,     v => { ink = HandDrawnFX.Ensure(); ink.intensity = v; });
        F("Line strength",  0f, 1f,    () => ink ? ink.lineStrength : 0f,  v => { if (ink) ink.lineStrength = v; });
        F("Outlines",       0f, 80f,   () => ink ? ink.silhouette : 0f,    v => { if (ink) ink.silhouette = v; });
        F("Crease lines",   0f, 60f,   () => ink ? ink.crease : 0f,        v => { if (ink) ink.crease = v; });
        F("Line thickness", 0.4f, 3f,  () => ink ? ink.thickness : 0f,     v => { if (ink) ink.thickness = v; });
        F("Edge threshold", 0f, 0.5f,  () => ink ? ink.edgeThreshold : 0f, v => { if (ink) ink.edgeThreshold = v; });
        F("Paper grain",    0f, 1f,    () => ink ? ink.paperGrain : 0f,    v => { if (ink) ink.paperGrain = v; });
        // Saturation/contrast are applied by the HandDrawnGradeFeature renderer feature (run Tools ▸ unwritten ▸
        // Setup Hand-Drawn Grade once). Min is HandDrawnFX.MinSaturation so it never reaches greyscale.
        F("Saturation",     HandDrawnFX.MinSaturation, 1f, () => ink ? ink.saturation : 1f, v => { ink = HandDrawnFX.Ensure(); ink.saturation = v; });
        F("Contrast",      -0.3f, 0.3f, () => ink ? ink.contrast : 0f,     v => { if (ink) ink.contrast = v; });

        // ---- Player Combat — Player3DAttacker ------------------------------
        Cat("Player Combat");
        F("Attack range",     0.5f, 4f, () => attacker ? attacker.range : 0f,      v => { if (attacker) attacker.range = v; });
        F("Cooldown (s)",     0.1f, 2f, () => attacker ? attacker.cooldown : 0f,   v => { if (attacker) attacker.cooldown = v; });
        F("Windup (s)",       0f, 0.6f, () => attacker ? attacker.windupTime : 0f, v => { if (attacker) attacker.windupTime = v; });
        I("Base damage*",     1f, 50f, () => attacker ? attacker.damage : 0f,      v => { if (attacker) attacker.damage = Mathf.RoundToInt(v); });
        F("Slash VFX scale",  0.2f, 3f, () => attacker ? attacker.slashVfxScale : 0f, v => { if (attacker) attacker.slashVfxScale = v; });

        // ---- Player Stats — Character (effective stats recompute live) -----
        Cat("Player Stats");
        I("Level",            1f, 200f, () => Ch ? Ch.Level : 1f,           v => { if (Ch) Ch.SetLevel(Mathf.RoundToInt(v)); });
        I("Ring level",       1f, 200f, () => Ch ? Ch.Ring.Level : 1f,      v => { if (Ch) Ch.Ring.SetLevel(Mathf.RoundToInt(v)); });
        I("Base max HP",      1f, 500f, () => Ch ? Ch.baseMaxHP : 0f,        v => { if (Ch) Ch.baseMaxHP = Mathf.RoundToInt(v); });
        I("Base attack",      0f, 100f, () => Ch ? Ch.baseAttack : 0f,       v => { if (Ch) Ch.baseAttack = Mathf.RoundToInt(v); });
        I("Base defense",     0f, 100f, () => Ch ? Ch.baseDefense : 0f,      v => { if (Ch) Ch.baseDefense = Mathf.RoundToInt(v); });
        I("Base max MP",      1f, 500f, () => Ch ? Ch.baseMaxMP : 0f,        v => { if (Ch) Ch.baseMaxMP = Mathf.RoundToInt(v); });
        I("Base magic atk",   0f, 100f, () => Ch ? Ch.baseMagicAttack : 0f,  v => { if (Ch) Ch.baseMagicAttack = Mathf.RoundToInt(v); });
        I("Base magic def",   0f, 100f, () => Ch ? Ch.baseMagicDefense : 0f, v => { if (Ch) Ch.baseMagicDefense = Mathf.RoundToInt(v); });
        I("Hard DEF %",       0f, 100f, () => Ch ? Ch.baseHardDefPercent : 0f,      v => { if (Ch) Ch.baseHardDefPercent = Mathf.RoundToInt(v); });        // pre-renewal % phys reduction
        I("Hard MDEF %",      0f, 100f, () => Ch ? Ch.baseHardMagicDefPercent : 0f, v => { if (Ch) Ch.baseHardMagicDefPercent = Mathf.RoundToInt(v); });   // pre-renewal % magic reduction
        F("MP regen / s",     0f, 20f,  () => Ch ? Ch.mpRegenPerSecond : 0f, v => { if (Ch) Ch.mpRegenPerSecond = v; });
        I("HP / level",       0f, 50f,  () => Ch ? Ch.hpPerLevel : 0f,       v => { if (Ch) Ch.hpPerLevel = Mathf.RoundToInt(v); });
        I("Attack / level",   0f, 20f,  () => Ch ? Ch.attackPerLevel : 0f,   v => { if (Ch) Ch.attackPerLevel = Mathf.RoundToInt(v); });
        I("Defense / level",  0f, 20f,  () => Ch ? Ch.defensePerLevel : 0f,  v => { if (Ch) Ch.defensePerLevel = Mathf.RoundToInt(v); });
        I("Starting stat pts", 0f, 200f, () => Ch ? Ch.startingStatPoints : 0f, v => { if (Ch) Ch.startingStatPoints = Mathf.RoundToInt(v); });
        Act("Grant +50 stat pts", () => { if (Ch) Ch.GrantStatPoints(50); });   // pre-renewal: feel the ⌊V/10⌋+2 cost curve
        Act("Respec stats (refund all)", () => { if (Ch) Ch.RespecStats(); });   // reset to 1s + full level budget (also migrates old saves)
        Act("Grant +200 ring XP", () => { if (Ch) Ch.AddRingXP(200); });   // level the ring (→ passive points; skill pick / 10 allocated)

        // ---- Mobs — every active MonsterAI3D + MobRespawn ------------------
        Cat("Mobs");
        F("Move speed",       0f, 8f,  () => MobRead(m => m.moveSpeed),
            v => MobForEach(m => { m.moveSpeed = v; var a = m.GetComponent<PathAgent3D>(); if (a != null) a.moveSpeed = v; }));
        F("Aggro radius",     0f, 20f, () => MobRead(m => m.aggroRadius),  v => MobForEach(m => m.aggroRadius = v));
        F("Leash radius",     0f, 30f, () => MobRead(m => m.leashRadius),  v => MobForEach(m => m.leashRadius = v));
        F("Stop distance",    0.2f, 3f, () => MobRead(m => m.stopDistance), v => MobForEach(m => m.stopDistance = v));
        F("Wander radius",    0f, 10f, () => MobRead(m => m.wanderRadius), v => MobForEach(m => m.wanderRadius = v));
        I("Attack",           0f, 50f, () => MobRead(m => m.attack),       v => MobForEach(m => m.attack = Mathf.RoundToInt(v)));
        F("Damage spread",    0f, 1f,  () => MobRead(m => m.damageSpread), v => MobForEach(m => m.damageSpread = v));
        I("Hit (accuracy)",   0f, 100f, () => MobRead(m => m.hit),         v => MobForEach(m => m.hit = Mathf.RoundToInt(v)));
        // Pre-renewal DEF split (lives on Health): hard % first, then soft flat. Crit ignores both.
        I("DEF (hard %)",     0f, 100f, () => MobRead(m => m.Health != null ? m.Health.hardDefPercent : 0),      v => MobForEach(m => { if (m.Health != null) m.Health.hardDefPercent = Mathf.RoundToInt(v); }));
        I("DEF (soft flat)",  0f, 100f, () => MobRead(m => m.Health != null ? m.Health.defense : 0),             v => MobForEach(m => { if (m.Health != null) m.Health.defense = Mathf.RoundToInt(v); }));
        I("MDEF (hard %)",    0f, 100f, () => MobRead(m => m.Health != null ? m.Health.hardMagicDefPercent : 0), v => MobForEach(m => { if (m.Health != null) m.Health.hardMagicDefPercent = Mathf.RoundToInt(v); }));
        I("LUK (crit shield)", 0f, 100f, () => MobRead(m => m.Health != null ? m.Health.luk : 0),                v => MobForEach(m => { if (m.Health != null) m.Health.luk = Mathf.RoundToInt(v); }));
        I("XP reward",        0f, 100f, () => MobRead(m => m.xpReward),     v => MobForEach(m => m.xpReward = Mathf.RoundToInt(v)));
        F("Touch range",      0.2f, 3f, () => MobRead(m => m.touchRange),  v => MobForEach(m => m.touchRange = v));
        F("Touch interval",   0.2f, 3f, () => MobRead(m => m.touchInterval), v => MobForEach(m => m.touchInterval = v));
        F("Respawn delay",    0f, 10f, () => RespawnRead(r => r.respawnDelay),     v => RespawnForEach(r => r.respawnDelay = v));
        F("Home radius",      0f, 15f, () => RespawnRead(r => r.homeRadius),       v => RespawnForEach(r => r.homeRadius = v));
        F("Player clear rad", 0f, 15f, () => RespawnRead(r => r.playerClearRadius), v => RespawnForEach(r => r.playerClearRadius = v));

        // ---- Combat Test (RO) — elements, status ailments, leech/reflect ----
        // Buttons act on the mob NEAREST the player (or on you). Cast Fireball (Fire) / Frost (Water) at a
        // mob whose armor element you've cycled to see Weak!/Resist + changed numbers; afflict status to
        // watch DoT ticks / freeze / blind misses; set Lifesteal/Reflect to feel the card mechanics.
        Cat("Combat Test (RO)");
        F("Lifesteal %", 0f, 1f, () => Ch ? Ch.lifeStealPct : 0f, v => { if (Ch) Ch.lifeStealPct = v; });
        F("Reflect %",   0f, 1f, () => Ch ? Ch.reflectPct : 0f,   v => { if (Ch) Ch.reflectPct = v; });
        Act(MobElementLabel, CycleMobElement);
        Act(() => "My armor: " + (Ch != null ? Ch.armorElement.ToString() : "-") + "   ▶ cycle", CyclePlayerElement);
        Act(() => "Weapon: " + (attacker != null ? attacker.meleeElement.ToString() : "-") + "   ▶ cycle", CycleWeaponElement);
        Act("Poison nearest (5s)", () => Afflict(Ailment.Poison, 5f, 4f));
        Act("Bleed nearest (5s)",  () => Afflict(Ailment.Bleed, 5f, 4f));
        Act("Burn nearest (5s)",   () => Afflict(Ailment.Burn, 5f, 4f));
        Act("Stun nearest (2s)",   () => Afflict(Ailment.Stun, 2f, 0f));
        Act("Freeze nearest (2s)", () => Afflict(Ailment.Freeze, 2f, 0f));
        Act("Slow nearest (4s)",   () => Afflict(Ailment.Slow, 4f, 0.4f));
        Act("Blind nearest (5s)",  () => Afflict(Ailment.Blind, 5f, 50f));
        Act("Stun ME (2s)",        () => AfflictMe(Ailment.Stun, 2f, 0f));
        Act("Freeze ME (2s)",      () => AfflictMe(Ailment.Freeze, 2f, 0f));
        Act("Slow ME (5s)",        () => AfflictMe(Ailment.Slow, 5f, 0.4f));
        // Persistent move-speed buff slider (1 = none). Folds into MoveMult on the Haste channel, so it lifts the
        // run/walk animation fps + footstep cadence with the speed (Player3DController.GaitSpeedMult) — drag it to
        // feel the animation↔speed lock. Held effectively-permanent (re-armed each drag) until you slide back to 1.
        F("Speed buff ME (×)", 1f, 5f,
            () => player ? Status.Of(player.gameObject).HasteMult : 1f,
            v => { if (player) Status.Of(player.gameObject).Haste(v, 9999f); });
        Act("Silence ME (4s)",     () => AfflictMe(Ailment.Silence, 4f, 0f));
        Act("Blind ME (6s)",       () => AfflictMe(Ailment.Blind, 6f, 60f));
        Act("Poison ME (6s)",      () => AfflictMe(Ailment.Poison, 6f, 3f));

        // ---- Environment — TimeOfDay + DayNightLight -----------------------
        Cat("Environment");
        Sub("Sky & Time");
        B("Pause clock",      () => tod && tod.paused,                       v => { if (tod) tod.paused = v; });
        F("Time of day",      0f, 1f,    () => tod ? tod.time01 : 0f,        v => { if (tod) tod.time01 = v; });
        F("Half-cycle (s)",   5f, 3600f, () => tod ? tod.halfCycleSeconds : 0f, v => { if (tod) tod.halfCycleSeconds = v; });
        F("Test speed",       0f, 50f,   () => tod ? tod.testSpeed : 0f,     v => { if (tod) tod.testSpeed = v; });
        FSun("Day intensity",   0f, 3f,    () => sun ? sun.dayIntensity : 0f,  v => { if (sun) sun.dayIntensity = v; });
        FSun("Night intensity", 0f, 2f,    () => sun ? sun.nightIntensity : 0f, v => { if (sun) sun.nightIntensity = v; });
        FSun("Sunset glow",     0f, 1f,    () => sun ? sun.horizonStrength : 0f, v => { if (sun) sun.horizonStrength = v; });
        FSun("Sunset band",     0.05f, 0.5f, () => sun ? sun.horizonBand : 0f, v => { if (sun) sun.horizonBand = v; });
        FSun("Sunset R",        0f, 1f,    () => sun ? sun.horizonColor.r : 0f, v => { if (sun) { var c = sun.horizonColor; c.r = v; sun.horizonColor = c; } });
        FSun("Sunset G",        0f, 1f,    () => sun ? sun.horizonColor.g : 0f, v => { if (sun) { var c = sun.horizonColor; c.g = v; sun.horizonColor = c; } });
        FSun("Sunset B",        0f, 1f,    () => sun ? sun.horizonColor.b : 0f, v => { if (sun) { var c = sun.horizonColor; c.b = v; sun.horizonColor = c; } });
        FSun("Sunrise R",       0f, 1f,    () => sun ? sun.sunriseColor.r : 0f, v => { if (sun) { var c = sun.sunriseColor; c.r = v; sun.sunriseColor = c; } });
        FSun("Sunrise G",       0f, 1f,    () => sun ? sun.sunriseColor.g : 0f, v => { if (sun) { var c = sun.sunriseColor; c.g = v; sun.sunriseColor = c; } });
        FSun("Sunrise B",       0f, 1f,    () => sun ? sun.sunriseColor.b : 0f, v => { if (sun) { var c = sun.sunriseColor; c.b = v; sun.sunriseColor = c; } });
        FSun("Noon pitch",      0f, 90f,   () => sun ? sun.noonPitch : 0f,     v => { if (sun) sun.noonPitch = v; });
        FSun("Horizon pitch",   0f, 90f,   () => sun ? sun.horizonPitch : 0f,  v => { if (sun) sun.horizonPitch = v; });
        FSun("Sun yaw",       -180f, 180f, () => sun ? sun.yaw : 0f,           v => { if (sun) sun.yaw = v; });
        FSun("Sun yaw swing", 0f, 180f,    () => sun ? sun.yawSwing : 0f,      v => { if (sun) sun.yawSwing = v; });
        BSun("Drive ambient",   () => sun && sun.driveAmbient,                 v => { if (sun) sun.driveAmbient = v; });
        BSun("Rotate sun",      () => sun && sun.rotateSun,                    v => { if (sun) sun.rotateSun = v; });
        BSun("Drive sky",       () => sun && sun.driveSky,                     v => { if (sun) sun.driveSky = v; });
        FSun("Sky horizon tint", 0f, 1f, () => sun ? sun.skyHorizonTint : 0f,  v => { if (sun) sun.skyHorizonTint = v; });
        FSun("Day sky R",       0f, 1f,    () => sun ? sun.daySky.r : 0f,      v => { if (sun) { var c = sun.daySky; c.r = v; sun.daySky = c; } });
        FSun("Day sky G",       0f, 1f,    () => sun ? sun.daySky.g : 0f,      v => { if (sun) { var c = sun.daySky; c.g = v; sun.daySky = c; } });
        FSun("Day sky B",       0f, 1f,    () => sun ? sun.daySky.b : 0f,      v => { if (sun) { var c = sun.daySky; c.b = v; sun.daySky = c; } });
        FSun("Night sky R",     0f, 1f,    () => sun ? sun.nightSky.r : 0f,    v => { if (sun) { var c = sun.nightSky; c.r = v; sun.nightSky = c; } });
        FSun("Night sky G",     0f, 1f,    () => sun ? sun.nightSky.g : 0f,    v => { if (sun) { var c = sun.nightSky; c.g = v; sun.nightSky = c; } });
        FSun("Night sky B",     0f, 1f,    () => sun ? sun.nightSky.b : 0f,    v => { if (sun) { var c = sun.nightSky; c.b = v; sun.nightSky = c; } });
        BSun("Dim sprites night", () => sun && sun.driveSpriteLight,            v => { if (sun) sun.driveSpriteLight = v; });
        FSun("Sprite warmth",   0f, 1f,    () => sun ? sun.spriteHorizonWarmth : 0f, v => { if (sun) sun.spriteHorizonWarmth = v; });
        FSun("Night sprite R",  0f, 1f,    () => sun ? sun.nightSpriteTint.r : 0f, v => { if (sun) { var c = sun.nightSpriteTint; c.r = v; sun.nightSpriteTint = c; } });
        FSun("Night sprite G",  0f, 1f,    () => sun ? sun.nightSpriteTint.g : 0f, v => { if (sun) { var c = sun.nightSpriteTint; c.g = v; sun.nightSpriteTint = c; } });
        FSun("Night sprite B",  0f, 1f,    () => sun ? sun.nightSpriteTint.b : 0f, v => { if (sun) { var c = sun.nightSpriteTint; c.b = v; sun.nightSpriteTint = c; } });
        Sub("Calendar & Weather");
        // The deterministic date + per-day weather (GameCalendar / WeatherDirector). Scrub days to fast-test season
        // and weather rollover; force a preset to lock the sky for a screenshot. Day-scrub detaches from real UTC.
        Act(() => "▶ " + (cal != null ? cal.DateString : "-") + "   ·   " +
                  (wx != null ? WeatherDirector.TagOf(wx.Current) : "-") + "    (tap = live clock)",
            () => { if (tod) tod.useRealClock = true; });
        B("Auto weather (calendar)", () => wx && wx.autoFromCalendar, v => { if (wx) wx.autoFromCalendar = v; });
        Act(() => "Force weather: " + (wx != null ? WeatherDirector.TagOf(wx.forcedPreset) : "-") + "    ▶ cycle",
            CycleForcedWeather);
        Act("◀  Previous day",               () => { if (tod) tod.NudgeDays(-1); });
        Act("Next day  ▶",                   () => { if (tod) tod.NudgeDays(1); });
        Act("Skip ▶ next season (+12 days)", () => { if (tod) tod.NudgeDays(GameCalendar.DaysPerSeason); });

        Sub("Night Lamps");
        // Night lamps — global dials over every NightLight prop (houses/candles/lamps). They self-update each
        // frame, so these apply live without needing a sun Refresh.
        B("Night lamps",      () => NightLight.GlobalEnabled,                  v => NightLight.GlobalEnabled = v);
        F("Lamp intensity",   0f, 4f,    () => NightLight.GlobalIntensity,     v => NightLight.GlobalIntensity = v);
        F("Lamp range x",     0.3f, 3f,  () => NightLight.GlobalRange,         v => NightLight.GlobalRange = v);
        Sub("Cloud Shadows");
        // Cloud shadows — coverage/darkness/softness rebake the texture (MarkDirty); the rest are live.
        B("Cloud shadows",    () => cloud && cloud.enabled,                    v => { if (cloud) cloud.enabled = v; });
        // Cloud cover is the Cloud weather channel now (a sequence drives it); snap it here for manual tuning.
        F("Cloud strength",   0f, 1f,    () => ChGet(WeatherChannel.Cloud),    v => ChSnap(WeatherChannel.Cloud, v));
        F("Cloud coverage",   0f, 1f,    () => cloud ? cloud.coverage : 0f,    v => { if (cloud) { cloud.coverage = v; cloud.MarkDirty(); } });
        F("Cloud darkness",   0f, 1f,    () => cloud ? cloud.darkness : 0f,    v => { if (cloud) { cloud.darkness = v; cloud.MarkDirty(); } });
        F("Cloud softness",   0.01f, 0.5f, () => cloud ? cloud.softness : 0f,  v => { if (cloud) { cloud.softness = v; cloud.MarkDirty(); } });
        F("Cloud patch size", 10f, 150f, () => cloud ? cloud.patchSize : 0f,   v => { if (cloud) cloud.patchSize = v; });
        F("Cloud wind base",  0f, 3f,    () => cloud ? cloud.windSpeed : 0f,   v => { if (cloud) cloud.windSpeed = v; });
        F("Cloud wind response", 0f, 4f, () => cloud ? cloud.windResponse : 0f, v => { if (cloud) cloud.windResponse = v; });
        F("Wind direction",   0f, 360f,  () => cloud ? cloud.windDirDeg : 0f,  v => { if (cloud) cloud.windDirDeg = v; });
        // VISIBLE clouds = the procedural SKYBOX (SkyboxSky): wraps the whole sky, seen everywhere + in wet-ground
        // reflections. Cover is the Cloud channel above; these shape the look.
        F("Cloud size (sky)", 4f, 120f,  () => sky ? sky.cloudSize : 0f,      v => { sky = SkyboxSky.Ensure(); sky.cloudSize = v; });
        F("Cloud density",    0f, 4f,    () => sky ? sky.density : 0f,        v => { sky = SkyboxSky.Ensure(); sky.density = v; });
        F("Cloud edge soft",  0.01f, 1f, () => sky ? sky.softEdge : 0f,       v => { sky = SkyboxSky.Ensure(); sky.softEdge = v; });
        F("Cloud horizon fade",0.01f, 1f,() => sky ? sky.horizonFade : 0f,    v => { sky = SkyboxSky.Ensure(); sky.horizonFade = v; });
        F("Cloud storm darken",0f, 1f,   () => sky ? sky.stormDarken : 0f,    v => { sky = SkyboxSky.Ensure(); sky.stormDarken = v; });
        F("Cloud grey onset", 0f, 1f,    () => sky ? sky.fairCloud : 0f,      v => { sky = SkyboxSky.Ensure(); sky.fairCloud = v; });
        F("Sky gradient pow", 0.2f, 4f,  () => sky ? sky.gradientPower : 0f,  v => { sky = SkyboxSky.Ensure(); sky.gradientPower = v; });
        F("Sun disk size",    50f, 4000f,() => sky ? sky.sunSharpness : 0f,   v => { sky = SkyboxSky.Ensure(); sky.sunSharpness = v; });
        F("Sun halo spread",  2f, 64f,   () => sky ? sky.sunHalo : 0f,        v => { sky = SkyboxSky.Ensure(); sky.sunHalo = v; });
        F("Moon disk size",   50f, 6000f,() => sky ? sky.moonSharpness : 0f,  v => { sky = SkyboxSky.Ensure(); sky.moonSharpness = v; });
        F("Moon halo spread", 2f, 64f,   () => sky ? sky.moonHalo : 0f,       v => { sky = SkyboxSky.Ensure(); sky.moonHalo = v; });
        F("Moon height",      0f, 2f,    () => sky ? sky.moonLift : 0f,       v => { sky = SkyboxSky.Ensure(); sky.moonLift = v; });
        Sub("Wind & Foliage Sway");
        // The Wind weather CHANNEL (0..1) — like Rain/Cloud, authored per weather. It drives the foliage sway, the
        // cloud drift speed AND the rain slant together, so one value = how windy it is. Direction is set above.
        F("Wind (weather)",   0f, 1f,    () => ChGet(WeatherChannel.Wind),     v => ChSnap(WeatherChannel.Wind, v));
        // These shape how the foliage responds to that Wind value.
        F("Sway amount",      0f, 3f,    () => wind ? wind.swayScale : 1f,     v => { wind = WindGlobals.Ensure(); wind.swayScale = v; });
        F("Sway base wind",   0f, 2f,    () => wind ? wind.baseStrength : 0f,  v => { wind = WindGlobals.Ensure(); wind.baseStrength = v; });
        F("Sway storm boost", 0f, 4f,    () => wind ? wind.stormBoost : 0f,    v => { wind = WindGlobals.Ensure(); wind.stormBoost = v; });
        Sub("Rain");
        // Rain (spike) — screen-space overlay; created on demand the first time you raise it, so it works even
        // on a Systems scene baked before RainOverlay joined the rig. Zero GPU cost while intensity is 0.
        B("Rain",             () => ChGet(WeatherChannel.Rain) > 0f,
                              v => { rain = RainOverlay.Ensure(); rain.enabled = true; ChSnap(WeatherChannel.Rain, v ? Mathf.Max(0.5f, ChGet(WeatherChannel.Rain)) : 0f); });
        F("Rain intensity",   0f, 1f,    () => ChGet(WeatherChannel.Rain),     v => { rain = RainOverlay.Ensure(); rain.enabled = true; ChSnap(WeatherChannel.Rain, v); });
        F("Rain fall speed",  0.2f, 20f, () => rain ? rain.fallSpeed : 0f,     v => { if (rain) rain.fallSpeed = v; });
        F("Rain slant",      -1f, 1f,    () => rain ? rain.slant : 0f,         v => { if (rain) rain.slant = v; });
        F("Rain density",     20f, 160f, () => rain ? rain.density : 0f,       v => { if (rain) rain.density = v; });
        F("Rain fade in",     0f, 30f,   () => rain ? rain.rampTime : 0f,      v => { if (rain) rain.rampTime = v; });
        Sub("Rain Audio");
        // Rain audio — Forest Day/Night Storm crossfaded by time-of-day, scaled by rain. Follows rain, not tier.
        B("Rain audio",       () => rainAudio && rainAudio.enabled && rainAudio.maxVolume > 0f,
                              v => { rainAudio = RainAudio.Ensure(); rainAudio.enabled = true;
                                     rainAudio.maxVolume = v ? Mathf.Max(0.5f, rainAudio.maxVolume) : 0f; });
        F("Rain volume",      0f, 1f,    () => rainAudio ? rainAudio.maxVolume : 0f,
                              v => { if (!rainAudio) rainAudio = RainAudio.Ensure(); rainAudio.maxVolume = v; });
        Sub("Wet Ground");
        // Heavy weather (High tier only) — wet ground + rain-impact rings; follow the live rain amount. Toggling on
        // raises the quality tier and spawns the effect; it costs nothing on Low/Medium so min spec is intact.
        B("Heavy weather",    () => WeatherFX.Heavy,
                              v => { WeatherFX.Quality = v ? WeatherFX.Tier.High : WeatherFX.Tier.Medium;
                                     if (v) wet = WetGround.Ensure(); });
        F("Wet darken",       0f, 1f,    () => wet ? wet.darken : 0f,          v => { if (wet) wet.darken = v; });
        // Puddles are their own weather CHANNEL now. "Puddle coverage" is the live channel (0..1, ×"Wet puddles" cap);
        // dragging it takes manual control (Auto puddle OFF). The four timers below drive the AUTO-puddle behaviour —
        // used when rain is raised manually and no sequence is authoring puddles (they're authored in WeatherSequences).
        F("Puddle coverage",  0f, 1f,    () => ChGet(WeatherChannel.Puddle),   v => { WC().autoPuddle = false; ChSnap(WeatherChannel.Puddle, v); });
        B("Auto puddle (from rain)", () => wc && wc.autoPuddle,                 v => WC().autoPuddle = v);
        F("Puddle soak delay",0f, 30f,   () => wc ? wc.puddleSoakDelay : 0f,   v => WC().puddleSoakDelay = v);
        F("Puddle build time",0.1f, 60f, () => wc ? wc.puddleSpread : 0f,      v => WC().puddleSpread = v);
        F("Puddle dry delay", 0f, 60f,   () => wc ? wc.puddleDryDelay : 0f,    v => WC().puddleDryDelay = v);
        F("Puddle dry time",  0.1f, 60f, () => wc ? wc.puddleRecede : 0f,      v => WC().puddleRecede = v);
        F("Wet puddles",      0f, 1f,    () => wet ? wet.puddleAmount : 0f,    v => { if (wet) wet.puddleAmount = v; });
        F("Wet puddle size",  0.03f, 0.4f, () => wet ? wet.puddleScale : 0f,   v => { if (wet) wet.puddleScale = v; });
        F("Wet reflection",   0f, 4f,    () => wet ? wet.specStrength : 0f,    v => { if (wet) wet.specStrength = v; });
        F("Wet ripple speed", 0f, 4f,    () => wet ? wet.rippleSpeed : 0f,     v => { if (wet) wet.rippleSpeed = v; });
        F("Water impact",     0f, 4f,    () => wet ? wet.waterImpact : 0f,     v => { if (wet) wet.waterImpact = v; });
        F("Ground impact",    0f, 4f,    () => wet ? wet.groundImpact : 0f,   v => { if (wet) wet.groundImpact = v; });
        F("Ground impact start", 0f, 1f, () => wet ? wet.groundImpactStart : 0f, v => { if (wet) wet.groundImpactStart = v; });
        F("Step ripples",     0f, 8f,    () => wet ? wet.stepBright : 0f,      v => { if (wet) wet.stepBright = v; });
        F("Step radius",      0.1f, 1f,  () => wet ? wet.stepRadius : 0f,      v => { if (wet) wet.stepRadius = v; });
        Sub("Footsteps");
        // Gait (shared FootstepTracker) — cadence/spacing/offset/jump-splash drive BOTH puddle ripples and snow prints.
        // Cadence is in SECONDS now (paced on gait-time like the SFX/animation), so prints track move speed correctly.
        F("Footstep cadence", 0.1f, 0.8f, () => foot ? foot.strideSeconds : 0f, v => { foot = FootstepTracker.Ensure(); foot.strideSeconds = v; });
        F("First step delay", 0f, 0.6f,   () => foot ? foot.firstStepDelay : 0f, v => { foot = FootstepTracker.Ensure(); foot.firstStepDelay = v; });
        F("Foot spacing",     0f, 0.6f,  () => foot ? foot.footSpacing : 0f,   v => { foot = FootstepTracker.Ensure(); foot.footSpacing = v; });
        F("Step offset",     -1f, 1f,    () => foot ? foot.stepOffset : 0f,    v => { foot = FootstepTracker.Ensure(); foot.stepOffset = v; });
        F("Step offset (walk)", -1f, 1f, () => foot ? foot.walkStepOffset : 0f, v => { foot = FootstepTracker.Ensure(); foot.walkStepOffset = v; });
        F("Jump splash",      1f, 6f,    () => foot ? foot.landingScale : 0f,  v => { foot = FootstepTracker.Ensure(); foot.landingScale = v; });
        Sub("Reflection");
        // Planar reflection (Step 1: 3D world only) — mirror camera into puddles. Needs High tier + rain.
        B("Water reflection", () => refl && refl.enabled && wet && wet.reflStrength > 0f,
                              v => { refl = PlanarReflection.Ensure(); refl.enabled = true;
                                     if (!wet) wet = WetGround.Ensure(); wet.reflStrength = v ? Mathf.Max(0.9f, wet.reflStrength) : 0f; });
        F("Reflection amount",0f, 2f,    () => wet ? wet.reflStrength : 0f,    v => { if (wet) wet.reflStrength = v; });
        B("Reflection flip Y",() => wet && wet.reflFlipY,                      v => { if (wet) wet.reflFlipY = v; });
        // Mirror plane: auto-snaps to the real ground (followGround). The fine offset nudges the seam and STICKS;
        // the manual "Water level" only applies when auto is off (narrow range so you can land on it).
        B("Auto water level", () => refl && refl.followGround,                 v => { if (refl) refl.followGround = v; });
        F("Water level offset", -0.3f, 0.3f, () => refl ? refl.groundOffset : 0f, v => { if (refl) refl.groundOffset = v; });
        F("Water level (manual)", -0.5f, 1.5f, () => refl ? refl.waterHeight : 0f, v => { if (refl) refl.waterHeight = v; });
        Sub("Snow");
        // Snow — drifting-flake overlay (all tiers, cheap) + ground build-up on roofs/terrain. The blanket ramps
        // up/melts over "Snow build time"; footprints in the snow are a High-tier extra (toggle Heavy weather on).
        // Created on demand like rain so it works on a Systems scene baked before snow joined the rig. OFF by default.
        B("Snow",             () => ChGet(WeatherChannel.Snow) > 0f,
                              v => { snow = SnowOverlay.Ensure(); snow.enabled = true; snowGround = SnowGround.Ensure();
                                     ChSnap(WeatherChannel.Snow, v ? Mathf.Max(0.6f, ChGet(WeatherChannel.Snow)) : 0f); });
        F("Snow intensity",   0f, 1f,    () => ChGet(WeatherChannel.Snow),     v => { snow = SnowOverlay.Ensure(); snow.enabled = true;
                                                                                       snowGround = SnowGround.Ensure(); ChSnap(WeatherChannel.Snow, v); });
        F("Snow fall speed",  0.5f, 12f, () => snow ? snow.fallSpeed : 0f,     v => { if (snow) snow.fallSpeed = v; });
        F("Snow drift",       0f, 1f,    () => snow ? snow.drift : 0f,         v => { if (snow) snow.drift = v; });
        F("Snow density",     10f, 120f, () => snow ? snow.density : 0f,       v => { if (snow) snow.density = v; });
        F("Flake size",       0.05f, 0.9f, () => snow ? snow.flakeSize : 0f,   v => { if (snow) snow.flakeSize = v; });
        F("Snow parallax",   -1f, 1f,    () => snow ? snow.parallax : 0f,      v => { if (snow) snow.parallax = v; });
        F("Flake fade in",    0f, 30f,   () => snow ? snow.rampTime : 0f,      v => { if (snow) snow.rampTime = v; });
        F("Snow coverage",    0f, 1f,    () => snowGround ? snowGround.coverage : 0f,  v => { if (snowGround) snowGround.coverage = v; });
        F("Snow on slopes",   0f, 1f,    () => snowGround ? snowGround.upCutoff : 0f,  v => { if (snowGround) snowGround.upCutoff = v; });
        F("Snow build time",  0.5f, 60f, () => snowGround ? snowGround.accumTime : 0f, v => { if (snowGround) snowGround.accumTime = v; });
        F("Snow patch size",  0.05f, 1f, () => snowGround ? snowGround.patchScale : 0f,v => { if (snowGround) snowGround.patchScale = v; });
        F("Snow thickness",   0f, 1f,    () => snowGround ? snowGround.thickness : 0f, v => { if (snowGround) snowGround.thickness = v; });
        F("Snow clumpiness",  0f, 1f,    () => snowGround ? snowGround.lumpiness : 0f, v => { if (snowGround) snowGround.lumpiness = v; });
        F("Footprint heal",   0.5f, 30f, () => snowGround ? snowGround.footHeal : 0f,  v => { if (snowGround) snowGround.footHeal = v; });
        F("Footprint carve",  0f, 1f,    () => snowGround ? snowGround.footCarve : 0f, v => { if (snowGround) snowGround.footCarve = v; });
        F("Footprint radius", 0.1f, 0.6f, () => snowGround ? snowGround.footRadius : 0f, v => { if (snowGround) snowGround.footRadius = v; });
        // Footprint CADENCE (shared FootstepTracker — same value as the wet "Footstep cadence" knob; also paces snow
        // prints). Seconds between prints at normal gait; raise if steps look too frequent.
        F("Footprint cadence",0.1f, 0.8f, () => foot ? foot.strideSeconds : 0f, v => { foot = FootstepTracker.Ensure(); foot.strideSeconds = v; });
        F("Footprint spacing",0f, 0.6f,  () => foot ? foot.footSpacing : 0f,    v => { foot = FootstepTracker.Ensure(); foot.footSpacing = v; });
        Sub("Fog");
        // Fog — DEPTH fog (all tiers, one pass), its own weather CHANNEL. Fogs by true distance from the camera so far
        // trees/ground dissolve into haze. Created on demand like rain/snow so it works on a Systems scene baked before
        // FogOverlay joined the rig. Zero GPU cost while intensity is 0.
        B("Fog",              () => ChGet(WeatherChannel.Fog) > 0f,
                              v => { fog = FogOverlay.Ensure(); fog.enabled = true; ChSnap(WeatherChannel.Fog, v ? Mathf.Max(0.7f, ChGet(WeatherChannel.Fog)) : 0f); });
        F("Fog intensity",    0f, 1f,    () => ChGet(WeatherChannel.Fog),      v => { fog = FogOverlay.Ensure(); fog.enabled = true; ChSnap(WeatherChannel.Fog, v); });
        F("Fog start dist",   0f, 60f,   () => fog ? fog.startDistance : 0f,   v => { if (fog) fog.startDistance = v; });
        F("Fog full dist",    1f, 150f,  () => fog ? fog.fullDistance : 0f,    v => { if (fog) fog.fullDistance = v; });
        F("Fog coverage",     0f, 1f,    () => fog ? fog.coverage : 0f,        v => { if (fog) fog.coverage = v; });
        F("Fog floor",        0f, 1f,    () => fog ? fog.floor : 0f,           v => { if (fog) fog.floor = v; });
        F("Fog patch size",   2f, 120f,  () => fog ? fog.patchSize : 0f,       v => { if (fog) fog.patchSize = v; });
        F("Fog drift speed",  0f, 4f,    () => fog ? fog.driftSpeed : 0f,      v => { if (fog) fog.driftSpeed = v; });
        F("Fog roll in",      0f, 30f,   () => fog ? fog.rampTime : 0f,        v => { if (fog) fog.rampTime = v; });

        // ---- Sound — Sfx 3D positional range (how far from the LISTENER a world sound is heard) ----
        // The listener rides the player (AudioListenerRig). "Full volume within" = loud at any closer range;
        // "Silent past" = inaudible beyond. Only affects Sfx.PlayAt (3D one-shots); 2D Sfx.Play ignores distance.
        // SetSpatialRange pushes the new values onto every pooled 3D voice so the slider takes effect live.
        Cat("Sound");
        F("Master volume",        0f, 1f,   () => Sfx.Instance ? Sfx.Instance.masterVolume : 0f,
            v => { if (Sfx.Instance) Sfx.Instance.masterVolume = v; });
        F("Full volume within",   0f, 30f,  () => Sfx.Instance ? Sfx.Instance.minDistance : 0f,
            v => { if (Sfx.Instance) Sfx.Instance.SetSpatialRange(v, Sfx.Instance.maxDistance); });
        F("Silent past",          1f, 120f, () => Sfx.Instance ? Sfx.Instance.maxDistance : 0f,
            v => { if (Sfx.Instance) Sfx.Instance.SetSpatialRange(Sfx.Instance.minDistance, v); });

        // ---- Camera — CameraFollow3D ---------------------------------------
        Cat("Camera");
        F("Distance",         2f, 25f, () => camRig ? camRig.distance : 0f,     v => { if (camRig) camRig.distance = v; });
        F("Height",           1f, 28f, () => camRig ? camRig.height : 0f,       v => { if (camRig) camRig.height = v; });
        F("Min height",       0f, 6f,  () => camRig ? camRig.minHeight : 0f,    v => { if (camRig) camRig.minHeight = v; });   // floor for right-drag tilt (low = reach ground-level view)
        F("Max pitch",       20f, 80f, () => camRig ? camRig.maxPitch : 0f,     v => { if (camRig) camRig.maxPitch = v; });
        F("Yaw",           -180f, 180f, () => camRig ? camRig.yaw : 0f,         v => { if (camRig) camRig.yaw = v; });
        F("Look-at height",   0f, 3f,  () => camRig ? camRig.lookAtHeight : 0f, v => { if (camRig) camRig.lookAtHeight = v; });
        F("Smooth time",      0f, 0.5f, () => camRig ? camRig.smoothTime : 0f,  v => { if (camRig) camRig.smoothTime = v; });
        F("Zoom step",        0.5f, 6f, () => camRig ? camRig.zoomStep : 0f,    v => { if (camRig) camRig.zoomStep = v; });
        F("Drag yaw speed",   0f, 1f,  () => camRig ? camRig.dragYawSpeed : 0f, v => { if (camRig) camRig.dragYawSpeed = v; });
        F("Drag height speed", 0f, 0.2f, () => camRig ? camRig.dragHeightSpeed : 0f, v => { if (camRig) camRig.dragHeightSpeed = v; });

        // ---- Occlusion Fade — CameraOccluderFader (fade props that block the player) ----
        // "Enabled" off instantly restores every faded prop to solid (the component's OnDisable cleans up).
        Cat("Occlusion Fade");
        B("Enabled",       () => fader && fader.enabled,         v => { if (fader) fader.enabled = v; });
        F("Faded alpha",   0f, 1f,     () => fader ? fader.fadedAlpha : 0f, v => { if (fader) fader.fadedAlpha = v; });
        F("Fade speed",    1f, 30f,    () => fader ? fader.fadeSpeed : 0f,  v => { if (fader) fader.fadeSpeed = v; });
        F("Cast radius",   0.1f, 1.5f, () => fader ? fader.castRadius : 0f, v => { if (fader) fader.castRadius = v; });
        F("Aim height",    0f, 3f,     () => fader ? fader.aimHeight : 0f,  v => { if (fader) fader.aimHeight = v; });
    }

    void Update()
    {
        if (Pressed())
        {
            open = !open;
            if (open) { EnsureRefs(); if (!built) { BuildKnobs(); built = true; } }
            else Blocking = false;
        }
        if (open) EnsureRefs();
    }

    // ---- IMGUI ----------------------------------------------------------------
    void OnGUI()
    {
        if (!open) return;
        if (headerStyle == null) InitStyles();
        if (!rectInit) { windowRect = new Rect(Screen.width - 392, 16, 376, 0f); rectInit = true; }

        windowRect = GUILayout.Window(WinId, windowRect, DrawWindow, "DEV SETTINGS   ·   P to close");

        // Block world input while the cursor is over the panel, so slider clicks don't also order the
        // player / orbit the camera. (Hover precedes the click, so this is already true on mouse-down.)
        Blocking = open && windowRect.Contains(Event.current.mousePosition);
        if (Blocking) UiBlock.MarkImguiHot();   // route through the shared blocker so all world-input honors it
    }

    void DrawWindow(int id)
    {
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Width(360), GUILayout.Height(Mathf.Min(Screen.height - 70, 660)));
        bool parentOpen = true;   // is the current top-level category expanded? (gates its sub-categories)
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var grp = groups[gi];
            if (grp.isSub)
            {
                if (!parentOpen) continue;   // parent category collapsed → hide its sub-categories
                GUILayout.BeginHorizontal();
                GUILayout.Space(12f);        // indent the whole sub-category
                GUILayout.BeginVertical();
                grp.open = GUILayout.Toggle(grp.open, (grp.open ? "▼  " : "▶  ") + grp.name, headerStyle);
                if (grp.open) for (int ki = 0; ki < grp.knobs.Count; ki++) DrawKnob(grp.knobs[ki]);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
                continue;
            }
            grp.open = GUILayout.Toggle(grp.open, (grp.open ? "▼  " : "▶  ") + grp.name, headerStyle);
            parentOpen = grp.open;
            if (!grp.open) continue;
            for (int ki = 0; ki < grp.knobs.Count; ki++) DrawKnob(grp.knobs[ki]);
            GUILayout.Space(4f);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4f);
        if (GUILayout.Button("Reset all to defaults")) ResetAll();
        GUILayout.Label("* base damage only used when there's no Character", valueStyle);

        GUI.DragWindow(new Rect(0, 0, 10000, 22));   // drag by the title bar only — sliders stay live
    }

    void DrawKnob(Knob k)
    {
        if (k.isRaw) { k.draw?.Invoke(); return; }
        if (k.isAct)
        {
            string lbl = k.dyn != null ? k.dyn() : k.label;
            if (GUILayout.Button(lbl, GUILayout.Width(330))) k.act?.Invoke();
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(k.label, labelStyle, GUILayout.Width(150));

        if (k.isBool)
        {
            bool cv = k.getB();
            if (!k.hasDefault) { k.defB = cv; k.hasDefault = true; }
            bool nv = GUILayout.Toggle(cv, cv ? " On" : " Off", GUILayout.Width(174));
            if (nv != cv) k.setB(nv);
        }
        else
        {
            float cv = k.get();
            if (!k.hasDefault) { k.def = cv; k.hasDefault = true; }
            float nv = GUILayout.HorizontalSlider(cv, k.min, k.max, GUILayout.Width(120));
            if (k.isInt) nv = Mathf.Round(nv);
            GUILayout.Label(k.isInt ? Mathf.RoundToInt(nv).ToString() : nv.ToString("0.00"), valueStyle, GUILayout.Width(46));
            if (!Mathf.Approximately(nv, cv)) k.set(nv);
        }
        GUILayout.EndHorizontal();
    }

    // ── Palette swap UI (called as a Raw knob from the Appearance group) ──────

    // A white-backgrounded box style so GUI.backgroundColor gives accurate color swatches.
    GUIStyle SwatchStyle
    {
        get
        {
            if (swatchStyle != null) return swatchStyle;
            swatchStyle = new GUIStyle();
            swatchStyle.normal.background = Texture2D.whiteTexture;
            return swatchStyle;
        }
    }

    void DrawSwapSlots()
    {
        if (dye == null) { GUILayout.Label("  (no CharacterDye)", labelStyle); return; }

        GUILayout.Space(2f);
        GUILayout.Label("Palette Swaps", labelStyle);

        for (int i = 0; i < CharacterDye.SwapCount; i++)
        {
            var  slot    = dye.swaps[i];
            bool picking = dye.EyedropperActive && dye.EyedropperSlot == i;
            bool changed = false;

            // ── row 1: enable toggle + src swatch → dst swatch + Pick button ──
            GUILayout.BeginHorizontal();

            bool en = GUILayout.Toggle(slot.enabled, " " + (i + 1), GUILayout.Width(42));
            if (en != slot.enabled) { slot.enabled = en; changed = true; }

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = slot.source;
            GUILayout.Box(GUIContent.none, SwatchStyle, GUILayout.Width(20), GUILayout.Height(14));
            GUI.backgroundColor = prevBg;

            GUILayout.Label("→", GUILayout.Width(16));

            GUI.backgroundColor = slot.target;
            GUILayout.Box(GUIContent.none, SwatchStyle, GUILayout.Width(20), GUILayout.Height(14));
            GUI.backgroundColor = prevBg;

            GUILayout.FlexibleSpace();

            if (picking)
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0.2f);
                if (GUILayout.Button("◉ cancel", GUILayout.Width(70))) dye.CancelEyedropper();
                GUI.backgroundColor = prevBg;
            }
            else
            {
                if (GUILayout.Button("Pick src", GUILayout.Width(64))) dye.StartEyedropper(i);
            }
            GUILayout.EndHorizontal();

            // ── row 2: target R / G / B ──────────────────────────────────────
            Color tgt = slot.target;
            float nr = DrawMiniSlider("R", tgt.r);
            float ng = DrawMiniSlider("G", tgt.g);
            float nb = DrawMiniSlider("B", tgt.b);
            if (!Mathf.Approximately(nr, tgt.r) || !Mathf.Approximately(ng, tgt.g) || !Mathf.Approximately(nb, tgt.b))
            {
                slot.target = new Color(nr, ng, nb);
                changed = true;
            }

            // ── row 3: tolerance ─────────────────────────────────────────────
            float ntol = DrawMiniSlider("Tol", slot.tolerance, 0.01f, 0.5f);
            if (!Mathf.Approximately(ntol, slot.tolerance)) { slot.tolerance = ntol; changed = true; }

            if (changed) dye.Apply();
            GUILayout.Space(3f);
        }

        if (GUILayout.Button("Clear all swaps", GUILayout.Width(152)))
        {
            foreach (var s in dye.swaps) s.enabled = false;
            dye.Apply();
        }
    }

    float DrawMiniSlider(string lbl, float v, float min = 0f, float max = 1f)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(lbl, labelStyle, GUILayout.Width(26));
        float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(84));
        GUILayout.Label(nv.ToString("0.00"), valueStyle, GUILayout.Width(34));
        GUILayout.EndHorizontal();
        return nv;
    }

    void ResetAll()
    {
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var ks = groups[gi].knobs;
            for (int ki = 0; ki < ks.Count; ki++)
            {
                var k = ks[ki];
                if (!k.hasDefault) continue;
                if (k.isBool) k.setB(k.defB); else k.set(k.def);
            }
        }
    }

    void InitStyles()
    {
        headerStyle = new GUIStyle(GUI.skin.label)
        { fontStyle = FontStyle.Bold, fontSize = 13, padding = new RectOffset(2, 2, 4, 2) };
        headerStyle.normal.textColor = new Color(0.85f, 0.95f, 1f);
        headerStyle.onNormal.textColor = headerStyle.normal.textColor;
        headerStyle.hover.textColor = Color.white;
        headerStyle.onHover.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
        valueStyle.normal.textColor = new Color(0.7f, 1f, 0.7f);
    }

    static bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.pKey.wasPressedThisFrame;
#endif
        return Input.GetKeyDown(KeyCode.P);
    }
}
