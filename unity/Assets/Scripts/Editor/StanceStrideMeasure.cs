using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MEASURES a walk, run or sprint clip on every character body, so nobody types a step frame or a stride: poses an
/// unseen copy of each body to every frame of the clip in a scratch scene, reads where its ankles are, and hands the
/// two ankle paths to <see cref="StrideMath"/>. Writes the binding's foot-plants, each body's walking speed and the
/// fallback for a body added later. Runs only when the Stance Composer's user picks the clip or presses its step
/// button — never on its own.
/// </summary>
public static class StanceStrideMeasure
{
    // Fractions of the body's own height. A foot within 2% of its lowest point is on the ground; a furthest-forward
    // point within 5% of it is a landing rather than a swing that never touched down.
    const float ContactFraction = 0.02f, PlantFraction = 0.05f;

    /// <summary>Measure <paramref name="clip"/> for <paramref name="b"/> and write the answer onto it. Returns a one-line
    /// summary for the console. A binding that is not a walk cycle is left untouched.</summary>
    public static string Measure(StanceBinding b, AnimationClip clip)
    {
        if (b == null || !StanceCatalog.IsWalkCycle(b.id)) return null;
        if (b.footPlants == null) b.footPlants = new List<FootPlant>();
        if (b.bodyStrides == null) b.bodyStrides = new List<BodyStride>();
        if (clip == null)
        {
            b.footPlants.Clear();
            b.bodyStrides.Clear();
            b.speedPerHumanScale = 0f;
            return $"{b.id}: no clip, so nothing to measure.";
        }

        var bodies = RosterBodies(out string rosterProblem);
        if (bodies.Count == 0) return $"{b.id}: nothing measured — {rosterProblem}";

        int frames = Mathf.Max(3, Mathf.RoundToInt(clip.length * clip.frameRate));
        var strides = new List<BodyStride>();
        var perHumanScale = new List<float>();
        var plantVotes = new Dictionary<string, List<FootPlant>>();
        var voteCount = new Dictionary<string, int>();
        var skipped = new List<string>();
        var leftF = new float[frames]; var leftH = new float[frames];
        var rightF = new float[frames]; var rightH = new float[frames];

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Measuring " + clip.name, bodies[i].name, (float)i / bodies.Count);
                var body = Object.Instantiate(bodies[i]);
                body.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    SceneManager.MoveGameObjectToScene(body, scene);
                    body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    body.transform.localScale = Vector3.one;
                    var anim = body.GetComponent<Animator>();
                    float height = Character3DBody.MeasureHeight(body);
                    if (anim == null || anim.avatar == null || !anim.avatar.isHuman || height <= 1e-4f)
                    { skipped.Add(bodies[i].name); continue; }
                    var leftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var rightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);

                    for (int f = 0; f < frames; f++)
                    {
                        clip.SampleAnimation(body, f / clip.frameRate);
                        Vector3 l = body.transform.InverseTransformPoint(leftFoot.position);
                        Vector3 r = body.transform.InverseTransformPoint(rightFoot.position);
                        leftF[f] = l.z; leftH[f] = l.y; rightF[f] = r.z; rightH[f] = r.y;
                    }

                    var plants = new List<FootPlant>();
                    StrideMath.FindPlants(leftF, leftH, false, PlantFraction * height, plants);
                    StrideMath.FindPlants(rightF, rightH, true, PlantFraction * height, plants);
                    plants.Sort((a, c) => a.frame.CompareTo(c.frame));

                    float sum = 0f; int counted = 0;
                    foreach (var p in plants)
                    {
                        float s = p.right
                            ? StrideMath.ContactSpeed(rightF, rightH, p.frame, ContactFraction * height, clip.frameRate)
                            : StrideMath.ContactSpeed(leftF, leftH, p.frame, ContactFraction * height, clip.frameRate);
                        if (s > 0f) { sum += s; counted++; }
                    }
                    if (counted == 0) { skipped.Add(bodies[i].name); continue; }

                    float speed = sum / counted;
                    strides.Add(new BodyStride { avatar = anim.avatar.name, speed = speed });
                    if (anim.humanScale > 1e-5f) perHumanScale.Add(speed / anim.humanScale);

                    string key = Summary(plants);
                    if (!plantVotes.ContainsKey(key)) { plantVotes[key] = plants; voteCount[key] = 0; }
                    voteCount[key]++;
                }
                finally { Object.DestroyImmediate(body); }
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            EditorUtility.ClearProgressBar();
        }

        // The step frames are the same on every body, because leg length does not change when a foot lands; the
        // answer most bodies agree on is kept, and how many disagreed is reported.
        string best = null; int bestVotes = 0;
        foreach (var kv in voteCount) if (kv.Value > bestVotes) { best = kv.Key; bestVotes = kv.Value; }
        b.footPlants = best != null ? plantVotes[best] : new List<FootPlant>();
        b.bodyStrides = strides;
        b.speedPerHumanScale = Median(perHumanScale);

        string line = $"{b.id} '{clip.name}': steps {best ?? "none found"} (agreed by {bestVotes} of {strides.Count} bodies), " +
                      $"measured on {strides.Count} bodies";
        if (skipped.Count > 0) line += $"; skipped {skipped.Count} with no humanoid skeleton or no footfall ({string.Join(", ", skipped)})";
        return line + ".";
    }

    /// <summary>"L8 R29" — the same short form the composer's step button shows.</summary>
    public static string Summary(List<FootPlant> plants)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < plants.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var p = plants[i];
            sb.Append(p != null && p.right ? 'R' : 'L').Append(p != null ? p.frame : 0);
        }
        return sb.ToString();
    }

    // Every body the game can wear: the Addressable entries carrying the character-model label (CharacterModels).
    static List<GameObject> RosterBodies(out string problem)
    {
        problem = "";
        var list = new List<GameObject>();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { problem = "the project has no Addressable settings, so there is no character roster."; return list; }
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            foreach (var entry in group.entries)
                if (entry != null && entry.labels.Contains(CharacterModels.Label))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath);
                    if (go != null) list.Add(go);
                }
        }
        if (list.Count == 0) problem = $"no Addressable entry carries the '{CharacterModels.Label}' label.";
        return list;
    }

    static float Median(List<float> values)
    {
        if (values.Count == 0) return 0f;
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : 0.5f * (values[mid - 1] + values[mid]);
    }
}
