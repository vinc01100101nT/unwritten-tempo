using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the world layout file the one owner of where every door stands. When the user saves a map's scene after
/// dragging one of its doors (<c>Portal_w{id}_to_{map}</c>) or arrival points (<c>SpawnPoint_w{id}_from_{map}</c>), the
/// new spot is written into the layout (<see cref="WorldLayoutFile"/>) as that door's <c>at</c> / <c>arriveAt</c>, and
/// the world atlas's copy of the doors is written again from the layout.
///
/// <para><b>What counts as moved</b> is decided against the scene FILE as it was just before this save, never against
/// the layout: only a door whose spot differs from its own saved spot by more than a centimetre was moved by hand. A
/// door that agrees with its file but not with the layout means the layout was edited in the browser tool since the
/// last build — that is reported, and nothing is written, so the tool's edit is never overwritten by an old scene.</para>
///
/// <para><b>When it does nothing:</b> in or entering Play, while Build World runs (it places doors FROM the layout),
/// when the scene is being saved to a different file (a copy), when the scene is not one of the layout's maps, and for
/// any door the layout does not list between exactly these two maps.</para>
/// </summary>
[InitializeOnLoad]
static class WorldDoorSync
{
    const float MovedBeyond = 0.01f;   // metres; under this a door has not moved
    const string LogTag = "[unwritten] World doors: ";

    internal struct Move
    {
        public int id; public string other; public bool arrival;
        public float x, z;                          // the moved thing's new spot
        public bool hasArrival; public float arrivalX, arrivalZ;   // a moved DOOR's own arrival point, where it stands now
    }

    // scene path → what was found moved while that scene was being saved; written out once the save has finished
    static readonly Dictionary<string, List<Move>> pending = new Dictionary<string, List<Move>>();

