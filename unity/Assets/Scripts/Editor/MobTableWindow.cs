using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A single spreadsheet-style view of every <see cref="MonsterDef"/> in the project — one mob per row,
/// with a live front-facing thumbnail and the balance-relevant stats editable inline. Edits write straight
/// back to the asset (Undo-able, marked dirty, saved on demand), so this doubles as a balancing table.
///
/// Open via <c>Tools ▸ unwritten ▸ Catalogs ▸ Mob Table</c>. Click a column header to sort; click the asset
/// name's select button (or a row's ◎) to ping/select the underlying asset for the full Inspector.
/// </summary>
public class MobTableWindow : EditorWindow
{
    const string MenuPath = "Tools/unwritten/Catalogs/Mob Table";
    const int Cell = 16;                 // Ninja Adventure mob sheets are 16px square cells
    const float RowHeight = 38f;

    [MenuItem(MenuPath)]
    static void Open()
    {
        var w = GetWindow<MobTableWindow>("Mob Table");
        w.minSize = new Vector2(40, 320);
        w.Reload();
    }

    readonly List<MonsterDef> _defs = new List<MonsterDef>();
    readonly Dictionary<MonsterDef, (Texture2D tex, Rect uv)> _sheets = new Dictionary<MonsterDef, (Texture2D, Rect)>();
    Vector2 _scroll, _hscroll;
    string _search = "";
    readonly HashSet<MonsterDef> _expanded = new HashSet<MonsterDef>();
    // Toggling an expander CHANGES how many layout groups the window draws. IMGUI requires the Layout pass and the
    // Repaint pass of one frame to agree on that count, so the click is recorded here and applied after the loop —
    // flipping it mid-draw is the classic "GUILayout: Mismatched LayoutGroup" console spam.
    MonsterDef _pendingToggle;
    string _sortField = "id";
    bool _sortAsc = true;

    void Reload()
    {
        DropAllLookPreviews();
        _rosterNames = null;
        _defs.Clear();
        _sheets.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:MonsterDef"))
        {
            var def = AssetDatabase.LoadAssetAtPath<MonsterDef>(AssetDatabase.GUIDToAssetPath(guid));
            if (def != null) _defs.Add(def);
        }
        Resort();
        Repaint();
    }

    void Resort()
    {
        System.Comparison<MonsterDef> cmp = _sortField switch
        {
            "name"   => (a, b) => string.Compare(a.displayName, b.displayName, System.StringComparison.OrdinalIgnoreCase),
            "biome"  => (a, b) => a.biome.CompareTo(b.biome),
            "lvl"    => (a, b) => a.level.CompareTo(b.level),
            "mult"   => (a, b) => a.multiplier.CompareTo(b.multiplier),
            "gear"   => (a, b) => GearCount(a).CompareTo(GearCount(b)),
            // Sort by how far off-budget a mob is, so the mis-authored ones surface together.
            "pts"    => (a, b) => (UnitStatMath.PointsSpent(a.StatInputs) - UnitStatMath.PointsEarnedByLevel(a.level))
                        .CompareTo(UnitStatMath.PointsSpent(b.StatInputs) - UnitStatMath.PointsEarnedByLevel(b.level)),
            // Sort by the DERIVED value the column shows, not the superseded stored field.
            "hp"     => (a, b) => Derived(a, MonsterDefStats.Stat.HP).CompareTo(Derived(b, MonsterDefStats.Stat.HP)),
            "atk"    => (a, b) => Derived(a, MonsterDefStats.Stat.Atk).CompareTo(Derived(b, MonsterDefStats.Stat.Atk)),
            "weight" => (a, b) => a.weight.CompareTo(b.weight),
            "xp"     => (a, b) => CreatureKinds.KillExperience(a).CompareTo(CreatureKinds.KillExperience(b)),
            "elem"   => (a, b) => a.element.CompareTo(b.element),
            "elemlv" => (a, b) => a.elementLevel.CompareTo(b.elementLevel),
            _        => (a, b) => string.Compare(a.id, b.id, System.StringComparison.OrdinalIgnoreCase),
        };
        _defs.Sort((a, b) => (_sortAsc ? 1 : -1) * cmp(a, b));
    }

    // The picture and the part of it that is the creature's front-facing frame, in texture coordinates. A grid
    // sheet's frame is its top-left 16px cell; strip art's (the bosses) is the first frame of the Idle picture.
    (Texture2D tex, Rect uv) SheetFor(MonsterDef def)
    {
        if (_sheets.TryGetValue(def, out var t)) return t;
        t = (null, default);
        string dir = RigBuilder25D.ArtFolderPath(def.artFolder);
        if (dir != null)
        {
            if (RigBuilder25D.TryStripArt(dir, out var idle, out _, out _))
                t = (idle.sheet, new Rect(0f, 0f, 1f / idle.frames, 1f));
            else
            {
                var tex = RigBuilder25D.MonsterSheet(dir);
                if (tex != null)
                {
                    // Unity tex coords are bottom-left origin, so the top row sits at the top of the [0,1] V range.
                    float c = Mathf.Min(Cell, Mathf.Min(tex.width, tex.height));
                    t = (tex, new Rect(0f, 1f - c / tex.height, c / tex.width, c / tex.height));
                }
            }
        }
        _sheets[def] = t;
        return t;
    }

