using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// One-click "raw dense GLB → drop-ready prop", the mass-production path for houses/trees/props.
/// Chains the two halves of the pipeline in a single pass over the selected GLB(s):
/// <list type="number">
/// <item><b>Optimize</b> — shells out to <c>tools/optimize-glb.sh</c> (in place, <c>INPLACE=1</c>: weld +
/// quadric simplify + texture downsize + meshopt compression), so the source GLB's GUID is preserved and the
/// heavy TRELLIS/Hunyuan mesh shrinks to game budget.</item>
/// <item><b>Reimport</b> the now-optimized bytes, then <b>bake</b> each into a URP-materialed, collider'd,
/// base-pivoted prop prefab at the GLB's <b>native size</b> (no auto-scaling) — reusing <see cref="GlbPropImporter"/>
/// (the single owner of the bake logic; this tool never re-implements it).</item>
/// </list>
/// Select the raw <c>.glb</c> model asset(s) in the Project window and run. Needs Git Bash + Node (the
/// script calls <c>npx @gltf-transform/cli</c>). Keeper — the standing bulk GLB pipeline.
/// </summary>
public static class GlbPropPipeline
{
    [MenuItem("Tools/unwritten/Props/Optimize + Import GLB Prop…", priority = 506)]
    static void Run()
    {
        var lit = GlbPropImporter.FindLit();
        if (lit == null)
        { EditorUtility.DisplayDialog("Optimize + Import GLB Prop", "URP/Lit shader not found — is this a URP project?", "OK"); return; }

        var glbs = Selection.objects
            .Select(AssetDatabase.GetAssetPath)
            .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            .Distinct().ToArray();

        // Empty selection → the mass path: every raw .glb under Assets/Prefabs (skip already-optimized copies).
        bool wholeFolder = glbs.Length == 0;
        if (wholeFolder)
        {
            const string root = "Assets/Prefabs";
            string absRoot = Application.dataPath + "/Prefabs";   // dataPath = …/unity/Assets
            if (!Directory.Exists(absRoot))
            { EditorUtility.DisplayDialog("Optimize + Import GLB Prop", $"Nothing selected and no {root} folder to scan.", "OK"); return; }

            // Filesystem scan (glTFast .glb assets aren't reliably indexed by AssetDatabase type filters).
            glbs = Directory.GetFiles(absRoot, "*.glb", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("_opt.glb", StringComparison.OrdinalIgnoreCase))
                .Select(f => "Assets" + f.Substring(Application.dataPath.Length).Replace('\\', '/'))
                .Distinct().ToArray();

            if (glbs.Length == 0)
            {
                EditorUtility.DisplayDialog("Optimize + Import GLB Prop",
                    $"No raw .glb files found under {root}.\n\nEither select GLB(s) in the Project window, or drop raw GLBs under {root} first.", "OK");
                return;
            }
        }

        // Idempotency (mass path): skip a GLB only if it's TRULY processed — i.e. the GLB itself is already
        // optimized (carries KHR_mesh_quantization) AND a baked prop prefab exists. Keying on the prefab alone
        // is wrong: a fresh dense GLB dropped in under an old name (whose prefab lingers) must still be processed.
        int alreadyBaked = 0;
        if (wholeFolder)
        {
            var pending = glbs.Where(p => !(IsQuantized(AbsPath(p)) && File.Exists(AbsPath(PropPrefabPath(p))))).ToArray();
            alreadyBaked = glbs.Length - pending.Length;
            glbs = pending;
            if (glbs.Length == 0)
            {
                EditorUtility.DisplayDialog("Optimize + Import GLB Prop",
                    $"All {alreadyBaked} GLB(s) under Assets/Prefabs are already baked into props — nothing to do.\n\n" +
                    "Select specific GLB(s) in the Project window to force re-processing.", "OK");
                return;
            }
        }

        string repoRoot = RepoRoot();
        string script = Path.Combine(repoRoot, "tools", "optimize-glb.sh");
        if (!File.Exists(script))
        { EditorUtility.DisplayDialog("Optimize + Import GLB Prop", $"Optimizer script not found:\n{script}", "OK"); return; }

        string bash = FindBash();
        if (bash == null)
        {
            EditorUtility.DisplayDialog("Optimize + Import GLB Prop",
                "Git Bash (bash.exe) not found. Install Git for Windows, or run tools/optimize-glb.sh manually " +
                "then use Tools ▸ unwritten ▸ Props ▸ Import GLB Prop… on the results.", "OK");
            return;
        }

        // Idempotency (per file): a GLB already carrying KHR_mesh_quantization was optimized before — skip the
        // optimize pass for it (re-simplifying would compound the loss) but still (re)bake it.
        var toOptimize = glbs.Where(p => !IsQuantized(AbsPath(p))).ToArray();
        int alreadyOptimized = glbs.Length - toOptimize.Length;

        string scope = wholeFolder ? $"{glbs.Length} unbaked GLB(s) under Assets/Prefabs" : $"{glbs.Length} selected GLB(s)";
        string skips = (alreadyBaked > 0 ? $"• {alreadyBaked} already-baked → skipped entirely\n" : "")
                     + (alreadyOptimized > 0 ? $"• {alreadyOptimized} already-optimized → bake only (no re-optimize)\n" : "");
        if (!EditorUtility.DisplayDialog("Optimize + Import GLB Prop",
                $"Process {scope} into prop prefabs?\n\n" +
                skips +
                (toOptimize.Length > 0 ? "• optimize IN PLACE (overwrites originals)\n• needs Node (npx @gltf-transform/cli) on PATH\n" : "") +
                "\nThis can take a while for dense meshes.", "Run", "Cancel"))
            return;

        string log = "";
        if (toOptimize.Length > 0)
        {
            // Repo-relative, forward-slashed paths ("unity/Assets/…"). Resolved against WorkingDirectory=repoRoot,
            // so they work under EVERY bash flavor (MSYS/Cygwin/WSL) without any drive-letter mount (/d vs /mnt/d).
            var relPaths = toOptimize.Select(p => "unity/" + p).ToArray();
            if (!Optimize(bash, repoRoot, relPaths, out log))
            {
                Debug.LogError($"[unwritten] GLB optimize step failed:\n{log}");
                EditorUtility.DisplayDialog("Optimize + Import GLB Prop — optimize failed",
                    "The optimize step failed (see Console for the full log). No props were baked.", "OK");
                return;
            }
            Debug.Log($"[unwritten] GLB optimize done:\n{log}");
        }

        // Reimport the optimized bytes SYNCHRONOUSLY, BEFORE the batch. Inside StartAssetEditing a reimport is
        // DEFERRED to StopAssetEditing, so BakeOne would load the STALE pre-optimize hierarchy (mesh still on a
        // child → base-pivot bake skipped, empty Meshes folder, child left in). That deferral is exactly why a
        // first run produced a bad prefab and only a second pass fixed it.
        foreach (var glb in glbs)
            AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate);

