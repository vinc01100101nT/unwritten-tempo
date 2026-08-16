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

    /// <summary>Rename a skill's id AND re-point everything that referenced the old one, in the same undo step — so an
    /// id is a label you can change, never a fragile string other assets silently depend on.
    ///
    /// <para>Sweeps every place a skill id is PERSISTED: a skill's <c>triggerSkillId</c> ("on cast complete of") and
    /// <c>requireSkillId</c> ("stacks from"), plus every Item's <c>grantedSkillIds</c> (a weapon hands its kit out by
    /// id, so a rename would otherwise strip that skill off the weapon). Refuses a blank id or one another skill
    /// already owns, since either would merge two skills into one reference.</para></summary>
    void RetargetSkillId(string oldId, string newId, SkillDef target)
    {
        if (target == null) return;
        newId = (newId ?? "").Trim();
        if (string.IsNullOrEmpty(newId))
        {
            EditorUtility.DisplayDialog("Rename skill id", "An id can't be empty — keeping the old one.", "OK");
            return;
        }
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i] != target && all[i].id == newId)
            {
                EditorUtility.DisplayDialog("Rename skill id",
                    $"\"{newId}\" is already used by {all[i].displayName}. Ids must be unique — keeping the old one.", "OK");
                return;
            }

        Undo.RecordObject(target, "Rename skill id");
        target.id = newId;
        EditorUtility.SetDirty(target);
        SyncFileNameToId(target);
        if (string.IsNullOrEmpty(oldId)) return;   // brand-new skill: nothing can point at it yet

        int skillRefs = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null) continue;
            bool touched = false;
            // Both ids a skill can hold live PER TRIGGER now, so the sweep walks every entry — miss one and that
            // trigger silently stops matching (or stops finding its stack pile) after a rename.
            if (s.triggers != null)
                for (int t = 0; t < s.triggers.Count; t++)
                {
                    var e = s.triggers[t];
                    if (e == null) continue;
                    if (e.triggerSkillId == oldId) { Undo.RecordObject(s, "Rename skill id"); e.triggerSkillId = newId; touched = true; }
                    if (e.requireSkillId == oldId) { Undo.RecordObject(s, "Rename skill id"); e.requireSkillId = newId; touched = true; }
                }
            if (touched) { EditorUtility.SetDirty(s); skillRefs++; }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Skill Composer] Renamed \"{oldId}\" → \"{newId}\"; re-pointed {skillRefs} skill reference(s). " +
                  "A weapon that already ROLLED the old id (in a save) simply drops it on load — kits are per-copy now.");
    }

    /// <summary>The file name ALWAYS follows the id — <c>skill.sword.parry</c> lives in <c>skill.sword.parry.asset</c>,
    /// no exceptions. One name, one meaning: browsing the folder tells you exactly what's authored, and no one has to
    /// open an asset to learn which skill it is. Every path that can change or mint an id calls this, so the two can't
    /// drift apart. Renaming through <see cref="AssetDatabase"/> carries the <c>.meta</c> along, so the GUID — and every
    /// scene/prefab/asset reference — survives untouched.</summary>
    static void SyncFileNameToId(SkillDef s)
    {
        if (s == null || string.IsNullOrEmpty(s.id)) return;
        string path = AssetDatabase.GetAssetPath(s);
        if (string.IsNullOrEmpty(path) || System.IO.Path.GetFileNameWithoutExtension(path) == s.id) return;
        string err = AssetDatabase.RenameAsset(path, s.id);
        if (!string.IsNullOrEmpty(err))
            Debug.LogWarning($"[Skill Composer] Couldn't rename \"{path}\" to \"{s.id}.asset\": {err}");
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
        RebuildGroups();
        foreach (var s in ungrouped) DrawRow(s, 0f, Color.clear);
        foreach (var g in groupNames)
        {
            var list = grouped[g];
            var hue = GroupHue(g);
            if (!groupOpen.TryGetValue(g, out bool open)) open = true;

            var headerRect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, Tint(hue, 0.30f));
            groupOpen[g] = open = EditorGUI.Foldout(headerRect, open, $"{g}  ({list.Count})", true);
            if (!open) continue;
            foreach (var s in list) DrawRow(s, 12f, Tint(hue, 0.14f));
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

    static Color Tint(float hue, float alpha)
    {
        var c = Color.HSVToRGB(hue, 0.65f, 0.9f);
        c.a = alpha;
        return c;
    }

    // Grouping for the left list: an id with three or more dot segments (skill.<group>.<name>) files under a foldout
    // named for the MIDDLE segment; a two-segment id (skill.<name>) stays flat above the groups. Rebuilt from the live
    // ids every repaint, so editing an id moves its row — and creates or removes the group — with no bookkeeping.
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

    void RebuildGroups()
    {
        ungrouped.Clear();
        groupNames.Clear();
        foreach (var list in grouped.Values) list.Clear();
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

    // Collapsible section headers — group the editor so it reads top-to-bottom (what it IS → how it's classified →
    // what it costs → how it looks → what it does). State is static so a section stays open/closed across repaints.
    static bool foId = true, foClass = true, foTrigger = true, foCost = true, foPresent = false, foGameplay = true;
    static bool Section(string title, ref bool open)
    {
        EditorGUILayout.Space(6);
        open = EditorGUILayout.Foldout(open, title, true);
        return open;
    }

    /// <summary>Draw ONE trigger entry — the moment, plus the state gate that firing through this moment must satisfy.
    /// Returns true if the author asked to delete it. The gate is drawn per entry, not per skill, because the two
    /// halves of a dual-trigger skill need different answers (Lunge on OnCast requires no charge; the same Lunge on
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
                return "A MOB DIED. Aims at the dying mob (announced before the corpse is cleaned up).";
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

    /// OnBasicAttackStart requires and spends one).</summary>
    bool DrawTriggerEntry(SkillTriggerEntry e, int index)
    {
        bool remove = false;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // The trigger IS the entry's name — no invented category label above it.
                e.trigger = (SkillTrigger)EditorGUILayout.EnumPopup(new GUIContent("trigger",
                    "WHEN this skill fires. OnCast = the player presses it (the only one that pays the MP + cooldown " +
                    "below); every other moment fires it free. Pick one and read the box beneath for exactly what it " +
                    "counts as — whether a miss, an absorbed hit or a parry fires it."), e.trigger);
                // The last entry can't be removed: a skill must start SOME way, and an empty list would silently read
                // as a plain OnCast skill — a state the author never chose.
                using (new EditorGUI.DisabledScope(sel.triggers.Count <= 1))
                    if (GUILayout.Button("Remove", GUILayout.Width(70f))) remove = true;
            }

            // Per-trigger help, not one blob covering all nine: an author only ever needs the one they picked, and the
            // edge cases (does a miss count? an absorbed hit? a parry?) are exactly what a general blurb leaves out.
            EditorGUILayout.HelpBox(TriggerHelp(e.trigger), MessageType.None);

            if (e.trigger == SkillTrigger.OnSkillCastComplete)
                e.triggerSkillId = DrawSkillPicker("  on cast complete of",
                    "The ACTIVE skill whose successful (uninterrupted) PLAYER cast fires this one. Only a press " +
                    "announces, so a skill auto-fired by one of its own triggers can't set this off. Lists saved " +
                    "skills under Resources/Skills; a skill can't be fired by itself.", e.triggerSkillId);

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
                "too, since most piles come from one.", e.requireSkillId, includePassives: true, allowSelf: true);
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
                "0 = NO GATE: fires with none and simply cashes whatever is there."),
                Mathf.Max(0, e.requireMinStacks), 0, UnitStates.MaxStacks);
            if (e.requireMinStacks <= 0)
                EditorGUILayout.HelpBox("No gate: this fires even on an empty pile — it just gets no payoff. " +
                    "(Deathblow, which must stay usable on a Sword that never rolled Momentum.)", MessageType.None);
            e.consumeAllStacks = EditorGUILayout.Toggle(new GUIContent("  consume ALL",
                "Spend every stack on use — a finisher cashing a whole pile."), e.consumeAllStacks);
            if (!e.consumeAllStacks)
                e.consumeStacks = EditorGUILayout.IntSlider(new GUIContent("  consume",
                    "How many stacks this use spends. 0 = spends nothing (a pure 'while X' condition)."),
                    Mathf.Max(0, e.consumeStacks), 0, UnitStates.MaxStacks);

            EditorGUILayout.HelpBox("This is the pile's COST, not its payoff — what the pile is WORTH is set on each " +
                "effect below (\"use stack value\"), so one cast can pay several effects out of the same pile.\n\n" +
                "The pile is snapshot when the cast STARTS and spent only after the effects have computed — so what " +
                "the effects are paid and what the cast costs read the same pile (a finisher sees the whole thing " +
                "it's about to cash).", MessageType.Info);
            EditorGUI.indentLevel--;
        }
        return remove;
    }

    // Dropdown of other skills' ids, returning the chosen one. Two callers with one difference: the TRIGGER picker
    // wants only OnCast actives (a passive isn't a cast you can key off), while the STACK-SOURCE picker must include
    // passives — the classic stack granter, Momentum, is one. Reads SkillCatalog (saved Resources/Skills assets); a
    // stale/unsaved pick stays visible so it isn't silently blanked.
    /// <summary>Every pile the given skill actually hands out — one entry per STACKING effect that creates a state
    /// (<see cref="SkillEffect.TryStateFor"/>). Reused by the picker below and by the sweeps that keep consumers in
    /// step, so "what piles does this skill grant" is answered in exactly one place.
    ///
    /// <para>Non-stacking effects are excluded on purpose: a single-instance state has no pile to spend down, so
    /// gating on it could never mean "how many do I hold".</para></summary>
    static void CollectGrantedStates(SkillDef granter, List<StateId> into)
    {
        into.Clear();
        if (granter?.bindings == null) return;
        for (int b = 0; b < granter.bindings.Count; b++)
        {
            var fx = granter.bindings[b]?.effects;
            if (fx == null) continue;
            for (int i = 0; i < fx.Count; i++)
            {
                var ef = fx[i];
                if (ef == null || !ef.stacking) continue;
                if (SkillEffect.TryStateFor(ef, out var id) && !into.Contains(id)) into.Add(id);
            }
        }
    }

    static readonly List<StateId> grantedScratch = new List<StateId>(4);
    static readonly List<StateId> grantedBefore = new List<StateId>(4);
    static readonly List<StateId> grantedAfter = new List<StateId>(4);

    /// <summary>Keep every consumer of THIS skill's piles honest after its stacking effects were edited. A gate names
    /// a pair — whose pile, and which — so changing what this skill grants would otherwise leave other skills pointing
    /// at a pile that no longer exists, and they would silently stop firing.
    ///
    /// <para>Diffed against the last SAVED version, so the rule is simple and predictable: exactly one pile gone and
    /// exactly one new one is an EDIT of that pile (consumers follow it); anything else that removes a pile is a
    /// DELETION (consumers lose that gate rather than keep a reference that can never match). Run at Save, next to
    /// the other whole-library fixups, because it writes to OTHER assets.</para></summary>
    void SyncConsumersToGrantedStates(SkillDef granter, SkillDef before)
    {
        if (granter == null || before == null || string.IsNullOrEmpty(granter.id)) return;
        CollectGrantedStates(before, grantedBefore);
        CollectGrantedStates(granter, grantedAfter);

        StateId? renamedFrom = null, renamedTo = null;
        int gone = 0;
        for (int i = 0; i < grantedBefore.Count; i++)
            if (!grantedAfter.Contains(grantedBefore[i])) { gone++; renamedFrom = grantedBefore[i]; }
        int fresh = 0;
        for (int i = 0; i < grantedAfter.Count; i++)
            if (!grantedBefore.Contains(grantedAfter[i])) { fresh++; renamedTo = grantedAfter[i]; }
        if (gone == 0) return;                       // nothing lost — every existing gate still matches
        bool isEdit = gone == 1 && fresh == 1;

        int followed = 0, dropped = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s?.triggers == null || s == granter) continue;
            bool touched = false;
            for (int t = 0; t < s.triggers.Count; t++)
            {
                var e = s.triggers[t];
                if (e == null || !e.requiresState || e.requireSkillId != granter.id) continue;
                if (grantedAfter.Contains(e.requireStateId)) continue;   // its pile survived untouched
                Undo.RecordObject(s, "Sync stack gate");
                if (isEdit && e.requireStateId.Equals(renamedFrom.Value))
                {
                    e.requireStateId = renamedTo.Value; followed++;
                }
                else
                {
                    // The pile it was spending is gone. Disarm the gate rather than leave it pointing at nothing.
                    e.requiresState = false; e.requireSkillId = ""; dropped++;
                }
                touched = true;
            }
            if (touched) EditorUtility.SetDirty(s);
        }
        if (followed + dropped == 0) return;
        Debug.Log($"[Skill Composer] \"{granter.id}\" changed what it stacks — " +
                  $"{followed} gate(s) followed the change, {dropped} lost their gate (the pile they spent is gone).");
    }

    /// <summary>The "which pile" half of the gate, listing only what the chosen granter really stacks. Returns false
    /// when the granter stacks nothing — there is then no pile to point at, and saying so is more useful than showing
    /// a dropdown of states that will never exist.</summary>
    static bool DrawGrantedStatePicker(SkillTriggerEntry e)
    {
        var granter = SkillCatalog.Get(e.requireSkillId);
        CollectGrantedStates(granter, grantedScratch);
        if (grantedScratch.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"{(granter != null ? granter.displayName : e.requireSkillId)} doesn't stack anything, so it leaves no " +
                "pile to read. Turn on 'stacking' for one of ITS effects first, or gate on a different skill.",
                MessageType.Warning);
            return false;
        }

        var labels = new string[grantedScratch.Count];
        for (int i = 0; i < grantedScratch.Count; i++) labels[i] = grantedScratch[i].ToString();
        int idx = Mathf.Max(0, grantedScratch.IndexOf(e.requireStateId));
        idx = EditorGUILayout.Popup(new GUIContent("  which pile",
            "WHICH of that skill's stacking states this reads. A skill can grant more than one — charges to spend and " +
            "parry charges to survive on — and the two must never be counted together or spent for each other. Only " +
            "the piles that skill really stacks are listed."), idx, labels);
        e.requireStateId = grantedScratch[idx];
        if (grantedScratch.Count == 1)
            EditorGUILayout.HelpBox($"{labels[0]} is the only pile this skill stacks.", MessageType.None);
        return true;
    }

    /// <summary>Pick a skill by id. <paramref name="allowSelf"/> separates the two questions this one popup answers:
    /// WHICH SKILL FIRES this one (no — a skill announcing its own cast would re-fire itself forever) versus WHOSE
    /// PILE this one reads (yes — a skill that grants its own charges and spends them over its own repeats is
    /// self-contained, not a loop: the repeats replay the cast, they never start a new one).</summary>
    string DrawSkillPicker(string label, string tooltip, string current, bool includePassives = false, bool allowSelf = false)
    {
        var ids = SkillCatalog.OrderedIds();
        var choices = new List<string> { "" };
        for (int i = 0; i < ids.Length; i++)
        {
            var d = SkillCatalog.Get(ids[i]);
            if (d == null || (d.IsPassive && !includePassives)) continue;
            if (!allowSelf && sel != null && ids[i] == sel.id) continue;   // a skill can't be FIRED by itself
            choices.Add(ids[i]);
        }
        if (!string.IsNullOrEmpty(current) && !choices.Contains(current)) choices.Add(current);

        var labels = new string[choices.Count];
        for (int i = 0; i < choices.Count; i++)
        {
            if (string.IsNullOrEmpty(choices[i])) { labels[i] = "(none)"; continue; }
            var d = SkillCatalog.Get(choices[i]);
            labels[i] = d != null ? $"{d.displayName}  ({choices[i]})" : choices[i];
        }

        int idx = Mathf.Max(0, choices.IndexOf(current ?? ""));
        // The tooltip is the CALLER's: this one popup answers two different questions, and a shared hint would
        // describe the wrong one at whichever site didn't write it.
        int next = EditorGUILayout.Popup(new GUIContent(label, tooltip), idx, labels);
        return choices[next];
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
                if (composed != beforeId) RetargetSkillId(beforeId, composed, sel);
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
                var pickRect = GUILayoutUtility.GetRect(new GUIContent("Skill Icons ▾"), EditorStyles.miniButton, GUILayout.Width(96));
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
        if (Section("Classification  (verb · family · tier)", ref foClass))
        {
            sel.archetype = (SkillArchetype)EditorGUILayout.EnumPopup(new GUIContent("archetype (verb)",
                "Battlefield-role VERB — what the skill DOES. Drives the hover card chip + color (Burst / Tempo / Fortress / Buff / Curse)."), sel.archetype);
            sel.family = (SkillFamily)EditorGUILayout.EnumPopup(new GUIContent("family",
                "Attribute CLUSTER (Might / Arcane / Finesse) — the RPS-triangle side; also the default damage-type source. Orthogonal to the verb."), sel.family);
            sel.tier = (SkillTier)EditorGUILayout.EnumPopup(new GUIContent("tier",
                "Base = a normal hotbar skill (keys 1–9). Ultimate = fires from the dedicated ULT slot, charge-gated."), sel.tier);
            if (sel.tier == SkillTier.Ultimate)   // charge cost only exists for an Ultimate; a Base skill uses cooldown
                sel.chargeCost = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("charge cost",
                    "Ult-meter charge spent per cast. The meter fills in combat (dealing/taking damage, casting)."), sel.chargeCost));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Designer metadata (not gameplay)", EditorStyles.miniLabel);
            sel.complexity = (SkillComplexity)EditorGUILayout.EnumPopup(new GUIContent("complexity (meta)",
                "Designer metadata only (not gameplay) — authoring complexity, for library search."), sel.complexity);
            sel.acquisitionTier = (AcquisitionTier)EditorGUILayout.EnumPopup(new GUIContent("acq. tier (meta)",
                "Designer metadata only (not gameplay) — early / mid / late progression bucket."), sel.acquisitionTier);
            sel.shape = (SkillShape)EditorGUILayout.EnumPopup(new GUIContent("shape (meta)",
                "LABEL ONLY — no gameplay reads it. Real targeting is each tick's 'targets' dropdown below. Kept for library filtering."), sel.shape);
        }

        // 2b) TRIGGERS — every way this skill can start, each with its own state gate. One OnCast entry = a pressed
        //     active; no OnCast entry = a pure passive; BOTH = pressed AND self-firing, from ONE payload.
        if (Section("Triggers", ref foTrigger))
        {
            if (sel.triggers == null) sel.triggers = new List<SkillTriggerEntry>();
            if (sel.triggers.Count == 0)
                sel.triggers.Add(new SkillTriggerEntry());   // never show an empty list: a skill always starts SOME way

            for (int i = 0; i < sel.triggers.Count; i++)
            {
                if (sel.triggers[i] == null) sel.triggers[i] = new SkillTriggerEntry();
                if (DrawTriggerEntry(sel.triggers[i], i)) { sel.triggers.RemoveAt(i); i--; }
            }

            if (GUILayout.Button("Add trigger"))
                sel.triggers.Add(new SkillTriggerEntry { trigger = SkillTrigger.OnDamageDealt });

            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox("EVERY trigger runs the SAME skill body — a trigger chooses only WHEN this fires " +
                "and what that firing costs in stacks, never what it does. If two triggers want different effects, " +
                "that's two skills wearing one name: split them.\n\n" +
                "OnCast pays the MP + cooldown below; every other trigger fires free — the trigger IS the gesture. A " +
                "run started by a non-OnCast trigger does NOT count as 'you cast this skill', so a partner keyed to " +
                "OnSkillCastComplete can't be re-fed by this skill's own self-fire.", MessageType.Info);

            if (sel.IsPassive)
                EditorGUILayout.HelpBox("No OnCast trigger — this skill takes NO hotbar slot. Cost / cadence / aim " +
                    "below are ignored; author the payload in the Gameplay section. It never fires off a chain it's " +
                    "already part of, so it can't feed itself.", MessageType.Info);
        }

        // 3) COST · CADENCE · AIM — what it costs to fire and WHERE it lands (aim is the cast gesture; each tick's
        //    'applies to' below is WHO it affects there — kept distinct on purpose).
        if (Section("Cost · Cadence · Aim", ref foCost))
        {
            sel.manaCost = EditorGUILayout.IntField(new GUIContent("mana cost",
                "MP spent each time the skill is cast. The cast is blocked if you don't have enough."), sel.manaCost);
            if (sel.tier != SkillTier.Ultimate)   // an Ultimate is gated by its charge meter, not a cooldown
                sel.cooldown = EditorGUILayout.Slider(new GUIContent("cooldown (s)",
                    "Seconds before the skill can be used again after casting."), sel.cooldown, 0f, 30f);
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

        // 4) PRESENTATION & HOTBAR — how it looks + where it sits on the bar.
        if (Section("Presentation & Hotbar", ref foPresent))
        {
            sel.vfx = (VfxSequence)EditorGUILayout.ObjectField(new GUIContent("vfx sequence",
                "The composed look from the Vfx Composer. Its exposed ticks appear in Gameplay below."), sel.vfx, typeof(VfxSequence), false);
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
                        "started. So the run walks target to target. Nothing living in range ENDS the run."),
                        sel.repeatReaimRange));
                    EditorGUILayout.HelpBox("Each beat hits ONE target. The range is re-measured from whoever was " +
                        "just struck, so the run chains outward from target to target and stops the moment there is " +
                        "nothing living within reach of the last one.", MessageType.None);
                }
            }


            // Projectile fan — only when the VFX actually launches a projectile (a Carrier step). Compose ONE projectile
            // in the Vfx Composer; multiply it here, like repeat.
            if (sel.IsProjectileAim)
            {
                sel.projectileCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("projectiles",
                    "Fire this many copies of the projectile at once, fanned into a cone around the aim. 1 = single."), sel.projectileCount));
                using (new EditorGUI.DisabledScope(sel.projectileCount <= 1))
                    sel.projectileSpread = EditorGUILayout.Slider(new GUIContent("fan spread °",
                        "Total cone angle the fan spreads across. 0 = all straight at the aim point."), sel.projectileSpread, 0f, 180f);
            }

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
            NormalizeAllAffinities(sel);
            EditorUtility.SetDirty(sel);
            SyncFileNameToId(sel);
            if (snapshotOwner == sel) SyncConsumersToGrantedStates(sel, snapshot);
            AssetDatabase.SaveAssets();
            SkillCatalog.Reload();
            CaptureSnapshot();
        }
        using (new EditorGUI.DisabledScope(!EditorUtility.IsDirty(sel)))
            if (GUILayout.Button("Discard changes", GUILayout.Height(24))) DiscardSkillEdits();
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
                if (a.kind == SkillEffectKind.Buff && a.buffStat != c.buffStat) continue;
                string label = a.kind == SkillEffectKind.Buff
                    ? "Stat Buff (" + ObjectNames.NicifyVariableName(a.buffStat.ToString()) + ")"
                    : ObjectNames.NicifyVariableName(a.kind.ToString());
                EditorGUILayout.HelpBox(
                    label + " is on this tick twice. Caster effects apply once EACH, so this one fires twice on every " +
                    "cast — delete one unless that's really the intent.", MessageType.Warning);
                return;
            }
        }
    }

    // The delivery dropdown is the SINGLE SOURCE OF TRUTH for "is this a projectile" — but the VFX's Carrier step has
    // its own visual "launch from caster" flag, and the two should agree (from-caster ⇒ Projectile; from-above ⇒ AoE).
    // We only READ the VFX here to warn on a mismatch — we never let it drive gameplay.
    void WarnDeliveryVfxMismatch(GroundDelivery delivery)
    {
        bool hasCarrier = false, fromCaster = false;
        if (sel.vfx != null && sel.vfx.steps != null)
            for (int i = 0; i < sel.vfx.steps.Count; i++)
            {
                var s = sel.vfx.steps[i];
                if (s != null && s.kind == VfxStepKind.Carrier) { hasCarrier = true; fromCaster = s.carrierFromCaster; break; }
            }

        if (delivery == GroundDelivery.Projectile && !(hasCarrier && fromCaster))
            EditorGUILayout.HelpBox(
                "Projectile delivery, but this skill's VFX doesn't launch a projectile from the caster. Turn ON " +
                "'launch from caster' on the VFX's Carrier step so the shot visually flies from the caster.",
                MessageType.Warning);
        else if (delivery != GroundDelivery.Projectile && hasCarrier && fromCaster)
            EditorGUILayout.HelpBox(
                "This skill's VFX launches from the caster (a projectile), but delivery is " + delivery +
                ". Set delivery to Projectile, or turn OFF 'launch from caster' on the VFX's Carrier step.",
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

    // ---- effect-kind cascading picker (State ▸ Buffs / Debuffs) --------------------------------------------------
    // A purely PRESENTATIONAL grouping over the flat SkillEffectKind (+ Ailment for the 8 Status debuffs). Rules:
    // "State" = any durational effect; Buffs are self-castable, Debuffs never self. This is organizational ONLY —
    // targeting stays on the affects checkboxes, unchanged. Order in each array = display order. No data-model change.
    // Top-level (non-STATE) effects: true instants + movement ACTIONS (Dash/Knockback/Pull). Their timer is MOVEMENT
    // time, not an ongoing condition, so they're not states and never show a tray/nameplate icon. Order = display order.
    static readonly SkillEffectKind[] InstantKinds =
        { SkillEffectKind.Damage, SkillEffectKind.Heal, SkillEffectKind.Execute, SkillEffectKind.Teleport,
          SkillEffectKind.Cleanse, SkillEffectKind.Dash, SkillEffectKind.Knockback, SkillEffectKind.Pull };
    static readonly SkillEffectKind[] BuffKinds =
        { SkillEffectKind.Buff, SkillEffectKind.Haste, SkillEffectKind.Guard, SkillEffectKind.Parry,
          SkillEffectKind.Brace, SkillEffectKind.Regen, SkillEffectKind.Shield, SkillEffectKind.ManaLeech,
          SkillEffectKind.Reflect };

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
        new DebuffOption("Vulnerability", SkillEffectKind.Vulnerability),
        new DebuffOption("Taunt", SkillEffectKind.Taunt),
        new DebuffOption("Poison", Ailment.Poison), new DebuffOption("Bleed", Ailment.Bleed),
        new DebuffOption("Burn", Ailment.Burn),     new DebuffOption("Slow", Ailment.Slow),
        new DebuffOption("Stun", Ailment.Stun),     new DebuffOption("Freeze", Ailment.Freeze),
        new DebuffOption("Silence", Ailment.Silence), new DebuffOption("Blind", Ailment.Blind),
    };

    // The menus above are split by SIDE, so each section only offers kinds it can actually run — one array pair per
    // side, grouped by the same SkillEffect.LandsOnCaster the runtime partitions on. Both sections offer every kind
    // and every state — a self-inflicted debuff (Overclock's own armour-fray) is as authorable as a buff on a mob.
    static SkillEffectKind[][] sideInstants, sideBuffs;     // [0] = target side, [1] = caster side
    static string[][] sideTopLabels, sideBuffLabels;
    static string[] typeLabels, debuffLabels;

    // Every kind is offered on BOTH sides: which side an effect lands on is now the AUTHOR's choice (SkillEffect.side),
    // stamped by whichever section the effect was added in — it is no longer a fixed property of the kind. Kept as a
    // pass-through (rather than deleted) so the two-side array shape below stays one code path.
    static SkillEffectKind[] FilterSide(SkillEffectKind[] src, bool caster) => src;

    static void EnsureKindLabels()
    {
        if (sideInstants != null) return;
        sideInstants = new SkillEffectKind[2][]; sideBuffs = new SkillEffectKind[2][];
        sideTopLabels = new string[2][]; sideBuffLabels = new string[2][];
        for (int s = 0; s < 2; s++)
        {
            bool caster = s == 1;
            var ins = FilterSide(InstantKinds, caster);
            var bfs = FilterSide(BuffKinds, caster);
            sideInstants[s] = ins; sideBuffs[s] = bfs;
            var top = new string[ins.Length + (bfs.Length > 0 ? 1 : 0)];
            for (int i = 0; i < ins.Length; i++) top[i] = ObjectNames.NicifyVariableName(ins[i].ToString());
            if (bfs.Length > 0) top[ins.Length] = "State ▸";
            sideTopLabels[s] = top;
            var bl = new string[bfs.Length];
            for (int i = 0; i < bfs.Length; i++)
                bl[i] = bfs[i] == SkillEffectKind.Buff ? "Stat Buff" : ObjectNames.NicifyVariableName(bfs[i].ToString());
            sideBuffLabels[s] = bl;
        }
        typeLabels = new[] { "Buffs", "Debuffs" };
        debuffLabels = new string[DebuffOptions.Length];
        for (int i = 0; i < DebuffOptions.Length; i++) debuffLabels[i] = DebuffOptions[i].label;
    }

    static int Side(bool caster) => caster ? 1 : 0;
    static bool IsInstantKind(SkillEffectKind k) { for (int i = 0; i < InstantKinds.Length; i++) if (InstantKinds[i] == k) return true; return false; }
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

    // Top-level popup (this side's instant kinds + "State ▸"), drawn inline on the effect header row. Restricted to
    // the section's own side, so an effect can never hop sections from under the author — to move one, delete and
    // re-add it in the other section.
    void DrawEffectKindTop(SkillEffect ef, bool casterSide)
    {
        EnsureKindLabels();
        int s = Side(casterSide);
        var kinds = sideInstants[s];
        bool isState = !IsInstantKind(ef.kind);
        int idx = isState ? kinds.Length : System.Array.IndexOf(kinds, ef.kind);
        if (idx < 0) idx = 0;
        int next = EditorGUILayout.Popup(idx, sideTopLabels[s]);
        if (next == idx) return;
        ef.kind = next < kinds.Length ? kinds[next] : sideBuffs[s][0];   // → State defaults to this side's first state
        StampSide(ef, casterSide);
    }

    // For a State effect, the Type (Buffs/Debuffs) + specific popups on their own rows. The caster side has no
    // debuffs at all, so it drops straight to the state picker instead of showing a one-option Type row.
    void DrawEffectStateBranch(SkillEffect ef, bool casterSide)
    {
        if (IsInstantKind(ef.kind)) return;
        EnsureKindLabels();
        int s = Side(casterSide);
        var buffs = sideBuffs[s];
        bool isBuff = IsBuffKind(ef.kind);

        {
            int typeIdx = isBuff ? 0 : 1;
            int typeNext = EditorGUILayout.Popup(new GUIContent("  type",
                "Buff = beneficial state. Debuff = harmful state. Organizational only — WHO it lands on is the targeting above."),
                typeIdx, typeLabels);
            if (typeNext != typeIdx) { ef.kind = typeNext == 0 ? buffs[0] : SkillEffectKind.Vulnerability; isBuff = typeNext == 0; StampSide(ef, casterSide); }
        }

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
        if (b.HasPointEffects)
            EditorGUILayout.LabelField("      → the cast asks you to aim a point; the caster is sent there",
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
                WarnDeliveryVfxMismatch(b.groundDelivery);
                if (b.groundDelivery != GroundDelivery.Point)
                {
                    b.radius = EditorGUILayout.Slider(new GUIContent("    radius",
                        "Circle size (world units) around the spot that catches entities. For a projectile skill this is its per-tick hit width."), b.radius <= 0f ? 1.5f : b.radius, 0.1f, 12f);
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
            if (GUILayout.Button("✕", GUILayout.Width(22))) rm = e;
            EditorGUILayout.EndHorizontal();
            DrawEffectStateBranch(ef, casterSide);   // State ▸ Buffs / Debuffs cascade (only for durational "state" effects)
            // Plain-English summary of what THIS effect does — directly under the kind dropdown, above its fields.
            EditorGUILayout.HelpBox(EffectSummary(ef.kind), MessageType.None);
            DrawStackValue(ef, sel);
            switch (ef.kind)
            {
                case SkillEffectKind.Damage:
                    ef.damageType = (DamageType)EditorGUILayout.EnumPopup(new GUIContent("damage type",
                        "Physical (ATK vs armour) / Magic (MATK, unmissable, no crit) / Crit (pierces armour, always crits)."), ef.damageType);
                    // Hidden only when the pile REPLACES the size (a carried total is dealt flat): a visible number that
                    // changes nothing is a lie about what this effect does. On the COUNT reading the pile ADDS to this
                    // number instead, so it stays live and stays shown.
                    if (!ef.useStackValue || ef.stackValueSource == StackReading.Count)
                        ef.damageMult = EditorGUILayout.Slider(new GUIContent("base power ×",
                            "The skill's INHERENT strength (Ember ~1×, Meteor ~8×). Affinity picks WHICH stats scale it; this sets HOW BIG. Final = caster ATK/MATK × base power × affinity."), ef.damageMult <= 0f ? 1f : ef.damageMult, 0.1f, 8f);
                    ef.element = (Element)EditorGUILayout.EnumPopup(new GUIContent("element",
                        "Attack element, checked against the target's armor element. Neutral = no matchup bonus/penalty."), ef.element);
                    ef.hitCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("hits",
                        "Strikes per landing. Each hit is the TRIGGER for this binding's proc effects (a Status or Execute with a chance)."), ef.hitCount));
                    break;
                case SkillEffectKind.Heal:
                    if (!ef.useStackValue)
                        ef.amount = EditorGUILayout.IntField(new GUIContent("HP", "Flat HP restored to the caster."), ef.amount);
                    break;
                case SkillEffectKind.Status:
                    // The ailment is chosen in the State ▸ Debuffs picker above — not repeated here (no double-knob).
                    ef.chance = EditorGUILayout.Slider(new GUIContent("chance",
                        "PROC chance rolled PER HIT of the binding's multi-hit Damage (e.g. a stun proc). 0 = not a proc — applies ONCE per landing. The 'affinity (chance)' channel below scales this."), ef.chance, 0f, 1f);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long the ailment lasts."), ef.seconds, 0.5f, 20f);
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
                    ef.element = (Element)EditorGUILayout.EnumPopup(new GUIContent("element",
                        "Attack element, checked against the target's armor element. Neutral = no matchup bonus/penalty."), ef.element);
                    break;
                case SkillEffectKind.Haste:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("speed bonus (0.3 = +30%)", "The BONUS, not a multiplier: 0.3 = +30% move speed. NEGATIVE slows instead (Haste's mob-debuff job): -0.4 = 40% slower."), ef.magnitude, -0.9f, 2f);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long the speed boost lasts."), ef.seconds, 0.5f, 20f);
                    break;
                case SkillEffectKind.Guard:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("damage cut", "Fraction of incoming damage prevented (0..1). 0.4 = take 40% less damage."), Mathf.Clamp01(ef.magnitude), 0f, 1f);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long the damage reduction lasts."), ef.seconds, 0.5f, 20f);
                    break;
                case SkillEffectKind.Brace:
                    // No magnitude, for the same reason Parry has none: a hit is held or it isn't. Charges come from
                    // the shared stacking block below — this kind owns no multiplicity of its own.
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("window (s)",
                        "How long the charges stay armed, and when the banked damage comes due. Every hit held during " +
                        "the window is banked; the whole total lands at once the moment the window ends."), ef.seconds, 0.5f, 20f);
                    EditorGUILayout.HelpBox("Brace DELAYS damage, it does not prevent it. Each CHARGE holds back ONE hit " +
                        "— exactly like Parry, except a parry erases the hit and a brace only postpones it. Out of " +
                        "charges, the next hit lands normally. Charges come from the stacking block below (1 = hold one " +
                        "hit), so stacking always buys something real.\n\n" +
                        "When the window ends, everything banked lands as ONE true-damage hit. Surviving is the " +
                        "player's job — a consumer skill must cash the debt before it comes due.\n\n" +
                        "What gets banked is what would really have cost HP: Guard, Vulnerability and Shield all apply " +
                        "BEFORE it is banked, and a parried hit never lands at all, so it banks nothing.",
                        MessageType.Info);
                    break;
                case SkillEffectKind.Parry:
                    // No magnitude on purpose: a parry is binary (the hit is erased or it isn't), so a "how much"
                    // field would be a dead knob. Charges come from the stacking block below — the shared knob.
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("window (s)",
                        "How long the caster waits for a hit to parry. The charge is spent by the HIT, not by this clock — " +
                        "the window only decides how long the read stays open if nothing comes."), ef.seconds, 0.1f, 10f);
                    EditorGUILayout.HelpBox("Parry ≠ Guard. Guard is a % cut on every hit in its window; Parry fully ERASES " +
                        "one hit (0 damage, before defence) and is SPENT by it, then fires the OnParry trigger for passives.\n\n" +
                        "One charge = 'blocks the next hit'. For more, turn on stacking below and set stacks-per-apply.",
                        MessageType.Info);
                    break;
                case SkillEffectKind.Taunt:
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long enemies are forced to attack the caster."), ef.seconds, 0.5f, 20f);
                    break;
                case SkillEffectKind.Teleport:
                    DrawStarCorners(ef);
                    break;   // destination = the aimed point (see the summary below); only the FIGURE is authored
                case SkillEffectKind.Dash:
                    DrawStarCorners(ef);
                    ef.dashSpeedMult = EditorGUILayout.Slider(new GUIContent("dash speed ×",
                        "How fast the caster rushes to the aimed point, as a multiple of run speed (8 = 8× run speed). Higher = snappier gap-close."),
                        ef.dashSpeedMult <= 0f ? 8f : ef.dashSpeedMult, 1f, 20f);
                    ef.dashEaseIn = EditorGUILayout.Toggle(new GUIContent("ease in",
                        "Accelerate from a slow start up to full speed over the ease-window seconds. Off = full speed instantly."), ef.dashEaseIn);
                    ef.dashEaseOut = EditorGUILayout.Toggle(new GUIContent("ease out",
                        "Decelerate to a gentle stop over the ease-window seconds before arrival. Off = full speed until arrival. (Both on = slow-fast-slow.)"), ef.dashEaseOut);
                    if (ef.dashEaseIn || ef.dashEaseOut)
                        ef.dashEaseSeconds = EditorGUILayout.Slider(new GUIContent("ease window (s)",
                            "How long each accelerate / decelerate ramp lasts. Time-based, so it reads the same at any distance; on a dash shorter than the window the ramps fill the whole trip."),
                            ef.dashEaseSeconds <= 0f ? 0.3f : ef.dashEaseSeconds, 0.05f, 1.5f);
                    break;
                case SkillEffectKind.Buff:
                    ef.buffStat = (BuffStat)EditorGUILayout.EnumPopup(new GUIContent("stat",
                        "Which self-stat is buffed. AMPLIFY buffs are FLAT — magnitude is NOT affinity-scaled, so a DPS can't out-buff via their offense."), ef.buffStat);
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("bonus (0.3 = +30%)",
                        "The BONUS, not a multiplier: 0.3 = +30%. On a STACKING buff this is the PER-STACK bonus, so ten 0.03 stacks = +30%. FLAT (no affinity). A support's value is UPTIME (duration ↓ scales off sustain) + the ramp, not a bigger number."), Mathf.Max(0f, ef.magnitude), 0f, 2f);
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
                    ef.magnitude = EditorGUILayout.FloatField(new GUIContent("HP / tick", "HP healed every 0.8s while active."), ef.magnitude);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long the heal-over-time lasts."), ef.seconds, 0.5f, 30f);
                    break;
                case SkillEffectKind.Cleanse:
                    ef.amount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("debuffs removed", "How many active debuffs to strip."), ef.amount));
                    break;
                case SkillEffectKind.Vulnerability:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("+damage taken", "Extra damage the marked enemy takes (0.3 = +30%)."), ef.magnitude, 0f, 2f);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long enemies stay marked as vulnerable."), ef.seconds, 0.5f, 20f);
                    break;
                case SkillEffectKind.Knockback:
                case SkillEffectKind.Pull:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("shove speed", "How fast the enemy is pushed away / pulled in (world units per second)."), ef.magnitude <= 0f ? 10f : ef.magnitude, 1f, 30f);
                    ef.shoveSeconds = EditorGUILayout.Slider(new GUIContent("shove time (s)", "How long the shove MOVEMENT lasts — movement time, NOT a state duration (so it shows no icon)."), ef.shoveSeconds <= 0f ? 0.25f : ef.shoveSeconds, 0.05f, 2f);
                    break;
                case SkillEffectKind.ManaLeech:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("MP leech", "Fraction of the damage you deal returned to you as MP (0..1). 0.15 = 15%."), Mathf.Clamp01(ef.magnitude), 0f, 1f);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long the leech window lasts."), ef.seconds, 0.5f, 30f);
                    break;
                case SkillEffectKind.Reflect:
                    ef.magnitude = EditorGUILayout.Slider(new GUIContent("reflect", "Fraction of the damage you take bounced back at the attacker (0..1). 0.3 = 30%."), Mathf.Clamp01(ef.magnitude), 0f, 1f);
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long the reflect window lasts."), ef.seconds, 0.5f, 30f);
                    break;
                case SkillEffectKind.Shield:
                    ef.amount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("barrier HP",
                        "Incoming HIT damage the shield soaks before it breaks (a finite pool, unlike Guard's %). Scales UP via the affinity channel below (sustain stats)."), ef.amount));
                    ef.seconds = EditorGUILayout.Slider(new GUIContent("seconds", "How long the barrier lasts if not broken first."), ef.seconds, 0.5f, 30f);
                    break;
            }

            // Affinity channels, each labeled by WHAT it boosts. The first scales the effect's main output (damage,
            // heal amount, proc chance, slow amount, …); the second scales its duration. A channel that scales nothing
            // (a pure stun has no output number) is hidden. Weights are relative; empty = ×1 (no scaling).
            string affLabel = AffinityOutputLabel(ef);
            if (affLabel != null) DrawAffinity(affLabel, ref ef.affinity);
            // An instant effect has NO duration — keep seconds 0 so the buff tray (which validates by duration) never
            // shows a phantom icon for it, and so switching a durational kind to an instant one clears the leftover.
            if (!HasDurationChannel(ef)) ef.seconds = 0f;
            if (HasDurationChannel(ef)) DrawAffinity("boosts DURATION", ref ef.durationAffinity);
            DrawStacking(ef);
            if (ef.kind == SkillEffectKind.Damage) DrawAffinity("boosts HITS COUNT", ref ef.hitCountAffinity);
            EditorGUILayout.EndVertical();
        }
        if (rm >= 0) b.effects.RemoveAt(rm);
        if (casterSide) WarnDuplicateCasterEffects(b);
        // A new effect is born on the side whose button made it, so it can't appear in the other section.
        if (GUILayout.Button(casterSide ? "  + caster effect" : "  + target effect", GUILayout.Width(120)))
            b.effects.Add(new SkillEffect
            {
                kind = casterSide ? SkillEffectKind.Guard : SkillEffectKind.Damage,
                side = casterSide ? SkillEffect.EffectSide.Caster : SkillEffect.EffectSide.Target,
            });
    }

    /// <summary>The FIGURE successive landings trace. Each hop crosses to the far side of the target and steps on, so
    /// N hops in a row draw an N-pointed star THROUGH it instead of walking a ring around it. Only odd counts close,
    /// so an even one is nudged up rather than silently drawing an unfinished shape.</summary>
    static void DrawStarCorners(SkillEffect ef)
    {
        ef.afterimageSeconds = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("afterimage (s)",
            "Leave a frozen copy of the mover where it stood, fading out over this many seconds — a fast chain of " +
            "hops then reads as several of you at once. 0 = off. Only characters with a 3D body leave one."),
            ef.afterimageSeconds));
        if (ef.afterimageSeconds > 0f)
        {
            ef.afterimageTint = EditorGUILayout.ColorField(new GUIContent("  afterimage tint",
                "Colour of the frozen copy. Alpha = how solid it starts before fading to nothing."), ef.afterimageTint);
            EditorGUILayout.HelpBox("The copy is unlit and casts no shadow, so it reads as a ghost rather than a " +
                "second character. It is a SNAPSHOT — it holds the pose it was struck in and does not animate.",
                MessageType.None);
        }

        ef.starCorners = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent("star corners",
            "How many corners repeated landings trace. 5 = the classic five-pointed star (what Lunge draws today). " +
            "Only matters when the skill relocates more than once — a single hop has no figure to close."),
            ef.starCorners));
        if ((ef.starCorners & 1) == 0)
            EditorGUILayout.HelpBox($"Even counts never close the figure — this will run as {ef.starCorners + 1}.",
                MessageType.Warning);
    }

    /// <summary>"Pull this effect's size from the pile this cast is cashing." Drawn only for the kinds whose output is
    /// a plain SIZE (see <see cref="SkillEffect.SupportsStackValue"/>) — on a duration or a proc chance there is no
    /// sensible conversion, so the control simply isn't offered rather than sitting there doing nothing.
    ///
    /// <para>The READING decides how the pile pays, because the two readings are different kinds of number: a carried
    /// total is already real damage, so it is dealt flat; a count is a tally, so it buys EXTRA POWER on this effect and
    /// still rolls through the caster's Attack. A heal has no roll to lift, so it takes the flat path either way.</para>
    ///
    /// <para>Several effects on one skill may each switch this on and read the SAME snapshot — that is the whole
    /// point: one cast can hit for the banked damage AND heal off it.</para></summary>
    static void DrawStackValue(SkillEffect ef, SkillDef sel)
    {
        if (!SkillEffect.SupportsStackValue(ef.kind)) { ef.useStackValue = false; return; }

        ef.useStackValue = EditorGUILayout.Toggle(new GUIContent("use stack value",
            "ON = the pile this cast cashes decides this effect's SIZE. HOW it pays depends on the reading below: a " +
            "carried total is dealt FLAT (it is already real damage, so it can't crit), while a COUNT adds power to " +
            "this effect and rolls through your Attack like any other hit."),
            ef.useStackValue);
        if (!ef.useStackValue) return;

        ef.stackValueSource = (StackReading)EditorGUILayout.EnumPopup(new GUIContent("  read",
            "Count = how many stacks the pile holds.\n\n" +
            "Carried total = the sum of what those stacks CARRY (a percentage pile reads as its decimal, +30% = 0.3)."),
            ef.stackValueSource);

        bool perStackPower = ef.kind == SkillEffectKind.Damage && ef.stackValueSource == StackReading.Count;
        ef.stackValueMult = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent(
            perStackPower ? "  power × per stack" : "  × multiplier",
            perStackPower
                ? "EXTRA power each stack adds, on top of this effect's own base power. 0.4 = +40% of your Attack per " +
                  "stack. Adds up, never compounds."
                : "What the reading is multiplied by to get this effect's output. 2.5 on a carried total of 400 = 1000."),
            ef.stackValueMult));

        string unit = ef.kind == SkillEffectKind.Heal ? "HP healed" : "damage";
        if (perStackPower)
        {
            float basePower = ef.damageMult <= 0f ? 1f : ef.damageMult;
            float per = Mathf.Max(0f, ef.stackValueMult);
            EditorGUILayout.HelpBox(
                $"At 10 stacks: {basePower * 100f:0}% + {per * 10f * 100f:0}% = {(basePower + per * 10f) * 100f:0}% Attack." +
                (per <= 0f ? "  (0 = the pile buys nothing.)" : ""), MessageType.None);
        }
        else
            EditorGUILayout.HelpBox(
                ef.stackValueSource == StackReading.CarriedTotal
                    ? $"Carrying 400 → {400f * Mathf.Max(0f, ef.stackValueMult):0} {unit}, flat."
                    : $"At 10 stacks → {10f * Mathf.Max(0f, ef.stackValueMult):0} {unit}, flat.",
                MessageType.None);

        // The pile comes from the TRIGGER's gate — without one there is nothing to read, and this would silently be 0.
        bool anyGate = false;
        if (sel != null && sel.triggers != null)
            for (int i = 0; i < sel.triggers.Count; i++)
                if (sel.triggers[i] != null && sel.triggers[i].requiresState) anyGate = true;
        if (!anyGate)
            EditorGUILayout.HelpBox("No trigger on this skill requires a state, so there is no pile to read — this " +
                "effect would land 0. Set 'requires state' on a trigger above and name the granting skill.",
                MessageType.Warning);
    }

    // Stacking knobs — only meaningful for a STATE (something with a duration), so they're hidden entirely for instant
    // effects rather than shown as dead fields. Stacks-per-apply IS the "charges" knob: 3 = one cast hands out 3.
    static void DrawStacking(SkillEffect ef)
    {
        if (!HasDurationChannel(ef)) { ef.stacking = false; return; }   // an instant effect can't be a stacking condition

        ef.stacking = EditorGUILayout.Toggle(new GUIContent("stacking",
            "OFF = one instance; re-applying replaces it (newest wins). ON = each application ADDS stacks, each with " +
            "its own duration and its own magnitude; the total is their SUM."), ef.stacking);
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
            ef.stackLimit = EditorGUILayout.IntSlider(new GUIContent("  max stacks",
                "Held at once. Past this the OLDEST stack drops."), Mathf.Max(1, ef.stackLimit), 1, UnitStates.MaxStacks);
            ef.stacksPerApply = EditorGUILayout.IntSlider(new GUIContent("  stacks per use",
                "How many stacks ONE application grants — the 'charges' knob. 3 = a cast hands out 3 charges sharing one " +
                "window, spent one at a time."), Mathf.Max(1, ef.stacksPerApply), 1, Mathf.Max(1, ef.stackLimit));
        }

        if (ef.stacksFromAttackSpeed)
        {
            ef.stackShare = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("  share of the count",
                "This effect's share. 1 = the full count; 0.5 = half of it, rounded up. Two effects on this skill " +
                "with the SAME duration stay in exact proportion at every attack speed — half is always half, and " +
                "changing the duration moves both on its own."), ef.stackShare));

            // Preview from the LIVE player, the same way every other estimate in this window is quoted — an authored
            // count is now a rate, and a rate with no worked example is a knob you can't reason about.
            float swing = Character.Instance != null ? Character.Instance.SwingTime : 0f;
            string live = swing > 0f
                ? $"Right now ({swing:0.00}s per swing): {Mathf.Max(1, Mathf.CeilToInt(ef.seconds / swing * Mathf.Max(0f, ef.stackShare)))} stacks."
                : "Enter Play mode to see the live count.";
            EditorGUILayout.HelpBox(
                $"{ef.seconds:0.#}s window ÷ seconds-per-swing × {Mathf.Max(0f, ef.stackShare):0.##}, rounded up.\n{live}",
                MessageType.None);
        }

        EditorGUILayout.HelpBox(
            "Magnitude above is PER STACK, and the total is the SUM.\n" +
            "For Haste and Stat Buff it is always the BONUS, never a multiplier — stacking or not: 0.03 = \"+3% per " +
            "stack\", so 10 stacks read as +30%.", MessageType.Info);
        EditorGUI.indentLevel--;
    }

    // One plain-English sentence per effect kind, shown as the grey note at the bottom of every effect so the author
    // always sees what it DOES without decoding the fields. Keep these simple and jargon-free.
    static string EffectSummary(SkillEffectKind k)
    {
        switch (k)
        {
            case SkillEffectKind.Damage:        return "Deals damage to whoever this tick hits. Scales off your ATK/MATK × base power.";
            case SkillEffectKind.Heal:          return "Restores a flat chunk of HP to the caster.";
            case SkillEffectKind.Status:        return "Inflicts an ailment (damage-over-time, stun, slow, …) for its duration.";
            case SkillEffectKind.Haste:         return "Temporarily speeds up movement.";
            case SkillEffectKind.Guard:         return "Temporarily reduces incoming damage to the caster.";
            case SkillEffectKind.Parry:         return "Fully negates the next incoming hit (0 damage) and is spent by it — a timing read, not a damage cut. Fires the OnParry trigger.";
            case SkillEffectKind.Brace:         return "Each charge holds ONE incoming hit back instead of erasing it; the banked damage all lands when the window ends. A delay, not a save.";
            case SkillEffectKind.Taunt:         return "Forces enemies in range to attack the caster for a while.";
            case SkillEffectKind.Teleport:      return "Blinks the caster INSTANTLY to the aimed point (use Ground aim). No fields — the destination is the aimed point.";
            case SkillEffectKind.Dash:          return "Rushes the caster FAST to the aimed point (use Ground aim) — visible travel, blocked by walls. Dash speed sets how fast; the skill's range is the max distance.";
            case SkillEffectKind.Buff:          return "Temporarily raises one of the caster's stats.";
            case SkillEffectKind.Regen:         return "Heals the caster a little every tick for a while (heal-over-time).";
            case SkillEffectKind.Cleanse:       return "Removes active debuffs from the caster.";
            case SkillEffectKind.Vulnerability: return "Marks enemies to take extra damage for a while.";
            case SkillEffectKind.Execute:       return "Bonus damage that grows as the target's HP drops — a finisher, tiny at full HP.";
            case SkillEffectKind.Knockback:     return "Shoves enemies AWAY from the caster on hit.";
            case SkillEffectKind.Pull:          return "Drags enemies TOWARD the caster on hit.";
            case SkillEffectKind.ManaLeech:     return "For a while, part of the damage the caster deals comes back as MP.";
            case SkillEffectKind.Reflect:       return "For a while, part of the damage the caster takes is bounced back at the attacker.";
            case SkillEffectKind.Shield:        return "Raises a barrier that soaks a set amount of incoming damage before it breaks.";
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
            case SkillEffectKind.Guard:         return "boosts DAMAGE REDUCTION (sustain: HP/Def)";
            case SkillEffectKind.Shield:        return "boosts BARRIER HP (sustain: HP/Def/MP)";
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
            case SkillEffectKind.Parry:
            case SkillEffectKind.Brace:
            case SkillEffectKind.Shield:
            case SkillEffectKind.Regen:
            case SkillEffectKind.Vulnerability:
            case SkillEffectKind.Haste:
            case SkillEffectKind.Taunt:
            case SkillEffectKind.ManaLeech:
            case SkillEffectKind.Reflect:
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