    // --- column layout (label, sortField or null, width) ---
    struct Col { public string label, sort; public float w; public Col(string l, string s, float wi) { label = l; sort = s; w = wi; } }
    static readonly Col[] Cols =
    {
        new Col("",      null,     20),   // expand toggle
        new Col("",      null,     42),   // preview
        new Col("id",    "id",     150),
        new Col("name",  "name",   120),
        new Col("biome", "biome",  90),
        new Col("kind",  null,     64),
        // THE ONE DIFFICULTY DIAL, typed: health × it, attack × its cube root, pay × it, rarer drops from a high one.
        new Col("multiplier", "mult", 70),
        new Col("size",  null,     100),
        new Col("movement speed", null, 150),
        // The AUTHORED stat block — a mob is a level and six primaries, exactly like the player.
        new Col("LVL",   "lvl",    38),
        new Col("STR",   null,     38),
        new Col("AGI",   null,     38),
        new Col("VIT",   null,     38),
        new Col("INT",   null,     38),
        new Col("DEX",   null,     38),
        new Col("LUK",   null,     38),
        // The level's point BUDGET vs what this spread costs — red when a mob carries stats its level can't afford.
        new Col("pts",   "pts",    74),
        // Worn gear + the ring's ranks, at a glance. Both are EDITED in the expander row (click ▸) — a spreadsheet
        // row can't hold 12 slots and 10 rank sliders without becoming unreadable.
        new Col("gear",  "gear",   120),
        // Its mastery in the ONE weapon it holds (picked with the rest of its gear, under ▸) — a creature has mastery
        // in one weapon only.
        new Col("mastery", null,   56),
        // DERIVED readouts (read-only): what UnitStatMath actually produces from the block on the left, so a
        // designer edits the cause and immediately sees the effect. Never editable — these have no storage.
        new Col("=HP",   "hp",     52),
        new Col("=ATK",  "atk",    46),
        new Col("=DEF",  null,     84),
        new Col("=FLEE", null,     46),
        new Col("elem",  "elem",   84),
        new Col("Element Lv", "elemlv", 70),
        new Col("wt",    "weight", 38),
        new Col("XP",    "xp",     46),
        new Col("",      null,     28),   // select asset
    };
    static float TotalWidth => Cols.Sum(c => c.w) + 8;

    void OnGUI()
    {
        DrawToolbar();
        DrawHeader();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _hscroll = GUILayout.BeginScrollView(_hscroll, GUILayout.ExpandWidth(true));
        foreach (var def in _defs)
        {
            if (!string.IsNullOrEmpty(_search) && !MatchesSearch(def)) continue;
            // Read BEFORE the row can change it: the row's kind dropdown may switch it in this very event, and the
            // look box appearing or vanishing mid-event would break IMGUI's layout count (see _pendingToggle).
            bool drawnAsCharacter = CreatureKinds.Is3D(def.kind);
            DrawRow(def);
            if (_expanded.Contains(def))
            {
                DrawGearAndRingEditor(def);
                if (drawnAsCharacter) DrawShadowLook(def);
            }
        }
        GUILayout.EndScrollView();
        EditorGUILayout.EndScrollView();

        // Apply a queued expander click now that the whole layout pass is done, then repaint with the new shape.
        if (_pendingToggle != null)
        {
            if (!_expanded.Remove(_pendingToggle)) _expanded.Add(_pendingToggle);
            else DropLookPreview(_pendingToggle);
            _pendingToggle = null;
            Repaint();
        }
    }

    // An undone body or colour must show at once, not on the next mouse move over the window.
    void OnEnable() => Undo.undoRedoPerformed += Repaint;