        int built = 0;
        GlbPropImporter.EnsureFolders();   // once, BEFORE batching — CreateFolder isn't visible mid-StartAssetEditing
        // Extract embedded textures to standalone PNGs AFTER the optimize+reimport above (so we grab the downsized
        // texture) and BEFORE the batch (a mid-StartAssetEditing import would defer). Repoints materials off the .glb.
        var texMap = GlbPropImporter.ExtractTextures(glbs);
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var glb in glbs)
                if (GlbPropImporter.BakeOne(glb, lit, texMap)) built++;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[unwritten] Optimize + Import: baked {built}/{glbs.Length} GLB prop(s) into Assets/Prefabs/Props.");
        EditorUtility.DisplayDialog("Optimize + Import GLB Prop — done",
            $"Optimized + baked {built} drop-ready prop prefab(s) into:\nAssets/Prefabs/Props\n\n" +
            "Drag a prefab into the scene (it ground-snaps) or place it with the Prop Palette.", "OK");
    }

    // Run the optimizer once over all files (INPLACE=1). Returns false on non-zero exit.
    // Paths are repo-relative and resolved against WorkingDirectory=repoRoot — no drive-letter mount assumptions.
    static bool Optimize(string bash, string repoRoot, string[] relPaths, out string log)
    {
        var args = new StringBuilder();
        args.Append("tools/optimize-glb.sh");
        foreach (var p in relPaths) args.Append(" \"").Append(p).Append('"');

        var psi = new ProcessStartInfo
        {
            FileName = bash,
            Arguments = args.ToString(),
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.EnvironmentVariables["INPLACE"] = "1";

        var sb = new StringBuilder();
        using (var proc = new Process { StartInfo = psi })
        {
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Poll so the editor shows a live progress bar instead of a silent frozen main thread.
            // (First run pays npx cold-start; without this it looks like "nothing happens".)
            try
            {
                int n = relPaths.Length;
                for (float t = 0f; !proc.WaitForExit(200); t += 0.02f)
                    EditorUtility.DisplayProgressBar("Optimize GLB Prop",
                        $"Optimizing {n} GLB(s) via gltf-transform… (first run downloads the CLI)", Mathf.Repeat(t, 1f));
                proc.WaitForExit();   // ensure async stdout/stderr are flushed
            }
            finally { EditorUtility.ClearProgressBar(); }

            log = sb.ToString();
            return proc.ExitCode == 0;
        }
    }

    // Repo root = two levels up from Assets/ (…/unity/Assets → …/).
    static string RepoRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    // "Assets/…" project path → absolute filesystem path.
    static string AbsPath(string assetPath) => Application.dataPath + assetPath.Substring("Assets".Length);

    // Where GlbPropImporter bakes a given GLB's prop prefab (same naming it uses).
    static string PropPrefabPath(string glbAssetPath) => $"Assets/Prefabs/Props/{Path.GetFileNameWithoutExtension(glbAssetPath)}.prefab";

    // True if the GLB already carries KHR_mesh_quantization (i.e. it's been through the optimizer before).
    // Reads only the binary GLB's JSON chunk — no import, no allocation of the whole file.
    static bool IsQuantized(string absGlbPath)
    {
        try
        {
            using var fs = File.OpenRead(absGlbPath);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != 0x46546C67u) return false;   // magic "glTF"
            br.ReadUInt32();                                    // version
            br.ReadUInt32();                                    // total length
            uint chunkLen = br.ReadUInt32();
            if (br.ReadUInt32() != 0x4E4F534Au) return false;   // first chunk must be JSON
            var json = System.Text.Encoding.UTF8.GetString(br.ReadBytes((int)chunkLen));
            return json.Contains("KHR_mesh_quantization");
        }
        catch { return false; }   // unreadable/odd file → treat as not-yet-optimized (safe: it'll be optimized)
    }

    // Find GIT BASH specifically — NOT WSL's C:\Windows\System32\bash.exe (mounts drives at /mnt/d, breaks the
    // script) and NOT a bare-PATH bash of unknown flavor. Known Git install dirs win; PATH is a last resort with
    // System32 excluded.
    static string FindBash()
    {
        string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // 1) Known Git-for-Windows locations, bin\bash.exe first (the wrapper that sets up MSYS mounts).
        foreach (var c in new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            Path.Combine(lad, @"Programs\Git\bin\bash.exe"),
            @"C:\Program Files\Git\usr\bin\bash.exe",
        })
            if (File.Exists(c)) return c;

        // 2) PATH fallback — skip the Windows system dir so we never grab the WSL launcher.
        string sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);   // …\System32
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var d = dir.Trim();
                if (string.IsNullOrEmpty(d) || d.StartsWith(sysRoot, StringComparison.OrdinalIgnoreCase)) continue;
                var c = Path.Combine(d, "bash.exe");
                if (File.Exists(c)) return c;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }
}
