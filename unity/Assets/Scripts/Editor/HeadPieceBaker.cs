using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Cuts the hair, the hats, the beards and the moustaches off every character model, and makes the hairstyles and
/// the headgear out of them. Run it through the live editor: <c>HeadPieceBaker.BakeAll(true)</c> writes,
/// <c>HeadPieceBaker.BakeAll(false)</c> only reports what it would do. The model files are read and never written.
///
/// <para><b>How a piece is found.</b> Hair, hats and beards are not separate objects in these models — each is a
/// patch of triangles inside the one body mesh that touches no other triangle. A patch belongs to the head when, on
/// average, at least <see cref="HeadShare"/> of its weight follows the head bone. Every model that has a plain head
/// has the same one: a 256-triangle patch in the skin colour plus four 80-triangle eyes, all identical in the
/// models' shared mesh space, so a piece cut from one model sits on every other without being moved.</para>
///
/// <para><b>What it writes.</b>
/// <list type="bullet">
/// <item><c>2_Generated/Characters/Bodies/&lt;Model&gt;.prefab</c> — each model with nothing on its head, which the
/// <c>Characters/&lt;Model&gt;</c> address now points at. A model whose helmet or hood IS its head is given the
/// plain head instead.</item>
/// <item><c>2_Generated/Characters/HeadPieces/</c> — the 13 hairstyles and the 15 pieces of headgear, each with its
/// own tiny colour texture, so a piece never drags a whole model's texture along with it.</item>
/// <item><c>3_Data/Resources/HeadPieces.asset</c> — the hairstyles, and the hair each model was made with.</item>
/// </list></para>
/// </summary>
public static class HeadPieceBaker
{
    const string ModelFolder = "Assets/1_Authored/Art/Characters/Models";
    const string BodyFolder = "Assets/2_Generated/Characters/Bodies";
    const string PieceFolder = "Assets/2_Generated/Characters/HeadPieces";
    const string TablePath = "Assets/3_Data/Resources/" + HeadPieces.ResourcePath + ".asset";
    const string PlainHeadModel = "BaseCharacter";

    // A patch belongs to the head when at least this share of its weight follows the head bone.
    const float HeadShare = 0.2f;
    // A piece whose weight is at least this much on the head bone is fixed to it; anything less bends with the body.
    const float FixedShare = 0.85f;

    const int ShellTris = 256, EyeTris = 80;
    static readonly Color32 Skin = new Color32(31, 31, 31, 255);
    static readonly Color32 EyeWhite = new Color32(255, 255, 248, 255);

    // A piece: where it is cut from, and which patches. A patch is taken when its main colour is one of these.
    struct Spec
    {
        public string id, model; public string[] colours; public bool hidesHair; public bool dropBeard;
        public Spec(string id, string model, bool hidesHair, bool dropBeard, params string[] colours)
        { this.id = id; this.model = model; this.colours = colours; this.hidesHair = hidesHair; this.dropBeard = dropBeard; }
    }

    // The 13 hairstyles, numbered as the user saw them on 2026-10-10. Two models carry a beard or a moustache in
    // their hair's own colour (dropBeard): it is left out, because nobody wears a beard. Only those two — on every
    // other hair the same test catches the locks that frame the face.
    static readonly Spec[] Hairs =
    {
        new Spec("hair01", "BlueSoldier_Female",  false, false, "139,118,67"),
        new Spec("hair02", "Casual_Female",       false, false, "92,62,42"),
        new Spec("hair03", "Worker_Female",       false, false, "127,97,50"),
        new Spec("hair04", "Knight_Golden_Female", false, false, "139,118,67"),
        new Spec("hair05", "Doctor_Female_Young", false, false, "156,122,67"),
        new Spec("hair06", "Kimono_Female",       false, false, "136,72,106"),
        new Spec("hair07", "Zombie_Female",       false, false, "59,36,21"),
        new Spec("hair08", "Viking_Female",       false, false, "107,53,33"),
        new Spec("hair09", "Ninja_Male_Hair",     false, false, "73,127,121"),
        new Spec("hair10", "Casual_Male",         false, false, "92,62,42"),
        new Spec("hair11", "Chef_Male",           false, true, "156,156,156"),
        new Spec("hair12", "Casual2_Male",        false, false, "139,118,67"),
        new Spec("hair13", "Viking_Male",         false, true, "107,53,33"),
    };

    // The 15 pieces of headgear. The full helmets and the hood hide the hair under them.
    static readonly Spec[] Headgear =
    {
        new Spec("army_helmet",          "Soldier_Male",         false, false, "58,75,40"),
        new Spec("chef_hat",             "Chef_Hat",             false, false, "245,244,233"),
        new Spec("cowboy_hat",           "Cowboy_Female",        false, false, "131,89,60"),
        new Spec("pointed_hat",          "Elf",                  false, false, "29,58,38"),
        new Spec("witch_hat",            "Witch",                false, false, "29,37,58"),
        new Spec("headband_tails",       "Kimono_Male",          false, false, "133,53,40"),
        new Spec("headband",             "Kimono_Female",        false, false, "133,53,40"),
        new Spec("golden_horned_helmet", "Knight_Golden_Male",   true,  false, "139,124,47", "100,32,27"),
        new Spec("golden_open_helmet",   "Knight_Golden_Female", false, false, "139,124,47", "100,32,27"),
        new Spec("plumed_helmet",        "Knight_Male",          true,  false, "75,75,75", "100,32,27"),
        new Spec("hood",                 "Ninja_Male",           true,  false, "22,22,22"),
        new Spec("top_hat",              "OldClassy_Male",       false, false, "20,20,15"),
        new Spec("pirate_hat",           "Pirate_Male",          false, false, "32,32,32"),
        new Spec("horned_helmet",        "VikingHelmet",         false, false, "70,70,70", "172,160,131"),
        new Spec("hard_hat",             "Worker_Male",          false, false, "163,153,64"),
    };

