using System;
using UnityEngine;

/// <summary>
/// The storage seam for a character's progress. There is exactly ONE implementation and exactly one place a
/// character's data lives: the account service's database (<see cref="ServerProfileStore"/>). The local JSON
/// file that used to sit beside it is gone — two homes for one character is the drift bug this project keeps
/// refusing to ship, and the file was the copy nobody could see going stale.
///
/// <para>Reading and writing are ASYNCHRONOUS because the answer comes over a network. That is not an
/// inconvenience to work around with a blocking call — a synchronous read here would freeze the game for the
/// length of a round trip.</para>
/// </summary>
public interface IProfileStore
{
    /// <summary>Fetch the signed-in character's progress. Delivers null when there is nothing to continue —
    /// no session, a character nobody has played yet, or a request that failed.</summary>
    void Load(Action<SaveData> onLoaded);

    /// <summary>Write the character's progress. <paramref name="onDone"/> reports whether it landed.</summary>
    void Save(SaveData data, Action<bool> onDone = null);

    /// <summary>Write the character's APPEARANCE — a separate call because it is stored on the character
    /// record rather than inside the progress document, and it changes at completely different moments.</summary>
    void SaveAppearance(string modelName, string[] dyeSources, string[] dyeTargets, Action<bool> onDone = null);
}

/// <summary>Stores progress with the account service, as whoever <see cref="AccountSession"/> says is playing.
/// The requests themselves go through <see cref="AccountClient"/>, the one place that talks to that service.</summary>
public sealed class ServerProfileStore : IProfileStore
{
    // JsonUtility needs a concrete type to read into, and the service wraps the save as { "progress": … }.
    [Serializable]
    class ProgressEnvelope
    {
        public SaveData progress;
    }

    [Serializable]
    class AppearanceBody
    {
        public string modelName;
        public string[] dyeSources;
        public string[] dyeTargets;
    }

    string CharacterPath => "/characters/" + AccountSession.CharacterId;

    public void Load(Action<SaveData> onLoaded)
    {
        if (!AccountSession.IsActive)
        {
            onLoaded?.Invoke(null);   // nobody is playing yet — nothing to continue, and not an error
            return;
        }

        AccountClient.Send("GET", CharacterPath + "/progress", null, true, (ok, body, error) =>
        {
            if (!ok)
            {
                Debug.LogWarning($"[unwritten] Could not load '{AccountSession.CharacterName}': {error}");
                onLoaded?.Invoke(null);
                return;
            }

            var envelope = JsonUtility.FromJson<ProgressEnvelope>(body);
            // A character nobody has played yet comes back as an empty document, which reads as version 0.
            // That is NOT a save — hand back null so the game builds a fresh character instead.
            bool played = envelope?.progress != null && envelope.progress.version > 0;
            onLoaded?.Invoke(played ? envelope.progress : null);
        });
    }

    public void Save(SaveData data, Action<bool> onDone = null)
    {
        if (data == null || !AccountSession.IsActive) { onDone?.Invoke(false); return; }
        Write("PUT", CharacterPath + "/progress",
              JsonUtility.ToJson(new ProgressEnvelope { progress = data }), "Save", onDone);
    }

    public void SaveAppearance(string modelName, string[] dyeSources, string[] dyeTargets, Action<bool> onDone = null)
    {
        if (!AccountSession.IsActive) { onDone?.Invoke(false); return; }
        var body = new AppearanceBody
        {
            modelName = modelName,
            dyeSources = dyeSources ?? Array.Empty<string>(),
            dyeTargets = dyeTargets ?? Array.Empty<string>(),
        };
        Write("PATCH", CharacterPath + "/appearance", JsonUtility.ToJson(body), "Appearance save", onDone);
    }

    static void Write(string method, string path, string body, string what, Action<bool> onDone)
    {
        AccountClient.Send(method, path, body, true, (ok, _, error) =>
        {
            // Losing a save silently is the one failure a player never forgives, so it is always said out loud.
            if (!ok) Debug.LogWarning($"[unwritten] {what} failed for '{AccountSession.CharacterName}': {error}");
            onDone?.Invoke(ok);
        });
    }
}

/// <summary>Holds the active profile store. One implementation today; the field exists so a test or a future
/// offline mode can stand in without every caller knowing.</summary>
public static class ProfileStore
{
    public static IProfileStore Current = new ServerProfileStore();
}
