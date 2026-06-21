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

    class Group { public string name; public bool open = false; public readonly List<Knob> knobs = new List<Knob>(); }

    readonly List<Group> groups = new List<Group>();
    Group cur;   // group currently being filled by the builder helpers

    // ---- lazily-resolved targets (re-found after map swaps destroy/recreate them) ----
    Player3DController player;
    Player3DAttacker attacker;
    CameraFollow3D camRig;
    CameraOccluderFader fader;
    TimeOfDay tod;
    DayNightLight sun;
    CloudShadows cloud;
    RainOverlay rain;
    WetGround wet;
    PlanarReflection refl;
    RainAudio rainAudio;
    SnowOverlay snow;
    SnowGround snowGround;
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
        if (attacker == null) attacker = FindFirstObjectByType<Player3DAttacker>();
        if (camRig == null)   camRig   = FindFirstObjectByType<CameraFollow3D>();
        if (fader == null)    fader    = FindFirstObjectByType<CameraOccluderFader>();
        if (tod == null)      tod      = TimeOfDay.Instance;
        if (sun == null)      sun      = FindFirstObjectByType<DayNightLight>();
        if (cloud == null)    cloud    = FindFirstObjectByType<CloudShadows>();
        if (rain == null)     rain     = FindFirstObjectByType<RainOverlay>();
        if (wet == null)      wet      = FindFirstObjectByType<WetGround>();
        if (refl == null)     refl     = FindFirstObjectByType<PlanarReflection>();
        if (rainAudio == null) rainAudio = FindFirstObjectByType<RainAudio>();
        if (snow == null)     snow     = FindFirstObjectByType<SnowOverlay>();
        if (snowGround == null) snowGround = FindFirstObjectByType<SnowGround>();
        if (foot == null)     foot     = FindFirstObjectByType<FootstepTracker>();
        if (dye == null)      dye      = FindFirstObjectByType<CharacterDye>();
    }

    // ---- builder helpers ------------------------------------------------------
    void Cat(string name) { cur = new Group { name = name }; groups.Add(cur); }
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

    void BuildKnobs()
    {
        // ---- Class (swap the active-skill kit live; persists via PlayerPrefs) ----
        Cat("Class");
        B("Mage (off = Swordsman)",
            () => ClassState.Current == PlayerClass.Mage,
            v => { if (player) player.SetClass(v ? PlayerClass.Mage : PlayerClass.Swordsman); });

        // ---- Player (movement / feel) — Player3DController -----------------
        Cat("Player");
        F("Move speed",       0f, 12f, () => player ? player.moveSpeed : 0f,       v => { if (player) player.moveSpeed = v; });
        F("Anim FPS",         1f, 20f, () => player ? player.framesPerSecond : 0f, v => { if (player) player.framesPerSecond = v; });
        F("Acquire range",    0f, 15f, () => player ? player.acquireRange : 0f,    v => { if (player) player.acquireRange = v; });
        F("Roam range",       0f, 30f, () => player ? player.roamRange : 0f,       v => { if (player) player.roamRange = v; });
        F("Snap radius (px)", 0f, 200f, () => player ? player.snapRadiusPixels : 0f, v => { if (player) { player.snapRadiusPixels = v; player.ApplySnapRadius(); } });
        F("Agent radius",     0.1f, 1f, () => player ? player.agentRadius : 0f,    v => { if (player) player.agentRadius = v; });
        F("Gravity",          0f, 30f, () => player ? player.gravity : 0f,         v => { if (player) player.gravity = v; });
        F("Step interval",    0.1f, 1f, () => player ? player.stepInterval : 0f,   v => { if (player) player.stepInterval = v; });
        B("Idle breathing",   () => player && player.breathe,                      v => { if (player) player.breathe = v; });
        F("Breathe amount",   0f, 0.2f, () => player ? player.breatheAmount : 0f,  v => { if (player) player.breatheAmount = v; });
        F("Breathe speed",    0f, 6f,  () => player ? player.breatheSpeed : 0f,    v => { if (player) player.breatheSpeed = v; });
        // Sscary traversal (only affect a Sscary player — SPACE = forward hop, E = dash burst).
        F("Foot offset",      0f, 0.5f,  () => player ? player.scaryFeetBelowCenter : 0f, v => { if (player) player.SetScaryFootOffset(v); });
        F("Jump distance",    0f, 6f,    () => player ? player.jumpDistance : 0f,  v => { if (player) player.jumpDistance = v; });
        F("Jump height",      0f, 2.5f,  () => player ? player.jumpHeight : 0f,    v => { if (player) player.jumpHeight = v; });
        F("Dash speed",       0f, 30f,   () => player ? player.eDashSpeed : 0f,    v => { if (player) player.eDashSpeed = v; });
        F("Dash duration",    0.05f, 0.6f, () => player ? player.eDashDuration : 0f, v => { if (player) player.eDashDuration = v; });
        F("Dash cooldown",    0f, 3f,    () => player ? player.eDashCooldown : 0f, v => { if (player) player.eDashCooldown = v; });
        F("Walk anim FPS",    1f, 24f,   () => player ? player.scaryWalkFps : 0f,  v => { if (player) player.scaryWalkFps = v; });

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

        // ---- Player Combat — Player3DAttacker ------------------------------
        Cat("Player Combat");
        F("Attack range",     0.5f, 4f, () => attacker ? attacker.range : 0f,      v => { if (attacker) attacker.range = v; });
        F("Cooldown (s)",     0.1f, 2f, () => attacker ? attacker.cooldown : 0f,   v => { if (attacker) attacker.cooldown = v; });
        F("Windup (s)",       0f, 0.6f, () => attacker ? attacker.windupTime : 0f, v => { if (attacker) attacker.windupTime = v; });
        I("Base damage*",     1f, 50f, () => attacker ? attacker.damage : 0f,      v => { if (attacker) attacker.damage = Mathf.RoundToInt(v); });
        F("Slash scale",      0.5f, 3f, () => attacker ? attacker.slashScale : 0f, v => { if (attacker) attacker.slashScale = v; });
        F("Slash FPS",        5f, 40f, () => attacker ? attacker.slashFps : 0f,    v => { if (attacker) attacker.slashFps = v; });

        // ---- Player Stats — Character (effective stats recompute live) -----
        Cat("Player Stats");
        I("Level",            1f, 50f,  () => Ch ? Ch.Level : 1f,           v => { if (Ch) Ch.SetLevel(Mathf.RoundToInt(v)); });
        I("Base max HP",      1f, 500f, () => Ch ? Ch.baseMaxHP : 0f,        v => { if (Ch) Ch.baseMaxHP = Mathf.RoundToInt(v); });
        I("Base attack",      0f, 100f, () => Ch ? Ch.baseAttack : 0f,       v => { if (Ch) Ch.baseAttack = Mathf.RoundToInt(v); });
        I("Base defense",     0f, 100f, () => Ch ? Ch.baseDefense : 0f,      v => { if (Ch) Ch.baseDefense = Mathf.RoundToInt(v); });
        I("Base max MP",      1f, 500f, () => Ch ? Ch.baseMaxMP : 0f,        v => { if (Ch) Ch.baseMaxMP = Mathf.RoundToInt(v); });
        I("Base magic atk",   0f, 100f, () => Ch ? Ch.baseMagicAttack : 0f,  v => { if (Ch) Ch.baseMagicAttack = Mathf.RoundToInt(v); });
        I("Base magic def",   0f, 100f, () => Ch ? Ch.baseMagicDefense : 0f, v => { if (Ch) Ch.baseMagicDefense = Mathf.RoundToInt(v); });
        F("MP regen / s",     0f, 20f,  () => Ch ? Ch.mpRegenPerSecond : 0f, v => { if (Ch) Ch.mpRegenPerSecond = v; });
        I("HP / level",       0f, 50f,  () => Ch ? Ch.hpPerLevel : 0f,       v => { if (Ch) Ch.hpPerLevel = Mathf.RoundToInt(v); });
        I("Attack / level",   0f, 20f,  () => Ch ? Ch.attackPerLevel : 0f,   v => { if (Ch) Ch.attackPerLevel = Mathf.RoundToInt(v); });
        I("Defense / level",  0f, 20f,  () => Ch ? Ch.defensePerLevel : 0f,  v => { if (Ch) Ch.defensePerLevel = Mathf.RoundToInt(v); });
        I("Stat pts / level", 0f, 20f,  () => Ch ? Ch.statPointsPerLevel : 0f, v => { if (Ch) Ch.statPointsPerLevel = Mathf.RoundToInt(v); });

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
        Act("Silence ME (4s)",     () => AfflictMe(Ailment.Silence, 4f, 0f));
        Act("Blind ME (6s)",       () => AfflictMe(Ailment.Blind, 6f, 60f));
        Act("Poison ME (6s)",      () => AfflictMe(Ailment.Poison, 6f, 3f));

        // ---- Environment — TimeOfDay + DayNightLight -----------------------
        Cat("Environment");
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
        // Night lamps — global dials over every NightLight prop (houses/candles/lamps). They self-update each
        // frame, so these apply live without needing a sun Refresh.
        B("Night lamps",      () => NightLight.GlobalEnabled,                  v => NightLight.GlobalEnabled = v);
        F("Lamp intensity",   0f, 4f,    () => NightLight.GlobalIntensity,     v => NightLight.GlobalIntensity = v);
        F("Lamp range x",     0.3f, 3f,  () => NightLight.GlobalRange,         v => NightLight.GlobalRange = v);
        // Cloud shadows — coverage/darkness/softness rebake the texture (MarkDirty); the rest are live.
        B("Cloud shadows",    () => cloud && cloud.enabled,                    v => { if (cloud) cloud.enabled = v; });
        F("Cloud strength",   0f, 1f,    () => cloud ? cloud.strength : 0f,    v => { if (cloud) cloud.strength = v; });
        F("Cloud coverage",   0f, 1f,    () => cloud ? cloud.coverage : 0f,    v => { if (cloud) { cloud.coverage = v; cloud.MarkDirty(); } });
        F("Cloud darkness",   0f, 1f,    () => cloud ? cloud.darkness : 0f,    v => { if (cloud) { cloud.darkness = v; cloud.MarkDirty(); } });
        F("Cloud softness",   0.01f, 0.5f, () => cloud ? cloud.softness : 0f,  v => { if (cloud) { cloud.softness = v; cloud.MarkDirty(); } });
        F("Cloud patch size", 10f, 150f, () => cloud ? cloud.patchSize : 0f,   v => { if (cloud) cloud.patchSize = v; });
        F("Wind speed",       0f, 3f,    () => cloud ? cloud.windSpeed : 0f,   v => { if (cloud) cloud.windSpeed = v; });
        F("Wind direction",   0f, 360f,  () => cloud ? cloud.windDirDeg : 0f,  v => { if (cloud) cloud.windDirDeg = v; });
        // Rain (spike) — screen-space overlay; created on demand the first time you raise it, so it works even
        // on a Systems scene baked before RainOverlay joined the rig. Zero GPU cost while intensity is 0.
        B("Rain",             () => rain && rain.enabled && rain.intensity > 0f,
                              v => { rain = RainOverlay.Ensure(); rain.enabled = true; rain.intensity = v ? Mathf.Max(0.5f, rain.intensity) : 0f; });
        F("Rain intensity",   0f, 1f,    () => rain ? rain.intensity : 0f,     v => { if (!rain) rain = RainOverlay.Ensure(); rain.enabled = true; rain.intensity = v; });
        F("Rain fall speed",  0.2f, 20f, () => rain ? rain.fallSpeed : 0f,     v => { if (rain) rain.fallSpeed = v; });
        F("Rain slant",      -1f, 1f,    () => rain ? rain.slant : 0f,         v => { if (rain) rain.slant = v; });
        F("Rain density",     20f, 160f, () => rain ? rain.density : 0f,       v => { if (rain) rain.density = v; });
        // Rain audio — Forest Day/Night Storm crossfaded by time-of-day, scaled by rain. Follows rain, not tier.
        B("Rain audio",       () => rainAudio && rainAudio.enabled && rainAudio.maxVolume > 0f,
                              v => { rainAudio = RainAudio.Ensure(); rainAudio.enabled = true;
                                     rainAudio.maxVolume = v ? Mathf.Max(0.5f, rainAudio.maxVolume) : 0f; });
        F("Rain volume",      0f, 1f,    () => rainAudio ? rainAudio.maxVolume : 0f,
                              v => { if (!rainAudio) rainAudio = RainAudio.Ensure(); rainAudio.maxVolume = v; });
        // Heavy weather (High tier only) — wet ground + splashes; both follow the live rain amount. Toggling on
        // raises the quality tier and spawns the effects; they cost nothing on Low/Medium so min spec is intact.
        B("Heavy weather",    () => WeatherFX.Heavy,
                              v => { WeatherFX.Quality = v ? WeatherFX.Tier.High : WeatherFX.Tier.Medium;
                                     if (v) wet = WetGround.Ensure(); });
        F("Wet darken",       0f, 1f,    () => wet ? wet.darken : 0f,          v => { if (wet) wet.darken = v; });
        F("Wet puddles",      0f, 1f,    () => wet ? wet.puddleAmount : 0f,    v => { if (wet) wet.puddleAmount = v; });
        F("Wet puddle size",  0.03f, 0.4f, () => wet ? wet.puddleScale : 0f,   v => { if (wet) wet.puddleScale = v; });
        F("Wet reflection",   0f, 4f,    () => wet ? wet.specStrength : 0f,    v => { if (wet) wet.specStrength = v; });
        F("Wet ripple speed", 0f, 4f,    () => wet ? wet.rippleSpeed : 0f,     v => { if (wet) wet.rippleSpeed = v; });
        F("Ambient ripples",  0f, 4f,    () => wet ? wet.ripAmbient : 0f,      v => { if (wet) wet.ripAmbient = v; });
        F("Step ripples",     0f, 8f,    () => wet ? wet.stepBright : 0f,      v => { if (wet) wet.stepBright = v; });
        F("Step radius",      0.1f, 1f,  () => wet ? wet.stepRadius : 0f,      v => { if (wet) wet.stepRadius = v; });
        // Gait (shared FootstepTracker) — gap/spacing/offset/jump-splash drive BOTH puddle ripples and snow prints.
        F("Footstep gap",     0.12f, 1.2f, () => foot ? foot.strideDist : 0f,  v => { foot = FootstepTracker.Ensure(); foot.strideDist = v; });
        F("Foot spacing",     0f, 0.6f,  () => foot ? foot.footSpacing : 0f,   v => { foot = FootstepTracker.Ensure(); foot.footSpacing = v; });
        F("Step offset",     -1f, 1f,    () => foot ? foot.stepOffset : 0f,    v => { foot = FootstepTracker.Ensure(); foot.stepOffset = v; });
        F("Jump splash",      1f, 6f,    () => foot ? foot.landingScale : 0f,  v => { foot = FootstepTracker.Ensure(); foot.landingScale = v; });
        // Planar reflection (Step 1: 3D world only) — mirror camera into puddles. Needs High tier + rain.
        B("Water reflection", () => refl && refl.enabled && wet && wet.reflStrength > 0f,
                              v => { refl = PlanarReflection.Ensure(); refl.enabled = true;
                                     if (!wet) wet = WetGround.Ensure(); wet.reflStrength = v ? Mathf.Max(0.9f, wet.reflStrength) : 0f; });
        F("Reflection amount",0f, 2f,    () => wet ? wet.reflStrength : 0f,    v => { if (wet) wet.reflStrength = v; });
        B("Reflection flip Y",() => wet && wet.reflFlipY,                      v => { if (wet) wet.reflFlipY = v; });
        F("Water level",     -5f, 5f,    () => refl ? refl.waterHeight : 0f,   v => { if (refl) refl.waterHeight = v; });
        // Snow — drifting-flake overlay (all tiers, cheap) + ground build-up on roofs/terrain. The blanket ramps
        // up/melts over "Snow build time"; footprints in the snow are a High-tier extra (toggle Heavy weather on).
        // Created on demand like rain so it works on a Systems scene baked before snow joined the rig.
        B("Snow",             () => snow && snow.enabled && snow.intensity > 0f,
                              v => { snow = SnowOverlay.Ensure(); snow.enabled = true; snow.intensity = v ? Mathf.Max(0.6f, snow.intensity) : 0f;
                                     if (v) snowGround = SnowGround.Ensure(); });
        F("Snow intensity",   0f, 1f,    () => snow ? snow.intensity : 0f,     v => { if (!snow) snow = SnowOverlay.Ensure(); snow.enabled = true; snow.intensity = v;
                                                                                       if (!snowGround) snowGround = SnowGround.Ensure(); });
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
        F("Snow sparkle",     0f, 2f,    () => snowGround ? snowGround.sparkle : 0f,   v => { if (snowGround) snowGround.sparkle = v; });
        F("Snow thickness",   0f, 1f,    () => snowGround ? snowGround.thickness : 0f, v => { if (snowGround) snowGround.thickness = v; });
        F("Snow clumpiness",  0f, 1f,    () => snowGround ? snowGround.lumpiness : 0f, v => { if (snowGround) snowGround.lumpiness = v; });
        F("Footprint heal",   0.5f, 30f, () => snowGround ? snowGround.footHeal : 0f,  v => { if (snowGround) snowGround.footHeal = v; });
        F("Footprint carve",  0f, 1f,    () => snowGround ? snowGround.footCarve : 0f, v => { if (snowGround) snowGround.footCarve = v; });
        F("Footprint radius", 0.1f, 0.6f, () => snowGround ? snowGround.footRadius : 0f, v => { if (snowGround) snowGround.footRadius = v; });
        // Footprint GAP (shared FootstepTracker — same value as the wet "Footstep gap" knob; also moves snow prints
        // closer/farther). Lower this if steps look too far apart.
        F("Footprint gap",    0.12f, 1.2f, () => foot ? foot.strideDist : 0f,   v => { foot = FootstepTracker.Ensure(); foot.strideDist = v; });
        F("Footprint spacing",0f, 0.6f,  () => foot ? foot.footSpacing : 0f,    v => { foot = FootstepTracker.Ensure(); foot.footSpacing = v; });

        // ---- Camera — CameraFollow3D ---------------------------------------
        Cat("Camera");
        F("Distance",         2f, 25f, () => camRig ? camRig.distance : 0f,     v => { if (camRig) camRig.distance = v; });
        F("Height",           1f, 28f, () => camRig ? camRig.height : 0f,       v => { if (camRig) camRig.height = v; });
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
    }

    void DrawWindow(int id)
    {
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Width(360), GUILayout.Height(Mathf.Min(Screen.height - 70, 660)));
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var grp = groups[gi];
            grp.open = GUILayout.Toggle(grp.open, (grp.open ? "▼  " : "▶  ") + grp.name, headerStyle);
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
