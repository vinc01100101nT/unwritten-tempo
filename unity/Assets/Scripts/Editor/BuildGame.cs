using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-click Windows build, pinned high in Tools ▸ unwritten so shipping a
/// playable .exe is a single menu click instead of a trip through the Build
/// Profiles window.
///
/// What it does (think of it as `npm run build` for the game):
///   1. Collects the ENABLED scenes from File ▸ Build Settings, in order, and
///      guarantees Systems.unity is first (it's the never-unloaded base layer
///      that everything else loads additively under — if it isn't first the
///      build boots into an empty map).
///   2. Builds a StandaloneWindows64 player into  &lt;repo&gt;/Builds/Windows/.
///   3. Opens the output folder so you can zip it or run the installer step.
///
/// The result is a FOLDER: unwritten.exe + unwritten_Data/ + a few DLLs. That
/// whole folder is the game — zip it and it runs on any Win64 PC, no Unity
/// required. To turn it into a single Setup.exe installer, see the Inno Setup
/// note in the build instructions.
/// </summary>
public static class BuildGame
{
    const string ProductExe = "unwritten.exe";
    const string OutDir = "Builds/Windows"; // relative to the repo root (one level above Assets/)

    [MenuItem("Tools/unwritten/Build Game (Windows .exe)", priority = 3)]
    static void BuildWindows()
    {
        // Scenes: take whatever is enabled in Build Settings, but force Systems first.
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToList();

        if (scenes.Count == 0)
        {
            EditorUtility.DisplayDialog("Build Game",
                "No scenes are enabled in File ▸ Build Settings. Add at least Systems.unity (first) plus a map.",
                "OK");
            return;
        }

        int sysIdx = scenes.FindIndex(p => p.EndsWith("/Systems.unity", StringComparison.OrdinalIgnoreCase));
        if (sysIdx > 0)
        {
            var sys = scenes[sysIdx];
            scenes.RemoveAt(sysIdx);
            scenes.Insert(0, sys);
        }

        // Output path is relative to the repo root (the folder that contains Assets/).
        string repoRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string outFolder = Path.Combine(repoRoot, OutDir);
        Directory.CreateDirectory(outFolder);
        string exePath = Path.Combine(outFolder, ProductExe);

        var opts = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None, // flip to BuildOptions.Development for a profiler-attachable test build
        };

        Debug.Log($"[unwritten] Building Windows player → {exePath}\n  Scenes ({scenes.Count}): {string.Join(", ", scenes)}");

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            double mb = summary.totalSize / (1024.0 * 1024.0);
            Debug.Log($"[unwritten] Build ✓ — {mb:F1} MB in {summary.totalTime.TotalSeconds:F0}s at {outFolder}. " +
                      "That whole folder is the game; zip it to share, or run the Inno Setup step to make a Setup.exe.");
            EditorUtility.RevealInFinder(exePath);
        }
        else
        {
            Debug.LogError($"[unwritten] Build {summary.result} with {summary.totalErrors} error(s). See the Console above for details.");
        }
    }
}
