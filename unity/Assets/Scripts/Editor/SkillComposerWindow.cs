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
    float listWidth = 200f;
    bool draggingSplitter;
    const string Dir = "Assets/3_Data/Resources/Skills";

    // Last-saved snapshot of the selected skill, used by "Discard changes". A dirty ScriptableObject can't be
    // reverted by re-importing (Unity keeps the in-memory edits), so we keep our own saved-state copy and copy it
    // back on discard. Captured on selection change and after each Save.
    SkillDef snapshot;
    SkillDef snapshotOwner;

    // A word-wrapping text-area style. The default TextArea doesn't wrap, so a long unbroken line reports its
    // full one-line width and blows out the panel (forcing a horizontal scrollbar); wrapping keeps it narrow.
    static GUIStyle _wrapTextArea;
    static GUIStyle WrapTextArea => _wrapTextArea ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

    void OnEnable()
    {
        minSize = new Vector2(40f, 120f);
        Refresh();
    }

    void OnDisable()
    {
        if (snapshot != null) DestroyImmediate(snapshot);
    }

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
        if (!listCollapsed) DrawSplitter();
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

        EditorGUILayout.BeginVertical(GUILayout.Width(listWidth), GUILayout.MinWidth(0f), GUILayout.MaxWidth(listWidth));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel, GUILayout.MinWidth(0f));
        if (GUILayout.Button("◀", GUILayout.Width(24))) listCollapsed = true;
        EditorGUILayout.EndHorizontal();
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        foreach (var s in all)
        {
            if (s == null) continue;
            bool on = s == sel;
            var style = on ? EditorStyles.helpBox : EditorStyles.label;
            if (GUILayout.Button($"{s.displayName}\n{s.id}", style, GUILayout.Height(34), GUILayout.MinWidth(0f))) sel = s;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New", GUILayout.MinWidth(0f))) Create();
        using (new EditorGUI.DisabledScope(sel == null))
        {
            if (GUILayout.Button("Dupe", GUILayout.MinWidth(0f))) Duplicate(sel);
            if (GUILayout.Button("Del", GUILayout.MinWidth(0f))) DeleteAsset(sel);
        }
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Refresh", GUILayout.MinWidth(0f))) Refresh();
        EditorGUILayout.EndVertical();
    }

    // Draggable divider between the list and the body — drag to resize, no minimum width.
    void DrawSplitter()
    {
        float w = EditorLayoutFlow.VerticalSplitter(listWidth, ref draggingSplitter);
        if (w != listWidth) { listWidth = w; Repaint(); }
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

    // Capture the current (assumed last-saved) state of sel into the scratch snapshot. Called on selection change
    // and right after a Save, so the snapshot always mirrors what's on disk.
    void CaptureSnapshot()
    {
        if (sel == null) { snapshotOwner = null; return; }
        if (snapshot == null)
        {
            snapshot = CreateInstance<SkillDef>();
            snapshot.hideFlags = HideFlags.HideAndDontSave;
        }
        EditorUtility.CopySerialized(sel, snapshot);
        snapshotOwner = sel;
    }

    // Revert the selected skill to its last-saved state, throwing away in-memory (unsaved) edits. A dirty
    // ScriptableObject can't be reverted by re-importing (Unity keeps the edits), so we copy our saved snapshot back.
    void DiscardSkillEdits()
    {
        if (sel == null || snapshot == null || snapshotOwner != sel) return;
        if (!EditorUtility.DisplayDialog("Discard changes",
            $"Revert '{sel.displayName}' to the last saved version? Unsaved edits will be lost.", "Discard", "Cancel"))
            return;
        GUI.FocusControl(null);   // drop focus so an in-progress text field can't re-commit its value
        Undo.RecordObject(sel, "Discard skill changes");
        EditorUtility.CopySerialized(snapshot, sel);
        EditorUtility.ClearDirty(sel);
        SkillCatalog.Reload();
        Repaint();
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

    // Collapsible section headers — group the editor so it reads top-to-bottom (what it IS → how it's classified →
    // what it costs → how it looks → what it does). State is static so a section stays open/closed across repaints.
    static bool foId = true, foClass = true, foCost = true, foPresent = false, foGameplay = true;
    static bool Section(string title, ref bool open)
    {
        EditorGUILayout.Space(6);
        open = EditorGUILayout.Foldout(open, title, true);
        return open;
    }

    void DrawBody()
    {
        EditorGUILayout.BeginVertical();
        if (sel == null) { EditorGUILayout.HelpBox("Select or create a skill.", MessageType.Info); EditorGUILayout.EndVertical(); return; }

        if (snapshotOwner != sel) CaptureSnapshot();   // freshly-selected skill → snapshot its saved state

        float contentWidth = position.width - (listCollapsed ? 20f : listWidth) - 5f;
        bodyScroll = EditorGUILayout.BeginScrollView(bodyScroll);
        // Pin the scroll content to the viewport width so the fields SHRINK to fit instead of the scroll view
        // laying them out at their preferred width and adding a horizontal scrollbar (the "won't shrink" symptom).
        float viewport = Mathf.Max(0f, contentWidth - 18f);   // minus the vertical scrollbar gutter
        EditorGUILayout.BeginVertical(GUILayout.Width(viewport));
        EditorLayoutFlow.ResponsiveLabelWidth(viewport, 130f);
        EditorGUI.BeginChangeCheck();

        // 1) IDENTITY — what the skill IS.
        if (Section("Identity", ref foId))
        {
            sel.id = EditorGUILayout.TextField(new GUIContent("id", "Stable kebab id (e.g. skill.meteor). Don't rename once shipped."), sel.id);
            sel.displayName = EditorGUILayout.TextField("display name", sel.displayName);
            sel.icon = (Sprite)EditorGUILayout.ObjectField("icon", sel.icon, typeof(Sprite), false);
            EditorGUILayout.LabelField(new GUIContent("description", "Flavor + mechanics blurb shown in the hover card."));
            sel.description = EditorGUILayout.TextArea(sel.description ?? "", WrapTextArea, GUILayout.Height(42), GUILayout.ExpandWidth(true));
        }

        // 2) CLASSIFICATION — the 4 authoring axes (verb · family · shape · tier) + designer metadata, all in one place.
        if (Section("Classification  (verb · family · shape · tier)", ref foClass))
        {
            sel.archetype = (SkillArchetype)EditorGUILayout.EnumPopup(new GUIContent("archetype (verb)",
                "Battlefield-role VERB — what the skill DOES. Drives the hover card chip + color (Burst / Tempo / Fortress / Buff / Curse)."), sel.archetype);
            sel.family = (SkillFamily)EditorGUILayout.EnumPopup(new GUIContent("family",
                "Attribute CLUSTER (Might / Arcane / Finesse) — the RPS-triangle side; also the default damage-type source. Orthogonal to the verb."), sel.family);
            sel.shape = (SkillShape)EditorGUILayout.EnumPopup(new GUIContent("shape",
                "Spatial DELIVERY (Single / Area / Aura / Self / Dash)."), sel.shape);
            sel.tier = (SkillTier)EditorGUILayout.EnumPopup(new GUIContent("tier",
                "Base = a normal hotbar skill (keys 1–9). Ultimate = fires from the dedicated ULT slot, charge-gated."), sel.tier);
            using (new EditorGUI.DisabledScope(sel.tier != SkillTier.Ultimate))
                sel.chargeCost = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("charge cost",
                    "Ult-meter charge spent per cast. The meter fills in combat (dealing/taking damage, casting)."), sel.chargeCost));
            EditorGUILayout.Space(2);
            sel.complexity = (SkillComplexity)EditorGUILayout.EnumPopup(new GUIContent("complexity (meta)",
                "Designer metadata only (not gameplay) — authoring complexity, for library search."), sel.complexity);
            sel.acquisitionTier = (AcquisitionTier)EditorGUILayout.EnumPopup(new GUIContent("acq. tier (meta)",
                "Designer metadata only (not gameplay) — early / mid / late progression bucket."), sel.acquisitionTier);
        }

        // 3) COST & CADENCE — what it costs to fire and how it aims.
        if (Section("Cost & Cadence", ref foCost))
        {
            sel.manaCost = EditorGUILayout.IntField("mana cost", sel.manaCost);
            if (sel.tier != SkillTier.Ultimate)   // an Ultimate is gated by its charge meter, not a cooldown
                sel.cooldown = EditorGUILayout.Slider("cooldown (s)", sel.cooldown, 0f, 30f);
            sel.castTime = EditorGUILayout.Slider(
                new GUIContent("cast time (s)", "Wind-up before the skill fires (0 = instant). DEX shortens it at runtime."),
                sel.castTime, 0f, 10f);
            sel.aim = (SkillAim)EditorGUILayout.EnumPopup(new GUIContent("aim", "How the cast point is picked."), sel.aim);
            using (new EditorGUI.DisabledScope(sel.aim == SkillAim.Self))
                sel.castRange = EditorGUILayout.Slider("cast range", sel.castRange, 1f, 30f);
        }

        // 4) PRESENTATION & HOTBAR — how it looks + where it sits on the bar.
        if (Section("Presentation & Hotbar", ref foPresent))
        {
            sel.vfx = (VfxSequence)EditorGUILayout.ObjectField(new GUIContent("vfx sequence",
                "The composed look from the Vfx Composer. Its exposed ticks appear in Gameplay below."), sel.vfx, typeof(VfxSequence), false);
            sel.repeatCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("repeat count",
                "Replay the WHOLE cycle N times per cast (e.g. 5 = a 5-meteor shower). Each cycle is an independent play " +
                "at the same cast point; per-fall hits still come from the VFX's per-component tickCount. 1 = play once."), sel.repeatCount));
            using (new EditorGUI.DisabledScope(sel.repeatCount <= 1))
                sel.repeatInterval = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("repeat interval (s)",
                    "Seconds between cycle starts. 0 = use the VFX sequence's RestartInterval (its loop-end marker)."), sel.repeatInterval));

            DrawCastCircle();
            DrawSkillType();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Hotbar", EditorStyles.miniBoldLabel);
            sel.tint = EditorGUILayout.ColorField(new GUIContent("tint", "Hotbar cell tint for this skill."), sel.tint);
        }

        // 5) GAMEPLAY — the tick bindings + effects (the actual mechanics). Has its own internal indentation.
        if (Section("Gameplay — Ticks & Effects", ref foGameplay))
            DrawTickBindings();

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(sel);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save")) { NormalizeAllAffinities(sel); EditorUtility.SetDirty(sel); AssetDatabase.SaveAssets(); SkillCatalog.Reload(); CaptureSnapshot(); }
        using (new EditorGUI.DisabledScope(!EditorUtility.IsDirty(sel)))
            if (GUILayout.Button("Discard changes")) DiscardSkillEdits();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
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
        EditorGUILayout.LabelField("Skill Release Animation", EditorStyles.miniBoldLabel);
        var all = SkillReleaseStances.All;
        int cur = 0;   // None
        if (sel.hasReleaseStance)
        {
            int i = System.Array.IndexOf(all, sel.releaseStance);
            cur = i >= 0 ? i + 1 : 0;
        }
        int next = EditorGUILayout.Popup(new GUIContent("release animation",
            "The animation the caster plays when this skill FIRES (after any cast wind-up). Bind each cue to a clip in " +
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
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = TickColor(t.id);   // tint the box so each tick owner is a distinct block at a glance
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            GUI.backgroundColor = prevBg;            // restore immediately so the inner controls aren't tinted
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
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.HelpBox($"Binding '{b.tickId}' has no matching tick in the sequence (renamed/removed?).", MessageType.Warning);
            if (GUILayout.Button("remove orphan", GUILayout.Width(120))) { sel.bindings.RemoveAt(i); i--; }
            EditorGUILayout.EndVertical();
        }
    }

    // A stable pastel per tick id — each tick's effects block reads as its own colored group (meteor_ball vs
    // fire_pillar, etc.) so multi-tick skills aren't a wall of identical grey boxes.
    static Color TickColor(string id)
    {
        if (string.IsNullOrEmpty(id)) return Color.white;
        int h = 0;
        for (int i = 0; i < id.Length; i++) h = h * 31 + id[i];
        float hue = ((h & 0x7fffffff) % 360) / 360f;
        return Color.HSVToRGB(hue, 0.30f, 1f);
    }

    void DrawBinding(SkillTickBinding b)
    {
        b.targets = (SkillTargets)EditorGUILayout.EnumFlagsField("  targets", b.targets);
        b.scope = (SkillScope)EditorGUILayout.EnumPopup(new GUIContent("  scope",
            "Area = everyone in the radius. Single = only the cast's target (always lands, radius ignored)."), b.scope);
        using (new EditorGUI.DisabledScope(b.scope == SkillScope.Single))
            b.radius = EditorGUILayout.Slider("  radius", b.radius <= 0f ? 1.5f : b.radius, 0.1f, 12f);

        if (b.effects == null) b.effects = new List<SkillEffect>();
        EditorGUILayout.LabelField($"  effects ({b.effects.Count})", EditorStyles.miniBoldLabel);
        int rm = -1;
        for (int e = 0; e < b.effects.Count; e++)
        {
            if (b.effects[e] == null) b.effects[e] = new SkillEffect();
            var ef = b.effects[e];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginHorizontal();
            ef.kind = (SkillEffectKind)EditorGUILayout.EnumPopup(ef.kind);
            if (GUILayout.Button("✕", GUILayout.Width(22))) rm = e;
            EditorGUILayout.EndHorizontal();
            switch (ef.kind)
            {
                case SkillEffectKind.Damage:
                    ef.damageType = (DamageType)EditorGUILayout.EnumPopup(new GUIContent("damage type",
                        "Physical (ATK vs armour) / Magic (MATK, unmissable, no crit) / Crit (pierces armour, always crits)."), ef.damageType);
                    ef.damageMult = EditorGUILayout.Slider(new GUIContent("base power ×",
                        "The skill's INHERENT strength (Ember ~1×, Meteor ~8×). Affinity picks WHICH stats scale it; this sets HOW BIG. Final = caster ATK/MATK × base power × affinity."), ef.damageMult <= 0f ? 1f : ef.damageMult, 0.1f, 8f);
                    ef.element = (Element)EditorGUILayout.EnumPopup("element", ef.element);
                    ef.hitCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("hits",
                        "Strikes per landing. Each hit is the TRIGGER for this binding's proc effects (a Status or Execute with a chance)."), ef.hitCount));
                    break;
                case SkillEffectKind.Heal:
                    ef.amount = EditorGUILayout.IntField("HP", ef.amount);
                    break;
                case SkillEffectKind.Status:
                    ef.ailment = (Ailment)EditorGUILayout.EnumPopup("ailment", ef.ailment);
                    ef.chance = EditorGUILayout.Slider(new GUIContent("chance",
                        "PROC chance rolled PER HIT of the binding's multi-hit Damage (e.g. a stun proc). 0 = not a proc — applies ONCE per landing. The 'affinity (chance)' channel below scales this."), ef.chance, 0f, 1f);
                    ef.seconds = EditorGUILayout.Slider("seconds", ef.seconds, 0.5f, 20f);
                    // magnitude is only meaningful for DoTs (dmg/tick), Slow (speed factor) and Blind (HIT loss).
                    // Pure CC (Stun/Freeze/Silence) is binary — no magnitude, so hide it to avoid the confusion.
                    if (ef.ailment != Ailment.Stun && ef.ailment != Ailment.Freeze && ef.ailment != Ailment.Silence)
                        ef.magnitude = EditorGUILayout.FloatField(new GUIContent("magnitude", "DoT dmg/tick, Slow factor (0..1), or Blind HIT loss."), ef.magnitude);
                    break;
                case SkillEffectKind.Execute:
                    ef.damageType = (DamageType)EditorGUILayout.EnumPopup(new GUIContent("damage type",
                        "Physical (ATK vs armour) / Magic (MATK, unmissable, no crit) / Crit (pierces armour, always crits)."), ef.damageType);
                    ef.chance = EditorGUILayout.Slider(new GUIContent("chance",
                        "PROC chance rolled PER HIT of the binding's multi-hit Damage. 1 = executes on every hit."), ef.chance, 0f, 1f);
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("execute power ×",
                        "Bonus damage = roll × this × (target's missing HP). Huge near death, ~0 at full HP."), ef.magnitude <= 0f ? 1f : ef.magnitude, 0.1f, 5f);
                    ef.element = (Element)EditorGUILayout.EnumPopup("element", ef.element);
                    break;
                case SkillEffectKind.Haste:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("speed ×", "Move-speed multiplier (>1 = faster)."), ef.magnitude <= 0f ? 1.3f : ef.magnitude, 0.2f, 3f);
                    ef.seconds = EditorGUILayout.Slider("seconds", ef.seconds, 0.5f, 20f);
                    break;
                case SkillEffectKind.Guard:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("damage cut", "Fraction of incoming damage prevented (0..1)."), Mathf.Clamp01(ef.magnitude), 0f, 1f);
                    ef.seconds = EditorGUILayout.Slider("seconds", ef.seconds, 0.5f, 20f);
                    break;
                case SkillEffectKind.Taunt:
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long mobs are forced onto the caster."), ef.seconds, 0.5f, 20f);
                    EditorGUILayout.HelpBox("Applies to mobs in the binding's radius — include Mobs in 'targets'.", MessageType.None);
                    break;
                case SkillEffectKind.Teleport:
                    EditorGUILayout.HelpBox("Blinks the caster to the cast point (Ground aim). No fields — destination is the aimed point.", MessageType.None);
                    break;
                case SkillEffectKind.Buff:
                    ef.buffStat = (BuffStat)EditorGUILayout.EnumPopup(new GUIContent("stat",
                        "Which self-stat is buffed. AMPLIFY buffs are FLAT — magnitude is NOT affinity-scaled, so a DPS can't out-buff via their offense."), ef.buffStat);
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("flat multiplier ×",
                        "1.3 = +30%. FLAT (no affinity). A support's value is UPTIME (duration ↓ scales off sustain) + the ramp, not a bigger number."), ef.magnitude <= 0f ? 1.3f : ef.magnitude, 1f, 3f);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds",
                        "Base duration — scales UP off sustain stats via the DURATION affinity below (high-MP support keeps it up longer)."), ef.seconds, 0.5f, 30f);
                    if (ef.buffStat == BuffStat.Damage)
                    {
                        EditorGUILayout.LabelField("  ramp — duration becomes magnitude (experimental)", EditorStyles.miniBoldLabel);
                        ef.rampDelay = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("  ramp delay (s)",
                            "Seconds before the ramp starts. Leave delay + perStep at 0 for a plain flat buff (no ramp)."), ef.rampDelay));
                        ef.rampPerStep = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("  ramp +×/step",
                            "Multiplier ADDED each interval (0.05 = +5% Atk per step). 0 = no ramp."), ef.rampPerStep));
                        using (new EditorGUI.DisabledScope(ef.rampPerStep <= 0f))
                        {
                            ef.rampInterval = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("  ramp interval (s)", "Seconds between ramp steps."), ef.rampInterval));
                            ef.rampCap = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("  ramp cap ×", "Ceiling for the ramped multiplier (1.5 = +50% max). 0 = uncapped."), ef.rampCap));
                        }
                    }
                    break;
                case SkillEffectKind.Regen:
                    ef.magnitude = EditorGUILayout.FloatField(new GUIContent("HP / tick", "Healed every 0.8s while active."), ef.magnitude);
                    ef.seconds = EditorGUILayout.Slider("seconds", ef.seconds, 0.5f, 30f);
                    break;
                case SkillEffectKind.Cleanse:
                    ef.amount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("debuffs removed", "How many active debuffs to strip."), ef.amount));
                    break;
                case SkillEffectKind.Vulnerability:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("+damage taken", "Extra damage fraction (0.3 = +30%)."), ef.magnitude, 0f, 2f);
                    ef.seconds = EditorGUILayout.Slider("seconds", ef.seconds, 0.5f, 20f);
                    break;
            }

            // Affinity channels, each labeled by WHAT it boosts. The first scales the effect's main output (damage,
            // heal amount, proc chance, slow amount, …); the second scales its duration. A channel that scales nothing
            // (a pure stun has no output number) is hidden. Weights are relative; empty = ×1 (no scaling).
            string affLabel = AffinityOutputLabel(ef);
            if (affLabel != null) DrawAffinity(affLabel, ref ef.affinity);
            if (HasDurationChannel(ef)) DrawAffinity("boosts DURATION", ref ef.durationAffinity);
            if (ef.kind == SkillEffectKind.Damage) DrawAffinity("boosts HITS COUNT", ref ef.hitCountAffinity);
            EditorGUILayout.EndVertical();
        }
        if (rm >= 0) b.effects.RemoveAt(rm);
        if (GUILayout.Button("  + effect", GUILayout.Width(90))) b.effects.Add(new SkillEffect());
    }

    // The label for an effect's main affinity channel — names exactly what it boosts. null = it scales nothing (a pure
    // stun has no output number), so the channel is hidden.
    static string AffinityOutputLabel(SkillEffect e)
    {
        switch (e.kind)
        {
            case SkillEffectKind.Damage:        return "boosts DAMAGE";
            case SkillEffectKind.Execute:       return "boosts EXECUTE DAMAGE";
            case SkillEffectKind.Heal:          return "boosts HEAL AMOUNT (sustain: HP/MP/Def)";
            case SkillEffectKind.Guard:         return "boosts DAMAGE REDUCTION (sustain: HP/Def)";
            // AMPLIFY buff magnitude is FLAT — no affinity channel (round-7 rule); only its DURATION scales (off sustain).
            case SkillEffectKind.Buff:          return null;
            case SkillEffectKind.Regen:         return "boosts HEAL / SEC (sustain: HP/MP)";
            case SkillEffectKind.Vulnerability: return "boosts DAMAGE TAKEN";
            case SkillEffectKind.Haste:         return "boosts MOVE SPEED";
            case SkillEffectKind.Status:
                if (e.chance > 0f) return "boosts PROC CHANCE";
                switch (e.ailment)
                {
                    case Ailment.Burn: case Ailment.Poison: case Ailment.Bleed: return "boosts DAMAGE / TICK";
                    case Ailment.Slow:  return "boosts SLOW AMOUNT";
                    case Ailment.Blind: return "boosts ACCURACY LOSS";
                    default:            return null;   // stun / freeze / silence: applied for sure, nothing to scale
                }
            default: return null;   // Taunt / Teleport / Cleanse have no scalable output
        }
    }

    // Which effects have a duration worth scaling (the second affinity channel).
    static bool HasDurationChannel(SkillEffect e)
    {
        switch (e.kind)
        {
            case SkillEffectKind.Status:
            case SkillEffectKind.Buff:
            case SkillEffectKind.Guard:
            case SkillEffectKind.Regen:
            case SkillEffectKind.Vulnerability:
            case SkillEffectKind.Haste:
            case SkillEffectKind.Taunt:
                return true;
            default:
                return false;
        }
    }

    // Per-channel lock state (editor-only, session-scoped) — deliberately NOT a field on StatWeight, so the serialized
    // affinity data stays pure runtime data with no dead 'locked' knob the game would ignore. Keyed by the list's
    // identity + the stat id, so it survives repaints/row reorder; it resets when the asset unloads (fine to author).
    static readonly HashSet<(int, int)> _lockedAffinity = new HashSet<(int, int)>();
    static int RtHash(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    static bool IsLocked(List<StatWeight> list, StatId stat) => _lockedAffinity.Contains((RtHash(list), (int)stat));
    static void SetLocked(List<StatWeight> list, StatId stat, bool locked)
    {
        var key = (RtHash(list), (int)stat);
        if (locked) _lockedAffinity.Add(key); else _lockedAffinity.Remove(key);
    }

    // One weighted stat-list (an effect's affinity channel): rows of (stat, lock, %-slider) + add/remove. Affinity is a
    // RATIO ("which stats scale this, in what mix") — the runtime normalizes by the sum, so only proportions matter.
    // We author it as a CONSERVED-TOTAL allocator: the channel always sums to exactly 100% BY CONSTRUCTION. Dragging
    // one stat's slider pulls the budget out of the OTHER stats proportionally, so the total can never be a stale/wrong
    // number. LOCK a stat to freeze it: it holds its value while you drag the others, and only the remaining unlocked
    // stats absorb the change (a dragged stat is clamped to the budget the locks leave free). The % is a read-only label.
    static void DrawAffinity(string label, ref List<StatWeight> list)
    {
        if (list == null) list = new List<StatWeight>();
        // Enforce the invariant on entry so legacy assets (or all-zero channels) start summing to 100.
        float sum = 0f;
        for (int i = 0; i < list.Count; i++) sum += Mathf.Max(0f, list[i].weight);
        if (list.Count > 0 && Mathf.Abs(sum - 100f) > 0.5f) NormalizeTo100(list);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(8);
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(190));
        if (list.Count > 0)
        {
            var prev = GUI.color;
            GUI.color = new Color(0.6f, 0.9f, 0.6f);
            EditorGUILayout.LabelField("(always = 100%)", GUILayout.Width(120));
            GUI.color = prev;
        }
        EditorGUILayout.EndHorizontal();

        int rm = -1, changed = -1;
        float changedVal = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            bool locked = IsLocked(list, w.stat);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);

            var newStat = (StatId)EditorGUILayout.EnumPopup(w.stat, GUILayout.Width(110));
            if (newStat != w.stat)
            {
                SetLocked(list, w.stat, false);          // carry the lock across a rename so it isn't silently dropped
                w.stat = newStat; list[i] = w;
                SetLocked(list, newStat, locked);
            }

            bool nowLocked = GUILayout.Toggle(locked,
                new GUIContent("🔒", "Lock: keep this stat fixed while you drag the others"), GUILayout.Width(30));
            if (nowLocked != locked) SetLocked(list, w.stat, nowLocked);

            using (new EditorGUI.DisabledScope(nowLocked))       // a locked stat's own slider is frozen too
            {
                EditorGUI.BeginChangeCheck();
                float sv = GUILayout.HorizontalSlider(w.weight, 0f, 100f, GUILayout.Width(110), GUILayout.Height(16));
                if (EditorGUI.EndChangeCheck()) { changed = i; changedVal = sv; }
            }

            GUILayout.Label($"{Mathf.RoundToInt(w.weight)}%", GUILayout.Width(38));
            if (GUILayout.Button("✕", GUILayout.Width(22))) rm = i;
            EditorGUILayout.EndHorizontal();
        }
        if (changed >= 0) Redistribute(list, changed, changedVal);
        if (rm >= 0) { SetLocked(list, list[rm].stat, false); list.RemoveAt(rm); RebalanceUnlocked(list); }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        // A new stat enters at 0% (sum stays 100); the first stat in an empty channel takes the whole 100%.
        if (GUILayout.Button("+ stat", GUILayout.Width(90)))
            list.Add(new StatWeight { stat = StatId.MAtk, weight = list.Count == 0 ? 100f : 0f });
        EditorGUILayout.EndHorizontal();
    }

    // Conserved-total allocator, lock-aware: set channel[changed] = newValue, hold every LOCKED stat fixed, and rescale
    // the OTHER unlocked stats so the whole list sums to exactly 100 again. The dragged stat is clamped to the budget
    // the locks leave free (100 − lockedSum), so it can never push an unlocked stat below 0 — "limit changeable sliders
    // to valid values" falls out of the clamp. Integer output (largest-remainder) so stored + displayed always match.
    static void Redistribute(List<StatWeight> list, int changed, float newValue)
    {
        int n = list.Count;
        if (n == 0) return;

        int lockedSum = 0, otherUnlocked = 0;
        float otherOld = 0f;
        for (int j = 0; j < n; j++)
        {
            if (j == changed) continue;
            if (IsLocked(list, list[j].stat)) lockedSum += Mathf.RoundToInt(Mathf.Max(0f, list[j].weight));
            else { otherUnlocked++; otherOld += Mathf.Max(0f, list[j].weight); }
        }
        int budget = Mathf.Clamp(100 - lockedSum, 0, 100);       // all the dragged + other-unlocked stats can hold
        int v = Mathf.Clamp(Mathf.RoundToInt(newValue), 0, budget);
        if (otherUnlocked == 0) v = budget;                       // nothing free to hold a remainder → dragged takes it all
        int othersBudget = budget - v;

        var targets = new float[n];
        var pinned = new bool[n];
        for (int j = 0; j < n; j++)
        {
            if (j == changed) { targets[j] = v; pinned[j] = true; }
            else if (IsLocked(list, list[j].stat)) { targets[j] = Mathf.RoundToInt(Mathf.Max(0f, list[j].weight)); pinned[j] = true; }
            else if (otherOld > 0f) targets[j] = Mathf.Max(0f, list[j].weight) / otherOld * othersBudget;
            else targets[j] = otherUnlocked > 0 ? othersBudget / (float)otherUnlocked : 0f;
        }
        int[] rounded = RoundSharesPinned(targets, pinned, othersBudget);
        for (int j = 0; j < n; j++) { var w = list[j]; w.weight = rounded[j]; list[j] = w; }
    }

    // Fill the UNLOCKED stats so the channel sums to 100 again while holding every locked stat fixed. Used after a stat
    // is removed. If nothing is unlocked (or all remaining are locked), falls back to a plain rescale-to-100.
    static void RebalanceUnlocked(List<StatWeight> list)
    {
        int n = list.Count;
        if (n == 0) return;
        int lockedSum = 0, unlocked = 0;
        float unlockedOld = 0f;
        for (int j = 0; j < n; j++)
        {
            if (IsLocked(list, list[j].stat)) lockedSum += Mathf.RoundToInt(Mathf.Max(0f, list[j].weight));
            else { unlocked++; unlockedOld += Mathf.Max(0f, list[j].weight); }
        }
        if (unlocked == 0) { NormalizeTo100(list); return; }
        int budget = Mathf.Clamp(100 - lockedSum, 0, 100);
        var targets = new float[n];
        var pinned = new bool[n];
        for (int j = 0; j < n; j++)
        {
            if (IsLocked(list, list[j].stat)) { targets[j] = Mathf.RoundToInt(Mathf.Max(0f, list[j].weight)); pinned[j] = true; }
            else targets[j] = unlockedOld > 0f ? Mathf.Max(0f, list[j].weight) / unlockedOld * budget : budget / (float)unlocked;
        }
        int[] rounded = RoundSharesPinned(targets, pinned, budget);
        for (int j = 0; j < n; j++) { var w = list[j]; w.weight = rounded[j]; list[j] = w; }
    }

    // Largest-remainder rounding: entries flagged `pinned` keep their exact (already-integer) target; the rest are
    // rounded so the UNPINNED entries sum to exactly `unpinnedTotal` (leftover units go to the largest fractions).
    static int[] RoundSharesPinned(float[] targets, bool[] pinned, int unpinnedTotal)
    {
        int n = targets.Length;
        var result = new int[n];
        var frac = new float[n];
        int floored = 0;
        for (int j = 0; j < n; j++)
        {
            if (pinned[j]) { result[j] = Mathf.RoundToInt(targets[j]); continue; }
            int f = Mathf.FloorToInt(targets[j]);
            result[j] = f; frac[j] = targets[j] - f; floored += f;
        }
        int remainder = unpinnedTotal - floored;
        for (int r = 0; r < remainder; r++)
        {
            int best = -1; float bestFrac = -1f;
            for (int j = 0; j < n; j++)
            {
                if (pinned[j]) continue;
                if (frac[j] > bestFrac) { bestFrac = frac[j]; best = j; }
            }
            if (best < 0) break;
            result[best]++; frac[best] = -1f;
        }
        return result;
    }

    // Rescale a channel's weights so they sum to exactly 100 (integers, largest-remainder). Used on entry for legacy
    // assets, after a stat is removed, and on Save. All-zero channels get an even split.
    static void NormalizeTo100(List<StatWeight> list)
    {
        if (list == null || list.Count == 0) return;
        int n = list.Count;
        float sum = 0f;
        for (int i = 0; i < n; i++) sum += Mathf.Max(0f, list[i].weight);
        var targets = new float[n];
        for (int i = 0; i < n; i++)
            targets[i] = sum > 0f ? Mathf.Max(0f, list[i].weight) / sum * 100f : 100f / n;
        // No pinned index here — largest-remainder over all n to hit 100 exactly.
        int[] rounded = RoundSharesAll(targets, 100);
        for (int i = 0; i < n; i++) { var w = list[i]; w.weight = rounded[i]; list[i] = w; }
    }

    // Largest-remainder rounding over ALL entries to sum exactly `total` (no pinned index).
    static int[] RoundSharesAll(float[] targets, int total)
    {
        int n = targets.Length;
        var result = new int[n];
        var frac = new float[n];
        int floored = 0;
        for (int j = 0; j < n; j++) { int f = Mathf.FloorToInt(targets[j]); result[j] = f; frac[j] = targets[j] - f; floored += f; }
        int remainder = total - floored;
        for (int r = 0; r < remainder; r++)
        {
            int best = -1; float bestFrac = -1f;
            for (int j = 0; j < n; j++) if (frac[j] > bestFrac) { bestFrac = frac[j]; best = j; }
            if (best < 0) break;
            result[best]++; frac[best] = -1f;
        }
        return result;
    }

    // Normalize every affinity channel on the skill (called on Save) so assets always store clean percentages.
    static void NormalizeAllAffinities(SkillDef def)
    {
        if (def == null || def.bindings == null) return;
        foreach (var b in def.bindings)
        {
            if (b?.effects == null) continue;
            foreach (var e in b.effects)
            {
                if (e == null) continue;
                NormalizeTo100(e.affinity);
                NormalizeTo100(e.durationAffinity);
                NormalizeTo100(e.hitCountAffinity);
            }
        }
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
