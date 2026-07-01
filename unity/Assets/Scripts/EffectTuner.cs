using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-game tuning overlay for the <see cref="EffectLibrary"/> (press <b>F8</b> in Play). Unlike the editor
/// <c>EffectPreviewWindow</c>, this spawns the effect on the REAL player / nearest mob during play, so you judge
/// size + position against actual game entities. Drag scale / offset / lifetime, hit a Spawn button to see it
/// land, then <b>Save</b> — because the library is an ASSET (not a scene object), edits made in editor-Play
/// persist after you stop. Dev-only: it self-bootstraps in the editor and development builds, never in a release
/// build. Values are applied to the in-memory def before each spawn, so what you see is what Save writes.
/// </summary>
public class EffectTuner : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("EffectTuner");
        DontDestroyOnLoad(go);   // dev tool only — same exemption as DevSettings
        go.AddComponent<EffectTuner>();
    }
#endif

    const KeyCode ToggleKey = KeyCode.F8;

    bool open;
    string filter = "";
    Vector2 scroll;
    readonly List<EffectDef> shown = new List<EffectDef>();
    EffectDef sel;

    // working (unsaved) values for the selected effect
    float scale = 1f, lifetime;
    Vector3 offset;

    Player3DController player;

    // Live preview: the burst plays and dies in a fraction of a second, so to make the sliders actually mean
    // something we (1) keep re-firing it on the last-used anchor on a cadence, and (2) push scale/offset onto
    // the currently-alive instance every frame so a drag updates what you're looking at, not just the next spawn.
    bool livePreview = true;
    Transform anchor;              // player / mob the preview last spawned on
    Vfx.EffectHandle live;         // the instance currently on screen (may have auto-returned)
    float nextReplay;

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey)) open = !open;
        if (!livePreview || sel == null || anchor == null) return;

        if (live != null && live.IsActive)
            live.TuneLive(anchor.position + offset, scale);   // real-time scale/offset on the live burst

        if (Time.time >= nextReplay) Replay();                // short one-shots die → keep re-firing
    }

    // (Re)fire the selected effect on the current anchor with the working values, and schedule the next replay
    // one lifetime (or a sane default) later so there's always something on screen to judge.
    void Replay()
    {
        if (sel == null || anchor == null) return;
        live = SpawnAt(anchor.position);
        nextReplay = Time.time + (lifetime > 0f ? lifetime : 1.5f) + 0.1f;
    }

    void StartLive(Transform t)
    {
        anchor = t;
        livePreview = true;
        nextReplay = 0f;   // fire this frame
    }

    Transform Player()
    {
        if (player == null) player = FindFirstObjectByType<Player3DController>();
        return player != null ? player.transform : null;
    }

    // Nearest live mob to the player (uses the zero-alloc Active registry + cached distance).
    Transform NearestMob()
    {
        var p = Player();
        Vector3 from = p != null ? p.position : Vector3.zero;
        Transform best = null;
        float bestSqr = float.MaxValue;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null) continue;
            float d = (m.transform.position - from).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = m.transform; }
        }
        return best;
    }

    void Select(EffectDef d)
    {
        sel = d;
        if (d == null) return;
        scale = d.scale <= 0f ? 1f : d.scale;
        offset = d.offset;
        lifetime = d.lifetime;
        nextReplay = 0f;   // preview the newly-picked effect on the next frame (if an anchor is set)
    }

    // Push the working values into the live def, then fire it at a world point. Returns the handle so the live
    // preview can keep nudging it as the sliders move.
    Vfx.EffectHandle SpawnAt(Vector3 pos)
    {
        if (sel == null) return null;
        sel.scale = scale;
        sel.offset = offset;
        sel.lifetime = lifetime;
        return Vfx.PlayAt(sel.id, pos);
    }

    void Save()
    {
        if (sel == null) return;
        sel.scale = scale; sel.offset = offset; sel.lifetime = lifetime;
#if UNITY_EDITOR
        var lib = UnityEditor.AssetDatabase.LoadAssetAtPath<EffectLibrary>("Assets/Resources/EffectLibrary.asset");
        if (lib != null) { UnityEditor.EditorUtility.SetDirty(lib); UnityEditor.AssetDatabase.SaveAssets(); }
        EffectCatalog.Reload();
        Debug.Log($"[EffectTuner] Saved '{sel.id}': scale {scale:0.###}, offset {offset}, life {lifetime:0.##}");
#else
        Debug.Log("[EffectTuner] Save only persists in the editor.");
#endif
    }

    void OnGUI()
    {
        if (!open)
        {
            GUI.Label(new Rect(10, Screen.height - 24, 320, 22), $"[{ToggleKey}] Effect Tuner");
            return;
        }

        const float w = 340f;
        var area = new Rect(Screen.width - w - 10, 10, w, Screen.height - 20);
        if (area.Contains(Event.current.mousePosition)) UiBlock.MarkImguiHot();   // eat clicks over the panel
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 8, area.y + 8, area.width - 16, area.height - 16));

        GUILayout.Label("EFFECT TUNER  (F8 to close)", GUI.skin.box);

        if (EffectCatalog.Count == 0)
        {
            GUILayout.Label("Library empty. Run Tools ▸ unwritten ▸ Art ▸ Rebuild Effect Library.");
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Filter", GUILayout.Width(40));
        filter = GUILayout.TextField(filter);
        GUILayout.EndHorizontal();

        // filtered id list
        shown.Clear();
        var all = EffectCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d == null || d.prefab == null || string.IsNullOrEmpty(d.id)) continue;
            if (filter.Length > 0 && d.id.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            shown.Add(d);
        }

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(190));
        foreach (var d in shown)
        {
            bool isSel = d == sel;
            var style = isSel ? GUI.skin.box : GUI.skin.label;
            if (GUILayout.Button(d.id, style)) Select(d);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6);
        if (sel == null) { GUILayout.Label("Pick an effect above."); GUILayout.EndArea(); return; }

        GUILayout.Label("Tuning: " + sel.id, GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Scale {scale:0.00}", GUILayout.Width(90));
        scale = GUILayout.HorizontalSlider(scale, 0.02f, 5f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Y off {offset.y:0.00}", GUILayout.Width(90));
        offset.y = GUILayout.HorizontalSlider(offset.y, -2f, 4f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"X off {offset.x:0.00}", GUILayout.Width(90));
        offset.x = GUILayout.HorizontalSlider(offset.x, -2f, 2f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Z off {offset.z:0.00}", GUILayout.Width(90));
        offset.z = GUILayout.HorizontalSlider(offset.z, -2f, 2f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Life {lifetime:0.00}", GUILayout.Width(90));
        lifetime = GUILayout.HorizontalSlider(lifetime, 0f, 6f);
        GUILayout.EndHorizontal();
        GUILayout.Label("(lifetime 0 = auto from the particle system)");

        GUILayout.Space(4);
        livePreview = GUILayout.Toggle(livePreview, " Live preview (auto-replay on last anchor; sliders update it live)");

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn on Player"))
        {
            var t = Player();
            if (t != null) StartLive(t);
        }
        if (GUILayout.Button("Spawn on Mob"))
        {
            var t = NearestMob();
            if (t != null) StartLive(t);
            else Debug.Log("[EffectTuner] No mobs alive to spawn on.");
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Revert")) Select(sel);
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("SAVE to Library")) Save();
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}