    // Headgear that sits on the head without covering the hair (the user's word, 2026-10-10: "we only do fixes for
    // the hats that really cover the hair"). Every other piece of headgear cuts the hair away above its brim.
    static readonly HashSet<string> LeavesHairAlone = new HashSet<string> { "headband", "headband_tails" };

    // Up in the models' shared mesh space, the middle of the head and how wide it is — read off the plain head
    // (every model shares the space; a model with no plain head of its own cannot say where its head is).
    static Vector3 sharedUp = Vector3.up, sharedCentre, sharedAcross = Vector3.right;
    static float headRadius;

    // How many evenly spaced directions around the head a hat's lower edge is measured in, and how far out from the
    // middle of the head a hat's own points count as sitting ON the head (a wide brim's tips do not).
    const int RimDirections = 24;
    const float OnHeadReach = 1.35f;

    // The hair each model was made with — measured 2026-10-10 by matching every model's hair patches against the 13
    // shapes. A model not listed was made with no hair (or only a hat, a hood or a beard).
    static readonly Dictionary<string, (string hair, string colour)> MadeWith = new Dictionary<string, (string, string)>
    {
        { "BlueSoldier_Female", ("hair01", "139,118,67") }, { "Casual2_Female", ("hair01", "127,64,61") },
        { "Doctor_Female_Old", ("hair01", "156,156,156") }, { "OldClassy_Female", ("hair01", "156,156,156") },
        { "Soldier_Female", ("hair01", "127,61,33") },      { "Suit_Female", ("hair01", "63,50,29") },
        { "Witch", ("hair01", "209,209,209") },             { "Goblin_Female", ("hair01", "37,32,33") },
        { "Casual_Female", ("hair02", "92,62,42") },        { "Cowboy_Female", ("hair02", "139,118,67") },
        { "Ninja_Female", ("hair02", "73,127,121") },       { "Ninja_Sand_Female", ("hair02", "83,36,67") },
        { "Worker_Female", ("hair03", "127,97,50") },       { "Knight_Golden_Female", ("hair04", "139,118,67") },
        { "Doctor_Female_Young", ("hair05", "156,122,67") }, { "Casual3_Female", ("hair05", "73,67,58") },
        { "Chef_Female", ("hair05", "139,136,107") },       { "Kimono_Female", ("hair06", "136,72,106") },
        { "Pirate_Female", ("hair06", "127,97,50") },       { "Zombie_Female", ("hair07", "59,36,21") },
        { "Viking_Female", ("hair08", "107,53,33") },       { "Ninja_Male_Hair", ("hair09", "73,127,121") },
        { "Casual_Male", ("hair10", "92,62,42") },          { "Cowboy_Hair", ("hair10", "92,57,20") },
        { "Doctor_Male_Young", ("hair10", "156,126,63") },  { "Suit_Male", ("hair10", "138,109,64") },
        { "Chef_Male", ("hair11", "156,156,156") },         { "OldClassy_Male", ("hair11", "156,156,156") },
        { "Doctor_Male_Old", ("hair11", "156,156,156") },   { "Casual2_Male", ("hair12", "139,118,67") },
        { "Viking_Male", ("hair13", "107,53,33") },
    };

    // A face that is not a plain head: everything on it stays part of the body, except the hair named here.
    static readonly Dictionary<string, string> FaceKeeps = new Dictionary<string, string>
    {
        { "Cow", null }, { "Pug", null }, { "Goblin_Male", null }, { "Zombie_Male", null },
        { "Goblin_Female", "37,32,33" }, { "Zombie_Female", "59,36,21" },
    };

    // An animal's head takes no hair.
    static readonly HashSet<string> NoHairHeads = new HashSet<string> { "Cow", "Pug" };

    // ---- reading a model ---------------------------------------------------------------------------------------

    class Island
    {
        public List<int> tris = new List<int>();   // triangle numbers (index into triangles / 3)
        public float headShare;
        public Color32 colour;
        public Vector3 centre;
        public bool shell, eye;
    }

    class Model
    {
        public string name;
        public GameObject asset;
        public SkinnedMeshRenderer smr;
        public Mesh mesh;
        public string[] boneNames;
        public int head = -1;
        public Color32[] triColour;
        public List<Island> islands = new List<Island>();
        public Vector3 shellCentre, faceForward;
        public bool hasPlainHead;
        public Material material;
    }

