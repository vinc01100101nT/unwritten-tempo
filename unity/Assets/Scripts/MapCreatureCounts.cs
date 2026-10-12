using System.Collections.Generic;

/// <summary>
/// HOW MANY OF EACH CREATURE A MAP HAS — the "12/30" beside each name in the map window's list of who lives there: how
/// many are alive right now, over how many the map keeps. Both come from the server. It counts the creatures it holds in
/// that map, and adds up the world spawn list's rows there that name each creature (<see cref="WorldSpawns.KeptIn"/>).
/// A player's machine asks for a map and shows what arrives, working nothing out.
///
/// <para>A creature that only an area drawing a random mix puts there has no number of its own to keep — the mix keeps
/// one number for all of them — so it shows how many are alive alone. A map the server has not loaded answers with no
/// counts, and the list shows names alone.</para>
/// </summary>
public static class MapCreatureCounts
{
    // ---- the server's side ----------------------------------------------------------------------------------

    static readonly Dictionary<string, int> alive = new Dictionary<string, int>(32);
    static readonly Dictionary<string, int> keptHere = new Dictionary<string, int>(32);

    /// <summary>SERVER: fill the three lists, side by side, with every creature alive in <paramref name="map"/> or kept
    /// by it, how many are alive, and how many it keeps (0 when it keeps no number of its own). False when the server
    /// holds no such map.</summary>
    public static bool Count(string map, List<string> ids, List<int> counts, List<int> kept)
    {
        ids.Clear();
        counts.Clear();
        kept.Clear();
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(map);
        if (!scene.IsValid() || !scene.isLoaded) return false;
        alive.Clear();
        var here = MonsterAI3D.ActiveIn(map);
        for (int i = 0; i < here.Count; i++)
        {
            var ai = here[i];
            if (ai == null || (ai.Health != null && ai.Health.IsDead)) continue;
            var shell = NetMob.ShellForServerMob(ai.gameObject);
            if (shell == null || string.IsNullOrEmpty(shell.defId)) continue;
            alive.TryGetValue(shell.defId, out int n);
            alive[shell.defId] = n + 1;
        }
        WorldSpawns.KeptIn(map, keptHere);
        foreach (var kv in alive)
        {
            ids.Add(kv.Key);
            counts.Add(kv.Value);
            kept.Add(keptHere.TryGetValue(kv.Key, out int k) ? k : 0);
        }
        // Kept by the map but none alive at this moment: still listed, as 0 of its number.
        foreach (var kv in keptHere)
        {
            if (alive.ContainsKey(kv.Key)) continue;
            ids.Add(kv.Key);
            counts.Add(0);
            kept.Add(kv.Value);
        }
        return true;
    }

    // ---- the player's machine: what the server sent ---------------------------------------------------------

    static readonly Dictionary<string, Dictionary<string, (int alive, int kept)>> sentByMap =
        new Dictionary<string, Dictionary<string, (int alive, int kept)>>();
    static readonly Dictionary<string, float> askedAt = new Dictionary<string, float>();

    /// <summary>How long one answer is shown before the map is asked about again, while it is on screen.</summary>
    public const float AskAgainAfter = 5f;

    /// <summary>Goes up every time an answer arrives, so the list is laid out again only then.</summary>
    public static int Version { get; private set; }

    /// <summary>PLAYER'S MACHINE: ask about <paramref name="map"/>, unless it was asked moments ago.</summary>
    public static void Ask(string map)
    {
        if (string.IsNullOrEmpty(map)) return;
        float now = UnityEngine.Time.unscaledTime;
        if (askedAt.TryGetValue(map, out float at) && now - at < AskAgainAfter) return;
        if (GameNetwork.AskMapCreatureCounts(map)) askedAt[map] = now;
    }

    /// <summary>PLAYER'S MACHINE: what the server said about creature <paramref name="id"/> in <paramref name="map"/> —
    /// how many are alive, and how many the map keeps (0 when it keeps no number of its own). False when no answer about
    /// that map has arrived, or the server holds no such map.</summary>
    public static bool TryGet(string map, string id, out int aliveNow, out int keptByMap)
    {
        aliveNow = 0;
        keptByMap = 0;
        if (string.IsNullOrEmpty(map) || !sentByMap.TryGetValue(map, out var byId)) return false;
        if (byId.TryGetValue(id ?? "", out var both)) { aliveNow = both.alive; keptByMap = both.kept; }
        return true;
    }

    /// <summary>PLAYER'S MACHINE: keep what the server sent. <paramref name="held"/> is false for a map it does not hold.</summary>
    public static void TakeSent(string map, bool held, string[] ids, int[] counts, int[] kept)
    {
        if (string.IsNullOrEmpty(map)) return;
        if (!held) sentByMap.Remove(map);
        else
        {
            if (!sentByMap.TryGetValue(map, out var byId)) sentByMap[map] = byId = new Dictionary<string, (int, int)>();
            byId.Clear();
            int n = ids != null && counts != null ? System.Math.Min(ids.Length, counts.Length) : 0;
            for (int i = 0; i < n; i++)
                if (!string.IsNullOrEmpty(ids[i]))
                    byId[ids[i]] = (counts[i], kept != null && i < kept.Length ? kept[i] : 0);
        }
        Version++;
    }

    /// <summary>PLAYER'S MACHINE: drop everything a server sent — on every new connection.</summary>
    public static void ForgetSent()
    {
        sentByMap.Clear();
        askedAt.Clear();
        Version++;
    }
}
