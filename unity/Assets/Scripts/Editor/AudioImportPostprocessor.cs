using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes every LONG audio clip stream from disk instead of unpacking into memory, the moment it enters the
/// project. A clip set to <c>Decompress On Load</c> holds its whole decoded waveform in RAM for as long as it is
/// loaded — the town theme alone is 46 MB that way, and every open client pays it again. Streaming replaces that
/// with a small rolling buffer, at no cost to how it sounds.
///
/// <para>Short clips are deliberately LEFT ALONE: below <see cref="StreamAboveSeconds"/> a clip costs almost
/// nothing to hold, and <c>Decompress On Load</c> is the right setting for a sound effect that has to fire the
/// instant it is asked for. Streaming one would add a disk seek to every hit and footstep.</para>
///
/// <para><b>It is a default, not a lock.</b> The rule runs only on a file's FIRST import — when it has no import
/// settings yet. Change a clip's Load Type by hand afterwards and that choice stands; nothing here will fight
/// you on the next reimport.</para>
///
/// <para><b>Why it applies everywhere rather than to one folder:</b> the gap this closes was a helper that only
/// covered a single purchased pack, so anything dropped anywhere else silently got the expensive setting. A
/// clip's memory cost has nothing to do with which folder it sits in.</para>
///
/// <para>Clips that were already in the project when this was written are not touched — they were set by hand at
/// the same time. To apply the rule to an old clip, right-click it in the Project window and choose Reimport
/// after clearing its import settings, or just set Load Type to Streaming yourself.</para>
///
/// <para><b>Editing this file makes Unity re-import every audio asset once</b> — adding or changing an audio
/// postprocessor invalidates the import result of everything it could touch. That took a few minutes over the
/// 226 clips here on 2026-09-07 and left the editor unresponsive while it ran. Expect it; it is not a fault, and
/// it does not repeat.</para>
/// </summary>
public class AudioImportPostprocessor : AssetPostprocessor
{
    /// <summary>A clip longer than this streams; anything shorter keeps whatever it was imported with. The
    /// project's longest short clip is a 12 s gate effect and its shortest loop is 30 s, so twenty sits in the
    /// empty middle of that gap and neither kind can land on the wrong side of it.</summary>
    const float StreamAboveSeconds = 20f;

    /// <summary>True when this import is the file's first — Unity has no saved settings for it yet. Recorded in
    /// the preprocess pass because by the postprocess pass the settings exist and the answer would always be no.
    /// Both passes run on the same instance for one asset, so a plain field carries it across.</summary>
    bool firstImport;

    void OnPreprocessAudio()
    {
        firstImport = assetImporter.importSettingsMissing;
    }

    void OnPostprocessAudio(AudioClip clip)
    {
        if (!firstImport || clip == null) return;
        if (clip.length <= StreamAboveSeconds) return;

        var importer = (AudioImporter)assetImporter;
        var settings = importer.defaultSampleSettings;
        if (settings.loadType == AudioClipLoadType.Streaming) return;

        settings.loadType = AudioClipLoadType.Streaming;
        importer.defaultSampleSettings = settings;

        // The clip's length is only known once it has been decoded, which is after the import that produced it —
        // so the new setting needs a second import to take effect. Deferred rather than immediate because
        // reimporting from inside a postprocessor re-enters the importer. The second pass sees settings that are
        // no longer missing, so firstImport is false and it stops there: exactly one extra import, never a loop.
        string path = assetPath;
        EditorApplication.delayCall += () => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        Debug.Log($"[unwritten] {System.IO.Path.GetFileName(path)} is {clip.length:0.#}s — imported as Streaming " +
                  "so it plays from disk instead of sitting decoded in memory.");
    }
}
