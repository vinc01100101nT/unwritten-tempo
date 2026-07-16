using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode authoring workspace for <see cref="SkillDef"/> assets — where you COMPOSE SKILLS: pick a composed
/// <see cref="VfxSequence"/> (from the Vfx Composer), and map each of its exposed <b>ticks</b> to gameplay
/// (damage/heal/status, element, multiplier, radius, targets) plus identity (icon, cost, cooldown, level growth).
///
/// <para>The split of concerns is the whole point: the Vfx Composer owns the LOOK and exposes named ticks; this
/// window owns WHAT each tick does. The final numbers stay in code (<see cref="SkillEffectApplier"/> rolls them off
/// the live caster) — this asset only stores the design intent. The tick list on the right is read live from the
/// chosen sequence, so you bind against real ids, never a typo.</para>
/// </summary>
public class SkillComposerWindow : EditorWindow
{
    [MenuItem("Tools/unwritten/Composers/Skill Composer")]
    public static void Open() => GetWindow<SkillComposerWindow>("Skill Composer");

    readonly List<SkillDef> all = new List<SkillDef>();
    SkillDef sel;
    Vector2 listScroll, bodyScroll;
    bool listCollapsed;
    const string Dir = "Assets/3_Data/Resources/Skills";

    void OnEnable() => Refresh();

