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
        public bool isInt, isBool;
        public Func<float> get; public Action<float> set;
        public Func<bool> getB; public Action<bool> setB;
        public bool hasDefault; public float def; public bool defB;
    }

    class Group { public string name; public bool open = true; public readonly List<Knob> knobs = new List<Knob>(); }

    readonly List<Group> groups = new List<Group>();
    Group cur;   // group currently being filled by the builder helpers

    // ---- lazily-resolved targets (re-found after map swaps destroy/recreate them) ----
    Player3DController player;
    Player3DAttacker attacker;
    CameraFollow3D camRig;
    TimeOfDay tod;
    DayNightLight sun;
    MobRespawn[] respawns;
    float nextRespawnRefresh;

    bool open;
    Vector2 scroll;
    Rect windowRect;
    bool built, rectInit;
    const int WinId = 920411;

    GUIStyle headerStyle, labelStyle, valueStyle;

    void EnsureRefs()
    {
        if (player == null)   player   = FindFirstObjectByType<Player3DController>();
        if (attacker == null) attacker = FindFirstObjectByType<Player3DAttacker>();
        if (camRig == null)   camRig   = FindFirstObjectByType<CameraFollow3D>();
        if (tod == null)      tod      = TimeOfDay.Instance;
        if (sun == null)      sun      = FindFirstObjectByType<DayNightLight>();
    }

    // ---- builder helpers ------------------------------------------------------
    void Cat(string name) { cur = new Group { name = name }; groups.Add(cur); }
    void F(string label, float min, float max, Func<float> get, Action<float> set)
        => cur.knobs.Add(new Knob { label = label, min = min, max = max, get = get, set = set });
    void I(string label, float min, float max, Func<float> get, Action<float> set)
        => cur.knobs.Add(new Knob { label = label, min = min, max = max, isInt = true, get = get, set = set });
    void B(string label, Func<bool> get, Action<bool> set)
        => cur.knobs.Add(new Knob { label = label, isBool = true, getB = get, setB = set });

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

    void BuildKnobs()
    {
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

        // ---- Environment — TimeOfDay + DayNightLight -----------------------
        Cat("Environment");
        B("Pause clock",      () => tod && tod.paused,                       v => { if (tod) tod.paused = v; });
        F("Time of day",      0f, 1f,    () => tod ? tod.time01 : 0f,        v => { if (tod) tod.time01 = v; });
        F("Half-cycle (s)",   5f, 3600f, () => tod ? tod.halfCycleSeconds : 0f, v => { if (tod) tod.halfCycleSeconds = v; });
        F("Test speed",       0f, 50f,   () => tod ? tod.testSpeed : 0f,     v => { if (tod) tod.testSpeed = v; });
        F("Day intensity",    0f, 3f,    () => sun ? sun.dayIntensity : 0f,  v => { if (sun) sun.dayIntensity = v; });
        F("Night intensity",  0f, 2f,    () => sun ? sun.nightIntensity : 0f, v => { if (sun) sun.nightIntensity = v; });
        F("Noon pitch",       0f, 90f,   () => sun ? sun.noonPitch : 0f,     v => { if (sun) sun.noonPitch = v; });
        F("Horizon pitch",    0f, 45f,   () => sun ? sun.horizonPitch : 0f,  v => { if (sun) sun.horizonPitch = v; });
        F("Sun yaw",        -180f, 180f, () => sun ? sun.yaw : 0f,           v => { if (sun) sun.yaw = v; });
        B("Drive ambient",    () => sun && sun.driveAmbient,                 v => { if (sun) sun.driveAmbient = v; });
        B("Rotate sun",       () => sun && sun.rotateSun,                    v => { if (sun) sun.rotateSun = v; });

        // ---- Camera — CameraFollow3D ---------------------------------------
        Cat("Camera");
        F("Distance",         2f, 25f, () => camRig ? camRig.distance : 0f,     v => { if (camRig) camRig.distance = v; });
        F("Height",           1f, 28f, () => camRig ? camRig.height : 0f,       v => { if (camRig) camRig.height = v; });
        F("Yaw",           -180f, 180f, () => camRig ? camRig.yaw : 0f,         v => { if (camRig) camRig.yaw = v; });
        F("Look-at height",   0f, 3f,  () => camRig ? camRig.lookAtHeight : 0f, v => { if (camRig) camRig.lookAtHeight = v; });
        F("Smooth time",      0f, 0.5f, () => camRig ? camRig.smoothTime : 0f,  v => { if (camRig) camRig.smoothTime = v; });
        F("Zoom step",        0.5f, 6f, () => camRig ? camRig.zoomStep : 0f,    v => { if (camRig) camRig.zoomStep = v; });
        F("Drag yaw speed",   0f, 1f,  () => camRig ? camRig.dragYawSpeed : 0f, v => { if (camRig) camRig.dragYawSpeed = v; });
        F("Drag height speed", 0f, 0.2f, () => camRig ? camRig.dragHeightSpeed : 0f, v => { if (camRig) camRig.dragHeightSpeed = v; });
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