    static Model Read(string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + "/" + name + ".fbx");
        if (asset == null) return null;
        var smr = asset.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.sharedMesh == null || smr.sharedMesh.uv.Length == 0) return null;
        var m = new Model { name = name, asset = asset, smr = smr, mesh = smr.sharedMesh, material = smr.sharedMaterial };
        m.boneNames = smr.bones.Select(b => b != null ? b.name : "").ToArray();
        for (int i = 0; i < m.boneNames.Length; i++) if (m.boneNames[i].EndsWith("Head")) m.head = i;

        var verts = m.mesh.vertices; var uv = m.mesh.uv; var tris = m.mesh.triangles; var bw = m.mesh.boneWeights;
        var px = Readback(CharacterDye3D.TextureOf(m.material), out int w, out int h);

        m.triColour = new Color32[tris.Length / 3];
        for (int t = 0; t < m.triColour.Length; t++)
        {
            var c = (uv[tris[t * 3]] + uv[tris[t * 3 + 1]] + uv[tris[t * 3 + 2]]) / 3f;
            int x = Mathf.Clamp((int)(Mathf.Repeat(c.x, 1f) * w), 0, w - 1);
            int y = Mathf.Clamp((int)(Mathf.Repeat(c.y, 1f) * h), 0, h - 1);
            m.triColour[t] = px[y * w + x];
        }

        // Join vertices that sit at the same spot, then flood the triangles into patches.
        var key = new int[verts.Length];
        var ids = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < verts.Length; i++)
        {
            var k = new Vector3Int(Mathf.RoundToInt(verts[i].x * 1e6f), Mathf.RoundToInt(verts[i].y * 1e6f), Mathf.RoundToInt(verts[i].z * 1e6f));
            if (!ids.TryGetValue(k, out int id)) { id = ids.Count; ids[k] = id; }
            key[i] = id;
        }
        var parent = new int[ids.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = Find(key[tris[t]]);
            parent[Find(key[tris[t + 1]])] = a;
            parent[Find(key[tris[t + 2]])] = Find(a);
        }
        var byRoot = new Dictionary<int, Island>();
        for (int t = 0; t < tris.Length / 3; t++)
        {
            int r = Find(key[tris[t * 3]]);
            if (!byRoot.TryGetValue(r, out var isl)) { isl = new Island(); byRoot[r] = isl; m.islands.Add(isl); }
            isl.tris.Add(t);
        }

        foreach (var isl in m.islands)
        {
            double share = 0; int n = 0;
            var counts = new Dictionary<int, int>();
            var lo = Vector3.one * 1e9f; var hi = -lo;
            foreach (int t in isl.tris)
            {
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[t * 3 + k];
                    share += WeightOn(bw[vi], m.head); n++;
                    lo = Vector3.Min(lo, verts[vi]); hi = Vector3.Max(hi, verts[vi]);
                }
                var c = m.triColour[t];
                int ck = c.r << 16 | c.g << 8 | c.b;
                counts[ck] = counts.TryGetValue(ck, out int q) ? q + 1 : 1;
            }
            isl.headShare = (float)(share / n);
            int best = counts.OrderByDescending(kv => kv.Value).First().Key;
            isl.colour = new Color32((byte)(best >> 16), (byte)(best >> 8 & 255), (byte)(best & 255), 255);
            isl.centre = (lo + hi) * 0.5f;
            bool onHead = isl.headShare >= HeadShare;
            isl.shell = onHead && isl.tris.Count == ShellTris && Same(isl.colour, Skin);
            isl.eye = onHead && isl.tris.Count == EyeTris && Same(isl.colour, EyeWhite);
        }

        var shell = m.islands.FirstOrDefault(i => i.shell);
        m.hasPlainHead = shell != null;
        if (shell != null)
        {
            m.shellCentre = shell.centre;
            var eyes = m.islands.Where(i => i.eye).ToList();
            var eyeCentre = eyes.Count > 0 ? eyes.Aggregate(Vector3.zero, (s, i) => s + i.centre) / eyes.Count : shell.centre;
            var f = eyeCentre - shell.centre;
            m.faceForward = f.sqrMagnitude > 1e-12f ? f.normalized : Vector3.forward;
        }
        return m;
    }

    static float WeightOn(BoneWeight w, int bone) =>
        (w.boneIndex0 == bone ? w.weight0 : 0f) + (w.boneIndex1 == bone ? w.weight1 : 0f) +
        (w.boneIndex2 == bone ? w.weight2 : 0f) + (w.boneIndex3 == bone ? w.weight3 : 0f);

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

    static bool Matches(Color32 c, string[] wanted)
    {
        foreach (var s in wanted)
        {
            var p = s.Split(',');
            if (c.r == byte.Parse(p[0]) && c.g == byte.Parse(p[1]) && c.b == byte.Parse(p[2])) return true;
        }
        return false;
    }

    static string Hex(Color32 c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    // A beard or a moustache: below the middle of the head and in front of it.
    static bool IsBeard(Model m, Island isl)
    {
        if (!m.hasPlainHead) return false;
        var d = isl.centre - m.shellCentre;
        float down = -Vector3.Dot(d, Up(m));
        float front = Vector3.Dot(d, m.faceForward);
        return down > 0f && front > 0f && front > down * 0.25f;
    }

    // The models' mesh space is turned (Z up in the file). Up is whichever axis runs from the feet to the head.
    static Vector3 Up(Model m)
    {
        var b = m.mesh.bounds;
        var axis = m.shellCentre - b.center;
        float ax = Mathf.Abs(axis.x), ay = Mathf.Abs(axis.y), az = Mathf.Abs(axis.z);
        if (ay >= ax && ay >= az) return new Vector3(0, Mathf.Sign(axis.y), 0);
        if (az >= ax) return new Vector3(0, 0, Mathf.Sign(axis.z));
        return new Vector3(Mathf.Sign(axis.x), 0, 0);
    }

    // The patches cut OFF a model to leave it bare-headed.
    static List<Island> ToStrip(Model m)
    {
        var strip = new List<Island>();
        FaceKeeps.TryGetValue(m.name, out string faceHair);
        bool faceBody = FaceKeeps.ContainsKey(m.name);
        foreach (var isl in m.islands)
        {
            if (isl.headShare < HeadShare || isl.shell || isl.eye) continue;
            if (faceBody) { if (faceHair != null && Matches(isl.colour, new[] { faceHair })) strip.Add(isl); continue; }
            // Skin-coloured patches are part of the head (an ear, a nose), not something worn on it.
            if (Same(isl.colour, Skin)) continue;
            strip.Add(isl);
        }
        return strip;
    }

    // ---- the bake ----------------------------------------------------------------------------------------------

    /// <summary>Bake every hairstyle, every piece of headgear and every bare body. With <paramref name="write"/>
    /// false nothing is written and the report says what would be.</summary>
    public static string BakeAll(bool write)
    {
        var report = new System.Text.StringBuilder();
        var models = new Dictionary<string, Model>();
        Model Get(string n) { if (!models.TryGetValue(n, out var m)) { m = Read(n); models[n] = m; } return m; }

        var names = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder })
            .Select(g => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)))
            .OrderBy(n => n, System.StringComparer.Ordinal).ToList();

        var plain = Get(PlainHeadModel);
        if (plain == null || !plain.hasPlainHead) return "FAILED: " + PlainHeadModel + " has no plain head to give the helmet bodies.";
        sharedUp = Up(plain);
        sharedCentre = plain.shellCentre;
        sharedAcross = Vector3.Cross(sharedUp, Mathf.Abs(sharedUp.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
        {
            var shell = plain.islands.First(i => i.shell);
            var pverts = plain.mesh.vertices; var ptris = plain.mesh.triangles;
            headRadius = 0f;
            foreach (int t in shell.tris)
                for (int k = 0; k < 3; k++)
                {
                    var d = pverts[ptris[t * 3 + k]] - sharedCentre;
                    headRadius = Mathf.Max(headRadius, (d - sharedUp * Vector3.Dot(d, sharedUp)).magnitude);
                }
        }

        if (write) { EnsureFolder(BodyFolder); EnsureFolder(PieceFolder); }
        var scene = EditorSceneManager.NewPreviewScene();
        var table = write ? (AssetDatabase.LoadAssetAtPath<HeadPieces>(TablePath) ?? ScriptableObject.CreateInstance<HeadPieces>()) : null;
        try
        {
            // ---- pieces
            if (table != null) table.hairstyles.Clear();
            foreach (var spec in Hairs.Concat(Headgear))
            {
                var m = Get(spec.model);
                if (m == null) { report.Append("FAILED piece ").Append(spec.id).Append(": no model ").Append(spec.model).Append('\n'); continue; }
                var take = m.islands.Where(i => i.headShare >= HeadShare && !i.shell && !i.eye && Matches(i.colour, spec.colours)
                                                && !(spec.dropBeard && IsBeard(m, i))).ToList();
                int dropped = spec.dropBeard ? m.islands.Count(i => i.headShare >= HeadShare && Matches(i.colour, spec.colours) && IsBeard(m, i)) : 0;
                if (take.Count == 0) { report.Append("FAILED piece ").Append(spec.id).Append(": no patch of that colour\n"); continue; }
                var prefab = BuildPiece(spec, m, take, write, scene, out bool bends, out int tris, out int colours);
                report.Append(spec.id).Append(" from ").Append(spec.model).Append(": ").Append(take.Count).Append(" patches, ")
                      .Append(tris).Append(" triangles, ").Append(colours).Append(" colours, ")
                      .Append(bends ? "bends with the body" : "fixed to the head")
                      .Append(dropped > 0 ? ", " + dropped + " beard patch(es) left out" : "").Append('\n');
                if (table != null && spec.id.StartsWith("hair")) table.hairstyles.Add(new HeadPieces.Hairstyle { id = spec.id, piece = prefab });
            }

            // ---- bare bodies
            if (table != null) table.bodies.Clear();
            foreach (var name in names)
            {
                var m = Get(name);
                if (m == null) { report.Append("skipped body ").Append(name).Append(" (no texture — not in the roster)\n"); continue; }
                var strip = ToStrip(m);
                // A helmet or a hood that IS the head gets the plain head in its place. A plain head with fewer
                // eyes than the plain head has (a hat hid the rest) gets the plain head's eyes in place of its own.
                bool giveHead = !m.hasPlainHead && !FaceKeeps.ContainsKey(name);
                bool giveEyes = m.hasPlainHead && m.islands.Count(i => i.eye) < plain.islands.Count(i => i.eye);
                if (giveEyes) strip.AddRange(m.islands.Where(i => i.eye));
                var body = BuildBody(m, strip, giveHead || giveEyes ? plain : null, giveHead, write, scene,
                                     out float height, out bool ownColours);
                report.Append("body ").Append(name).Append(": ").Append(strip.Count).Append(" patches cut (")
                      .Append(strip.Sum(i => i.tris.Count)).Append(" triangles)")
                      .Append(giveHead ? ", plain head given" : giveEyes ? ", plain eyes given" : "")
                      .Append(giveHead || giveEyes ? (ownColours ? " in its own colours" : " drawn with the plain head's own material") : "")
                      .Append(", stands ").Append(height.ToString("F3")).Append('\n');

                MadeWith.TryGetValue(name, out var made);
                if (made.hair != null && !strip.Any(i => Matches(i.colour, new[] { made.colour })))
                    report.Append("  WARNING: ").Append(name).Append(" was listed as made with ").Append(made.hair)
                          .Append(" in ").Append(made.colour).Append(" but no patch of that colour was cut\n");
                if (table != null)
                    table.bodies.Add(new HeadPieces.Body
                    {
                        model = name,
                        hair = made.hair ?? HeadPieces.NoHair,
                        hairColour = made.hair != null ? Hex(ParseColour(made.colour)) : "",
                        takesHair = !NoHairHeads.Contains(name),
                    });
                if (write && body != null) PointAddressAt(name, body, report);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        if (write)
        {
            // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
            if (!AssetDatabase.Contains(table)) AssetDatabase.CreateAsset(table, TablePath);
            else EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            HeadPieces.Forget();
        }
        return report.ToString();
    }

    static Color32 ParseColour(string rgb)
    {
        var p = rgb.Split(',');
        return new Color32(byte.Parse(p[0]), byte.Parse(p[1]), byte.Parse(p[2]), 255);
    }

    // ---- a piece -----------------------------------------------------------------------------------------------

    static GameObject BuildPiece(Spec spec, Model m, List<Island> take, bool write, UnityEngine.SceneManagement.Scene scene,
                                 out bool bends, out int triCount, out int colourCount)
    {
        var src = m.mesh;
        var verts = src.vertices; var normals = src.normals; var tris = src.triangles; var bw = src.boneWeights;

        // Weight on the head bone, over every vertex of the piece.
        double share = 0; int n = 0;
        foreach (var isl in take) foreach (int t in isl.tris) for (int k = 0; k < 3; k++) { share += WeightOn(bw[tris[t * 3 + k]], m.head); n++; }
        bends = share / n < FixedShare;

        // The piece's colours, most-covered first, and a vertex per (vertex, colour) so each triangle reads its own.
        var cover = new Dictionary<int, int>();
        foreach (var isl in take) foreach (int t in isl.tris)
        {
            var c = m.triColour[t]; int ck = c.r << 16 | c.g << 8 | c.b;
            cover[ck] = cover.TryGetValue(ck, out int q) ? q + 1 : 1;
        }
        var palette = cover.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        colourCount = palette.Count;
        int texW = Mathf.NextPowerOfTwo(Mathf.Max(4, palette.Count * 4));

        // Bones a bending piece follows, by name, in the order its weights will use.
        var usedBones = new SortedSet<string>(System.StringComparer.Ordinal);
        if (bends)
            foreach (var isl in take) foreach (int t in isl.tris) for (int k = 0; k < 3; k++)
            {
                var w = bw[tris[t * 3 + k]];
                if (w.weight0 > 0) usedBones.Add(m.boneNames[w.boneIndex0]);
                if (w.weight1 > 0) usedBones.Add(m.boneNames[w.boneIndex1]);
                if (w.weight2 > 0) usedBones.Add(m.boneNames[w.boneIndex2]);
                if (w.weight3 > 0) usedBones.Add(m.boneNames[w.boneIndex3]);
            }
        var boneList = usedBones.ToList();
        int Remap(int i) => boneList.IndexOf(m.boneNames[i]);

        var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pw = new List<BoneWeight>();
        var pt = new List<int>();
        var made = new Dictionary<long, int>();
        foreach (var isl in take) foreach (int t in isl.tris)
        {
            var c = m.triColour[t]; int pal = palette.IndexOf(c.r << 16 | c.g << 8 | c.b);
            for (int k = 0; k < 3; k++)
            {
                int vi = tris[t * 3 + k];
                long key = (long)vi << 8 | (uint)pal;
                if (!made.TryGetValue(key, out int ni))
                {
                    ni = pv.Count; made[key] = ni;
                    pv.Add(verts[vi]); pn.Add(normals[vi]);
                    pu.Add(new Vector2((pal * 4 + 2f) / texW, 0.5f));
                    if (bends)
                    {
                        var w = bw[vi];
                        pw.Add(new BoneWeight
                        {
                            boneIndex0 = w.weight0 > 0 ? Remap(w.boneIndex0) : 0, weight0 = w.weight0,
                            boneIndex1 = w.weight1 > 0 ? Remap(w.boneIndex1) : 0, weight1 = w.weight1,
                            boneIndex2 = w.weight2 > 0 ? Remap(w.boneIndex2) : 0, weight2 = w.weight2,
                            boneIndex3 = w.weight3 > 0 ? Remap(w.boneIndex3) : 0, weight3 = w.weight3,
                        });
                    }
                }
                pt.Add(ni);
            }
        }
        triCount = pt.Count / 3;
        if (!write) return null;

        var mesh = new Mesh { name = spec.id + "_mesh" };
        mesh.indexFormat = pv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(pv); mesh.SetNormals(pn); mesh.SetUVs(0, pu);
        if (bends)
        {
            mesh.boneWeights = pw.ToArray();
            var bind = new Matrix4x4[boneList.Count];
            for (int i = 0; i < bind.Length; i++) bind[i] = src.bindposes[System.Array.IndexOf(m.boneNames, boneList[i])];
            mesh.bindposes = bind;
        }
        mesh.SetTriangles(pt, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        string meshPath = PieceFolder + "/" + spec.id + "_mesh.asset";
        // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
        SaveAsset(mesh, meshPath);

        var colours = palette.Select(ck => new Color32((byte)(ck >> 16), (byte)(ck >> 8 & 255), (byte)(ck & 255), 255)).ToArray();
        var texPath = PieceFolder + "/" + spec.id + "_colours.png";
        WritePalette(colours, texW, texPath);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        var mat = new Material(m.material) { name = spec.id };
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        string matPath = PieceFolder + "/" + spec.id + ".mat";
        // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
        SaveAsset(mat, matPath);

        // AN EXISTING PIECE IS EDITED IN PLACE, never replaced: a Head item links to the prefab's root object, and a
        // prefab saved fresh over the old one gives that object a new id and breaks every item that wears it.
        string prefabPath = PieceFolder + "/" + spec.id + ".prefab";
        bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
        GameObject go;
        if (existed) go = PrefabUtility.LoadPrefabContents(prefabPath);
        else
        {
            go = new GameObject(spec.id);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }
        var hp = go.GetComponent<HeadPiece>() ?? go.AddComponent<HeadPiece>();
        hp.colours = colours.Select(c => (Color)c).ToArray();
        hp.hidesHair = spec.hidesHair;
        // WHERE IT CUTS THE HAIR: how low the piece comes down ON the head, in each direction around it — so a worn
        // hat shows the hair hanging below it and nothing above it, and a tilted hat cuts along its own tilt.
        hp.coversHair = Headgear.Any(h => h.id == spec.id) && !LeavesHairAlone.Contains(spec.id);
        hp.cutCentre = sharedCentre; hp.cutUp = sharedUp; hp.cutAcross = sharedAcross;
        hp.cutRim = hp.coversHair ? RimOf(pv) : System.Array.Empty<float>();
        hp.bones = bends ? boneList.ToArray() : System.Array.Empty<string>();
        var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        var savedMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (bends)
        {
            if (go.GetComponent<MeshRenderer>() != null) Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            if (go.GetComponent<MeshFilter>() != null) Object.DestroyImmediate(go.GetComponent<MeshFilter>());
            var smr = go.GetComponent<SkinnedMeshRenderer>() ?? go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = savedMesh; smr.sharedMaterial = savedMat;
        }
        else
        {
            if (go.GetComponent<SkinnedMeshRenderer>() != null) Object.DestroyImmediate(go.GetComponent<SkinnedMeshRenderer>());
            (go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>()).sharedMesh = savedMesh;
            (go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>()).sharedMaterial = savedMat;
        }
        // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        if (existed) PrefabUtility.UnloadPrefabContents(go);
        else Object.DestroyImmediate(go);
        return prefab;
    }

    // The lowest the piece comes down in each direction around the head, counting only its points over the head
    // itself — a wide brim's tips hang lower than where the hat meets the head and would cut the hair too low.
    // A direction with no point over the head takes its nearest neighbour's; each is then the lowest of itself and
    // its two neighbours, so a gap between two points can never let a strand through.
    static float[] RimOf(List<Vector3> pv)
    {
        var rim = new float[RimDirections];
        for (int i = 0; i < rim.Length; i++) rim[i] = float.MaxValue;
        var other = Vector3.Cross(sharedUp, sharedAcross);
        foreach (var v in pv)
        {
            var d = v - sharedCentre;
            float h = Vector3.Dot(d, sharedUp);
            var flat = d - sharedUp * h;
            if (flat.magnitude > headRadius * OnHeadReach) continue;
            float angle = Mathf.Atan2(Vector3.Dot(flat, other), Vector3.Dot(flat, sharedAcross));
            int s = Mathf.FloorToInt(Mathf.Repeat(angle / (2f * Mathf.PI), 1f) * RimDirections) % RimDirections;
            rim[s] = Mathf.Min(rim[s], h);
        }
        for (int pass = 0; pass < RimDirections; pass++)
            for (int i = 0; i < rim.Length; i++)
                if (rim[i] == float.MaxValue)
                {
                    float a = rim[(i + RimDirections - 1) % RimDirections], b = rim[(i + 1) % RimDirections];
                    rim[i] = Mathf.Min(a, b);
                }
        var smooth = new float[RimDirections];
        for (int i = 0; i < rim.Length; i++)
            smooth[i] = Mathf.Min(rim[i], Mathf.Min(rim[(i + RimDirections - 1) % RimDirections], rim[(i + 1) % RimDirections]));
        return smooth;
    }

    // A strip of 4x4 blocks, one per colour: every UV sits in the middle of its block, so the texture can be drawn
    // with any filtering and still return the exact colour.
    static void WritePalette(Color32[] colours, int width, string path)
    {
        var tex = new Texture2D(width, 4, TextureFormat.RGBA32, false);
        var px = new Color32[width * 4];
        for (int x = 0; x < width; x++)
        {
            var c = colours[Mathf.Min(x / 4, colours.Length - 1)];
            for (int y = 0; y < 4; y++) px[y * width + x] = c;
        }
        tex.SetPixels32(px); tex.Apply();
        // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
    }

    // ---- a bare body -------------------------------------------------------------------------------------------

    static GameObject BuildBody(Model m, List<Island> strip, Model plainHead, bool withShell, bool write,
                                UnityEngine.SceneManagement.Scene scene, out float height, out bool ownColours)
    {
        ownColours = true;
        var src = m.mesh;
        var cut = new HashSet<int>(strip.SelectMany(i => i.tris));
        var tris = src.triangles;
        var keepTris = new List<int>();
        for (int t = 0; t < tris.Length / 3; t++) if (!cut.Contains(t)) { keepTris.Add(tris[t * 3]); keepTris.Add(tris[t * 3 + 1]); keepTris.Add(tris[t * 3 + 2]); }

        // Only the vertices the kept triangles use.
        var verts = src.vertices; var normals = src.normals; var tangents = src.tangents; var uv = src.uv; var bw = src.boneWeights;
        var remap = new Dictionary<int, int>();
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nu = new List<Vector2>(); var nw = new List<BoneWeight>();
        var ntri = new List<int>(keepTris.Count);
        foreach (int vi in keepTris)
        {
            if (!remap.TryGetValue(vi, out int ni))
            {
                ni = nv.Count; remap[vi] = ni;
                nv.Add(verts[vi]); nn.Add(normals[vi]); if (tangents.Length > 0) nt.Add(tangents[vi]); nu.Add(uv[vi]); nw.Add(bw[vi]);
            }
            ntri.Add(ni);
        }

        // The plain head (or only its eyes) put on from the plain-head model, its weights moved onto this model's
        // bones by name. Drawn in this model's own skin and eye colours when its texture has them exactly; when it
        // does not (the knights' textures have no white), drawn as a second part with the plain head's own material.
        var headTris = new List<int>();
        if (plainHead != null)
        {
            var skinUv = FindColour(m, Skin, out bool skinExact); var eyeUv = FindColour(m, EyeWhite, out bool eyeExact);
            ownColours = (skinExact || !withShell) && eyeExact;
            var puv = plainHead.mesh.uv;
            var pverts = plainHead.mesh.vertices; var pnorm = plainHead.mesh.normals; var pbw = plainHead.mesh.boneWeights;
            var ptris = plainHead.mesh.triangles;
            int Bone(int i) => System.Array.IndexOf(m.boneNames, plainHead.boneNames[i]);
            var added = new Dictionary<int, int>();
            foreach (var isl in plainHead.islands.Where(i => (withShell && i.shell) || i.eye))
                foreach (int t in isl.tris)
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = ptris[t * 3 + k];
                        if (!added.TryGetValue(vi, out int ni))
                        {
                            ni = nv.Count; added[vi] = ni;
                            nv.Add(pverts[vi]); nn.Add(pnorm[vi]); if (nt.Count > 0) nt.Add(new Vector4(1, 0, 0, 1));
                            nu.Add(!ownColours ? puv[vi] : isl.eye ? eyeUv : skinUv);
                            var w = pbw[vi];
                            nw.Add(new BoneWeight
                            {
                                boneIndex0 = w.weight0 > 0 ? Bone(w.boneIndex0) : 0, weight0 = w.weight0,
                                boneIndex1 = w.weight1 > 0 ? Bone(w.boneIndex1) : 0, weight1 = w.weight1,
                                boneIndex2 = w.weight2 > 0 ? Bone(w.boneIndex2) : 0, weight2 = w.weight2,
                                boneIndex3 = w.weight3 > 0 ? Bone(w.boneIndex3) : 0, weight3 = w.weight3,
                            });
                        }
                        (ownColours ? ntri : headTris).Add(ni);
                    }
        }

        var mesh = new Mesh { name = m.name + "_Body" };
        mesh.indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(nv); mesh.SetNormals(nn); if (nt.Count == nv.Count) mesh.SetTangents(nt); mesh.SetUVs(0, nu);
        mesh.boneWeights = nw.ToArray();
        mesh.bindposes = src.bindposes;
        mesh.subMeshCount = headTris.Count > 0 ? 2 : 1;
        mesh.SetTriangles(ntri, 0);
        if (headTris.Count > 0) mesh.SetTriangles(headTris, 1);
        mesh.RecalculateBounds();

        // HOW TALL IT STANDS, measured the way the game measures it: the union of its renderers' bounds.
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(m.asset, scene);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
        smr.sharedMesh = mesh;
        // The body's own material first: it is the one a body's colours and its signature are read from.
        smr.sharedMaterials = headTris.Count > 0 ? new[] { m.material, plainHead.material } : new[] { m.material };
        FitBounds(smr);
        height = Character3DBody.MeasureHeight(inst);

        GameObject prefab = null;
        if (write)
        {
            string meshPath = BodyFolder + "/" + m.name + "_Body.asset";
            // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
            SaveAsset(mesh, meshPath);
            smr.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            FitBounds(smr);
            inst.name = m.name;
            // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
            prefab = PrefabUtility.SaveAsPrefabAsset(inst, BodyFolder + "/" + m.name + ".prefab");
        }
        Object.DestroyImmediate(inst);
        return prefab;
    }

    // The renderer's box, made to hold exactly what it now draws: baked at rest and expressed in the space the box
    // is kept in (the root bone's, when it has one).
    static void FitBounds(SkinnedMeshRenderer smr)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var space = smr.rootBone != null ? smr.rootBone : smr.transform;
        var lo = Vector3.one * 1e9f; var hi = -lo;
        foreach (var v in baked.vertices)
        {
            var p = space.InverseTransformPoint(smr.transform.TransformPoint(v));
            lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
        }
        Object.DestroyImmediate(baked);
        smr.localBounds = new Bounds((lo + hi) * 0.5f, hi - lo);
    }

    // The middle of the first texel of this colour in the model's texture — exactly this colour when it has it,
    // else the nearest (exact says which).
    static Vector2 FindColour(Model m, Color32 c, out bool exact)
    {
        var px = Readback(CharacterDye3D.TextureOf(m.material), out int w, out int h);
        int best = -1, bestD = int.MaxValue;
        for (int i = 0; i < px.Length; i++)
        {
            int d = Mathf.Abs(px[i].r - c.r) + Mathf.Abs(px[i].g - c.g) + Mathf.Abs(px[i].b - c.b);
            if (d < bestD) { bestD = d; best = i; if (d == 0) break; }
        }
        exact = bestD == 0;
        return new Vector2((best % w + 0.5f) / w, (best / w + 0.5f) / h);
    }

    // ---- the address -------------------------------------------------------------------------------------------

    static void PointAddressAt(string model, GameObject body, System.Text.StringBuilder report)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { report.Append("  FAILED: no Addressables settings\n"); return; }
        string address = CharacterModels.AddressOf(model);
        AddressableAssetEntry old = null;
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries) if (e.address == address) { old = e; break; }
            if (old != null) break;
        }
        if (old == null) { report.Append("  FAILED: no address ").Append(address).Append('\n'); return; }
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(body));
        if (old.guid == guid) return;
        var group = old.parentGroup;
        var labels = old.labels.ToList();
        // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
        settings.RemoveAssetEntry(old.guid, false);
        var entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = address;
        foreach (var l in labels) entry.SetLabel(l, true, false, false);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }

    // ---- small helpers -----------------------------------------------------------------------------------------

    static Color32[] Readback(Texture tex, out int w, out int h)
    {
        w = tex.width; h = tex.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        t.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        return px;
    }

    static void SaveAsset(Object asset, string path)
    {
        var existing = AssetDatabase.LoadMainAssetAtPath(path);
        if (existing == null) { AssetDatabase.CreateAsset(asset, path); return; }
        // A mesh is rewritten through the mesh's own calls. Copied over as serialized data it kept the old mesh's
        // drawing data in the running editor, and a body whose vertex count changed was drawn as nothing at all;
        // deleted and made again, every prefab already loaded lost its link to it.
        if (existing is Mesh to && asset is Mesh from) { Rewrite(from, to); EditorUtility.SetDirty(to); return; }
        // Overwritten in place, so everything that points at it keeps pointing at it.
        EditorUtility.CopySerialized(asset, existing);
        EditorUtility.SetDirty(existing);
    }

    static void Rewrite(Mesh from, Mesh to)
    {
        to.Clear();
        to.indexFormat = from.indexFormat;
        to.SetVertices(from.vertices);
        to.SetNormals(from.normals);
        if (from.tangents.Length == from.vertexCount) to.SetTangents(from.tangents);
        to.SetUVs(0, from.uv);
        if (from.boneWeights.Length == from.vertexCount) { to.boneWeights = from.boneWeights; to.bindposes = from.bindposes; }
        to.subMeshCount = from.subMeshCount;
        for (int s = 0; s < from.subMeshCount; s++) to.SetTriangles(from.GetTriangles(s), s);
        to.RecalculateBounds();
        to.name = from.name;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        // editor-write-ok: reached only through BakeAll(true), which the user runs on purpose
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