    void Refresh()
    {
        all.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:SkillDef"))
        {
            var a = AssetDatabase.LoadAssetAtPath<SkillDef>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) all.Add(a);
        }
        all.Sort((x, y) => x.slotOrder.CompareTo(y.slotOrder));
        if (sel == null && all.Count > 0) sel = all[0];
    }

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawBody();
        EditorGUILayout.EndHorizontal();
    }

    // ---- left: asset list ---------------------------------------------------
    void DrawList()
    {
        if (listCollapsed)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(20));
            if (GUILayout.Button("▶", GUILayout.Width(20), GUILayout.Height(40))) listCollapsed = false;
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.BeginVertical(GUILayout.Width(200));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
        if (GUILayout.Button("◀", GUILayout.Width(24))) listCollapsed = true;
        EditorGUILayout.EndHorizontal();
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        foreach (var s in all)
        {
            if (s == null) continue;
            bool on = s == sel;
            var style = on ? EditorStyles.helpBox : EditorStyles.label;
            if (GUILayout.Button($"{s.displayName}\n{s.id}", style, GUILayout.Height(34))) sel = s;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New")) Create();
        using (new EditorGUI.DisabledScope(sel == null))
        {
            if (GUILayout.Button("Dupe")) Duplicate(sel);
            if (GUILayout.Button("Del")) DeleteAsset(sel);
        }
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Refresh")) Refresh();
        EditorGUILayout.EndVertical();
    }

    void Create()
    {
        EnsureFolder();
        var s = CreateInstance<SkillDef>();
        s.id = "skill.new";
        s.displayName = "New Skill";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/skill.new.asset");
        AssetDatabase.CreateAsset(s, path);
        AssetDatabase.SaveAssets();
        Refresh();
        sel = AssetDatabase.LoadAssetAtPath<SkillDef>(path);
    }

    void Duplicate(SkillDef s)
    {
        string src = AssetDatabase.GetAssetPath(s);
        string path = AssetDatabase.GenerateUniqueAssetPath(src);
        AssetDatabase.CopyAsset(src, path);
        AssetDatabase.SaveAssets();
        Refresh();
        sel = AssetDatabase.LoadAssetAtPath<SkillDef>(path);
    }

    void DeleteAsset(SkillDef s)
    {
        if (!EditorUtility.DisplayDialog("Delete skill", $"Delete '{s.displayName}'?", "Delete", "Cancel")) return;
        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(s));
        sel = null;
        Refresh();
    }

    // ---- right: editor ------------------------------------------------------
    void DrawBody()
    {
        EditorGUILayout.BeginVertical();
        if (sel == null) { EditorGUILayout.HelpBox("Select or create a skill.", MessageType.Info); EditorGUILayout.EndVertical(); return; }

        bodyScroll = EditorGUILayout.BeginScrollView(bodyScroll);
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        sel.id = EditorGUILayout.TextField(new GUIContent("id", "Stable kebab id (e.g. skill.meteor). Don't rename once shipped."), sel.id);
        sel.displayName = EditorGUILayout.TextField("display name", sel.displayName);
        sel.icon = (Sprite)EditorGUILayout.ObjectField("icon", sel.icon, typeof(Sprite), false);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Cost / cadence", EditorStyles.boldLabel);
        sel.manaCost = EditorGUILayout.IntField("mana cost", sel.manaCost);
        sel.cooldown = EditorGUILayout.Slider("cooldown (s)", sel.cooldown, 0f, 30f);
        sel.castTime = EditorGUILayout.Slider(
            new GUIContent("cast time (s)", "Wind-up before the skill fires (0 = instant). DEX shortens it at runtime."),
            sel.castTime, 0f, 10f);
        sel.maxLevel = EditorGUILayout.IntSlider("max level", sel.maxLevel, 1, 20);
        sel.aim = (SkillAim)EditorGUILayout.EnumPopup(new GUIContent("aim", "How the cast point is picked."), sel.aim);
        using (new EditorGUI.DisabledScope(sel.aim == SkillAim.Self))
            sel.castRange = EditorGUILayout.Slider("cast range", sel.castRange, 1f, 30f);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Hotbar", EditorStyles.boldLabel);
        sel.shortLabel = EditorGUILayout.TextField("short label", sel.shortLabel);
        sel.keyLabel = EditorGUILayout.TextField("key glyph", sel.keyLabel);
        sel.slotOrder = EditorGUILayout.IntField("slot order", sel.slotOrder);
        sel.tint = EditorGUILayout.ColorField("tint", sel.tint);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Presentation", EditorStyles.boldLabel);
        sel.vfx = (VfxSequence)EditorGUILayout.ObjectField(new GUIContent("vfx sequence",
            "The composed look from the Vfx Composer. Its exposed ticks appear below."), sel.vfx, typeof(VfxSequence), false);
        sel.repeatCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("repeat count",
            "Replay the WHOLE cycle N times per cast (e.g. 5 = a 5-meteor shower). Each cycle is an independent play " +
            "at the same cast point; per-fall hits still come from the VFX's per-component tickCount. 1 = play once."), sel.repeatCount));
        using (new EditorGUI.DisabledScope(sel.repeatCount <= 1))
            sel.repeatInterval = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("repeat interval (s)",
                "Seconds between cycle starts. 0 = use the VFX sequence's RestartInterval (its loop-end marker)."), sel.repeatInterval));

        DrawCastCircle();

        DrawSkillType();

        EditorGUILayout.Space(6);
        DrawTickBindings();

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(sel);

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Save")) { AssetDatabase.SaveAssets(); SkillCatalog.Reload(); }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // The "skill type" dropdown: pick which release cue plays when the skill fires. Index 0 = None (no release
    // animation); the rest are the SkillReleaseStances subset (the single owner of that list). Assign each cue a clip
    // in the Stance Composer. Labels are built once and cached — editor-only, but no reason to rebuild per repaint.
    static GUIContent[] _skillTypeLabels;
    static GUIContent[] SkillTypeLabels()
    {
        if (_skillTypeLabels != null) return _skillTypeLabels;
        var all = SkillReleaseStances.All;
        _skillTypeLabels = new GUIContent[all.Length + 1];
        _skillTypeLabels[0] = new GUIContent("None (no release anim)");
        for (int i = 0; i < all.Length; i++) _skillTypeLabels[i + 1] = new GUIContent(SkillReleaseStances.Label(all[i]));
        return _skillTypeLabels;
    }

    // The cast-circle picker: choose a ground circle shown during the wind-up, tint it, and calibrate its size. The
    // radius itself is NOT authored here — it's shown read-only from LargestAreaRadius (the bindings own it), so the
    // circle can't drift from the skill's real footprint. Ids are read live from the EffectLibrary each draw (editor
    // window, not a hot path) so a fresh Rebuild Effect Library shows up without reopening.
    void DrawCastCircle()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Cast circle (wind-up only)", EditorStyles.boldLabel);
        if (sel.castTime <= 0f)
        { EditorGUILayout.HelpBox("Set a cast time > 0 to show a casting circle.", MessageType.Info); return; }

        var all = EffectCatalog.All;
        var ids = new List<string> { "" };
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && !string.IsNullOrEmpty(all[i].id)) ids.Add(all[i].id);
        var labels = new GUIContent[ids.Count];
        labels[0] = new GUIContent("(none — use global cue)");
        for (int i = 1; i < ids.Count; i++) labels[i] = new GUIContent(ids[i]);

        int cur = Mathf.Max(0, ids.IndexOf(sel.castCircleId ?? ""));
        int next = EditorGUILayout.Popup(new GUIContent("cast circle",
            "Ground circle shown WHILE this skill winds up. Auto-sized to the skill's largest AoE radius. " +
            "Pick an EffectLibrary id (add packs via Art ▸ Rebuild Effect Library)."), cur, labels);
        sel.castCircleId = next <= 0 ? "" : ids[next];

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(sel.castCircleId)))
        {
            sel.castCircleTint = EditorGUILayout.ColorField(new GUIContent("circle tint",
                "Recolour the circle (white = the prefab's own colours)."), sel.castCircleTint);
            EditorGUILayout.HelpBox(
                $"Auto-sized to {sel.LargestAreaRadius:0.##} units (the largest AoE binding radius). The ring's edge " +
                "lands on the AoE via each circle prefab's groundRingRadius (set once in the Effect Library). " +
                "Radius is owned by the bindings below.",
                MessageType.None);
        }
    }

    void DrawSkillType()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Skill type", EditorStyles.boldLabel);
        var all = SkillReleaseStances.All;
        int cur = 0;   // None
        if (sel.hasReleaseStance)
        {
            int i = System.Array.IndexOf(all, sel.releaseStance);
            cur = i >= 0 ? i + 1 : 0;
        }
        int next = EditorGUILayout.Popup(new GUIContent("skill type",
            "The release animation played when this skill fires (after any cast wind-up). Bind each cue to a clip in " +
            "the Stance Composer."), cur, SkillTypeLabels());
        if (next == 0) sel.hasReleaseStance = false;
        else { sel.hasReleaseStance = true; sel.releaseStance = all[next - 1]; }
    }

    // The tick list read live from the chosen sequence: bind gameplay to each exposed tickId. Orphan bindings
    // (id no longer in the sequence) are flagged so a renamed component can't silently do nothing.
    void DrawTickBindings()
    {
        EditorGUILayout.LabelField("Ticks (gameplay)", EditorStyles.boldLabel);
        if (sel.vfx == null) { EditorGUILayout.HelpBox("Assign a Vfx Sequence to see its ticks.", MessageType.Info); return; }
        if (sel.bindings == null) sel.bindings = new List<SkillTickBinding>();

        var seqTicks = CollectTicks(sel.vfx);
        if (seqTicks.Count == 0)
            EditorGUILayout.HelpBox("This sequence exposes no ticks. In the Vfx Composer, give a step a 'tick id'.", MessageType.Warning);

        // Exposed ticks from the sequence — bind/unbind each.
        foreach (var t in seqTicks)
        {
            var b = sel.BindingFor(t.id);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{t.id}   ({t.count}× @ {t.interval:0.##}s)", EditorStyles.boldLabel);
            if (b == null)
            {
                if (GUILayout.Button("+ bind", GUILayout.Width(70)))
                    sel.bindings.Add(new SkillTickBinding { tickId = t.id });
            }
            else if (GUILayout.Button("unbind", GUILayout.Width(70)))
            {
                sel.bindings.Remove(b);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                continue;
            }
            EditorGUILayout.EndHorizontal();
            if (b != null) DrawBinding(b);
            EditorGUILayout.EndVertical();
        }

        // Orphan bindings — an id that isn't in the sequence anymore.
        for (int i = 0; i < sel.bindings.Count; i++)
        {
            var b = sel.bindings[i];
            if (b == null || TicksContain(seqTicks, b.tickId)) continue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox($"Binding '{b.tickId}' has no matching tick in the sequence (renamed/removed?).", MessageType.Warning);
            if (GUILayout.Button("remove orphan", GUILayout.Width(120))) { sel.bindings.RemoveAt(i); i--; }
            EditorGUILayout.EndVertical();
        }
    }

    void DrawBinding(SkillTickBinding b)
    {
        b.targets = (SkillTargets)EditorGUILayout.EnumFlagsField("  targets", b.targets);
        b.scope = (SkillScope)EditorGUILayout.EnumPopup(new GUIContent("  scope",
            "Area = everyone in the radius. Single = only the cast's target (always lands, radius ignored)."), b.scope);
        using (new EditorGUI.DisabledScope(b.scope == SkillScope.Single))
            b.radius = EditorGUILayout.Slider("  radius", b.radius <= 0f ? 1.5f : b.radius, 0.1f, 12f);

        if (b.scale == null) b.scale = new SkillLevelScale();
        b.scale.damageMultPerLevel = EditorGUILayout.Slider(new GUIContent("  +×/level", "Damage multiplier added per level over 1."), b.scale.damageMultPerLevel, 0f, 2f);
        b.scale.flatBonusPerLevel = EditorGUILayout.IntField(new GUIContent("  +flat/level", "Flat damage added per level over 1."), b.scale.flatBonusPerLevel);

        if (b.effects == null) b.effects = new List<SkillEffect>();
        EditorGUILayout.LabelField($"  effects ({b.effects.Count})", EditorStyles.miniBoldLabel);
        int rm = -1;
        for (int e = 0; e < b.effects.Count; e++)
        {
            if (b.effects[e] == null) b.effects[e] = new SkillEffect();
            var ef = b.effects[e];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            ef.kind = (SkillEffectKind)EditorGUILayout.EnumPopup(ef.kind);
            if (GUILayout.Button("✕", GUILayout.Width(22))) rm = e;
            EditorGUILayout.EndHorizontal();
            switch (ef.kind)
            {
                case SkillEffectKind.Damage:
                    ef.magic = EditorGUILayout.Toggle("magic", ef.magic);
                    ef.damageMult = EditorGUILayout.Slider("× caster dmg", ef.damageMult <= 0f ? 1f : ef.damageMult, 0.1f, 8f);
                    ef.element = (Element)EditorGUILayout.EnumPopup("element", ef.element);
                    break;
                case SkillEffectKind.Heal:
                    ef.amount = EditorGUILayout.IntField("HP", ef.amount);
                    break;
                case SkillEffectKind.Status:
                    ef.ailment = (Ailment)EditorGUILayout.EnumPopup("ailment", ef.ailment);
                    ef.seconds = EditorGUILayout.Slider("seconds", ef.seconds, 0.5f, 20f);
                    ef.magnitude = EditorGUILayout.FloatField(new GUIContent("magnitude", "DoT dmg/tick, Slow factor (0..1), or Blind HIT loss."), ef.magnitude);
                    break;
                case SkillEffectKind.Haste:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("speed ×", "Move-speed multiplier (>1 = faster)."), ef.magnitude <= 0f ? 1.3f : ef.magnitude, 0.2f, 3f);
                    ef.seconds = EditorGUILayout.Slider("seconds", ef.seconds, 0.5f, 20f);
                    break;
            }
            EditorGUILayout.EndVertical();
        }
        if (rm >= 0) b.effects.RemoveAt(rm);
        if (GUILayout.Button("  + effect", GUILayout.Width(90))) b.effects.Add(new SkillEffect());
    }

    struct TickInfo { public string id; public int count; public float interval; }

    static List<TickInfo> CollectTicks(VfxSequence seq)
    {
        var result = new List<TickInfo>();
        if (seq == null || seq.steps == null) return result;
        foreach (var st in seq.steps)
        {
            if (st == null || !st.HasTick) continue;
            if (TicksContain(result, st.tickId)) continue;   // one row per distinct id
            result.Add(new TickInfo { id = st.tickId, count = st.tickCount, interval = st.tickInterval });
        }
        return result;
    }

    static bool TicksContain(List<TickInfo> list, string id)
    {
        for (int i = 0; i < list.Count; i++) if (list[i].id == id) return true;
        return false;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/3_Data/Resources"))
            AssetDatabase.CreateFolder("Assets/3_Data", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/3_Data/Resources", "Skills");
    }
}