    static WorldDoorSync()
    {
        EditorSceneManager.sceneSaving -= OnSceneSaving;   // avoid double-subscribe on domain reload
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorSceneManager.sceneSaved -= OnSceneSaved;
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    static bool Idle => !Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && !WorldCreator.Building;

    static bool SamePath(string a, string b) =>
        string.Equals((a ?? "").Replace('\\', '/'), (b ?? "").Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    static void OnSceneSaving(Scene scene, string path)
    {
        string own = scene.path ?? "";
        pending.Remove(own);
        if (!Idle) return;
        if (own.Length == 0 || !SamePath(path, own)) return;                    // a copy saved elsewhere is not the map
        if (!SamePath(own, WorldCreator.ScenePathOf(scene.name))) return;        // not a map's own scene
        try
        {
            var moves = MovedDoors(scene);
            if (moves.Count > 0) pending[own] = moves;
        }
        catch (Exception e)
        {
            Debug.LogWarning(LogTag + $"could not compare the doors of '{scene.name}' with the world layout, so the layout " +
                             "was not changed: " + e.Message);
        }
    }

    static void OnSceneSaved(Scene scene)
    {
        string own = scene.path ?? "";
        if (!pending.TryGetValue(own, out var moves)) return;
        pending.Remove(own);
        if (!Idle) return;
        try
        {
            string file = WorldLayoutFile.Path;
            var bytes = File.ReadAllBytes(file);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            string updated = ApplyMoves(text, scene.name, moves, out int doors);
            if (updated == null) return;
            // editor-write-ok: runs only on the user's own save of a map's scene, and only for a door they dragged since the last save
            File.WriteAllText(file, updated, new UTF8Encoding(bom));
            Debug.Log(LogTag + $"{doors} door(s) moved in '{scene.name}' — the new spot(s) are written to the world layout.");
            // editor-write-ok: the atlas's copy of the doors follows the layout the user's save just changed
            EditorApplication.delayCall += RefreshMirror;
        }
        catch (Exception e)
        {
            Debug.LogError(LogTag + $"a door moved in '{scene.name}' but the world layout could not be written, so the two " +
                           "now disagree: " + e.Message);
        }
    }

    static void RefreshMirror()
    {
        if (!Idle)
        {
            Debug.LogWarning(LogTag + "the world map's copy of the doors was not refreshed because the game is starting or a " +
                             "build is running. Run WorldCreator.BakeDoorMirror() afterwards.");
            return;
        }
        // editor-write-ok: follows a door the user moved and saved; the copy is rewritten whole from the layout
        string result = WorldCreator.BakeDoorMirror();
        if (result.StartsWith("ERROR")) Debug.LogWarning(LogTag + result);
    }

    // Every door and arrival point of this map that stands somewhere other than where its own scene file has it.
    static List<Move> MovedDoors(Scene scene)
    {
        var moves = new List<Move>();
        if (!File.Exists(WorldLayoutFile.Path) || !File.Exists(scene.path)) return moves;
        var root = Json.Parse(WorldLayoutFile.ReadText()) as Json.Obj;
        var map = FindMap(root, scene.name);
        if (map == null) return moves;
        MapSize(map, out float sizeX, out float sizeZ);

        var found = new List<Transform>();
        foreach (var go in scene.GetRootGameObjects())
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (TryDoorName(t.name, out _, out _, out _)) found.Add(t);
        if (found.Count == 0) return moves;

        SavedScene saved = null;
        bool layoutAhead = false;
        foreach (var t in found)
        {
            TryDoorName(t.name, out int id, out string other, out bool arrival);
            var anchor = AnchorOf(root, scene.name, id, other);
            if (anchor == null) continue;                    // not a door the layout lists between these two maps
            if (t.parent != null)
            {
                Debug.Log(LogTag + $"'{t.name}' in '{scene.name}' sits under another object, so whether it moved cannot be " +
                          "told. The world layout was not changed for it.");
                continue;
            }
            if (saved == null) saved = new SavedScene(File.ReadAllText(scene.path));
            if (saved.RootXZ(t.name, out float wasX, out float wasZ) != SavedScene.Result.Found)
            {
                Debug.Log(LogTag + $"'{t.name}' in '{scene.name}' could not be found in the scene file as it was before this " +
                          "save, so whether it moved cannot be told. The world layout was not changed for it.");
                continue;
            }
            float x = t.position.x, z = t.position.z;
            if (Far(x, z, wasX, wasZ))
            {
                moves.Add(new Move { id = id, other = other, arrival = arrival, x = x, z = z });
                continue;
            }
            AnchorPoints(anchor, sizeX, sizeZ, out float doorX, out float doorZ, out float arriveX, out float arriveZ);
            if (arrival ? Far(x, z, arriveX, arriveZ) : Far(x, z, doorX, doorZ)) layoutAhead = true;
        }

        for (int i = 0; i < moves.Count; i++)
        {
            var m = moves[i];
            if (m.arrival) continue;
            string arrivalName = ArrivalName(m.id, m.other);
            foreach (var t in found)
                if (t.parent == null && t.name == arrivalName)
                { m.hasArrival = true; m.arrivalX = t.position.x; m.arrivalZ = t.position.z; break; }
            moves[i] = m;
        }

        if (layoutAhead) Debug.Log(LogTag + $"the world layout is ahead of {scene.name}: run Build World");
        return moves;
    }

    // ── names ─────────────────────────────────────────────────────────────────────────────────────────────
    static readonly Regex DoorNameRx = new Regex(@"^Portal_w(\d+)_to_(.+)$", RegexOptions.CultureInvariant);
    static readonly Regex ArrivalNameRx = new Regex(@"^SpawnPoint_w(\d+)_from_(.+)$", RegexOptions.CultureInvariant);

    internal static string DoorName(int id, string otherMap) => $"Portal_w{id}_to_{otherMap}";
    internal static string ArrivalId(int id, string otherMap) => $"w{id}_from_{otherMap}";
    internal static string ArrivalName(int id, string otherMap) => "SpawnPoint_" + ArrivalId(id, otherMap);

    internal static bool TryDoorName(string name, out int id, out string other, out bool arrival)
    {
        id = 0; other = null; arrival = false;
        var m = DoorNameRx.Match(name ?? "");
        if (!m.Success) { m = ArrivalNameRx.Match(name ?? ""); arrival = m.Success; }
        if (!m.Success) return false;
        other = m.Groups[2].Value;
        return int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    internal static bool Far(float x, float z, float fromX, float fromZ) =>
        Math.Abs(x - fromX) > MovedBeyond || Math.Abs(z - fromZ) > MovedBeyond;

    // ── the layout, as a tree that keeps every field the browser tool wrote ────────────────────────────────
    static Json.Obj FindMap(Json.Obj root, string mapId)
    {
        if (root?.Get("maps") is List<object> maps)
            foreach (var m in maps)
                if (m is Json.Obj o && o.Get("id") as string == mapId) return o;
        return null;
    }

    static void MapSize(Json.Obj map, out float sizeX, out float sizeZ)
    {
        sizeX = sizeZ = WorldLayoutFile.DefaultSize;
        var size = Json.Floats(map.Get("size"));
        if (size != null && size.Length >= 2 && size[0] > 0f && size[1] > 0f) { sizeX = size[0]; sizeZ = size[1]; }
    }

    // This map's end of door <id>, only when the layout lists that door between exactly this map and <other>.
    static Json.Obj AnchorOf(Json.Obj root, string mapId, int id, string other)
    {
        if (mapId == other || !(root?.Get("portals") is List<object> portals)) return null;
        Json.Obj hit = null;
        foreach (var p in portals)
        {
            if (!(p is Json.Obj o) || !(o.Get("id") is Json.Num n)) continue;
            if (!int.TryParse(n.Raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int pid) || pid != id) continue;
            if (hit != null) return null;                    // the id is used twice: nothing can be told apart
            hit = o;
        }
        if (hit == null) return null;
        string a = hit.Get("a") as string, b = hit.Get("b") as string;
        if (a == mapId && b == other) return hit.Get("anchorA") as Json.Obj;
        if (b == mapId && a == other) return hit.Get("anchorB") as Json.Obj;
        return null;
    }

    static void AnchorPoints(Json.Obj anchor, float sizeX, float sizeZ, out float doorX, out float doorZ,
                             out float arriveX, out float arriveZ)
    {
        string edge = anchor.Get("edge") as string;
        float offset = anchor.Get("offset") is Json.Num n ? Json.Float(n) : 0.5f;
        float[] at = Json.Floats(anchor.Get("at")), arriveAt = Json.Floats(anchor.Get("arriveAt"));
        WorldLayoutFile.DoorPoint(edge, offset, sizeX, sizeZ, at, out doorX, out doorZ);
        WorldLayoutFile.ArrivalPoint(edge, offset, sizeX, sizeZ, at, arriveAt, out arriveX, out arriveZ);
    }

    static decimal Round2(float v) => Math.Round((decimal)v, 2, MidpointRounding.AwayFromZero);

    static List<object> Spot(float x, float z) => new List<object> { Json.Number(Round2(x)), Json.Number(Round2(z)) };

    /// <summary>The layout text with these moves written into map <paramref name="mapId"/>'s doors and its revision
    /// raised by one — or null when no move changes anything. Text in, text out: nothing here reads a scene or a file.</summary>
    internal static string ApplyMoves(string layoutText, string mapId, IList<Move> moves, out int changed)
    {
        changed = 0;
        var root = Json.Parse(layoutText) as Json.Obj;
        var map = FindMap(root, mapId);
        if (map == null) return null;
        MapSize(map, out float sizeX, out float sizeZ);

        foreach (var mv in moves)
        {
            var anchor = AnchorOf(root, mapId, mv.id, mv.other);
            if (anchor == null) continue;
            string before = Json.Write(anchor, "\n");
            if (mv.arrival) anchor.Set("arriveAt", Spot(mv.x, mv.z));
            else
            {
                float x = (float)Round2(mv.x), z = (float)Round2(mv.z);
                WorldLayoutFile.NearestSide(x, z, sizeX, sizeZ, out string edge, out float offset);
                anchor.Set("edge", edge);
                anchor.Set("offset", Json.Number(Round2(offset)));
                anchor.Set("at", Spot(mv.x, mv.z));
                // Without a spot of its own the arrival point would follow the door; it stays where the scene has it.
                if (anchor.Get("arriveAt") == null && mv.hasArrival) anchor.Set("arriveAt", Spot(mv.arrivalX, mv.arrivalZ));
            }
            anchor.MoveToFront("edge", "offset", "at", "arriveAt");
            if (Json.Write(anchor, "\n") != before) changed++;
        }
        if (changed == 0) return null;

        long revision = root.Get("revision") is Json.Num r && long.TryParse(r.Raw, NumberStyles.AllowLeadingSign,
                                                                            CultureInfo.InvariantCulture, out long was) ? was : 0;
        root.Set("revision", Json.Number(revision + 1));
        root.MoveToFront("version", "revision");
        return Rewrite(root, layoutText);
    }

    /// <summary>The layout text read into the tree and written straight back with nothing changed. Returns the same text
    /// for any file the browser tool wrote — the proof that a door move loses no field and reshapes nothing.</summary>
    internal static string RewriteWithNothingMoved(string layoutText) => Rewrite(Json.Parse(layoutText), layoutText);

    // Written the way the browser tool writes it (JSON.stringify with a 2-space indent), keeping the file's own line
    // endings and whether it ended with one.
    static string Rewrite(object root, string original)
    {
        string eol = original.Contains("\r\n") ? "\r\n" : "\n";
        bool endsWithLine = original.EndsWith("\n", StringComparison.Ordinal);
        return Json.Write(root, eol) + (endsWithLine ? eol : "");
    }

    // ── a scene file, read for where its top-level objects stand ───────────────────────────────────────────
    internal sealed class SavedScene
    {
        internal enum Result { Found, Absent, Unreadable }

        readonly string text;
        readonly Dictionary<string, List<int>> objectsByName = new Dictionary<string, List<int>>();   // name → block starts
        readonly Dictionary<string, int> blockStart = new Dictionary<string, int>();                 // file id → block start
        readonly Dictionary<int, int> blockEnd = new Dictionary<int, int>();
        readonly Dictionary<string, string> classOf = new Dictionary<string, string>();

        static readonly Regex HeaderRx = new Regex(@"^--- !u!(\d+) &(-?\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        static readonly Regex NameRx = new Regex(@"^  m_Name: (.*?)\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        static readonly Regex ComponentRx = new Regex(@"^  - component: \{fileID: (-?\d+)\}", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        static readonly Regex PositionRx = new Regex(@"^  m_LocalPosition: \{x: ([^,}]+), y: ([^,}]+), z: ([^,}]+)\}", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        static readonly Regex FatherRx = new Regex(@"^  m_Father: \{fileID: (-?\d+)\}", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        internal SavedScene(string sceneText)
        {
            text = sceneText ?? "";
            int prev = -1;
            for (var m = HeaderRx.Match(text); m.Success; m = m.NextMatch())
            {
                if (prev >= 0) blockEnd[prev] = m.Index;
                prev = m.Index;
                blockStart[m.Groups[2].Value] = m.Index;
                classOf[m.Groups[2].Value] = m.Groups[1].Value;
            }
            if (prev >= 0) blockEnd[prev] = text.Length;
            foreach (var kv in blockStart)
            {
                if (classOf[kv.Key] != "1") continue;                       // 1 = GameObject
                var name = NameRx.Match(text, kv.Value, blockEnd[kv.Value] - kv.Value);
                if (!name.Success) continue;
                if (!objectsByName.TryGetValue(name.Groups[1].Value, out var l)) objectsByName[name.Groups[1].Value] = l = new List<int>();
                l.Add(kv.Value);
            }
        }

        /// <summary>Where the object called <paramref name="name"/> stood when this file was saved. Unreadable = it
        /// exists but its spot cannot be told: the name is used more than once, it sits under another object, or it is
        /// an instance of a prefab.</summary>
        internal Result RootXZ(string name, out float x, out float z)
        {
            x = z = 0f;
            if (!objectsByName.TryGetValue(name, out var starts))
                return text.Contains("value: " + name + "\n") || text.Contains("value: " + name + "\r")
                    ? Result.Unreadable : Result.Absent;                    // named by a prefab instance's override
            if (starts.Count != 1) return Result.Unreadable;
            int from = starts[0], length = blockEnd[from] - from;
            for (var c = ComponentRx.Match(text, from, length); c.Success; c = c.NextMatch())
            {
                string id = c.Groups[1].Value;
                if (!classOf.TryGetValue(id, out var cls) || (cls != "4" && cls != "224")) continue;   // Transform, RectTransform
                int tFrom = blockStart[id], tLength = blockEnd[tFrom] - tFrom;
                var father = FatherRx.Match(text, tFrom, tLength);
                var pos = PositionRx.Match(text, tFrom, tLength);
                if (!father.Success || father.Groups[1].Value != "0" || !pos.Success) return Result.Unreadable;
                if (!float.TryParse(pos.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    || !float.TryParse(pos.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                    return Result.Unreadable;
                return Result.Found;
            }
            return Result.Unreadable;
        }
    }

    // ── JSON as a plain tree: every key kept in its order, every number kept as it was written ─────────────
    // An object is an Obj, an array a List<object>, a string a string, a number a Num, and true/false/null a Lit.
    internal static class Json
    {
        internal sealed class Num { public string Raw; }
        internal sealed class Lit { public string Raw; }

        internal sealed class Obj
        {
            public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();

            public object Get(string key)
            {
                foreach (var kv in Items) if (kv.Key == key) return kv.Value;
                return null;
            }

            public void Set(string key, object value)
            {
                for (int i = 0; i < Items.Count; i++)
                    if (Items[i].Key == key) { Items[i] = new KeyValuePair<string, object>(key, value); return; }
                Items.Add(new KeyValuePair<string, object>(key, value));
            }

            /// <summary>Put these keys first, in this order; every other key keeps its place after them.</summary>
            public void MoveToFront(params string[] keys)
            {
                var front = new List<KeyValuePair<string, object>>();
                foreach (var k in keys)
                    for (int i = 0; i < Items.Count; i++)
                        if (Items[i].Key == k) { front.Add(Items[i]); Items.RemoveAt(i); break; }
                Items.InsertRange(0, front);
            }
        }

        internal static Num Number(decimal v)
        {
            string s = v.ToString(CultureInfo.InvariantCulture);
            if (s.IndexOf('.') >= 0) s = s.TrimEnd('0').TrimEnd('.');
            if (s == "-0" || s.Length == 0) s = "0";
            return new Num { Raw = s };
        }

        internal static float Float(Num n) => float.Parse(n.Raw, NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>An array of numbers as floats, or null when the value is absent or anything else.</summary>
        internal static float[] Floats(object value)
        {
            if (!(value is List<object> list)) return null;
            var f = new float[list.Count];
            for (int i = 0; i < f.Length; i++)
            {
                if (!(list[i] is Num n)) return null;
                f[i] = Float(n);
            }
            return f;
        }

        internal static object Parse(string text)
        {
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;
            object value = Value(text, ref i);
            Blank(text, ref i);
            if (i != text.Length) throw new FormatException("unexpected text after the end of the JSON, at character " + i);
            return value;
        }

        static void Blank(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
        }

        static char Peek(string s, int i)
        {
            if (i >= s.Length) throw new FormatException("the JSON ends too early");
            return s[i];
        }

        static void Expect(string s, ref int i, char c)
        {
            if (Peek(s, i) != c) throw new FormatException($"expected '{c}' at character {i}");
            i++;
        }

        static object Value(string s, ref int i)
        {
            Blank(s, ref i);
            char c = Peek(s, i);
            if (c == '{')
            {
                var o = new Obj(); i++;
                Blank(s, ref i);
                if (Peek(s, i) == '}') { i++; return o; }
                while (true)
                {
                    Blank(s, ref i);
                    string key = Text(s, ref i);
                    Blank(s, ref i);
                    Expect(s, ref i, ':');
                    o.Items.Add(new KeyValuePair<string, object>(key, Value(s, ref i)));
                    Blank(s, ref i);
                    if (Peek(s, i) == ',') { i++; continue; }
                    Expect(s, ref i, '}');
                    return o;
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++;
                Blank(s, ref i);
                if (Peek(s, i) == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Blank(s, ref i);
                    if (Peek(s, i) == ',') { i++; continue; }
                    Expect(s, ref i, ']');
                    return a;
                }
            }
            if (c == '"') return Text(s, ref i);
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int from = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return new Num { Raw = s.Substring(from, i - from) };
            }
            foreach (var word in new[] { "true", "false", "null" })
                if (string.CompareOrdinal(s, i, word, 0, word.Length) == 0) { i += word.Length; return new Lit { Raw = word }; }
            throw new FormatException($"unexpected '{c}' at character {i}");
        }

        static string Text(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                char c = Peek(s, i++);
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = Peek(s, i++);
                switch (e)
                {
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("the JSON ends too early");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;   // \" \\ \/
                }
            }
        }

        internal static string Write(object value, string eol)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0, eol);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value, int depth, string eol)
        {
            switch (value)
            {
                case Obj o when o.Items.Count == 0: sb.Append("{}"); break;
                case Obj o:
                    sb.Append('{');
                    for (int i = 0; i < o.Items.Count; i++)
                    {
                        sb.Append(i == 0 ? "" : ",").Append(eol).Append(' ', (depth + 1) * 2);
                        Quote(sb, o.Items[i].Key);
                        sb.Append(": ");
                        Write(sb, o.Items[i].Value, depth + 1, eol);
                    }
                    sb.Append(eol).Append(' ', depth * 2).Append('}');
                    break;
                case List<object> a when a.Count == 0: sb.Append("[]"); break;
                case List<object> a:
                    sb.Append('[');
                    for (int i = 0; i < a.Count; i++)
                    {
                        sb.Append(i == 0 ? "" : ",").Append(eol).Append(' ', (depth + 1) * 2);
                        Write(sb, a[i], depth + 1, eol);
                    }
                    sb.Append(eol).Append(' ', depth * 2).Append(']');
                    break;
                case string s: Quote(sb, s); break;
                case Num n: sb.Append(n.Raw); break;
                case Lit l: sb.Append(l.Raw); break;
                default: sb.Append("null"); break;
            }
        }

        // A string the way JSON.stringify writes it: only the quote, the backslash, control characters and a
        // surrogate with no partner are escaped; every other character is written as it is.
        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                bool paired = (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                           || (char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(s[i - 1]));
                if (c < ' ' || (char.IsSurrogate(c) && !paired))
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
