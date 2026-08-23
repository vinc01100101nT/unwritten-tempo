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

    /// <summary>The first free id starting from <paramref name="wanted"/> (<c>skill.new</c> → <c>skill.new_2</c> → …).
    /// Checks BOTH the authored ids and the folder, because the file name mirrors the id — so a free id is also a free
    /// file name, and a new/duplicated skill can never be born already colliding.</summary>
    static string UniqueId(string wanted)
    {
        if (string.IsNullOrEmpty(wanted)) wanted = "skill.new";
        for (int n = 1; ; n++)
        {
            string candidate = n == 1 ? wanted : $"{wanted}_{n}";
            if (!IdTaken(candidate) && !System.IO.File.Exists($"{Dir}/{candidate}.asset")) return candidate;
        }
    }

    static bool IdTaken(string id)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:SkillDef"))
        {
            var a = AssetDatabase.LoadAssetAtPath<SkillDef>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null && a.id == id) return true;
        }
        return false;
    }

    /// <summary>The editable tail of an id — everything after the last dot, so both <c>skill.meteor</c> and
    /// <c>skill.sword.deathblow</c> hand back the part the author actually names.</summary>
    static string IdTail(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        int dot = id.LastIndexOf('.');
        return dot >= 0 && dot < id.Length - 1 ? id.Substring(dot + 1) : id;
    }

    /// <summary>Build the stable id from its two authored parts: <c>skill.&lt;weapon&gt;.&lt;name&gt;</c>, or
    /// <c>skill.&lt;name&gt;</c> when the skill belongs to no weapon pool.</summary>
    static string ComposeId(WeaponType weapon, string tail)
    {
        tail = (tail ?? "").Trim().ToLowerInvariant().Replace(' ', '_');
        if (string.IsNullOrEmpty(tail)) tail = "new";
        return weapon == WeaponType.None
            ? $"skill.{tail}"
            : $"skill.{weapon.ToString().ToLowerInvariant()}.{tail}";
    }

    // Ultimates first, then pressed actives, then pure passives — slotOrder breaks ties within a rank. Re-applied
    // every repaint (never only on load), so flipping a skill's tier or adding its OnCast entry moves the row at once.
    static int ListRank(SkillDef s) => s.IsUltimate ? 0 : s.IsPassive ? 2 : 1;

    void Refresh()
    {
        all.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:SkillDef"))
        {
            var a = AssetDatabase.LoadAssetAtPath<SkillDef>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) all.Add(a);
        }
        if (sel == null && all.Count > 0) sel = all[0];
        // A rescan hands back FRESH objects, so the cached groups are holding dead references even when every id and
        // order is identical. Force the regroup rather than let the fingerprint conclude nothing changed.
        groupsFingerprint = -1;
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
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(20));
            if (GUILayout.Button("▶", GUILayout.MaxWidth(20), GUILayout.Height(40))) listCollapsed = false;
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.BeginVertical(GUILayout.MaxWidth(listWidth), GUILayout.MinWidth(0f), GUILayout.MaxWidth(listWidth));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel, GUILayout.MinWidth(0f));
        if (GUILayout.Button("◀", GUILayout.MaxWidth(24))) listCollapsed = true;
        EditorGUILayout.EndHorizontal();
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        RebuildGroupsIfChanged();
        foreach (var s in ungrouped) DrawRow(s, 0f, Color.clear);
        foreach (var g in groupNames)
        {
            var list = grouped[g];
            var hue = GroupHue(g);
            if (!groupOpen.TryGetValue(g, out bool open)) open = true;

            var headerRect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, Tint(hue, 0.30f));
            groupOpen[g] = open = EditorGUI.Foldout(headerRect, open, $"{g}  ({list.Count})", true);

            var countRect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight - 3f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(countRect, Tint(hue, 0.30f));
            countRect.xMin += 14f;
            EditorGUI.LabelField(countRect, TierCounts(list), EditorStyles.miniLabel);

            if (!open) continue;
            int prevRank = -1;
            foreach (var s in list)
            {
                int rank = ListRank(s);
                if (prevRank >= 0 && rank != prevRank) DrawRankSeparator();
                prevRank = rank;
                DrawRow(s, 12f, Tint(hue, 0.14f));
            }
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

    // Drawn where the sorted list crosses from one rank to the next, so the three runs read as separate blocks.
    static void DrawRankSeparator()
    {
        var r = GUILayoutUtility.GetRect(0f, 5f, GUILayout.ExpandWidth(true));
        r.xMin += 12f;
        r.y += 2f;
        r.height = 1f;
        EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.22f));
    }

    void DrawRow(SkillDef s, float indent, Color band)
    {
        if (s == null) return;
        var r = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
        r.xMin += indent;
        if (band.a > 0f) EditorGUI.DrawRect(r, band);
        if (GUI.Button(r, $"{s.displayName}\n{s.id}", RowStyle(s == sel, s.published))) sel = s;
    }

    // The list doubles as the publish overview: a PUBLISHED skill (one a weapon can roll) reads BOLD and full-
    // strength, an unpublished one is dimmed — so "what's live" is legible at a glance without opening each skill.
    // Four cached styles (selected × published); built lazily because EditorStyles is only valid inside OnGUI.
    GUIStyle[] rowStyles;

    GUIStyle RowStyle(bool selected, bool published)
    {
        rowStyles ??= new GUIStyle[4];
        int i = (selected ? 2 : 0) + (published ? 1 : 0);
        if (rowStyles[i] == null)
        {
            var st = new GUIStyle(selected ? EditorStyles.helpBox : EditorStyles.label)
            {
                fontStyle = published ? FontStyle.Bold : FontStyle.Normal,
            };
            var c = st.normal.textColor;
            if (!published) c.a *= 0.45f;
            st.normal.textColor = st.onNormal.textColor = st.hover.textColor = c;
            rowStyles[i] = st;
        }
        return rowStyles[i];
    }

    // Each group gets its own colored band so a family is recognizable at a glance. The hue comes from a stable hash
    // of the group NAME (not its position), so a group keeps its color as skills are added, renamed, or reordered —
    // and a brand-new group picks one up automatically with nothing to configure.
    static float GroupHue(string name)
    {
        int h = 17;
        unchecked { foreach (char c in name) h = h * 31 + c; }
        return (h & 0x7fffffff) % 360 / 360f;
    }

    // One hue per group; brightness carries the tier so a row's rank reads without moving the eye to any text.
    static Color Tint(float hue, float alpha)
    {
        var c = Color.HSVToRGB(hue, 0.65f, 0.9f);
        c.a = alpha;
        return c;
    }

    // The group's make-up in the same order the rows are sorted, so the header reads as a summary of what's below it.
    static string TierCounts(List<SkillDef> list)
    {
        int ult = 0, active = 0, passive = 0;
        foreach (var s in list)
        {
            switch (ListRank(s)) { case 0: ult++; break; case 1: active++; break; default: passive++; break; }
        }
        return $"{ult} ULT · {active} Active · {passive} Passive";
    }

    // Grouping for the left list: an id with three or more dot segments (skill.<group>.<name>) files under a foldout
    // named for the MIDDLE segment; a two-segment id (skill.<name>) stays flat above the groups.
    //
    // Rebuilt WHENEVER THE ANSWER WOULD DIFFER, and not otherwise — checked by fingerprinting the only four things
    // the order and grouping depend on. That keeps the original no-bookkeeping property (editing an id still moves
    // its row immediately, with no call site to remember) while dropping a full sort + regroup + allocation from
    // every redraw the window sees. Unity redraws on mouse movement, so "every redraw" meant continuously.
    long groupsFingerprint = -1;

    readonly List<SkillDef> ungrouped = new List<SkillDef>();
    readonly List<string> groupNames = new List<string>();
    readonly Dictionary<string, List<SkillDef>> grouped = new Dictionary<string, List<SkillDef>>();
    readonly Dictionary<string, bool> groupOpen = new Dictionary<string, bool>();

    static string GroupOf(SkillDef s)
    {
        string id = s.id;
        if (string.IsNullOrEmpty(id)) return null;
        int a = id.IndexOf('.');
        if (a < 0) return null;
        int b = id.IndexOf('.', a + 1);
        return b > a + 1 ? id.Substring(a + 1, b - a - 1) : null;
    }

    /// <summary>Regroup only if the result would actually change. The fingerprint covers exactly the inputs
    /// <see cref="RebuildGroups"/> reads — how many skills there are, and each one's id (grouping + the tiebreak),
    /// slot order, and rank. Anything else an author edits leaves the list alone, which is correct.</summary>
    void RebuildGroupsIfChanged()
    {
        long fp = 17;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            fp = fp * 31 + (s == null ? 0 : s.id != null ? s.id.GetHashCode() : 1);
            if (s != null) fp = fp * 31 + s.slotOrder * 7 + ListRank(s);
        }
        fp = fp * 31 + all.Count;
        if (fp == groupsFingerprint) return;
        groupsFingerprint = fp;
        RebuildGroups();
    }

    void RebuildGroups()
    {
        ungrouped.Clear();
        groupNames.Clear();
        foreach (var list in grouped.Values) list.Clear();
        // The comparator must be TOTAL — it has to separate every pair, not just most of them. This runs inside the
        // draw code, so it re-sorts on every event the window sees, mouse movement included; the sort underneath is
        // not a stable one, so any two skills it calls equal are free to swap places on each pass. With rank and
        // slotOrder alone, 20 of the 26 skills tied, and the list visibly reshuffled under the cursor. The id is the
        // last word because it is unique and never changes.
        all.Sort((x, y) =>
        {
            if (x == null || y == null) return (x == null ? 1 : 0).CompareTo(y == null ? 1 : 0);
            int r = ListRank(x).CompareTo(ListRank(y));
            if (r != 0) return r;
            int o = x.slotOrder.CompareTo(y.slotOrder);
            return o != 0 ? o : string.CompareOrdinal(x.id ?? "", y.id ?? "");
        });
        foreach (var s in all)
        {
            if (s == null) continue;
            var g = GroupOf(s);
            if (g == null) { ungrouped.Add(s); continue; }
            if (!grouped.TryGetValue(g, out var list)) grouped[g] = list = new List<SkillDef>();
            if (list.Count == 0) groupNames.Add(g);
            list.Add(s);
        }
        groupNames.Sort(string.CompareOrdinal);
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
        s.id = UniqueId("skill.new");
        s.displayName = "New Skill";
        string path = $"{Dir}/{s.id}.asset";   // never GenerateUniqueAssetPath: it would suffix the FILE and leave the id behind
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
        string copyId = UniqueId(s.id);   // the copy gets its own id FIRST, so its file can be named after it
        string path = $"{Dir}/{copyId}.asset";
        if (!AssetDatabase.CopyAsset(src, path)) return;
        var copy = AssetDatabase.LoadAssetAtPath<SkillDef>(path);
        if (copy != null) { copy.id = copyId; EditorUtility.SetDirty(copy); }
        AssetDatabase.SaveAssets();
        Refresh();
        sel = copy;
    }

    void DeleteAsset(SkillDef s)
    {
        if (!EditorUtility.DisplayDialog("Delete skill", $"Delete '{s.displayName}'?", "Delete", "Cancel")) return;
        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(s));
        sel = null;
        Refresh();
    }

    // ---- right: editor ------------------------------------------------------

    // Collapsible section headers, in the order an author actually thinks: what it IS → how it's classified → WHEN it
    // fires → what it COSTS to fire → WHAT IT DOES → how many times → how it looks. Repeats sits directly under
    // Gameplay because it multiplies exactly what that section authored. State is static so a section stays open
    // across repaints.
    // Every group starts OPEN. A group that hides itself hides fields the author never chose to hide, and a skill you
    // are opening for the first time shouldn't keep half of itself out of sight. Closing one is the author's decision.
    static bool foId = true, foClass = true, foTrigger = true, foCost = true, foPresent = true, foGameplay = true,
                foRepeat = true;
    // A section header is deliberately louder than anything inside it: a full-width rule above, then the title in a
    // larger bold face. The rule is what makes a collapsed stack of sections read as separate groups rather than one
    // list of similar-looking rows.
    // Green, and ONLY the group headers are green — nothing else in the window uses it, so the colour means exactly
    // one thing: "a new group starts here". Muted rather than pure green so it reads as a heading rather than as a
    // success message, and it stays clear of the blue Unity paints on a hovered/focused control.
    static Color SectionGreen => EditorGUIUtility.isProSkin
        ? new Color(0.44f, 0.82f, 0.47f)
        : new Color(0.13f, 0.44f, 0.18f);

    static GUIStyle _sectionTitle;
    static GUIStyle SectionTitle
    {
        get
        {
            if (_sectionTitle != null) return _sectionTitle;
            _sectionTitle = new GUIStyle(EditorStyles.foldout)
            { fontSize = 14, fontStyle = FontStyle.Bold, fixedHeight = 0f };
            // Every state, not just the resting one: otherwise the title flips to Unity's blue the moment the mouse
            // crosses it, and the colour would stop meaning "group" exactly when you are pointing at one.
            var g = SectionGreen;
            foreach (var st in new[] { _sectionTitle.normal, _sectionTitle.onNormal, _sectionTitle.hover,
                                       _sectionTitle.onHover, _sectionTitle.focused, _sectionTitle.onFocused,
                                       _sectionTitle.active, _sectionTitle.onActive })
                st.textColor = g;
            return _sectionTitle;
        }
    }

    static bool Section(string title, ref bool open)
    {
        EditorGUILayout.Space(8);
        // The separator: height only, never a width demand, so a narrow docked window still can't grow a horizontal
        // scrollbar (see the editor-window rule — MaxWidth only, and MinWidth(0) states "no floor").
        var rule = EditorGUILayout.GetControlRect(false, 4f, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rule, SectionGreen);
        EditorGUILayout.Space(4);

        var header = EditorGUILayout.GetControlRect(false, 22f, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
        open = EditorGUI.Foldout(header, open, title, true, SectionTitle);
        EditorGUILayout.Space(2);
        return open;
    }

    // Per-effect fold state, keyed by the effect object and living only for this editor session — deliberately NOT a
    // field on the effect: whether a panel happens to be open is the window's business, and a serialized one would be
    // a knob in the saved skill that the game ignores.
    static readonly Dictionary<object, bool> folds = new Dictionary<object, bool>();

    /// <summary>A sub-fold inside one item, CLOSED until the author opens it — for the settings an effect only
    /// sometimes needs, so its identity (the verb and its numbers) isn't buried under them.</summary>
    static bool Foldout(object owner, string title)
    {
        folds.TryGetValue(owner, out bool open);
        bool now = EditorGUILayout.Foldout(open, title, true);
        if (now != open) folds[owner] = now;
        return now;
    }

    /// <summary>What the selected trigger ACTUALLY counts as — the same facts documented on each SkillTrigger member,
    /// worded for an author. Every line states the edge cases (miss / absorbed / parried) rather than leaving them to
    /// be discovered in play, and says which unit the skill is handed to aim at.</summary>
    static string TriggerHelp(SkillTrigger t)
    {
        switch (t)
        {
            case SkillTrigger.OnCast:
                return "The player PRESSES it. Takes a hotbar slot, and is the only trigger that pays the MP and " +
                       "cooldown below — every other moment fires the skill for free.";
            case SkillTrigger.OnSkillCastComplete:
                return "The skill you pick below finished its wind-up WITHOUT being interrupted.\n\n" +
                       "Only counts when the PLAYER pressed that skill. If that same skill self-fires from one of its " +
                       "own other triggers, this does NOT fire — which is what stops a pair like Lock Blades and Lunge " +
                       "from endlessly refilling each other. Nothing was necessarily hit, so no target is handed over.";
            case SkillTrigger.OnBasicAttackDealt:
                return "A BASIC ATTACK YOU SWUNG DEALT DAMAGE. Aims at the mob you hit.\n\n" +
                       "A MISS does not fire it. Skill strikes do not fire it — only real basic attacks. This is the " +
                       "moment Momentum is built from, which is why attack speed is the lever on any pile made this way.";
            case SkillTrigger.OnKill:
                return "A MOB DIED. Aims at the dying mob (announced before the corpse is cleaned up).\n\n" +
                       "Below you can name WHICH skill's killing blow counts — leave it on 'any skill' for every kill, " +
                       "including basic attacks and damage-over-time.";
            case SkillTrigger.OnLowHP:
                return "YOUR HP DROPPED TO 30% OR BELOW. No target is handed over.\n\n" +
                       "Fires only on the way DOWN, and only if the hit left you alive — so it re-arms by itself after " +
                       "any heal. Poison, bleed and burn ticks do NOT fire it.";
            case SkillTrigger.OnDamageDealt:
                return "YOU DEALT DAMAGE THAT ACTUALLY LANDED. Aims at the victim.\n\n" +
                       "Basics AND skills both fire it, once per damage instance. It does NOT fire on a miss, on a hit " +
                       "a Shield fully absorbed, or on a hit a Brace window held — the number checked is what was left " +
                       "AFTER the victim's mitigation. Exact mirror of OnDamageReceived.";
            case SkillTrigger.OnParry:
                return "YOU PARRIED — a charge was spent erasing an incoming hit. Aims at the attacker you parried.\n\n" +
                       "A parried hit never produces a damage number, so it fires NEITHER damage moment. That is why " +
                       "this is its own trigger: it marks the hit you took nothing from.";
            case SkillTrigger.OnBasicAttackStart:
                return "YOU ARE ABOUT TO SWING — the intent, not the result. Aims at the intended target.\n\n" +
                       "The ONLY moment here that doesn't need damage: it fires even if the swing goes on to miss. It " +
                       "runs BEFORE reach is judged, so a gap-closer can close the distance and the same swing still " +
                       "lands. Fires at most once per swing interval.";
            case SkillTrigger.OnDamageReceived:
                return "DAMAGE LANDED ON YOU. Aims at the attacker, so a counter can strike back.\n\n" +
                       "Fires only when a hit truly cost you HP. A parried hit, a hit a Shield fully absorbed, and a " +
                       "hit held by a Brace window all fire NOTHING. Poison, bleed and burn ticks don't fire it either. " +
                       "Exact mirror of OnDamageDealt — same rule on both sides, so they can't drift apart.";
            default:
                return "";
        }
    }

    /// <summary>Draw ONE trigger entry — the moment, plus the state gate that firing through this moment must satisfy.
    /// Returns true if the author asked to delete it. The gate is drawn per entry, not per skill, because the two
    /// halves of a dual-trigger skill need different answers (Lunge on OnCast requires no charge; the same Lunge on
    /// OnBasicAttackStart requires and spends one).</summary>
    bool DrawTriggerEntry(SkillTriggerEntry e, int index)
    {
        bool remove = false;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // The trigger IS the entry's name — no invented category label above it.
                // The chosen trigger's exact meaning — does a miss count, an absorbed hit, a parry — rides the TOOLTIP
                // rather than a paragraph under the row: you only need it while choosing, and on screen it pushed every
                // field below it out of view.
                e.trigger = (SkillTrigger)EditorGUILayout.EnumPopup(new GUIContent("trigger",
                    "WHEN this skill fires. OnCast = the player presses it (the only one that pays the MP + cooldown " +
                    "below); every other moment fires it free.\n\n" + TriggerHelp(e.trigger)), e.trigger);
                // The last entry can't be removed: a skill must start SOME way, and an empty list would silently read
                // as a plain OnCast skill — a state the author never chose.
                using (new EditorGUI.DisabledScope(sel.triggers.Count <= 1))
                    if (GUILayout.Button("Remove", GUILayout.MaxWidth(70f))) remove = true;
            }

            if (e.trigger == SkillTrigger.OnSkillCastComplete)
                e.triggerSkillId = DrawSkillPicker("  on cast complete of",
                    "The ACTIVE skill whose successful (uninterrupted) PLAYER cast fires this one. Only a press " +
                    "announces, so a skill auto-fired by one of its own triggers can't set this off. Lists saved " +
                    "skills under Resources/Skills; a skill can't be fired by itself.", e.triggerSkillId);

            // A kill can name its cause. Blank = any kill, which is what every OnKill passive meant before this existed.
            // The list is only skills that can actually deal damage, read live from their effects — a skill that can't
            // hurt anything can never be the reason something died.
            if (e.trigger == SkillTrigger.OnKill)
                e.triggerSkillId = DrawSkillPicker("  killed by",
                    "WHICH skill's killing blow fires this. Any kill = leave it on 'any skill' (a basic attack counts). " +
                    "Pick one and only kills landed by that skill fire this — the way a passive belongs to one skill's " +
                    "kills instead of every kill in the fight. Only damaging skills are listed.",
                    e.triggerSkillId, includePassives: true, allowSelf: true,
                    damagingOnly: true, noneLabel: "any skill");

            for (int i = 0; i < sel.triggers.Count; i++)
                if (i != index && sel.triggers[i] != null && sel.triggers[i].trigger == e.trigger
                    && (e.trigger != SkillTrigger.OnSkillCastComplete || sel.triggers[i].triggerSkillId == e.triggerSkillId))
                {
                    EditorGUILayout.HelpBox("Another entry already answers this moment — only the FIRST will ever " +
                        "fire. Two entries can only differ in their gate, and one skill body can't honour both.",
                        MessageType.Warning);
                    break;
                }

            EditorGUILayout.Space(2);
            e.requiresState = EditorGUILayout.Toggle(new GUIContent("requires a state",
                "ON = firing through THIS moment READS a caster state below — it can require stacks, spend them, and " +
                "be paid off by them."), e.requiresState);
            if (!e.requiresState) return remove;

            EditorGUI.indentLevel++;
            // WHOSE stacks, not which state: a pile belongs to the skill that granted it, so a finisher can only
            // ever cash its own partner's (Deathblow eats Momentum, never Lock Blades' charges — both buff attack).
            // THIS SKILL ITSELF is a legal answer: a skill whose cast grants its own charges and whose repeats spend
            // them is self-contained. Only the "fired by" question above excludes self.
            e.requireSkillId = DrawSkillPicker("  stacks from",
                "WHOSE stacks this reads — the skill that GRANTED them. A pile belongs to the skill that created it, " +
                "not to what it does, so two skills that both buff attack keep separate piles. THIS SKILL is a valid " +
                "answer: a skill can grant its own charges and spend them over its own repeats. Passives are listed " +
                "too, since most piles come from one.\n\n" +
                "The one entry that is NOT a skill is the run of consecutive hits: nothing grants it, the fight " +
                "counts it, and it is never spent.",
                e.requireSkillId, includePassives: true, allowSelf: true,
                runOfHits: StateGate.MomentHasLiveRunOfHits(e.trigger));
            // The run of hits stops here: there is no pile to name (nothing grants it) and nothing to spend (the fight
            // ends it, not a skill), so those rows are not drawn rather than shown doing nothing.
            if (StateGate.ReadsRunOfHits(e))
            {
                // A gate SAVED against the run, whose moment was later changed to one that doesn't offer it, keeps
                // reading — the picker never silently repoints anyone. But it is now reading a number that means
                // nothing at that moment, so the window says so instead of letting it sit and look deliberate.
                if (!StateGate.MomentHasLiveRunOfHits(e.trigger))
                    EditorGUILayout.HelpBox("This moment has no live run of hits — it fires when nothing of yours just " +
                        "landed, so the count read here is stale, over, or about something else entirely. Move this to " +
                        "'On Basic Attack Dealt' or 'On Damage Dealt', or read a real pile instead.", MessageType.Warning);
                e.requireMinStacks = EditorGUILayout.IntSlider(new GUIContent("  min hits in a row",
                    "How many hits in a row on the SAME enemy this needs to fire. The run restarts at 1 the moment a " +
                    "hit lands on somebody else, and empties whenever a hit deals NOTHING — a miss, a parry, a full " +
                    "shield absorb, an immune element.\n\n" +
                    "0 = no gate: it fires anyway and simply reads however long the run happens to be."),
                    Mathf.Max(0, e.requireMinStacks), 0, UnitStates.MaxStacks);
                EditorGUILayout.HelpBox("Counted automatically for every unit — there is nothing to author on the " +
                    "granting side, and nothing to wire wrong. Reading it never spends it.", MessageType.None);
                EditorGUI.indentLevel--;
                return remove;
            }
            if (string.IsNullOrEmpty(e.requireSkillId))
            {
                EditorGUILayout.HelpBox("Pick the skill whose stacks this reads — without it there's no pile to gate " +
                    "on, cash, or be paid by.", MessageType.Warning);
                EditorGUI.indentLevel--;
                return remove;   // WHICH pile is meaningless until WHOSE is answered
            }
            if (!DrawGrantedStatePicker(e)) { EditorGUI.indentLevel--; return remove; }
            e.requireMinStacks = EditorGUILayout.IntSlider(new GUIContent("  min stacks",
                "How many stacks must be present to fire ('detonate at 3'). 1 = just needs the state. " +
                "0 = NO GATE: it fires even on an empty pile and simply cashes whatever is there, getting no payoff " +
                "— Deathblow, which must stay usable on a Sword that never rolled Momentum."),
                Mathf.Max(0, e.requireMinStacks), 0, UnitStates.MaxStacks);
            e.consumeAllStacks = EditorGUILayout.Toggle(new GUIContent("  consume ALL",
                "Spend every stack on use — a finisher cashing a whole pile.\n\nThis is the pile's COST, not its " +
                "payoff: what the pile is WORTH is set on each effect below, so one cast can pay several effects out " +
                "of the same pile. The pile is read when the cast starts and spent only after the effects have " +
                "computed, so a finisher sees the whole thing it is about to cash."), e.consumeAllStacks);
            if (!e.consumeAllStacks)
                e.consumeStacks = EditorGUILayout.IntSlider(new GUIContent("  consume",
                    "How many stacks this use spends. 0 = spends nothing (a pure 'while X' condition)."),
                    Mathf.Max(0, e.consumeStacks), 0, UnitStates.MaxStacks);

            EditorGUI.indentLevel--;
        }
        return remove;
    }

    /// <summary>The "which pile" half of the gate, listing only what the chosen granter really stacks. Returns false
    /// when the granter stacks nothing — there is then no pile to point at, and saying so is more useful than showing
    /// a dropdown of states that will never exist.</summary>
    static bool DrawGrantedStatePicker(SkillTriggerEntry e)
        => DrawPilePicker(e.requireSkillId, ref e.requireStateId, ref e.requireStateIdSet,
                          "reads", "this gate counts nothing and spends nothing");

    /// <summary>WHICH skill's pile a modifier edits, plus how much it changes — the whole authored surface of both
    /// Modifier effects. The skill and the pile are asked in the same two-part way the stack gate asks them (same
    /// drawer), because it is the same question about the same thing.</summary>
    void DrawModifierTarget(SkillEffect ef)
    {
        ef.modifierSkillId = DrawSkillPicker("  of skill",
            "WHOSE pile this edits. A pile belongs to the skill that granted it, so this names that skill — including " +
            "THIS one, for a skill that lengthens itself. Passives are listed too, since most piles come from one.",
            ef.modifierSkillId, includePassives: true, allowSelf: true);
        if (string.IsNullOrEmpty(ef.modifierSkillId))
        {
            EditorGUILayout.HelpBox("Pick the skill whose pile this edits — without it there is nothing to change.",
                                    MessageType.Warning);
            return;
        }
        if (!DrawPilePicker(ef.modifierSkillId, ref ef.modifierStateId, ref ef.modifierStateIdSet,
                            "edits", "this changes nothing")) return;

        if (ef.kind == SkillEffectKind.ExtendDuration)
            NumberRow("  seconds added", "Added to EVERY stack of that pile, so the front of the pile stops expiring on the old clock.\n\n" + "Lengthens a pile that is ALREADY running — on a lapsed pile there is nothing to lengthen, so this " + "does nothing.", ref ef.modifierSeconds, ef.useStacks);
        else
            NumberRow("  stacks added", "How many stacks to add. Each is a copy of that pile's newest one — same worth, same deadline — because " + "what one of its charges is worth is the granting skill's business, not this effect's.\n\n" + "Works whether or not that skill is running: if its pile is up the stacks join it; if not, the pile " + "starts with exactly the charges that skill's own effect authors.\n\n" + "0 is a real answer: it means this effect adds nothing ON ITS OWN, and hands over only what the cast's " + "pile buys it above. That is how 'one Momentum per three hits in a row' is written.", ref ef.modifierStacks, ef.useStacks, whole: true);

    }

    /// <summary>The "which pile" question, wherever it is asked — the stack gate asks it about the pile it CASHES, a
    /// modifier effect about the pile it EDITS. One drawer for both, so the unset rule and the "only what that skill
    /// really stacks" list can never be enforced in one place and forgotten in the other.</summary>
    static readonly List<StateId> grantedScratch = new List<StateId>(4);

    static bool DrawPilePicker(string granterId, ref StateId stateId, ref bool stateIdSet,
                               string verb, string consequence)
    {
        var granter = SkillCatalog.Get(granterId);
        SkillLibrarySweeps.CollectGrantedStates(granter, grantedScratch);
        if (grantedScratch.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"{(granter != null ? granter.displayName : granterId)} doesn't stack anything, so it leaves no " +
                $"pile to {verb}. Turn on 'stacking' for one of ITS effects first, or pick a different skill.",
                MessageType.Warning);
            return false;
        }

        // An UNSET gate is drawn as unset — never quietly resolved to the first entry. Showing a plausible pile for a
        // choice nobody made is what made a broken gate survive a day of play: the window said one thing, the saved
        // skill said nothing, and the two only disagreed once Unity was restarted. A blank must LOOK blank.
        bool set = stateIdSet && grantedScratch.Contains(stateId);
        var labels = new string[grantedScratch.Count + (set ? 0 : 1)];
        int off = set ? 0 : 1;
        if (!set) labels[0] = "— pick one —";
        for (int i = 0; i < grantedScratch.Count; i++) labels[i + off] = grantedScratch[i].ToString();

        int idx = set ? grantedScratch.IndexOf(stateId) : 0;
        int picked = EditorGUILayout.Popup(new GUIContent("  which pile",
            $"WHICH of that skill's stacking states this {verb}. A skill can grant more than one — charges to spend and " +
            "parry charges to survive on — and the two must never be counted together or spent for each other. Only " +
            "the piles that skill really stacks are listed."), idx, labels);
        if (picked != idx && (set || picked > 0))
        {
            stateId = grantedScratch[picked - off];
            stateIdSet = true;
        }

        if (!stateIdSet)
            EditorGUILayout.HelpBox($"No pile picked — {consequence}. Pick one, then save.", MessageType.Error);
        return true;
    }

    /// <summary>Can this skill actually deal damage — i.e. can it ever land a killing blow? Read from its effects, so
    /// the list re-derives itself the moment a skill gains or loses its damage; nothing is tagged by hand.</summary>
    static bool Deals(SkillDef d)
    {
        if (d?.bindings == null) return false;
        for (int b = 0; b < d.bindings.Count; b++)
        {
            var fx = d.bindings[b]?.effects;
            if (fx == null) continue;
            for (int i = 0; i < fx.Count; i++)
                if (fx[i] != null && (fx[i].kind == SkillEffectKind.Damage || fx[i].kind == SkillEffectKind.Execute))
                    return true;
        }
        return false;
    }

    // Dropdown of other skills' ids, returning the chosen one. Two callers with one difference: the TRIGGER picker
    // wants only OnCast actives (a passive isn't a cast you can key off), while the STACK-SOURCE picker must include
    // passives — the classic stack granter, Momentum, is one. Reads SkillCatalog (saved Resources/Skills assets); a
    // stale/unsaved pick stays visible so it isn't silently blanked.
    /// <summary>Pick a skill by id. <paramref name="allowSelf"/> separates the two questions this one popup answers:
    /// WHICH SKILL FIRES this one (no — a skill announcing its own cast would re-fire itself forever) versus WHOSE
    /// PILE this one reads (yes — a skill that grants its own charges and spends them over its own repeats is
    /// self-contained, not a loop: the repeats replay the cast, they never start a new one).</summary>
    string DrawSkillPicker(string label, string tooltip, string current, bool includePassives = false,
                           bool allowSelf = false, bool damagingOnly = false, string noneLabel = "(none)",
                           bool runOfHits = false)
    {
        var ids = SkillCatalog.OrderedIds();
        var choices = new List<string> { "" };
        // Offered only where it is a legal answer (the stack gate). It is not a skill, so it sits at the top rather
        // than inside the alphabetical run of real skills.
        if (runOfHits) choices.Add(StateGate.RunOfHitsId);
        for (int i = 0; i < ids.Length; i++)
        {
            var d = SkillCatalog.Get(ids[i]);
            if (d == null || (d.IsPassive && !includePassives)) continue;
            if (!allowSelf && sel != null && ids[i] == sel.id) continue;   // a skill can't be FIRED by itself
            if (damagingOnly && !Deals(d)) continue;                       // only a skill that can land a kill
            choices.Add(ids[i]);
        }
        if (!string.IsNullOrEmpty(current) && !choices.Contains(current)) choices.Add(current);

        var labels = new string[choices.Count];
        for (int i = 0; i < choices.Count; i++)
        {
            if (string.IsNullOrEmpty(choices[i])) { labels[i] = noneLabel; continue; }
            if (choices[i] == StateGate.RunOfHitsId) { labels[i] = "Consecutive hits  (automatic)"; continue; }
            var d = SkillCatalog.Get(choices[i]);
            labels[i] = d != null ? $"{d.displayName}  ({choices[i]})" : choices[i];
        }

        int idx = Mathf.Max(0, choices.IndexOf(current ?? ""));
        // The tooltip is the CALLER's: this one popup answers two different questions, and a shared hint would
        // describe the wrong one at whichever site didn't write it.
        int next = EditorGUILayout.Popup(new GUIContent(label, tooltip), idx, labels);
        return choices[next];
    }

    /// <summary>The edited skill's name, pinned above the scroll view so it stays put however far the body scrolls —
    /// scroll into an effect's numbers and WHICH skill you are editing is still on screen. Reads the live
    /// <c>displayName</c>, so it renames as you type. Falls back to the id for a skill with no name yet.</summary>
    static GUIStyle _skillNameBar;
    static GUIStyle SkillNameBar => _skillNameBar ??= new GUIStyle(EditorStyles.boldLabel)
    {
        fontSize = 15,
        alignment = TextAnchor.MiddleLeft,
        clipping = TextClipping.Clip,
        // The same green as the group headers — one colour, one meaning: "this names something, it isn't a setting".
        normal = { textColor = SectionGreen },
    };

    void DrawSkillNameBar()
    {
        // Height only, never a width demand, so a narrow docked window still can't grow a horizontal scrollbar.
        var bar = EditorGUILayout.GetControlRect(false, 26f, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(bar, new Color(0.09f, 0.09f, 0.10f));
        var text = new Rect(bar.x + 8f, bar.y, Mathf.Max(0f, bar.width - 12f), bar.height);
        GUI.Label(text, string.IsNullOrEmpty(sel.displayName) ? sel.id : sel.displayName, SkillNameBar);
    }

    void DrawBody()
    {
        EditorGUILayout.BeginVertical();
        if (sel == null) { EditorGUILayout.HelpBox("Select or create a skill.", MessageType.Info); EditorGUILayout.EndVertical(); return; }

        if (snapshotOwner != sel) CaptureSnapshot();   // freshly-selected skill → snapshot its saved state

        float contentWidth = position.width - (listCollapsed ? 20f : listWidth) - 5f;
        DrawSkillNameBar();
        bodyScroll = EditorGUILayout.BeginScrollView(bodyScroll);
        // Pin the scroll content to the viewport width so the fields SHRINK to fit instead of the scroll view
        // laying them out at their preferred width and adding a horizontal scrollbar (the "won't shrink" symptom).
        float viewport = Mathf.Max(0f, contentWidth - 18f);   // minus the vertical scrollbar gutter
        EditorGUILayout.BeginVertical(GUILayout.MaxWidth(viewport));
        EditorLayoutFlow.ResponsiveLabelWidth(viewport, 130f);
        EditorGUI.BeginChangeCheck();

        // 1) IDENTITY — what the skill IS.
        if (Section("Identity — what this skill is", ref foId))
        {
            // DELAYED on purpose: a plain TextField commits per KEYSTROKE, so typing a new id would fire a rename for
            // every half-typed prefix. This commits once, on Enter or focus-loss — then everything pointing at the old
            // id is re-pointed at the new one, so a rename can never leave a dangling reference.
            // The id is COMPOSED, never free-typed: skill.<weapon>.<name> (or skill.<name> for a skill in no weapon's
            // pool). One value, one meaning — the weapon you pick here is the pool the skill rolls from, the group it
            // files under in the list on the left, AND the middle segment of its id, so those can never disagree.
            string beforeId = sel.id;
            var beforeWeapon = sel.weaponType;
            var weapon = (WeaponType)EditorGUILayout.EnumPopup(new GUIContent("weapon",
                "WHICH weapon's skill pool this belongs to. A dropped weapon rolls its kit only from PUBLISHED skills " +
                "of its own type. None = in no pool: never rolled, but still usable from the Grimoire for testing."), sel.weaponType);
            string beforeName = sel.displayName;
            string named = EditorGUILayout.DelayedTextField(new GUIContent("display name",
                "The skill's name shown to the player (hotbar hover, tooltips) — and the source of its id: " +
                "'Lock Blades' becomes skill.sword.lock_blades. Safe to rename: every reference to the old id is " +
                "re-pointed automatically."), sel.displayName);
            if (weapon != beforeWeapon || named != beforeName)
            {
                sel.weaponType = weapon;
                sel.displayName = named;
                string composed = ComposeId(weapon, named);
                if (composed != beforeId) SkillLibrarySweeps.RetargetSkillId(all, beforeId, composed, sel);
                EditorUtility.SetDirty(sel);
            }
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(new GUIContent("id",
                    "Composed from the weapon and the display name — lowercased, spaces to underscores."), sel.id);

            sel.published = EditorGUILayout.Toggle(new GUIContent("published",
                "PUBLISHED = this skill may be rolled onto a dropped weapon of its type. Unpublished skills stay fully " +
                "usable in the Grimoire (J) and on the hotbar for testing — publishing only opens the loot roll. " +
                "A skill with weapon = None can never be rolled, published or not."), sel.published);
            if (sel.published && sel.weaponType == WeaponType.None)
                EditorGUILayout.HelpBox("Published, but in no weapon's pool — nothing can roll it. Pick a weapon above.",
                    MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                sel.icon = (Sprite)EditorGUILayout.ObjectField(new GUIContent("icon",
                    "Hotbar icon. Leave empty to fall back to the short text label."), sel.icon, typeof(Sprite), false);
                var pickRect = GUILayoutUtility.GetRect(new GUIContent("Skill Icons ▾"), EditorStyles.miniButton, GUILayout.MaxWidth(96));
                if (GUI.Button(pickRect, new GUIContent("Skill Icons ▾", "Pick from " + SkillIconPicker.Folder), EditorStyles.miniButton))
                {
                    var target = sel;   // capture the edited def; the popup callback fires after this GUI pass
                    PopupWindow.Show(pickRect, new SkillIconPicker(s =>
                    {
                        Undo.RecordObject(target, "Set Skill Icon");
                        target.icon = s;
                        EditorUtility.SetDirty(target);
                    }));
                }
            }
            EditorGUILayout.LabelField(new GUIContent("description", "Flavor + mechanics blurb shown in the hover card."));
            sel.description = EditorGUILayout.TextArea(sel.description ?? "", WrapTextArea, GUILayout.Height(42), GUILayout.ExpandWidth(true));
        }

        // 2) CLASSIFICATION — the gameplay axes (verb · family · tier) + designer-only metadata (shape/complexity/acq).
        if (Section("Classification — verb, family and tier", ref foClass))
        {
            sel.archetype = (SkillArchetype)EditorGUILayout.EnumPopup(new GUIContent("archetype (verb)",
                "Battlefield-role VERB — what the skill DOES. Drives the hover card chip + color (Burst / Tempo / Fortress / Buff / Curse)."), sel.archetype);
            sel.family = (SkillFamily)EditorGUILayout.EnumPopup(new GUIContent("family",
                "Attribute CLUSTER (Might / Arcane / Finesse) — the RPS-triangle side; also the default damage-type source. Orthogonal to the verb."), sel.family);
            sel.tier = (SkillTier)EditorGUILayout.EnumPopup(new GUIContent("tier",
                "Base = a normal hotbar skill (keys 1–9). Ultimate = fires from the dedicated ULT slot, charge-gated. " +
                "An Ultimate pays charge instead of a cooldown — both live in the cost section below."), sel.tier);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Designer metadata (not gameplay)", EditorStyles.miniLabel);
            sel.complexity = (SkillComplexity)EditorGUILayout.EnumPopup(new GUIContent("complexity (meta)",
                "Designer metadata only (not gameplay) — authoring complexity, for library search."), sel.complexity);
            sel.acquisitionTier = (AcquisitionTier)EditorGUILayout.EnumPopup(new GUIContent("acq. tier (meta)",
                "Designer metadata only (not gameplay) — early / mid / late progression bucket."), sel.acquisitionTier);
            sel.shape = (SkillShape)EditorGUILayout.EnumPopup(new GUIContent("shape (meta)",
                "LABEL ONLY — no gameplay reads it. Real targeting is each tick's 'targets' dropdown below. Kept for library filtering."), sel.shape);
        }

        // 3) TRIGGERS — every way this skill can start, each with its own state gate. One OnCast entry = a pressed
        //    active; no OnCast entry = a pure passive; BOTH = pressed AND self-firing, from ONE payload.
        if (Section("Triggers — when it fires", ref foTrigger))
        {
            if (sel.triggers == null) sel.triggers = new List<SkillTriggerEntry>();
            if (sel.triggers.Count == 0)
                sel.triggers.Add(new SkillTriggerEntry());   // never show an empty list: a skill always starts SOME way

            for (int i = 0; i < sel.triggers.Count; i++)
            {
                if (sel.triggers[i] == null) sel.triggers[i] = new SkillTriggerEntry();
                if (DrawTriggerEntry(sel.triggers[i], i)) { sel.triggers.RemoveAt(i); i--; }
            }

            if (GUILayout.Button(new GUIContent("Add trigger",
                "EVERY trigger runs the SAME skill body — a trigger chooses only WHEN this fires and what that firing " +
                "costs in stacks, never what it does. If two triggers want different effects, that's two skills " +
                "wearing one name: split them.\n\n" +
                "OnCast pays the MP + cooldown below; every other trigger fires free — the trigger IS the gesture. A " +
                "run started by a non-OnCast trigger does NOT count as 'you cast this skill', so a partner keyed to " +
                "OnSkillCastComplete can't be re-fed by this skill's own self-fire.")))
                sel.triggers.Add(new SkillTriggerEntry { trigger = SkillTrigger.OnDamageDealt });

            if (sel.IsPassive)
                EditorGUILayout.HelpBox("No OnCast trigger — this skill takes NO hotbar slot. Cost / cadence / aim " +
                    "below are ignored; author the payload in the Gameplay section. It never fires off a chain it's " +
                    "already part of, so it can't feed itself.", MessageType.Info);
        }

        // 4) COST & AIM — everything a cast PAYS to happen, plus WHERE it lands (aim is the cast gesture; each tick's
        //    'applies to' in Gameplay is WHO it affects there — kept distinct on purpose).
        if (Section("Cost & Aim — what firing takes, and how far it reaches", ref foCost))
        {
            sel.manaCost = EditorGUILayout.IntField(new GUIContent("mana cost",
                "MP spent each time the skill is cast. The cast is blocked if you don't have enough."), sel.manaCost);
            // Everything a cast PAYS is asked in one place. Every tier answers the SAME two questions — an ultimate
            // is a slot, not a second cost model, so it edits mana and cooldown like any other skill.
            sel.cooldown = EditorGUILayout.Slider(new GUIContent("cooldown (s)",
                "Seconds before the skill can be used again after casting."), sel.cooldown, 0f, 120f);
            sel.castTime = EditorGUILayout.Slider(
                new GUIContent("cast time (s)", "Wind-up before the skill fires (0 = instant). DEX shortens it at runtime."),
                sel.castTime, 0f, 10f);
            // Aim is DERIVED from each tick's targeting (see Gameplay below) — no field here. Cast range only matters
            // when the skill actually aims at something (a ground point or a unit); an instant self-cast has none.
            // castRange is reused as the projectile's TRAVEL distance (a projectile aims a direction, not a spot) —
            // one number, relabeled by context so there's no parallel field to keep in sync.
            if (sel.Reticle != CastReticle.Instant)
                sel.castRange = EditorGUILayout.Slider(sel.IsProjectileAim
                    ? new GUIContent("travel range", "How far the projectile flies from the caster along the aim direction. The aim arrow is only a direction hint — the shot always travels this full distance.")
                    : new GUIContent("cast range", "Max distance from the caster the aim point / target can be."),
                    sel.castRange, 1f, 30f);
        }

        // 5) GAMEPLAY — the tick bindings + effects (the actual mechanics). Has its own internal indentation.
        if (Section("Gameplay — what each tick does", ref foGameplay))
            DrawTickBindings();

        // 6) REPEATS — how many times the payload above plays, and on what beat. Sits right after Gameplay because
        //    everything here MULTIPLIES what that section authored: one cycle is the payload, this decides how many.
        if (Section("Repeats — how many times it plays", ref foRepeat))
        {
            // A charge-driven run has no authored length, so the count would be a dead knob — hide it rather than
            // show a number the run ignores.
            sel.repeatCostsCharge = EditorGUILayout.Toggle(new GUIContent("repeat costs a charge",
                "ON = each repeat SPENDS one charge of the state this skill's trigger requires, and the repeats run " +
                "until that pile is empty (the count below is ignored). The loop reads the LIVE pile, so a passive " +
                "that grants more charges mid-run genuinely lengthens the run."), sel.repeatCostsCharge);
            if (sel.repeatCostsCharge)
            {
                bool gated = false;
                if (sel.triggers != null)
                    for (int i = 0; i < sel.triggers.Count; i++)
                        if (sel.triggers[i] != null && sel.triggers[i].requiresState) gated = true;
                if (!gated)
                    EditorGUILayout.HelpBox("No trigger on this skill requires a state, so there are no charges to " +
                        "spend — the repeats would stop after the first beat.", MessageType.Warning);
            }
            else
                sel.repeatCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("repeat count",
                    "Replay the WHOLE cycle N times per cast (e.g. 5 = a 5-meteor shower). Each cycle is an independent play " +
                    "at the same cast point; per-fall hits still come from the VFX's tick rule. 1 = play once."), sel.repeatCount));

            if (sel.repeatCount > 1 || sel.repeatCostsCharge)   // only meaningful when the cycle actually repeats
            {
                sel.repeatScaleWithAttackSpeed = EditorGUILayout.Toggle(new GUIContent("scale with attack speed",
                    "ON = the beat follows the caster's SWING SPEED rather than a fixed number of seconds, so a faster " +
                    "caster beats faster. It takes precedence, so the interval below is hidden."),
                    sel.repeatScaleWithAttackSpeed);
                if (sel.repeatScaleWithAttackSpeed)
                {
                    sel.repeatBeatsPerSwing = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("  beats per swing",
                        "1 = one beat per swing, your normal rhythm. 2 = twice as fast as you swing. Whole numbers " +
                        "only — a beat is either on your rhythm or a clean multiple of it."), sel.repeatBeatsPerSwing));
                    float swing = Character.Instance != null ? Character.Instance.SwingTime : 0f;
                    EditorGUILayout.HelpBox(swing > 0f
                        ? $"Right now ({swing:0.00}s per swing): a beat every {swing / Mathf.Max(1, sel.repeatBeatsPerSwing):0.00}s."
                        : "Enter Play mode to see the live beat.", MessageType.None);
                }
                else
                    sel.repeatInterval = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("repeat interval (s)",
                        "Seconds between cycle starts. 0 = use the VFX sequence's RestartInterval (its loop-end marker)."), sel.repeatInterval));
                sel.repeatReaim = EditorGUILayout.Toggle(new GUIContent("repeat re-aims",
                    "ON = each repeat picks a random living mob within the range below and goes there. OFF = every " +
                    "repeat replays where the cast was originally aimed (a meteor shower stays put)."), sel.repeatReaim);
                if (sel.repeatReaim)
                {
                    sel.repeatReaimRange = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("  re-aim range",
                        "World units, measured from the CURRENT target — not from you, and not from where the cast " +
                        "started. So the run walks target to target — each beat hits ONE target, chaining outward from " +
                        "whoever was just struck, and the run ENDS the moment nothing living is within reach of the " +
                        "last one."),
                        sel.repeatReaimRange));
                }
                sel.showRepeatsTotalDamage = EditorGUILayout.Toggle(new GUIContent("show repeats total damage dealt",
                    "ON = each mob shows ONE yellow total that climbs with every repeat that strikes it, closing a " +
                    "second after the run last touched it — so a dance that hops away and comes back starts a fresh " +
                    "number. OFF = every repeat pops its own total (a meteor shower shows one per meteor). Purely what " +
                    "you SEE: no damage, timing or state changes either way."), sel.showRepeatsTotalDamage);
                sel.holdCasterForRun = EditorGUILayout.Toggle(new GUIContent("holds the caster",
                    "ON = the caster is held for the WHOLE run — from the first beat until the last one ends it " +
                    "can't move, attack, start another skill or launch a combo. OFF = only the release animation's " +
                    "own aftercast roots it, so it's free again between beats (a meteor shower rains while you keep " +
                    "fighting)."), sel.holdCasterForRun);
            }
        }

        // 7) PRESENTATION — how it looks + where it sits on the bar.
        if (Section("Presentation — how it looks and sits on the bar", ref foPresent))
        {
            sel.vfx = (VfxSequence)EditorGUILayout.ObjectField(new GUIContent("vfx sequence",
                "The composed look from the Vfx Composer. Its exposed ticks appear in Gameplay above."), sel.vfx, typeof(VfxSequence), false);

            DrawCastCircle();
            DrawSkillType();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Hotbar", EditorStyles.miniBoldLabel);
            sel.tint = EditorGUILayout.ColorField(new GUIContent("tint", "Hotbar cell tint for this skill."), sel.tint);
        }

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(sel);

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();

        // Pinned action bar — lives OUTSIDE the scroll view so Save / Discard stay at the window bottom no matter how
        // far the content scrolls (no scrolling to the end just to save).
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        // The consumer sweep runs BEFORE the snapshot is refreshed — the snapshot IS the "what this skill granted
        // last time" it diffs against.
        if (GUILayout.Button("Save", GUILayout.Height(24)))
        {
            SkillAffinityWeights.NormalizeAll(sel);
            EditorUtility.SetDirty(sel);
            SkillLibrarySweeps.SyncFileNameToId(sel);
            if (snapshotOwner == sel) SkillLibrarySweeps.SyncConsumersToGrantedStates(all, sel, snapshot);
            AssetDatabase.SaveAssets();
            SkillCatalog.Reload();
            CaptureSnapshot();
        }
        using (new EditorGUI.DisabledScope(!EditorUtility.IsDirty(sel)))
            if (GUILayout.Button("Discard changes", GUILayout.Height(24))) DiscardSkillEdits();
        if (GUILayout.Button(new GUIContent("Check gates",
                "Read every saved skill's stack gates and report the broken ones — no Play needed. A gate breaks " +
                "silently (it just counts nothing), so this is the only cheap way to see them all at once."),
                GUILayout.Height(24), GUILayout.MaxWidth(96)))
            SkillLibrarySweeps.CheckAllStackGates(all);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);

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

        if (sel.hasReleaseStance)
        {
            EditorGUI.indentLevel++;
            sel.releaseStanceReplayPerRepeat = EditorGUILayout.Toggle(new GUIContent("replay per repeat",
                "Play the release animation again on EVERY repeat, restarted from its first frame and fit to that " +
                "repeat's interval — so each beat shows one WHOLE animation (a full swing per hit) instead of one " +
                "animation stretched across the run. Off = it plays once, when the skill fires."),
                sel.releaseStanceReplayPerRepeat);
            EditorGUI.indentLevel--;
        }
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
            string whenLabel = t.when == TickTiming.Interval ? $"every {t.interval:0.##}s" : t.when.ToString().ToLower();
            EditorGUILayout.LabelField($"{t.id}   ({whenLabel})", EditorStyles.boldLabel);
            if (b == null)
            {
                if (GUILayout.Button("+ bind", GUILayout.MaxWidth(70)))
                    sel.bindings.Add(new SkillTickBinding { tickId = t.id });
            }
            else if (GUILayout.Button("unbind", GUILayout.MaxWidth(70)))
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
            if (GUILayout.Button("remove orphan", GUILayout.MaxWidth(120))) { sel.bindings.RemoveAt(i); i--; }
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

    // The reusable entity multi-select: Mobs / Players (+ Ally/Enemy) / NPC (reserved). Used for Single Target and
    // Ground → AoE/Projectile. For Single Target it's the VALIDATION set (the pick must match); for area/projectile
    // it's WHO gets hit. Never allowed to be empty (falls back to Mobs).
    void DrawAffects(SkillTickBinding b)
    {
        bool mob    = EditorGUILayout.ToggleLeft(new GUIContent("    affects: Mobs", "Enemy monsters."), (b.affects & SkillTargets.Mobs) != 0);
        bool player = EditorGUILayout.ToggleLeft(new GUIContent("    affects: Players", "Player characters (self / others)."), (b.affects & SkillTargets.Player) != 0);
        SkillTargets a = SkillTargets.None;
        if (mob) a |= SkillTargets.Mobs;
        if (player) a |= SkillTargets.Player;
        b.affects = a == SkillTargets.None ? SkillTargets.Mobs : a;
        if (player)
            b.playerFaction = (PlayerFaction)EditorGUILayout.EnumPopup(new GUIContent("      player faction",
                "Which players it applies to. Ally (default) — Enemy is the PvP tag, wired later."), b.playerFaction);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ToggleLeft(new GUIContent("    affects: NPC  (reserved — not yet)", "NPCs aren't damageable/healable yet."), false);
    }

    // Plain-English readout of a tick's targeting — shown live under the control so nothing is ever hidden.
    // hasTargetFx = this tick has effects that land on gathered targets. Without them the tick hits nobody, so the
    // readout must say the aim gesture only instead of promising hits that can't happen.
    static string TargetingSummary(SkillTickBinding b, bool hasTargetFx)
    {
        switch (b.targetMode)
        {
            case TargetMode.Self:         return "no aiming — lands on the caster";
            case TargetMode.SingleTarget: return hasTargetFx ? "click one " + AffectsWord(b) + " to hit it"
                                                            : "click one " + AffectsWord(b) + " — it isn't hit, it's just the aimed spot";
            default:
                switch (b.groundDelivery)
                {
                    case GroundDelivery.Point: return "aim a spot — the caster acts there";
                    case GroundDelivery.Projectile: return hasTargetFx
                        ? "aim a direction — a shot flies out, hitting " + AffectsWord(b) + " within " + b.radius.ToString("0.#") + " units along its path"
                        : "aim a direction — a shot flies out, hitting nobody (no target effects on this tick)";
                    default: return hasTargetFx
                        ? "hits " + AffectsWord(b) + " within " + b.radius.ToString("0.#") + " units of the spot"
                        : "aim a spot — nobody is hit (no target effects on this tick)";
                }
        }
    }

    // Two caster effects of the same kind on one tick = it happens twice (two dashes, two shields). Legitimate only for
    // Stat Buff, where each entry lifts a DIFFERENT stat — so that one is keyed by stat, not just kind.
    static void WarnDuplicateCasterEffects(SkillTickBinding b)
    {
        for (int i = 0; i < b.effects.Count; i++)
        {
            var a = b.effects[i];
            if (a == null || !SkillEffect.LandsOnCaster(a)) continue;
            for (int j = i + 1; j < b.effects.Count; j++)
            {
                var c = b.effects[j];
                if (c == null || c.kind != a.kind) continue;
                // Two sub-stat rows only collide when they name the SAME stat — one tick raising attack and
                // evasion is two different things, not a duplicate.
                bool subStat = a.kind == SkillEffectKind.SubStatBuff || a.kind == SkillEffectKind.SubStatDebuff;
                if (subStat && a.subStat != c.subStat) continue;
                string label = subStat
                    ? ObjectNames.NicifyVariableName(a.kind.ToString()) + " (" + ObjectNames.NicifyVariableName(a.subStat.ToString()) + ")"
                    : ObjectNames.NicifyVariableName(a.kind.ToString());
                EditorGUILayout.HelpBox(
                    label + " is on this tick twice. Caster effects apply once EACH, so this one fires twice on every " +
                    "cast — delete one unless that's really the intent.", MessageType.Warning);
                return;
            }
        }
    }

    // The delivery dropdown is the SINGLE SOURCE OF TRUTH for "is this tick a projectile" — but the VFX step this tick
    // names has its own visual "launch from caster" flag, and the two should agree (from-caster ⇒ Projectile;
    // from-above ⇒ AoE). We only READ the VFX here to warn on a mismatch — we never let it drive gameplay.
    //
    // The question is asked of THIS TICK'S OWN STEP, never of the sequence as a whole: a skill that dashes on one tick
    // and shoots on another would otherwise be told its dash tick was a projectile, because some OTHER step in the same
    // sequence launches one. A tick names exactly one step, so the answer is one-to-one.
    void WarnDeliveryVfxMismatch(SkillTickBinding b)
    {
        bool fromCaster = false;
        if (sel.vfx != null && sel.vfx.steps != null && !string.IsNullOrEmpty(b.tickId))
            for (int i = 0; i < sel.vfx.steps.Count; i++)
            {
                var s = sel.vfx.steps[i];
                if (s == null || s.tickId != b.tickId) continue;
                if (s.kind == VfxStepKind.Carrier && s.carrierFromCaster) { fromCaster = true; break; }
            }

        if (b.groundDelivery == GroundDelivery.Projectile && !fromCaster)
            EditorGUILayout.HelpBox(
                "Projectile delivery, but the VFX step this tick names doesn't launch from the caster. Turn ON " +
                "'launch from caster' on that step (it must be a Carrier) so the shot visually flies from the caster.",
                MessageType.Warning);
        else if (b.groundDelivery != GroundDelivery.Projectile && fromCaster)
            EditorGUILayout.HelpBox(
                "The VFX step this tick names launches from the caster (a projectile), but delivery is " +
                b.groundDelivery + ". Set delivery to Projectile, or turn OFF 'launch from caster' on that step.",
                MessageType.Warning);
    }

    static string AffectsWord(SkillTickBinding b)
    {
        bool mob = (b.affects & SkillTargets.Mobs) != 0, pl = (b.affects & SkillTargets.Player) != 0;
        string players = b.playerFaction == PlayerFaction.Enemy ? "enemy players" : "players";
        if (mob && pl) return "mobs & " + players;
        if (pl) return players;
        return "mobs";
    }

    // ---- effect-kind cascading picker (Health / Movement / State / Modifiers) ------------------------------------
    // A purely PRESENTATIONAL grouping over the flat SkillEffectKind (+ Ailment for the 8 Status debuffs) — no data
    // model changes, and targeting still lives on the affects checkboxes. ONE question decides every group: WHAT DOES
    // THIS EFFECT TOUCH? An HP bar (Health), where a body stands (Movement), a unit's ongoing conditions (State), or
    // another skill (Modifiers). Every kind lands in exactly one, nothing sits loose at the top, and a new kind has an
    // obvious home — which is the whole reason to group at all. Order in each array = display order.
    static readonly SkillEffectKind[] HealthKinds =
        { SkillEffectKind.Damage, SkillEffectKind.Execute, SkillEffectKind.Heal };
    // Every verb that MOVES a body. Who gets moved (you or what you hit) is the targeting question, not this one —
    // a shove and a blink are the same kind of thing pointed at different people. Their seconds are travel time, not
    // an ongoing condition, which is exactly why they are not States.
    static readonly SkillEffectKind[] MovementKinds =
        { SkillEffectKind.Teleport, SkillEffectKind.Dash, SkillEffectKind.Knockback, SkillEffectKind.Pull };
    static readonly SkillEffectKind[] BuffKinds =
        { SkillEffectKind.SubStatBuff, SkillEffectKind.BoostOutgoingDamage,
          SkillEffectKind.Parry, SkillEffectKind.Brace, SkillEffectKind.ImmuneToDisables,
          SkillEffectKind.Shield };

    // Modifiers: the effects whose target is a SKILL's pile rather than a body. Labelled as the whole sentence they
    // read as, because that IS the effect — "extend duration of <skill>" says what it does and what it needs, where a
    // one-word name would need the tooltip to explain both.
    //
    // ⛔ THE RULE FOR ADDING ONE (project CLAUDE.md, hard-pinned): a modifier FIRES AT A MOMENT AND FINISHES. It does
    // its work at the instant it runs — a press, a kill, a parry — and is then over. "Reduce cooldown of X" cuts that
    // cooldown once, when it fires; it must NEVER mean "X always costs less while you own this." A standing change
    // needs a layer that remembers what it altered and how to put it back, which is a different mechanism and a design
    // decision, not a new entry in this array. Arming a promise for a later cast ("your NEXT skill …") is the same
    // violation wearing a trigger. Ask first: does this happen once, right now, and then it's done?
    static readonly SkillEffectKind[] ModifierKinds =
        { SkillEffectKind.ExtendDuration, SkillEffectKind.AddStacks };
    // The slash in "stacks∕charges" is U+2215, NOT the ASCII "/": a dropdown entry splits on "/" and builds a SUBMENU
    // out of it, so the plain sentence rendered as "add stacks ▸ charges to <skill>" — two menu levels out of one
    // phrase. The look-alike keeps the sentence exactly as it reads and is just a character to the menu.
    static readonly string[] ModifierLabels =
        { "extend duration of <skill>", "add stacks∕charges to <skill>" };

    // A debuff entry is either its own kind (Vulnerability/Taunt/Knockback/Pull) or a Status ailment (the 8 ailments
    // all share kind = Status, differing only by Ailment) — so picking "Burn" sets kind=Status, ailment=Burn.
    struct DebuffOption
    {
        public string label; public SkillEffectKind kind; public bool isAilment; public Ailment ailment;
        public DebuffOption(string l, SkillEffectKind k) { label = l; kind = k; isAilment = false; ailment = default; }
        public DebuffOption(string l, Ailment a) { label = l; kind = SkillEffectKind.Status; isAilment = true; ailment = a; }
    }
    static readonly DebuffOption[] DebuffOptions =
    {
        new DebuffOption("Sub Stat Debuff", SkillEffectKind.SubStatDebuff),
        new DebuffOption("Reduce Outgoing Damage", SkillEffectKind.ReduceOutgoingDamage),
        new DebuffOption("Taunt", SkillEffectKind.Taunt),
        new DebuffOption("Poison", Ailment.Poison), new DebuffOption("Bleed", Ailment.Bleed),
        new DebuffOption("Burn", Ailment.Burn),
        new DebuffOption("Stun", Ailment.Stun),     new DebuffOption("Freeze", Ailment.Freeze),
        new DebuffOption("Silence", Ailment.Silence),
    };

    // The menus above are split by SIDE, so each section only offers kinds it can actually run — one array pair per
    // side, grouped by the same SkillEffect.LandsOnCaster the runtime partitions on. Both sections offer every kind
    // and every state — a self-inflicted debuff (Overclock's own armour-fray) is as authorable as a buff on a mob.
    static SkillEffectKind[][] sideBuffs;                   // [0] = target side, [1] = caster side
    static string[][] sideBuffLabels;
    static string[] typeLabels, debuffLabels, healthLabels, movementLabels;

    // The four doors, in display order. Kept as one list so the row's index arithmetic can't drift from the labels.
    enum EffectGroup { Health = 0, Movement = 1, State = 2, Modifiers = 3 }
    static readonly string[] GroupLabels = { "Health ▸", "Movement ▸", "State ▸", "Modifiers ▸" };

    // Which door an existing effect already sits behind. State is the default because it is the only open-ended one:
    // anything that isn't one of the three closed lists is a state (a buff, a debuff, or an ailment).
    static EffectGroup GroupOf(SkillEffectKind k)
        => System.Array.IndexOf(HealthKinds, k) >= 0 ? EffectGroup.Health
         : System.Array.IndexOf(MovementKinds, k) >= 0 ? EffectGroup.Movement
         : SkillEffect.IsModifier(k) ? EffectGroup.Modifiers
         : EffectGroup.State;

    // Every kind is offered on BOTH sides: which side an effect lands on is now the AUTHOR's choice (SkillEffect.side),
    // stamped by whichever section the effect was added in — it is no longer a fixed property of the kind. Kept as a
    // pass-through (rather than deleted) so the two-side array shape below stays one code path.
    static SkillEffectKind[] FilterSide(SkillEffectKind[] src, bool caster) => src;

    static void EnsureKindLabels()
    {
        if (sideBuffs != null) return;
        sideBuffs = new SkillEffectKind[2][];
        sideBuffLabels = new string[2][];
        for (int s = 0; s < 2; s++)
        {
            bool caster = s == 1;
            var bfs = FilterSide(BuffKinds, caster);
            sideBuffs[s] = bfs;
            var bl = new string[bfs.Length];
            for (int i = 0; i < bfs.Length; i++)
                bl[i] = ObjectNames.NicifyVariableName(bfs[i].ToString());
            sideBuffLabels[s] = bl;
        }
        healthLabels = Nicify(HealthKinds);
        movementLabels = Nicify(MovementKinds);
        // Cleanse is the one verb that REMOVES states, so it belongs with them rather than off on its own — but it is
        // neither a buff nor a debuff, so it sits beside those two branches instead of inside either.
        typeLabels = new[] { "Buffs", "Debuffs", "Cleanse" };
        debuffLabels = new string[DebuffOptions.Length];
        for (int i = 0; i < DebuffOptions.Length; i++) debuffLabels[i] = DebuffOptions[i].label;
    }

    static string[] Nicify(SkillEffectKind[] kinds)
    {
        var a = new string[kinds.Length];
        for (int i = 0; i < kinds.Length; i++) a[i] = ObjectNames.NicifyVariableName(kinds[i].ToString());
        return a;
    }

    static int Side(bool caster) => caster ? 1 : 0;
    static bool IsModifierKind(SkillEffectKind k) => SkillEffect.IsModifier(k);
    static bool IsBuffKind(SkillEffectKind k) { for (int i = 0; i < BuffKinds.Length; i++) if (BuffKinds[i] == k) return true; return false; }
    static int IndexOfDebuff(SkillEffect ef)
    {
        for (int i = 0; i < DebuffOptions.Length; i++)
        {
            var o = DebuffOptions[i];
            if (o.isAilment ? (ef.kind == SkillEffectKind.Status && ef.ailment == o.ailment) : ef.kind == o.kind) return i;
        }
        return 0;
    }

    // Write down WHICH SIDE this effect lands on, so it stays in the section the author put it in even after its kind
    // changes. Before the side was authored, the kind decided it — which is why changing a Guard to a Damage used to be
    // impossible rather than just moving it.
    static void StampSide(SkillEffect ef, bool casterSide)
        => ef.side = casterSide ? SkillEffect.EffectSide.Caster : SkillEffect.EffectSide.Target;

    // The GROUP popup, drawn inline on the effect header row — which of the four things this effect touches. Picking a
    // group lands on its first member, and the row below then picks within it. Side is stamped on any change, so an
    // effect can never hop sections from under the author.
    void DrawEffectKindTop(SkillEffect ef, bool casterSide)
    {
        EnsureKindLabels();
        var group = GroupOf(ef.kind);
        var next = (EffectGroup)EditorGUILayout.Popup((int)group, GroupLabels);
        if (next == group) return;
        ef.kind = next == EffectGroup.Health    ? HealthKinds[0]
                : next == EffectGroup.Movement  ? MovementKinds[0]
                : next == EffectGroup.Modifiers ? ModifierKinds[0]
                                                : sideBuffs[Side(casterSide)][0];   // State → this side's first state
        StampSide(ef, casterSide);
    }

    // The row UNDER the group: which member of that group this effect is. Health and Movement are flat lists;
    // Modifiers reads as whole sentences; State keeps its own Buffs / Debuffs / Cleanse branch.
    void DrawEffectStateBranch(SkillEffect ef, bool casterSide)
    {
        EnsureKindLabels();
        var group = GroupOf(ef.kind);

        if (group == EffectGroup.Health || group == EffectGroup.Movement)
        {
            var kinds = group == EffectGroup.Health ? HealthKinds : MovementKinds;
            var labels = group == EffectGroup.Health ? healthLabels : movementLabels;
            int i = System.Array.IndexOf(kinds, ef.kind); if (i < 0) i = 0;
            int n = EditorGUILayout.Popup(new GUIContent("  effect", group == EffectGroup.Health
                ? "Which way this moves an HP bar."
                : "Which way this moves a body. WHO gets moved is the targeting above — a shove and a blink are the " +
                  "same verb pointed at different people."), i, labels);
            if (n != i) { ef.kind = kinds[n]; StampSide(ef, casterSide); }
            return;
        }

        if (group == EffectGroup.Modifiers)
        {
            int m = System.Array.IndexOf(ModifierKinds, ef.kind); if (m < 0) m = 0;
            int mn = EditorGUILayout.Popup(new GUIContent("  effect",
                "What this does to the skill it names below.\n\n" +
                "A modifier happens AT THE MOMENT this skill runs — the press, the kill, the parry — and is then " +
                "done. It never sits on the other skill changing it: \"reduce cooldown\" would cut that cooldown " +
                "once, here and now, not make the skill permanently cheaper. So a modifier has no duration of its " +
                "own and never shows on the buff tray."), m, ModifierLabels);
            if (mn != m) { ef.kind = ModifierKinds[mn]; StampSide(ef, casterSide); }
            return;
        }

        int s = Side(casterSide);
        var buffs = sideBuffs[s];
        bool isBuff = IsBuffKind(ef.kind);
        bool isCleanse = ef.kind == SkillEffectKind.Cleanse;

        {
            int typeIdx = isCleanse ? 2 : isBuff ? 0 : 1;
            int typeNext = EditorGUILayout.Popup(new GUIContent("  type",
                "Buff = beneficial state. Debuff = harmful state. Cleanse strips states off instead of applying one. " +
                "Organizational only — WHO it lands on is the targeting above."),
                typeIdx, typeLabels);
            if (typeNext != typeIdx)
            {
                // Each branch lands on the FIRST entry of its own list, so the state row below always opens showing
                // what was actually just selected. Naming one kind here instead would drift the moment either list
                // is reordered.
                ef.kind = typeNext == 0 ? buffs[0] : typeNext == 1 ? DebuffOptions[0].kind : SkillEffectKind.Cleanse;
                isBuff = typeNext == 0; isCleanse = typeNext == 2;
                StampSide(ef, casterSide);
            }
        }
        if (isCleanse) return;   // Cleanse IS the choice — there is no third row to pick under it

        if (isBuff)
        {
            int i = System.Array.IndexOf(buffs, ef.kind); if (i < 0) i = 0;
            int n = EditorGUILayout.Popup(new GUIContent("  state", "Which state to apply."), i, sideBuffLabels[s]);
            if (n != i) { ef.kind = buffs[n]; StampSide(ef, casterSide); }
        }
        else
        {
            int i = IndexOfDebuff(ef);
            int n = EditorGUILayout.Popup(new GUIContent("  state", "Which debuff to apply."), i, debuffLabels);
            if (n != i) { var o = DebuffOptions[n]; ef.kind = o.kind; if (o.isAilment) ef.ailment = o.ailment; StampSide(ef, casterSide); }
        }
    }

    // A tick is authored as TWO composable lists: what it does to the CASTER and what it does to whoever it catches.
    // Both can be filled on the same tick (Lunge = dash yourself + strike the mob you clicked). The split is the same
    // partition the runtime resolves by (SkillEffect.LandsOnCaster), so what you see grouped here is exactly what runs in
    // each pass. Targeting lives INSIDE the target section, because that's the only thing it decides — the caster
    // section needs no "who", only (for a Dash/Teleport) the aimed point the cast asks for anyway.
    void DrawBinding(SkillTickBinding b)
    {
        if (b.effects == null) b.effects = new List<SkillEffect>();

        // Stay duration is the TICK's own knob (how long its looping VFX step lives), not either side's — so it sits
        // above both sections. Only shown when this tick's VFX step actually loops.
        if (sel.TickStepLoops(b.tickId))
            b.stayDuration = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("  stay duration (s)",
                "This tick's VFX step is a LOOPING Play At / Attach — how long that loop stays before it ends " +
                "(and, for an End tick, when it lands). 0 = default 3s."), b.stayDuration));

        DrawCasterSection(b);
        DrawTargetSection(b);
    }

    // ---- section 1: what this tick does to the CASTER --------------------------------------------------------------
    void DrawCasterSection(SkillTickBinding b)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"  ▾ To the caster  ({CountSide(b, true)})", EditorStyles.miniBoldLabel);
        if (b.HasCasterPointEffects)
            EditorGUILayout.LabelField(sel != null && sel.NeedsUnitPick
                                           ? "      → the caster is sent to the entity this cast picks; no point is asked for"
                                           : "      → the cast asks you to aim a point; the caster is sent there",
                                       EditorStyles.miniLabel);
        DrawEffectList(b, true);
    }

    // ---- section 2: what this tick does to whoever it CATCHES ------------------------------------------------------
    void DrawTargetSection(SkillTickBinding b)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"  ▾ To the target  ({CountSide(b, false)})", EditorStyles.miniBoldLabel);

        // TARGETING — ONE control, everything else reveals beneath it. No hidden rules. It belongs to THIS section:
        // it decides who the effects below land on, and nothing else.
        b.targetMode = (TargetMode)EditorGUILayout.EnumPopup(new GUIContent("  targets",
            "Self = lands on the caster. Single Target = click one matching entity. Ground = aim a spot on the floor."), b.targetMode);

        if (b.targetMode == TargetMode.Self)
        {
            b.affects = SkillTargets.Player;   // self is the caster; nothing to pick
        }
        else
        {
            bool needsAffects = b.targetMode == TargetMode.SingleTarget;
            if (b.targetMode == TargetMode.Ground)
            {
                b.groundDelivery = (GroundDelivery)EditorGUILayout.EnumPopup(new GUIContent("    delivery",
                    "Point = the exact spot (the caster acts there — dash/place). AoE = splash in a radius at the aimed spot (cast-circle reticle). " +
                    "Projectile = aim a DIRECTION (line-arrow reticle); the shot flies and hits along its path."), b.groundDelivery);
                WarnDeliveryVfxMismatch(b);
                if (b.groundDelivery != GroundDelivery.Point)
                {
                    b.radius = EditorGUILayout.Slider(new GUIContent("    radius",
                        "Circle size (world units) around the spot that catches entities. For a projectile skill this is its per-tick hit width."), b.radius <= 0f ? 1.5f : b.radius, 0.1f, 12f);
                    // The volley belongs to THIS tick, so it is asked here, under the delivery that turned it into a
                    // projectile — one carrier step, one count. A second projectile tick fans its own.
                    if (b.groundDelivery == GroundDelivery.Projectile)
                    {
                        b.projectileCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("    projectiles",
                            "How many copies of THIS tick's projectile launch at once, fanned into a cone around the " +
                            "aim. 1 = a single shot. Compose ONE projectile in the Vfx Composer; multiply it here."),
                            b.projectileCount));
                        using (new EditorGUI.DisabledScope(b.projectileCount <= 1))
                            b.projectileSpread = EditorGUILayout.Slider(new GUIContent("    fan spread °",
                                "Total cone angle the volley spreads across. 0 = every copy flies straight at the aim."),
                                b.projectileSpread, 0f, 180f);
                    }
                    needsAffects = true;
                }
                else
                {
                    b.affects = SkillTargets.Player;   // Point = the caster acts at the spot
                }
            }
            if (needsAffects) DrawAffects(b);
        }

        EditorGUILayout.LabelField("  → " + TargetingSummary(b, b.HasTargetEffects), EditorStyles.miniLabel);
        DrawEffectList(b, false);
    }

    static int CountSide(SkillTickBinding b, bool casterSide)
    {
        int n = 0;
        for (int i = 0; i < b.effects.Count; i++)
            if (b.effects[i] != null && SkillEffect.LandsOnCaster(b.effects[i]) == casterSide) n++;
        return n;
    }

    // ONE effect list, drawn twice — filtered to the side that owns it. The underlying data stays a single
    // b.effects list (what the runtime iterates), so the two sections can never disagree with what actually fires.
    void DrawEffectList(SkillTickBinding b, bool casterSide)
    {
        int rm = -1;
        for (int e = 0; e < b.effects.Count; e++)
        {
            if (b.effects[e] == null) b.effects[e] = new SkillEffect();
            var ef = b.effects[e];
            if (SkillEffect.LandsOnCaster(ef) != casterSide) continue;   // drawn by the other section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginHorizontal();
            DrawEffectKindTop(ef, casterSide);
            if (GUILayout.Button("✕", GUILayout.MaxWidth(22))) rm = e;
            EditorGUILayout.EndHorizontal();
            DrawEffectStateBranch(ef, casterSide);   // the row under the group: which member of it this effect is
            // Plain-English summary of what THIS effect does — directly under the kind dropdown, above its fields.
            EditorGUILayout.HelpBox(EffectSummary(ef.kind), MessageType.None);
            switch (ef.kind)
            {
                case SkillEffectKind.ExtendDuration:
                case SkillEffectKind.AddStacks:
                    DrawModifierTarget(ef);
                    break;
                case SkillEffectKind.Damage:
                    // Offered on EVERY strike, banked reading included — a swing paid by banked value lands that exact
                    // amount and is a swing in every other way, so there is no case where this choice doesn't apply.
                    ef.resolveAsBasicAttack = EditorGUILayout.Toggle(new GUIContent("resolve as a basic attack",
                        "ON = this strike IS an ordinary attack: the caster swings by ITS OWN rules — its crit, its " +
                        "accuracy against the target's dodge (so it CAN miss), its weapon's element — and a landed " +
                        "strike announces a basic attack, feeding Momentum and every on-basic passive. The ATK " +
                        "multiplier still scales it: 1 = one normal swing, 2 = twice one. Damage type and element " +
                        "belong to the weapon then, so they're hidden.\n\n" +
                        "Paid by a BANKED total, the swing lands that exact amount — it can still miss and still " +
                        "announces, but it does not crit, because there is no roll for a crit to multiply."),
                        ef.resolveAsBasicAttack);
                    if (!ef.resolveAsBasicAttack)
                        ef.damageType = (DamageType)EditorGUILayout.EnumPopup(new GUIContent("damage type",
                            "Physical (ATK vs armour) / Magic (MATK, unmissable, no crit) / Crit (pierces armour, always crits)."), ef.damageType);
                    // ONE meaning, always: this number MULTIPLIES the damage. Where it came from — typed, or built with
                    // the pile — changes nothing about what it then does, so the row never has to be re-read.
                    NumberRow(new GUIContent("damage mult",
                            "The base scaler of this effect's damage. It ALWAYS multiplies the damage roll: 1 = one " +
                            "normal hit, 8 = a heavy nuke.\n\n" +
                            "This never changes meaning. Whatever the number ends up being — typed here, or built from " +
                            "the pile with the controls beside it — the result multiplies the damage, every time."),
                        ref ef.damageMult, ef.useStacks);
                    if (!ef.resolveAsBasicAttack)
                        ef.element = (Element)EditorGUILayout.EnumPopup(new GUIContent("element",
                            "Attack element, checked against the target's armor element. Neutral = no matchup bonus/penalty."), ef.element);
                    NumberRow("hits", "Strikes per landing. Each hit is the TRIGGER for every other effect on this binding — each rolls its chance once per hit.", ref ef.hitCount, ef.useStacks, whole: true);
                    break;
                case SkillEffectKind.Heal:
                    // The base value of the one formula — always shown, whether or not a pile sizes it.
                    NumberRow("HP", "Flat HP restored to the caster.", ref ef.amount, ef.useStacks, whole: true);
                    break;
                case SkillEffectKind.Status:
                    // The ailment is chosen in the State ▸ Debuffs picker above — not repeated here (no double-knob).
                    NumberRow("seconds", "How long the ailment lasts.", ref ef.seconds, ef.useStacks);
                    // magnitude is only meaningful for DoTs (dmg/tick), Slow (speed factor) and Blind (HIT loss).
                    // Pure CC (Stun/Freeze/Silence) is binary — no magnitude, so hide it to avoid the confusion.
                    if (ef.ailment != Ailment.Stun && ef.ailment != Ailment.Freeze && ef.ailment != Ailment.Silence)
                        NumberRow("magnitude", "DoT dmg/tick, Slow factor (0..1), or Blind HIT loss.", ref ef.magnitude, ef.useStacks);
                    break;
                case SkillEffectKind.Execute:
                    ef.damageType = (DamageType)EditorGUILayout.EnumPopup(new GUIContent("damage type",
                        "Physical (ATK vs armour) / Magic (MATK, unmissable, no crit) / Crit (pierces armour, always crits)."), ef.damageType);
                    NumberRow("execute power ×", "Bonus damage = roll × this × (target's missing HP). Huge near death, ~0 at full HP.", ref ef.magnitude, ef.useStacks);
                    ef.element = (Element)EditorGUILayout.EnumPopup(new GUIContent("element",
                        "Attack element, checked against the target's armor element. Neutral = no matchup bonus/penalty."), ef.element);
                    break;
                case SkillEffectKind.BoostOutgoingDamage:
                    NumberRow("damage bonus (1 = +100%)", "The BONUS one charge is worth, not a multiplier: 1 = the attack hits for double. Applied " + "BEFORE the target's armour, so the flat part of armour is paid out of the bigger number and " + "the boost is worth more against tanky targets.", ref ef.magnitude, ef.useStacks);
                    NumberRow("window (s)", "How long unspent charges stay held. They lapse when it ends — the window is a deadline for " + "using them, not what makes them work.", ref ef.seconds, ef.useStacks);
                    EditorGUILayout.HelpBox("ONE charge boosts ONE attack, and that attack spends it whether it hits " +
                        "or misses. Basics, skill strikes, executes and banked damage all count; damage-over-time " +
                        "ticks never do. Set how many charges below (charges per use).", MessageType.None);
                    break;
                case SkillEffectKind.ImmuneToDisables:
                    NumberRow("seconds", "How long nothing can stop the holder. No other knob — being stopped is yes or no, so there " + "is no amount to set.", ref ef.seconds, ef.useStacks);
                    EditorGUILayout.HelpBox("Turns away, by name: FLINCH (the stagger a landing hit causes), STUN, " +
                        "FREEZE, SILENCE, KNOCKBACK, PULL, and a hit CANCELLING a cast in progress.\n\n" +
                        "It does NOT touch damage, poison, bleed, burn, slow or blind — this is about being stopped, " +
                        "not about being hurt. It is never used up by what it blocks: one stun does not end it.",
                        MessageType.None);
                    break;
                case SkillEffectKind.ReduceOutgoingDamage:
                    NumberRow("damage cut (0.4 = −40%)", "How much weaker ONE attack comes out. 0.4 = that attack deals 40% less; 1 = it deals nothing. " + "Cut BEFORE the target's armour, the same place the boost multiplies.", ref ef.magnitude, ef.useStacks);
                    NumberRow("window (s)", "How long unspent charges stay held. They lapse when it ends — the window is a deadline for " + "spending them, not what makes them work.", ref ef.seconds, ef.useStacks);
                    EditorGUILayout.HelpBox("The mirror of Boost Outgoing Damage: ONE charge weakens ONE attack sent " +
                        "by whoever holds it, spent whether that attack hits or misses. Put it on the target side to " +
                        "blunt their swing, or on the caster side as a self-inflicted cost. Its charges are a " +
                        "SEPARATE pile from the boost's, so the two never spend each other. Set how many charges " +
                        "below (charges per use).", MessageType.None);
                    break;
                case SkillEffectKind.Brace:
                    // No magnitude, for the same reason Parry has none: a hit is held or it isn't. Charges come from
                    // the shared stacking block below — this kind owns no multiplicity of its own.
                    NumberRow("window (s)", "How long the charges stay armed, and when the banked damage comes due. Every hit held during " + "the window is banked; the whole total lands at once the moment the window ends.\n\n" + "Brace DELAYS damage, it does not prevent it. Each CHARGE holds back ONE hit — exactly like " + "Parry, except a parry erases the hit and a brace only postpones it. Out of charges the next " + "hit lands normally, and charges come from the stacking block below.\n\n" + "When the window ends everything banked lands as ONE true-damage hit; a consumer skill must " + "cash the debt before it comes due. What gets banked is what would really have cost HP: Guard, " + "Vulnerability and Shield all apply BEFORE it is banked, and a parried hit never lands at all, " + "so it banks nothing.", ref ef.seconds, ef.useStacks);
                    break;
                case SkillEffectKind.Parry:
                    // No magnitude on purpose: a parry is binary (the hit is erased or it isn't), so a "how much"
                    // field would be a dead knob. Charges come from the stacking block below — the shared knob.
                    NumberRow("window (s)", "How long the caster waits for a hit to parry. The charge is spent by the HIT, not by this clock — " + "the window only decides how long the read stays open if nothing comes.\n\n" + "Parry is not Guard: Guard is a % cut on every hit in its window, while Parry fully ERASES one " + "hit (0 damage, before defence) and is SPENT by it, then fires the OnParry trigger. One charge " + "= blocks the next hit; for more, turn on stacking below.", ref ef.seconds, ef.useStacks);
                    break;
                case SkillEffectKind.Taunt:
                    NumberRow("seconds", "How long enemies are forced to attack the caster.", ref ef.seconds, ef.useStacks);
                    break;
                case SkillEffectKind.Teleport:
                    DrawStarCorners(ef);
                    break;   // destination = the aimed point (see the summary below); only the FIGURE is authored
                case SkillEffectKind.Dash:
                    DrawStarCorners(ef);
                    NumberRow("dash speed ×", "How fast the caster rushes to the aimed point, as a multiple of run speed (8 = 8× run speed). Higher = snappier gap-close.", ref ef.dashSpeedMult, ef.useStacks);
                    ef.dashEaseIn = EditorGUILayout.Toggle(new GUIContent("ease in",
                        "Accelerate from a slow start up to full speed over the ease-window seconds. Off = full speed instantly."), ef.dashEaseIn);
                    ef.dashEaseOut = EditorGUILayout.Toggle(new GUIContent("ease out",
                        "Decelerate to a gentle stop over the ease-window seconds before arrival. Off = full speed until arrival. (Both on = slow-fast-slow.)"), ef.dashEaseOut);
                    if (ef.dashEaseIn || ef.dashEaseOut)
                        NumberRow("ease window (s)", "How long each accelerate / decelerate ramp lasts. Time-based, so it reads the same at any distance; on a dash shorter than the window the ramps fill the whole trip.", ref ef.dashEaseSeconds, ef.useStacks);
                    // Dash only, deliberately: Blink arrives instantly, so a travel-length fade would have nothing to
                    // last for — the control isn't offered there rather than sitting in the Blink rows doing nothing.
                    NumberRow("caster opacity", "How see-through the mover's own body is WHILE IT TRAVELS — solid again the instant it lands, " + "so the fade is always exactly one hop long. 1 = solid (no fade).\n\n" + "Several fades can overlap on one body — the most see-through one wins, and the body returns " + "to solid when the last of them lets go.", ref ef.casterOpacity, ef.useStacks);
                    break;
                case SkillEffectKind.SubStatBuff:
                case SkillEffectKind.SubStatDebuff:
                {
                    bool worse = ef.kind == SkillEffectKind.SubStatDebuff;
                    ef.subStat = (SubStat)EditorGUILayout.EnumPopup(new GUIContent("sub stat",
                        "WHICH sub-stat this moves. Every timed sub-stat change is authored here — there is no separate verb for move speed, regen, leech or reflect any more."), ef.subStat);
                    // The amount field says what the number MEANS for the stat that is actually selected, because it
                    // differs: a percentage of the stat for most, a plain amount for the ones whose base is normally
                    // zero. One label for both would be wrong half the time.
                    NumberRow((worse ? "drop (" : "bonus (") + SubStats.UnitLabel(ef.subStat) + ")",
                        (worse
                            ? "How far the stat DROPS. Author it POSITIVE — the debuff subtracts it for you, so no minus signs.\n\n"
                            : "How far the stat RISES.\n\n")
                        + "On a STACKING one this is the PER-STACK amount, so ten 0.03 stacks = 0.3 in total. FLAT (no affinity): a support's value is UPTIME — duration scales off sustain — not a bigger number.",
                        ref ef.magnitude, ef.useStacks);
                    NumberRow("seconds", "Base duration — scales UP off sustain stats via the DURATION affinity below (high-MP support keeps it up longer).", ref ef.seconds, ef.useStacks);
                    break;
                }
                case SkillEffectKind.Cleanse:
                    NumberRow("debuffs removed", "How many active debuffs to strip.", ref ef.amount, ef.useStacks, whole: true);
                    break;
                case SkillEffectKind.Knockback:
                case SkillEffectKind.Pull:
                    NumberRow("shove speed", "How fast the enemy is pushed away / pulled in (world units per second).", ref ef.magnitude, ef.useStacks);
                    NumberRow("shove time (s)", "How long the shove MOVEMENT lasts — movement time, NOT a state duration (so it shows no icon).", ref ef.shoveSeconds, ef.useStacks);
                    break;
                case SkillEffectKind.Shield:
                    NumberRow("barrier HP", "Incoming HIT damage the shield soaks before it breaks (a finite pool, unlike Guard's %). Scales UP via the affinity channel below (sustain stats).", ref ef.amount, ef.useStacks, whole: true);
                    NumberRow("seconds", "How long the barrier lasts if not broken first.", ref ef.seconds, ef.useStacks);
                    break;
            }

            // ONE chance row for every effect that has one — drawn here rather than inside each case, so a new kind
            // inherits it instead of needing its own copy. Which kinds have one is SkillEffect.RollsChance, the same
            // answer the runtime roll uses.
            if (SkillEffect.RollsChance(ef.kind))
            {
                NumberRow("chance", "How likely this effect is to happen, rolled once per hit of this binding's Damage — so a 3-hit " + "strike gives it three chances. 100% (the default) = certain, every time.\n\n" + "The roll belongs to the EFFECT, so it happens the same way whatever moment ran the skill: a " + "press, a kill, a parry. An effect on the caster is applied once per landing, so it rolls once.\n\n" + "0% means it can never happen — the skill-data check reports that rather than let it rot.", ref ef.chance, ef.useStacks);
                if (ef.chance.value <= 0f)
                    EditorGUILayout.HelpBox("At 0% this effect can never happen. Raise it, or delete the effect.",
                        MessageType.Warning);
                else if (ef.chance.value < 1f)
                    EditorGUILayout.HelpBox("Below 100% this effect gains a \"boosts CHANCE\" channel under scaling & " +
                        "stacks — ring investment then makes it land more often, separately from whatever scales its " +
                        "own number.", MessageType.None);
            }

            // AFTER the kind's own fields, never before: this section's formula opens with that kind's BASE VALUE, and
            // a formula printed above the number it starts from is unreadable — the author meets the leading term with
            // no idea which field it came from.
            DrawStackValue(ef, sel);

            // An instant effect has NO duration — keep seconds 0 so the buff tray (which validates by duration) never
            // shows a phantom icon for it, and so switching a durational kind to an instant one clears the leftover.
            if (!HasDurationChannel(ef)) ef.seconds.value = 0f;

            // SCALING & STACKS live behind a fold, CLOSED by default: an effect's identity is its verb and its one or
            // two numbers, and those were being buried under ring-scaling rows and stacking knobs that most effects
            // never touch. Folded away, a plain damage effect reads in three lines instead of twenty. The state is
            // per-effect and editor-only (a dictionary keyed by the effect object), so nothing is saved into the skill.
            if (Foldout(ef, "  scaling & stacks"))
            {
                EditorGUI.indentLevel++;
                // Affinity channels, each labeled by WHAT it boosts: the effect's main output (damage, heal amount,
                // slow amount, …), its odds, its duration. A channel that scales nothing is hidden rather than shown
                // dead — a pure stun has no output number, and a certain effect has no odds to improve. Weights are
                // relative; empty = ×1 (no scaling).
                string affLabel = AffinityOutputLabel(ef);
                if (affLabel != null) DrawAffinity(affLabel, ref ef.affinity);
                if (SkillEffect.RollsChance(ef.kind) && ef.chance.value < 1f) DrawAffinity("boosts CHANCE", ref ef.chanceAffinity);
                if (HasDurationChannel(ef)) DrawAffinity("boosts DURATION", ref ef.durationAffinity);
                // A MODIFIER's numbers live in fields of their own (they describe another skill's pile rather than this
                // effect's body), so they can't ride the two rows above — but they are the same two channels, and there
                // is no reason a modifier should be the one kind whose numbers nothing is allowed to lift.
                if (ef.kind == SkillEffectKind.AddStacks) DrawAffinity("boosts STACKS ADDED", ref ef.affinity);
                if (ef.kind == SkillEffectKind.ExtendDuration) DrawAffinity("boosts SECONDS ADDED", ref ef.durationAffinity);
                DrawStacking(ef);
                if (ef.kind == SkillEffectKind.Damage) DrawAffinity("boosts HITS COUNT", ref ef.hitCountAffinity);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }
        if (rm >= 0) b.effects.RemoveAt(rm);
        if (casterSide) WarnDuplicateCasterEffects(b);
        // A new effect is born on the side whose button made it, so it can't appear in the other section.
        if (GUILayout.Button(casterSide ? "  + caster effect" : "  + target effect", GUILayout.MaxWidth(120)))
            b.effects.Add(new SkillEffect
            {
                kind = casterSide ? SkillEffectKind.SubStatBuff : SkillEffectKind.Damage,
                side = casterSide ? SkillEffect.EffectSide.Caster : SkillEffect.EffectSide.Target,
            });
    }

    /// <summary>The FIGURE successive landings trace. Each hop crosses to the far side of the target and steps on, so
    /// N hops in a row draw an N-pointed star THROUGH it instead of walking a ring around it. Only odd counts close,
    /// so an even one is nudged up rather than silently drawing an unfinished shape.</summary>
    static void DrawStarCorners(SkillEffect ef)
    {
        NumberRow("afterimage (s)", "Leave a frozen copy of the mover where it stood, fading out over this many seconds — a fast chain of " + "hops then reads as several of you at once. 0 = off. Only characters with a 3D body leave one.", ref ef.afterimageSeconds, ef.useStacks);
        if (ef.afterimageSeconds.value > 0f)
        {
            ef.afterimageTint = EditorGUILayout.ColorField(new GUIContent("  afterimage tint",
                "Colour of the frozen copy. Alpha = how solid it starts before fading to nothing.\n\n" +
                "The copy is unlit and casts no shadow, so it reads as a ghost rather than a second character, and it " +
                "is a SNAPSHOT — it holds the pose it was struck in and does not animate."), ef.afterimageTint);
        }

        NumberRow("star corners", "How many corners repeated landings trace. 5 = the classic five-pointed star (what Lunge draws today). " + "Only matters when the skill relocates more than once — a single hop has no figure to close.", ref ef.starCorners, ef.useStacks, whole: true);
        if ((Mathf.RoundToInt(ef.starCorners.value) & 1) == 0)
            EditorGUILayout.HelpBox($"Even counts never close the figure — this will run as {Mathf.RoundToInt(ef.starCorners.value) + 1}.",
                MessageType.Warning);
    }

    /// <summary>The ONE opt-in: "this effect's numbers may answer to the pile this cast is cashing."
    ///
    /// <para>It is a REVEAL, not a formula. Ticking it grows every number row on this effect by an operation and a
    /// reading, each number choosing for itself — so a strike can build on the pile while its duration ignores it.
    /// Untick it and every number goes back to being a plain number, which is what an effect is by default.</para></summary>
    static void DrawStackValue(SkillEffect ef, SkillDef sel)
    {
        ef.useStacks = EditorGUILayout.Toggle(new GUIContent("use stack variables",
            "ON = each number on this effect may ALSO answer to the pile this cast is cashing. A tick appears beside " +
            "every number; tick the ones that should pull, and each picks its own operation (Add / Subtract / " +
            "Multiply / Divide) and reading (Count / Carried total).\n\n" +
            "OFF = every number is a plain number. An effect never needs the pile to work."),
            ef.useStacks);
        if (!ef.useStacks) return;

        // The pile comes from the TRIGGER's gate — without one there is nothing to read, and every pulling number
        // would silently be 0.
        bool anyGate = false;
        if (sel != null && sel.triggers != null)
            for (int i = 0; i < sel.triggers.Count; i++)
                if (sel.triggers[i] != null && sel.triggers[i].requiresState) anyGate = true;
        if (!anyGate)
            EditorGUILayout.HelpBox("No trigger on this skill requires a state, so there is no pile to read — every " +
                "number set to pull would read 0. Set 'requires state' on a trigger above and name the granting skill.",
                MessageType.Warning);
        else if (OnlyReadsRunOfHits(sel))
            EditorGUILayout.HelpBox("This skill reads the automatic run of hits, which carries nothing — those numbers " +
                "must read Count. Carried total would be 0.", MessageType.None);
    }

    // Stacking knobs — only meaningful for a STATE (something with a duration), so they're hidden entirely for instant
    // effects rather than shown as dead fields. Stacks-per-apply IS the "charges" knob: 3 = one cast hands out 3.
    static void DrawStacking(SkillEffect ef)
    {
        if (!HasDurationChannel(ef)) { ef.stacking = false; return; }   // an instant effect can't be a stacking condition

        ef.stacking = EditorGUILayout.Toggle(new GUIContent("stacking",
            "OFF = one instance; re-applying replaces it (newest wins). ON = each application ADDS stacks, each with " +
            "its own duration and its own magnitude; the total is their SUM.\n\n" +
            "The magnitude above is then PER STACK. For Haste and Stat Buff it is always the BONUS, never a " +
            "multiplier: 0.03 means +3% per stack, so ten stacks read as +30%."), ef.stacking);
        if (!ef.stacking) return;

        EditorGUI.indentLevel++;

        ef.stacksFromAttackSpeed = EditorGUILayout.Toggle(new GUIContent("  count from attack speed",
            "ON = the count is NOT authored. It is worked out in-game from how many times the caster would swing " +
            "during this effect's own duration, so a faster caster gets MORE stacks in the SAME window. The two " +
            "knobs below no longer apply — the count is whatever the caster's speed earns."), ef.stacksFromAttackSpeed);

        // The two authored knobs describe a hand-sized pile. A speed-earned count replaces both, so they're shown
        // greyed rather than hidden: the author can still see the numbers the effect falls back to.
        using (new EditorGUI.DisabledScope(ef.stacksFromAttackSpeed))
        {
            NumberRow("  max stacks", "Held at once. Past this the OLDEST stack drops.", ref ef.stackLimit, ef.useStacks, whole: true);
            NumberRow("  stacks per use", "How many stacks ONE application grants — the 'charges' knob. 3 = a cast hands out 3 charges sharing one " + "window, spent one at a time.", ref ef.stacksPerApply, ef.useStacks, whole: true);
        }

        if (ef.stacksFromAttackSpeed)
        {
            NumberRow("  share of the count", "This effect's share. 1 = the full count; 0.5 = half of it, rounded up. Two effects on this skill " + "with the SAME duration stay in exact proportion at every attack speed — half is always half, and " + "changing the duration moves both on its own.", ref ef.stackShare, ef.useStacks);

            // Preview from the LIVE player, the same way every other estimate in this window is quoted — an authored
            // count is now a rate, and a rate with no worked example is a knob you can't reason about.
            float swing = Character.Instance != null ? Character.Instance.SwingTime : 0f;
            string live = swing > 0f
                ? $"Right now ({swing:0.00}s per swing): {Mathf.Max(1, Mathf.CeilToInt(ef.seconds.value / swing * Mathf.Max(0f, ef.stackShare.value)))} stacks."
                : "Enter Play mode to see the live count.";
            EditorGUILayout.HelpBox(
                $"{ef.seconds.value:0.#}s window ÷ seconds-per-swing × {Mathf.Max(0f, ef.stackShare.value):0.##}, rounded up.\n{live}",
                MessageType.None);
        }

        EditorGUI.indentLevel--;
    }

    // One plain-English sentence per effect kind, shown as the grey note at the bottom of every effect so the author
    // always sees what it DOES without decoding the fields. Keep these simple and jargon-free.
    static string EffectSummary(SkillEffectKind k)
    {
        switch (k)
        {
            case SkillEffectKind.Damage:        return "Deals damage to whoever this tick hits. The ATK/MATK multiplier below says how many of the caster's own hits it is worth.";
            case SkillEffectKind.Heal:          return "Restores a flat chunk of HP to the caster.";
            case SkillEffectKind.Status:        return "Inflicts an ailment (damage-over-time, stun, slow, …) for its duration.";
            case SkillEffectKind.Parry:         return "Fully negates the next incoming hit (0 damage) and is spent by it — a timing read, not a damage cut. Fires the OnParry trigger.";
            case SkillEffectKind.Brace:         return "Each charge holds ONE incoming hit back instead of erasing it; the banked damage all lands when the window ends. A delay, not a save.";
            case SkillEffectKind.BoostOutgoingDamage:
                                                return "Each charge makes ONE outgoing attack hit harder and is spent by it, hit or miss. Every kind of attack counts except damage-over-time ticks.";
            case SkillEffectKind.ImmuneToDisables:
                                                return "Nothing stops the holder for a while: no flinch, stun, freeze, silence, knockback, pull, and hits can't cancel their casts.";
            case SkillEffectKind.ReduceOutgoingDamage:
                                                return "Each charge makes ONE attack sent by whoever holds it land weaker, and is spent by it, hit or miss. Its own pile — it never spends a boost charge.";
            case SkillEffectKind.Taunt:         return "Forces enemies in range to attack the caster for a while.";
            case SkillEffectKind.Teleport:      return "Blinks the caster INSTANTLY to the aimed point (use Ground aim). No fields — the destination is the aimed point.";
            case SkillEffectKind.Dash:          return "Rushes the caster FAST to the aimed point (use Ground aim) — visible travel, blocked by walls. Dash speed sets how fast; the skill's range is the max distance.";
            case SkillEffectKind.SubStatBuff:   return "Temporarily RAISES one sub-stat — pick which below. Every timed sub-stat change lives here: movement speed, HP and MP regen, leech, reflect, evasion, all of them.";
            case SkillEffectKind.SubStatDebuff: return "Temporarily LOWERS one sub-stat — the same list as the buff. Author the amount as a positive number; it is subtracted for you.";
            case SkillEffectKind.Cleanse:       return "Removes active debuffs from the caster.";
            case SkillEffectKind.Execute:       return "Bonus damage that grows as the target's HP drops — a finisher, tiny at full HP.";
            case SkillEffectKind.Knockback:     return "Shoves enemies AWAY from the caster on hit.";
            case SkillEffectKind.Pull:          return "Drags enemies TOWARD the caster on hit.";
            case SkillEffectKind.Shield:        return "Raises a barrier that soaks a set amount of incoming damage before it breaks.";
            case SkillEffectKind.ExtendDuration: return "Gives another skill's running pile more time — every stack of it lasts longer.";
            case SkillEffectKind.AddStacks:     return "Adds stacks to another skill's running pile. The only way one skill can feed another's charges.";
            default:                            return "";
        }
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
            case SkillEffectKind.Shield:        return "boosts BARRIER HP (sustain: HP/Def/MP)";
            // A sub-stat change's amount is FLAT — no affinity channel (round-7 rule); only its DURATION scales
            // (off sustain). One rule for every sub-stat, so none of them can quietly become affinity-scaled.
            case SkillEffectKind.SubStatBuff:
            case SkillEffectKind.SubStatDebuff: return null;
            case SkillEffectKind.Status:
                // The odds live in their own channel now, so this one always names the ailment's own number.
                switch (e.ailment)
                {
                    case Ailment.Burn: case Ailment.Poison: case Ailment.Bleed: return "boosts DAMAGE / TICK";
                    default:            return null;   // stun / freeze / silence: binary, nothing to scale
                }
            default: return null;   // Taunt / Teleport / Cleanse have no scalable output
        }
    }

    /// <summary>WHAT this effect's main number IS, named the way its own row names it — the label for the row that
    /// lets the cast's pile lift it. Deliberately NOT <see cref="AffinityOutputLabel"/>: that one answers a different
    /// question ("which numbers may your BUILD lift"), and several kinds are flat against the ring on purpose while
    /// still being fair game for what a cast earned this fight. Null = this kind has no output number at all.</summary>
    static readonly string[] CountOnlyRead = { "Count" };

    /// <summary>True when every gate on this skill reads the automatic run of hits — the case where "carried total"
    /// has no meaning at all. False the moment ONE gate reads a real pile, because the reading is chosen per effect
    /// and a mixed skill can legitimately want the other answer.</summary>
    static bool OnlyReadsRunOfHits(SkillDef d)
    {
        if (d?.triggers == null) return false;
        bool any = false;
        for (int i = 0; i < d.triggers.Count; i++)
        {
            var e = d.triggers[i];
            if (e == null || !e.requiresState) continue;
            if (!StateGate.ReadsRunOfHits(e)) return false;
            any = true;
        }
        return any;
    }

    // ---- THE number row -----------------------------------------------------------------------------------------
    /// <summary>ONE authored number, drawn the same way everywhere it appears — the whole point of the shape: an
    /// author learns one row and then knows every number on every effect.
    ///
    /// <para>A plain field by default. When the effect has opted in to the pile, the row grows a small tick and, once
    /// ticked, the two dropdowns that say HOW this number meets the pile — operation, then reading — sitting beside
    /// the number they belong to rather than in a section of their own.</para>
    ///
    /// <para>Every control is a MAX width with no floor, so the row shrinks with a narrow window instead of forcing a
    /// horizontal scrollbar across the whole composer.</para></summary>
    static void NumberRow(GUIContent label, ref ScaledNumber n, bool effectUsesStacks, bool whole = false)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        n.value = whole
            ? EditorGUILayout.IntField(Mathf.RoundToInt(n.value), GUILayout.MinWidth(0f))
            : EditorGUILayout.FloatField(n.value, GUILayout.MinWidth(0f));
        if (effectUsesStacks)
        {
            n.useStacks = EditorGUILayout.Toggle(n.useStacks, GUILayout.MaxWidth(16f), GUILayout.MinWidth(0f));
            if (n.useStacks)
            {
                n.op   = (StackOp)EditorGUILayout.EnumPopup(n.op, GUILayout.MaxWidth(84f), GUILayout.MinWidth(0f));
                n.read = (StackReading)EditorGUILayout.EnumPopup(n.read, GUILayout.MaxWidth(104f), GUILayout.MinWidth(0f));
                // The reading is a raw tally; this is what puts it in the same units as the number it is meeting, so it
                // sits immediately after the reading it scales and reads as part of the same term.
                GUILayout.Label("×", GUILayout.MaxWidth(12f), GUILayout.MinWidth(0f));
                n.scaler = EditorGUILayout.FloatField(n.scaler, GUILayout.MaxWidth(56f), GUILayout.MinWidth(0f));
            }
        }
        else n.useStacks = false;   // the controls are gone, so the setting cannot linger unseen behind them
        EditorGUILayout.EndHorizontal();
    }

    static void NumberRow(string label, string tip, ref ScaledNumber n, bool uses, bool whole = false)
        => NumberRow(new GUIContent(label, tip), ref n, uses, whole);

    /// <summary>WHAT this effect's number IS, named the way its own row names it — used to word the worked example
    /// under 'use stack value' so it says "HP restored" or "shove speed" instead of a generic word.</summary>
    static string StackOutputLabel(SkillEffect e)
    {
        switch (e.kind)
        {
            // A damage effect's number is a MULTIPLIER of the caster's own roll, never an amount of damage — saying
            // "× ATK" is the difference between a line the author can act on and one they have to decode.
            case SkillEffectKind.Damage:
            case SkillEffectKind.Execute:              return e.damageType == DamageType.Magic && !e.resolveAsBasicAttack
                                                            ? "× MATK" : "× ATK";
            case SkillEffectKind.Heal:                 return "HP RESTORED";
            case SkillEffectKind.BoostOutgoingDamage:  return "DAMAGE BONUS";
            case SkillEffectKind.ReduceOutgoingDamage: return "DAMAGE CUT";
            // Named for the STAT the author picked, so the row says what it moves rather than "bonus".
            case SkillEffectKind.SubStatBuff:
            case SkillEffectKind.SubStatDebuff:        return ObjectNames.NicifyVariableName(e.subStat.ToString()).ToUpperInvariant();
            case SkillEffectKind.Cleanse:              return "DEBUFFS REMOVED";
            case SkillEffectKind.Knockback:
            case SkillEffectKind.Pull:                 return "SHOVE SPEED";
            case SkillEffectKind.Shield:               return "BARRIER HP";
            case SkillEffectKind.AddStacks:            return "STACKS ADDED";
            case SkillEffectKind.Status:
                switch (e.ailment)
                {
                    case Ailment.Burn: case Ailment.Poison: case Ailment.Bleed: return "DAMAGE / TICK";
                    default:            return null;   // stun / freeze / silence are binary — no number to lift
                }
            // Extend Duration's only number is the seconds it adds, which is the DURATION row below, not an output.
            // Teleport, Dash, Parry, Brace, Immune To Disables and Taunt have no output number either.
            default: return null;
        }
    }

    // Which effects have a duration worth scaling (the second affinity channel).
    static bool HasDurationChannel(SkillEffect e)
    {
        switch (e.kind)
        {
            case SkillEffectKind.Status:
            case SkillEffectKind.SubStatBuff:
            case SkillEffectKind.SubStatDebuff:
            case SkillEffectKind.Parry:
            case SkillEffectKind.Brace:
            case SkillEffectKind.BoostOutgoingDamage:
            case SkillEffectKind.ReduceOutgoingDamage:
            case SkillEffectKind.ImmuneToDisables:
            case SkillEffectKind.Shield:
            case SkillEffectKind.Taunt:
                return true;
            default:
                return false;
        }
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
        if (list.Count > 0 && Mathf.Abs(sum - 100f) > 0.5f) SkillAffinityWeights.NormalizeTo100(list);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(8);
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.MaxWidth(190));
        if (list.Count > 0)
        {
            var prev = GUI.color;
            GUI.color = new Color(0.6f, 0.9f, 0.6f);
            EditorGUILayout.LabelField("(always = 100%)", GUILayout.MaxWidth(120));
            GUI.color = prev;
        }
        EditorGUILayout.EndHorizontal();

        int rm = -1, changed = -1;
        float changedVal = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            bool locked = SkillAffinityWeights.IsLocked(list, w.stat);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);

            var newStat = (StatId)EditorGUILayout.EnumPopup(w.stat, GUILayout.MaxWidth(110));
            if (newStat != w.stat)
            {
                SkillAffinityWeights.SetLocked(list, w.stat, false);   // carry the lock across a rename so it isn't silently dropped
                w.stat = newStat; list[i] = w;
                SkillAffinityWeights.SetLocked(list, newStat, locked);
            }

            bool nowLocked = GUILayout.Toggle(locked,
                new GUIContent("🔒", "Lock: keep this stat fixed while you drag the others"), GUILayout.MaxWidth(30));
            if (nowLocked != locked) SkillAffinityWeights.SetLocked(list, w.stat, nowLocked);

            using (new EditorGUI.DisabledScope(nowLocked))       // a locked stat's own slider is frozen too
            {
                EditorGUI.BeginChangeCheck();
                float sv = GUILayout.HorizontalSlider(w.weight, 0f, 100f, GUILayout.MaxWidth(110), GUILayout.Height(16));
                if (EditorGUI.EndChangeCheck()) { changed = i; changedVal = sv; }
            }

            GUILayout.Label($"{Mathf.RoundToInt(w.weight)}%", GUILayout.MaxWidth(38));
            if (GUILayout.Button("✕", GUILayout.MaxWidth(22))) rm = i;
            EditorGUILayout.EndHorizontal();
        }
        if (changed >= 0) SkillAffinityWeights.Redistribute(list, changed, changedVal);
        if (rm >= 0)
        {
            SkillAffinityWeights.SetLocked(list, list[rm].stat, false);
            list.RemoveAt(rm);
            SkillAffinityWeights.RebalanceUnlocked(list);
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        // A new stat enters at 0% (sum stays 100); the first stat in an empty channel takes the whole 100%.
        if (GUILayout.Button("+ stat", GUILayout.MaxWidth(90)))
            list.Add(new StatWeight { stat = StatId.MAtk, weight = list.Count == 0 ? 100f : 0f });
        EditorGUILayout.EndHorizontal();
    }

    struct TickInfo { public string id; public TickTiming when; public float interval; }

    static List<TickInfo> CollectTicks(VfxSequence seq)
    {
        var result = new List<TickInfo>();
        if (seq == null || seq.steps == null) return result;
        foreach (var st in seq.steps)
        {
            if (st == null || !st.HasTick) continue;
            if (TicksContain(result, st.tickId)) continue;   // one row per distinct id
            result.Add(new TickInfo { id = st.tickId, when = st.tickWhen, interval = st.tickInterval });
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