    void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
        DropAllLookPreviews();
    }

    // ── A SHADOW'S LOOK: its body and outfit colours ─────────────────────────────────────────────────────────────
    // The only editor of MonsterDef.modelName / dyeSources / dyeTargets (they are hidden from the Inspector). The 3D
    // preview and its turning are presentation: they live outside every change check and never touch the asset.
    // Only picking a body or a colour writes, under its own change check, Undo-able, and nothing reaches the disk
    // until Save All.

    // One preview per expanded shadow row. Everything in it lives in the preview's own hidden scene, never the open one.
    sealed class LookPreview
    {
        public PreviewRenderUtility stage;
        public GameObject root;           // HideAndDontSave, handed to the stage before anything is put under it
        public string body;               // the body the stage is showing; rebuilt when the entry names another
        public SkinnedMeshRenderer[] renderers;
        public IReadOnlyList<Color> parts = System.Array.Empty<Color>();
        public string signature;
        public Vector3 centre;
        public float height = 1f;
        public float yaw;                 // the turn angle — the window's, never the asset's
        public GameObject hair;           // the hair worn on the stage's body, put on the way the game puts it on
        public string hairShown;          // which hair is on it, so a changed entry puts a new one on
    }

    readonly Dictionary<MonsterDef, LookPreview> _lookPreviews = new Dictionary<MonsterDef, LookPreview>();
    // Made on first use, never in a field initialiser: Unity refuses to create one while the window itself is being
    // constructed.
    MaterialPropertyBlock _lookBlock;
    readonly List<Color> _lookSources = new List<Color>();
    readonly List<Color> _lookTargets = new List<Color>();

    // The loadable roster: every Addressable entry carrying the character-model label (CharacterModels.Label), by
    // the short name the game loads it by. Read from the Addressable settings, so it is exactly what the game can
    // wear, and refreshed on Reload.
    string[] _rosterNames;
    Dictionary<string, string> _rosterPaths;
    string _rosterProblem;

    void EnsureRoster()
    {
        if (_rosterNames != null) return;
        var names = new List<string>();
        _rosterPaths = new Dictionary<string, string>(System.StringComparer.Ordinal);
        _rosterProblem = null;
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) _rosterProblem = "The project has no Addressable settings, so there is no character roster.";
        else
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null || !entry.labels.Contains(CharacterModels.Label)) continue;
                    string address = entry.address ?? "";
                    if (!address.StartsWith(CharacterModels.AddressPrefix, System.StringComparison.Ordinal)) continue;
                    string shortName = address.Substring(CharacterModels.AddressPrefix.Length);
                    if (_rosterPaths.ContainsKey(shortName)) continue;
                    _rosterPaths[shortName] = entry.AssetPath;
                    names.Add(shortName);
                }
            }
        // The same order the game lists the roster in (CharacterModels.Names).
        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        _rosterNames = names.ToArray();
        if (_rosterProblem == null && _rosterNames.Length == 0)
            _rosterProblem = "No Addressable entry carries the '" + CharacterModels.Label + "' label.";
    }

    void DrawShadowLook(MonsterDef def)
    {
        EnsureRoster();
        var look = PreviewFor(def);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxWidth(TotalWidth - 20));
        EditorGUILayout.LabelField($"   {def.displayName} — look", EditorStyles.miniBoldLabel);
        if (_rosterProblem != null) EditorGUILayout.HelpBox(_rosterProblem, MessageType.Warning);

        EditorGUILayout.BeginHorizontal();
        DrawLookStage(look, def);

        EditorGUILayout.BeginVertical(GUILayout.MinWidth(0f));
        // The parts are read BEFORE the body can change in this event, so the number of swatches drawn below stays
        // the same as the layout pass counted. A new body's swatches appear on the next repaint.
        var parts = look.parts;

        EditorGUI.BeginChangeCheck();
        int at = System.Array.IndexOf(_rosterNames, def.modelName);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Body", EditorStyles.miniLabel, GUILayout.MaxWidth(40), GUILayout.MinWidth(0f));
        int picked = at;
        using (new EditorGUI.DisabledScope(_rosterNames.Length == 0))
        {
            if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.MaxWidth(24), GUILayout.MinWidth(0f)))
                picked = at <= 0 ? _rosterNames.Length - 1 : at - 1;
            picked = EditorGUILayout.Popup(picked, _rosterNames, GUILayout.MaxWidth(200), GUILayout.MinWidth(0f));
            if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.MaxWidth(24), GUILayout.MinWidth(0f)))
                picked = at < 0 || at >= _rosterNames.Length - 1 ? 0 : at + 1;
        }
        EditorGUILayout.EndHorizontal();
        if (at < 0)
            EditorGUILayout.LabelField(string.IsNullOrEmpty(def.modelName)
                                           ? "No body picked — the server will not make this creature."
                                           : "'" + def.modelName + "' is not in the roster — the server will not make this creature.",
                                       EditorStyles.wordWrappedMiniLabel);

        // One swatch per part of the body, showing the colour it is drawn in now.
        CharacterDye3D.DecodePairs(def.dyeSources, def.dyeTargets, _lookSources, _lookTargets);
        int changedPart = -1;
        Color changedTo = default;
        if (parts.Count > 0) EditorGUILayout.LabelField("Colours", EditorStyles.miniLabel);
        const int perRow = 4;
        for (int i = 0; i < parts.Count; i++)
        {
            if (i % perRow == 0) EditorGUILayout.BeginHorizontal();
            Color shown = ChosenColour(parts[i]);
            Color next = EditorGUILayout.ColorField(new GUIContent("", "#" + ColorUtility.ToHtmlStringRGB(parts[i])),
                                                    shown, true, false, false,
                                                    GUILayout.MaxWidth(56), GUILayout.MinWidth(0f));
            if (next != shown) { changedPart = i; changedTo = next; }
            if (i % perRow == perRow - 1 || i == parts.Count - 1) EditorGUILayout.EndHorizontal();
        }

        if (EditorGUI.EndChangeCheck())
        {
            if (picked != at && picked >= 0 && picked < _rosterNames.Length)
            {
                // A new body is a new set of parts: colours chosen on the old one mean nothing on it, so both lists
                // are emptied in the same Undo step as the body itself.
                Undo.RecordObject(def, "Change Shadow Body");
                def.modelName = _rosterNames[picked];
                def.dyeSources = System.Array.Empty<string>();
                def.dyeTargets = System.Array.Empty<string>();
                EditorUtility.SetDirty(def);
            }
            else if (changedPart >= 0)
            {
                Undo.RecordObject(def, "Change Shadow Colour");
                SetPartColour(def, parts[changedPart], changedTo);
                EditorUtility.SetDirty(def);
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // The colour a part is drawn in under the colours just decoded — the same lookup the game paints with.
    Color ChosenColour(Color part)
    {
        for (int i = 0; i < _lookSources.Count && i < _lookTargets.Count; i++)
            if (SameColour(_lookSources[i], part)) return _lookTargets[i];
        return part;
    }

    // The game's own tolerance for "this is that part" (CharacterDye3D), one step of 8-bit colour.
    static bool SameColour(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

    // Write one part's chosen colour into the entry, in the player's save shape: a part set back to its own colour is
    // dropped from the lists rather than stored as a change to itself.
    void SetPartColour(MonsterDef def, Color part, Color chosen)
    {
        var src = new List<string>();
        var dst = new List<string>();
        bool placed = false;
        for (int i = 0; i < _lookSources.Count; i++)
        {
            if (SameColour(_lookSources[i], part))
            {
                placed = true;
                if (SameColour(chosen, part)) continue;
                src.Add(Hex(part)); dst.Add(Hex(chosen));
                continue;
            }
            src.Add(Hex(_lookSources[i])); dst.Add(Hex(_lookTargets[i]));
        }
        if (!placed && !SameColour(chosen, part)) { src.Add(Hex(part)); dst.Add(Hex(chosen)); }
        def.dyeSources = src.ToArray();
        def.dyeTargets = dst.ToArray();
    }

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    // The preview for this row, rebuilt whenever the entry names a different body than the one on show.
    LookPreview PreviewFor(MonsterDef def)
    {
        if (!_lookPreviews.TryGetValue(def, out var look))
        {
            look = new LookPreview { yaw = 0f };
            _lookPreviews[def] = look;
        }
        if (look.stage != null && look.body == def.modelName) return look;

        ClearStage(look);
        look.body = def.modelName;
        look.parts = System.Array.Empty<Color>();
        if (string.IsNullOrEmpty(def.modelName) || _rosterPaths == null ||
            !_rosterPaths.TryGetValue(def.modelName, out var path)) return look;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) return look;

        look.stage = new PreviewRenderUtility();
        look.stage.camera.fieldOfView = 30f;
        look.stage.camera.nearClipPlane = 0.01f;
        look.stage.camera.farClipPlane = 100f;
        look.stage.camera.clearFlags = CameraClearFlags.SolidColor;
        look.stage.camera.backgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);
        // Lit from the camera's side, bright enough that every outfit colour reads true against the dark backdrop.
        look.stage.lights[0].intensity = 2.5f;
        look.stage.lights[0].transform.rotation = Quaternion.Euler(30f, 200f, 0f);
        look.stage.lights[1].intensity = 1.2f;
        look.stage.ambientColor = Color.white;

        // The root goes into the preview's own scene FIRST, then the body is made directly under it — so nothing of
        // this is ever created in, or saved with, the scene the user has open.
        look.root = new GameObject("ShadowLookPreview") { hideFlags = HideFlags.HideAndDontSave };
        look.stage.AddSingleGO(look.root);
        var body = Object.Instantiate(model, look.root.transform, false);
        foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;

        look.renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        look.signature = CharacterDye3D.SignatureOf(look.renderers);
        look.parts = CharacterDyeParts.For(look.signature);

        var bounds = new Bounds(body.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in look.renderers)
        {
            if (r == null) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        look.centre = bounds.center;
        look.height = Mathf.Max(0.01f, bounds.size.y);
        look.hair = null;
        look.hairShown = null;
        return look;
    }

    // ITS HAIR, from the entry, through the game's own socket — the piece goes on and is coloured exactly as a
    // shadow's hair is in play (HeadSocket.Wear / PaintHair).
    void ShowHair(LookPreview look, MonsterDef def)
    {
        HeadPieces.Resolve(def.modelName, def.hairStyle, def.hairColour, def.dyeSources, def.dyeTargets,
                           out var id, out var hex);
        string want = id + "|" + hex;
        if (look.hairShown == want) return;
        if (look.hair != null) Object.DestroyImmediate(look.hair);
        look.hair = null;
        look.hairShown = want;
        var body = look.renderers != null && look.renderers.Length > 0 ? look.renderers[0] : null;
        look.hair = HeadSocket.Wear(body, HeadPieces.HairPiece(id));
        if (look.hair == null) return;
        foreach (var t in look.hair.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
        var colour = ColorUtility.TryParseHtmlString(hex, out var c) ? c : HeadPieces.OwnColourOf(id);
        _hairBlock ??= new MaterialPropertyBlock();
        HeadSocket.PaintHair(look.hair, colour, _hairBlock);
    }

    static MaterialPropertyBlock _hairBlock;

    void DrawLookStage(LookPreview look, MonsterDef def)
    {
        var rect = GUILayoutUtility.GetRect(0f, 220f, 220f, 220f);
        if (look.stage == null)
        {
            GUI.Label(rect, "no body", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        // Drag to turn. Window state only — nothing here is recorded or written.
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) { look.yaw -= e.delta.x; e.Use(); Repaint(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
        }

        if (e.type != EventType.Repaint) return;

        // The entry's colours, put on through the game's one colouring routine — and, exactly like the game, a look
        // stamped with another body shows this body's own colours (CharacterDye3D.ShowLook).
        CharacterDye3D.DecodePairs(def.dyeSources, def.dyeTargets, _lookSources, _lookTargets);
        bool stampFits = string.IsNullOrEmpty(look.signature) || look.signature == def.modelName;
        _lookBlock ??= new MaterialPropertyBlock();
        CharacterDye3D.Paint(look.renderers, stampFits ? look.parts : null, _lookSources, _lookTargets, _lookBlock,
                             CharacterDye3D.Unchangeable);
        ShowHair(look, def);

        look.root.transform.rotation = Quaternion.Euler(0f, look.yaw, 0f);
        float distance = look.height * 0.5f / Mathf.Tan(look.stage.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
        look.stage.camera.transform.position = look.centre + new Vector3(0f, 0f, distance);
        look.stage.camera.transform.LookAt(look.centre);
        look.stage.BeginPreview(rect, GUIStyle.none);
        look.stage.Render(true);
        GUI.DrawTexture(rect, look.stage.EndPreview(), ScaleMode.StretchToFill, false);
    }

    static void ClearStage(LookPreview look)
    {
        if (look.root != null) Object.DestroyImmediate(look.root);
        look.root = null;
        look.stage?.Cleanup();
        look.stage = null;
        look.renderers = null;
    }

    void DropLookPreview(MonsterDef def)
    {
        if (def != null && _lookPreviews.TryGetValue(def, out var look)) ClearStage(look);
        if (def != null) _lookPreviews.Remove(def);
    }

    void DropAllLookPreviews()
    {
        foreach (var look in _lookPreviews.Values) ClearStage(look);
        _lookPreviews.Clear();
    }

    bool MatchesSearch(MonsterDef d)
    {
        string s = _search.ToLowerInvariant();
        return (d.id ?? "").ToLowerInvariant().Contains(s)
            || (d.displayName ?? "").ToLowerInvariant().Contains(s)
            || (RigBuilder25D.ArtFolderPath(d.artFolder) ?? "").ToLowerInvariant().Contains(s)
            || d.biome.ToString().ToLowerInvariant().Contains(s);
    }

    void DrawToolbar()
    {
        using (var flow = new EditorLayoutFlow.Flow(position.width, EditorStyles.toolbar))
        {
            flow.Item(70);
            GUILayout.Label($"{_defs.Count} mobs", EditorStyles.miniLabel, GUILayout.Width(70));
            flow.Item(44 + 180);   // "Search" label + field kept together
            GUILayout.Label("Search", EditorStyles.miniLabel, GUILayout.Width(44));
            _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(180));
            flow.Item(70);
            if (GUILayout.Button("Save All", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                AssetDatabase.SaveAssets();
                MonsterCatalog.Reload();
                WikiExport.RunAll();
            }
            flow.Item(60);
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60))) Reload();
        }
    }

    void DrawHeader()
    {
        // Lay the header out with the SAME manual per-column rects the rows use (see DrawRow's Next()),
        // so titles sit exactly over their values. Auto-layout (GUILayout) added padding/spacing that
        // drifted the headers off their columns. Offset by -_hscroll.x to track the horizontal scroll.
        var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
        var bar = EditorGUILayout.GetControlRect(false, 18, GUILayout.Width(TotalWidth));
        float x = bar.x - _hscroll.x;
        foreach (var c in Cols)
        {
            var r = new Rect(x, bar.y, c.w - 3, bar.height);
            x += c.w;
            string label = c.label;
            if (c.sort != null && c.sort == _sortField) label += _sortAsc ? " ▲" : " ▼";
            if (c.sort != null)
            {
                if (GUI.Button(r, label, EditorStyles.toolbarButton))
                {
                    if (_sortField == c.sort) _sortAsc = !_sortAsc; else { _sortField = c.sort; _sortAsc = true; }
                    Resort();
                }
            }
            else GUI.Label(r, label, style);
        }
    }

    void DrawRow(MonsterDef def)
    {
        var rect = EditorGUILayout.GetControlRect(false, RowHeight, GUILayout.Width(TotalWidth));
        if (Mathf.FloorToInt(rect.y / RowHeight) % 2 == 0)
            EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.03f));

        float x = rect.x;
        int ci = 0;
        Rect Next() { var r = new Rect(x, rect.y + 3, Cols[ci].w - 3, RowHeight - 6); x += Cols[ci].w; ci++; return r; }

        // expander — opens the gear + ring-ranks editor beneath this row
        bool open = _expanded.Contains(def);
        if (GUI.Button(VCenter(Next(), 18), open ? "▾" : "▸", EditorStyles.label)) _pendingToggle = def;

        // preview
        var pr = Next();
        DrawPreview(new Rect(pr.x, rect.y + 2, 34, 34), def);

        EditorGUI.BeginChangeCheck();
        string id    = EditorGUI.TextField(VCenter(Next(), 18), def.id);
        string name  = EditorGUI.TextField(VCenter(Next(), 18), def.displayName);
        // Biome.None = biome-agnostic (spawns in any biome); it's just another choice in the dropdown.
        var biome    = (Biome)EditorGUI.EnumPopup(VCenter(Next(), 18), def.biome);
        var kind     = (CreatureKind)EditorGUI.EnumPopup(VCenter(Next(), 18), def.kind);
        float mult   = EditorGUI.FloatField(VCenter(Next(), 18), def.multiplier);
        // Size is one number, times a character's height; its group (Small / Medium / Large) is worked out from it and
        // shown beside it, never typed. A shadow is always 1 whatever its entry says (MobSizes.Of), so it is locked.
        var sizeCell = Next();
        EditorGUI.BeginDisabledGroup(def.kind == CreatureKind.Shadow);
        float size   = EditorGUI.FloatField(VCenter(new Rect(sizeCell.x, sizeCell.y, 44f, sizeCell.height), 18), def.size);
        EditorGUI.EndDisabledGroup();
        GUI.Label(VCenter(new Rect(sizeCell.x + 48f, sizeCell.y, sizeCell.width - 48f, sizeCell.height), 18),
                  MobSizes.GroupOf(MobSizes.Of(def.kind, def.size)).ToString(), EditorStyles.miniLabel);
        // Its own movement speed: 1 is the shared pace, 0 never moves, 2 is the fastest there is.
        float moveSpeed = EditorGUI.Slider(VCenter(Next(), 18), def.moveSpeed,
                                           UnitStatMath.MinCreatureMoveSpeed, UnitStatMath.MaxCreatureMoveSpeed);
        int lvl      = EditorGUI.IntField(VCenter(Next(), 18), def.level);
        int str      = EditorGUI.IntField(VCenter(Next(), 18), def.str);
        int agi      = EditorGUI.IntField(VCenter(Next(), 18), def.agi);
        int vit      = EditorGUI.IntField(VCenter(Next(), 18), def.vit);
        int itl      = EditorGUI.IntField(VCenter(Next(), 18), def.intel);
        int dex      = EditorGUI.IntField(VCenter(Next(), 18), def.dex);
        int luk      = EditorGUI.IntField(VCenter(Next(), 18), def.luk);
        DrawPoints(Next(), def);
        DrawGearSummary(Next(), def);
        bool holds   = def.Weapon != null;
        EditorGUI.BeginDisabledGroup(!holds);
        int mastery  = EditorGUI.IntField(VCenter(Next(), 18), def.MasteryOnWeapon);
        EditorGUI.EndDisabledGroup();
        // Derived readouts — computed live from the block just edited, never stored and never editable.
        DrawDerived(Next(), Derived(def, MonsterDefStats.Stat.HP));
        DrawDerived(Next(), Derived(def, MonsterDefStats.Stat.Atk));
        DrawDerived(Next(), MonsterDefStats.DefenseLabel(def));
        DrawDerived(Next(), Derived(def, MonsterDefStats.Stat.Flee));
        var elem     = (Element)EditorGUI.EnumPopup(VCenter(Next(), 18), def.element);
        int elemLv   = EditorGUI.IntField(VCenter(Next(), 18), def.elementLevel);
        int wt       = EditorGUI.IntField(VCenter(Next(), 18), def.weight);
        // What a kill pays is worked out from the level and the multiplier, never typed.
        DrawDerived(Next(), CreatureKinds.KillExperience(def));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(def, "Edit Mob");
            def.id = id; def.displayName = name; def.biome = biome;
            def.kind = kind;
            def.multiplier = CreaturePay.Clamp(mult);
            def.size = MobSizes.Clamp(size);
            def.moveSpeed = Mathf.Clamp(moveSpeed, UnitStatMath.MinCreatureMoveSpeed, UnitStatMath.MaxCreatureMoveSpeed);
            def.level = Mathf.Max(1, lvl);
            // Budget guard: an entry the level can't afford SNAPS to the highest value that fits, rather than being
            // stored as an over-budget spread. Each stat is checked against what's left once the other five are paid
            // for, in field order — so typing 99 into STR spends the remainder and leaves the rest untouched.
            var typed = new[] { str, agi, vit, itl, dex, luk };
            var prior = new[] { def.str, def.agi, def.vit, def.intel, def.dex, def.luk };
            FitSpreadToBudget(def.level, typed, prior);
            def.str = typed[0]; def.agi = typed[1]; def.vit = typed[2];
            def.intel = typed[3]; def.dex = typed[4]; def.luk = typed[5];
            def.element = elem; def.elementLevel = Mathf.Clamp(elemLv, 1, Elements.MaxLevel); def.weight = wt;
            // Never above what the best grade lets count, since no weapon could use more.
            def.weaponMastery = holds ? Mathf.Clamp(mastery, 0, CraftingMath.WeaponMasteryCap(Quality.A)) : 0;
            EditorUtility.SetDirty(def);
        }

        if (GUI.Button(VCenter(Next(), 18), "◎"))
        {
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
        }
    }

    static Rect VCenter(Rect r, float h) => new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h);

    static int Clamp99(int v) => Mathf.Clamp(v, 1, UnitStatMath.MaxStatValue);

    /// <summary>Force a typed spread inside the level's point budget, IN PLACE. Only a stat the user actually EDITED
    /// is capped — it absorbs whatever points the others leave over, so typing 99 into STR at level 9 becomes 27
    /// rather than dragging the untouched stats down with it. Stats left alone are never modified, which is why this
    /// needs <paramref name="prior"/>: the committed values are what the edited stat is measured against.
    ///
    /// <para>Result always satisfies <c>PointsSpent ≤ PointsEarnedByLevel</c>, so the table cannot write a mob
    /// stronger than its level allows. (Lowering the LEVEL doesn't retro-clamp a spread — nothing was typed into a
    /// stat, so the pts column just turns red and the choice of what to give up stays the designer's.)</para></summary>
    static void FitSpreadToBudget(int level, int[] s, int[] prior)
    {
        int budget = UnitStatMath.PointsEarnedByLevel(level);
        for (int i = 0; i < s.Length; i++)
        {
            s[i] = Clamp99(s[i]);
            if (s[i] == prior[i]) continue;   // untouched this edit — leave it exactly as authored
            // What the rest cost: already-resolved values to the left, still-committed values to the right.
            int others = 0;
            for (int j = 0; j < s.Length; j++)
                if (j != i) others += UnitStatMath.PointsToRaise(j < i ? s[j] : prior[j]);
            s[i] = Mathf.Min(s[i], UnitStatMath.MaxAffordableStat(budget - others));
        }
    }

    // The derived columns read the one calculation /mobinfo reads too, so the table and the game can never disagree.
    static int Derived(MonsterDef def, MonsterDefStats.Stat which) => MonsterDefStats.Derived(def, which);

    static int GearCount(MonsterDef d)
    {
        if (d.startingGear == null) return 0;
        int n = 0;
        for (int i = 0; i < d.startingGear.Length; i++) if (d.startingGear[i] != null) n++;
        return n;
    }

    static GUIStyle gearStyle;
    // At-a-glance: how many pieces worn, and the ring's total ranks. Editing happens in the expander.
    static void DrawGearSummary(Rect r, MonsterDef def)
    {
        int worn = GearCount(def);
        int ranks = 0;
        if (def.ringRankValues != null) for (int i = 0; i < def.ringRankValues.Length; i++) ranks += Mathf.Max(0, def.ringRankValues[i]);

        gearStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
        gearStyle.normal.textColor = worn > 0 ? new Color(0.75f, 0.7f, 1f) : new Color(0.45f, 0.45f, 0.45f);
        string label = worn == 0 && ranks == 0 ? "—" : ranks > 0 ? $"{worn} worn · ring +{ranks}" : $"{worn} worn";
        GUI.Label(VCenter(r, 18), label, gearStyle);
    }

    // Every slot a unit can fill. Cosmetic layers are omitted — they carry no stats, so authoring them here would be
    // a knob that changes nothing.
    static readonly EquipSlotType[] GearSlots =
    {
        EquipSlotType.Weapon, EquipSlotType.Head, EquipSlotType.Armor,
        EquipSlotType.Garment, EquipSlotType.Boots,
    };

    /// <summary>The per-mob detail editor: what it WEARS (one object field per slot) and, when a ring is worn, that
    /// ring's invested ranks. Kept out of the row itself so the table stays scannable; everything here writes straight
    /// back to the asset, Undo-able, exactly like the inline columns.</summary>
    void DrawGearAndRingEditor(MonsterDef def)
    {
        var box = EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(TotalWidth - 20));
        GUILayout.Space(2);

        EditorGUILayout.LabelField($"   {def.displayName} — equipment", EditorStyles.miniBoldLabel);

        EditorGUI.BeginChangeCheck();

        // --- worn gear, one field per slot ---
        var worn = new Dictionary<EquipSlotType, Item>();
        if (def.startingGear != null)
            foreach (var it in def.startingGear)
                if (it != null && it.slot != EquipSlotType.None) worn[it.slot] = it;

        int perRow = 3;
        for (int i = 0; i < GearSlots.Length; i++)
        {
            if (i % perRow == 0) EditorGUILayout.BeginHorizontal();
            var slot = GearSlots[i];
            worn.TryGetValue(slot, out var cur);
            EditorGUILayout.LabelField(slot.ToString(), EditorStyles.miniLabel, GUILayout.Width(74));
            var picked = (Item)EditorGUILayout.ObjectField(cur, typeof(Item), false, GUILayout.Width(150));
            // Only accept an item that actually belongs in this slot — otherwise the "worn set" would lie about
            // where a piece sits.
            if (picked != null && picked.slot != slot) picked = cur;
            if (picked != null) worn[slot] = picked; else worn.Remove(slot);
            GUILayout.Space(8);
            if (i % perRow == perRow - 1 || i == GearSlots.Length - 1) EditorGUILayout.EndHorizontal();
        }

        // --- what state every piece comes to life at: one set for the whole creature, from its level ---
        GUILayout.Space(4);
        EditorGUILayout.LabelField("   gear state  (every piece this creature wears comes to life at these)",
                                   EditorStyles.miniBoldLabel);
        var quality = def.WornGrade;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Grade", EditorStyles.miniLabel, GUILayout.Width(74));
        EditorGUILayout.LabelField(quality + " (its level's)", EditorStyles.miniLabel, GUILayout.Width(110));
        GUILayout.Space(8);
        // folds and Ink are not authored: the kind rolls the folds each life, the grade decides the Ink
        EditorGUILayout.LabelField("Folds " + CreatureLoot.FileFolds(def.kind) + "–10, rolled each life",
                                   EditorStyles.miniLabel, GUILayout.Width(170));
        int inkTier = CreatureLoot.InkTierFor(quality);
        EditorGUILayout.LabelField(inkTier > 0 ? "Ink " + InkTierNames[inkTier - 1] : "no Ink", EditorStyles.miniLabel,
                                   GUILayout.Width(80));
        int written = inkTier > 0 ? CraftingMath.InkSlots(quality) : 0;
        EditorGUILayout.LabelField(written == 0 ? "writes nothing" : written == 1 ? "writes 1 stat"
                                                                   : "writes " + written + " stats",
                                   EditorStyles.miniLabel, GUILayout.Width(110));
        EditorGUILayout.EndHorizontal();

        // --- ring ranks: part of the creature, so they always count ---
        var stats = new List<StatId>();
        var vals  = new List<int>();
        {
            GUILayout.Space(4);
            EditorGUILayout.LabelField("   ring ranks  (same as the player's ring: 1% a rank, at most 33)", EditorStyles.miniBoldLabel);
            for (int i = 0; i < RingStatOrder.Length; i++)
            {
                var st = RingStatOrder[i];
                int cur = RanksIn(def, st);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(st.ToString(), EditorStyles.miniLabel, GUILayout.Width(90));
                int next = Mathf.Clamp(EditorGUILayout.IntField(cur, GUILayout.Width(50)), 0, SkillRing.NodeCap);
                // Show what that rank count is actually WORTH, so a designer tunes the effect, not the raw number.
                EditorGUILayout.LabelField(next > 0 ? $"+{SkillRing.Percent(st, next) * 100f:0.#}%" : "—",
                                           EditorStyles.miniLabel, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
                if (next > 0) { stats.Add(st); vals.Add(next); }
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(def, "Edit Mob Equipment");
            var gear = new List<Item>();
            foreach (var kv in worn) if (kv.Value != null) gear.Add(kv.Value);
            def.startingGear = gear.ToArray();
            // No weapon, no mastery: it is mastery IN a weapon, so it cannot outlive the weapon it belongs to.
            if (def.Weapon == null) def.weaponMastery = 0;
            def.ringRankStats  = stats.ToArray();
            def.ringRankValues = vals.ToArray();
            EditorUtility.SetDirty(def);
        }

        GUILayout.Space(2);
        EditorGUILayout.EndVertical();
    }

    // The five Inks, by the names the game calls them, for the gear-state dropdown.
    static readonly string[] InkTierNames = { "Watery", "Thin", "Faint", "Clear", "Sharp" };

    // Ring stats in the canonical LOOP order (SkillRing.Ring), never enum order.
    static readonly StatId[] RingStatOrder =
    {
        StatId.Def, StatId.HP, StatId.Atk, StatId.Accuracy, StatId.CastSpeed,
        StatId.MAtk, StatId.MP, StatId.Evasion, StatId.Critical, StatId.AttackSpeed,
    };

    static int RanksIn(MonsterDef def, StatId stat)
    {
        if (def.ringRankStats == null || def.ringRankValues == null) return 0;
        for (int i = 0; i < def.ringRankStats.Length && i < def.ringRankValues.Length; i++)
            if (def.ringRankStats[i] == stat) return def.ringRankValues[i];
        return 0;
    }

    static GUIStyle pointsStyle;
    // "spent/budget" against the SAME economy the player spends from: a mob at level L may carry exactly the stats
    // a player who reached L could have bought. Red = over-budget (this mob is stronger than its level allows);
    // amber = a meaningful chunk unspent (weaker than its level implies). Green = on budget.
    static void DrawPoints(Rect r, MonsterDef def)
    {
        var u = def.StatInputs;
        int spent = UnitStatMath.PointsSpent(u);
        int budget = UnitStatMath.PointsEarnedByLevel(def.level);
        pointsStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        // A few points can always be unspendable (the next point costs more than what's left), so only flag a real gap.
        pointsStyle.normal.textColor =
            spent > budget            ? new Color(1f, 0.45f, 0.45f) :
            spent < budget - 10       ? new Color(0.95f, 0.8f, 0.4f) :
                                        new Color(0.62f, 0.72f, 0.62f);
        GUI.Label(VCenter(r, 18), $"{spent}/{budget}", pointsStyle);
    }

    static GUIStyle derivedStyle;
    // Greyed + right-aligned so a derived number reads as a readout, not a field you can type into.
    static void DrawDerived(Rect r, int value) => DrawDerived(r, value.ToString());

    static void DrawDerived(Rect r, string value)
    {
        derivedStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.62f, 0.72f, 0.62f) },
        };
        GUI.Label(VCenter(r, 18), value, derivedStyle);
    }

    void DrawPreview(Rect r, MonsterDef def)
    {
        var (tex, uv) = SheetFor(def);
        if (tex == null) { GUI.Label(r, "?", EditorStyles.centeredGreyMiniLabel); return; }

        // A strip frame is drawn at its own shape (a 96×48 Samurai frame is twice as wide as tall), fitted in the cell.
        float aspect = (uv.width * tex.width) / (uv.height * tex.height);
        if (aspect > 1f) { float h = r.width / aspect; r = new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h); }
        else if (aspect < 1f) { float w = r.height * aspect; r = new Rect(r.x + (r.width - w) * 0.5f, r.y, w, r.height); }
        var prev = GUI.color;
        GUI.color = def.tint.a > 0f ? def.tint : Color.white;
        GUI.DrawTextureWithTexCoords(r, tex, uv, true);
        GUI.color = prev;
    }
}
