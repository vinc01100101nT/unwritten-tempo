using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// ONE MESSAGE FOR EVERY BLOW A SKILL TICK LANDS. While a batch is open on the server, the two blow announcements
/// (<see cref="GameNetwork.AnnounceBlowOnMob"/>, <see cref="GameNetwork.AnnounceBlowOnPlayer"/>) are collected here
/// instead of being sent one by one; when the outermost batch closes, every connection that can see at least one of
/// the units struck gets the whole list in one <see cref="GameNetwork.HitBatch"/>.
///
/// <para><b>Why.</b> A rain that catches ten creatures five times a second sent fifty messages a second to every
/// watcher, one per creature per volley. Batched, it is five. A multi-hit strike is one message per cast instead of
/// one per hit.</para>
///
/// <para><b>Split.</b> A multi-hit strike is worked out ONCE per target (one roll, one blow through defence) and
/// shown as its hits: while <see cref="Split"/> is above 1, a landed blow is announced as that many equal pieces
/// (the remainder on the last), each with its own place in the queue and its own rising total — every number
/// final, worked out here. The watching machine only plays them out one after another.</para>
///
/// <para>Server only. Nothing here decides anything about a blow; it only changes how the decided blow travels.</para>
/// </summary>
public static class BlowBatch
{
    static int depth;

    /// <summary>True while a batch is open — announcements are collected, not sent.</summary>
    public static bool Open => depth > 0;

    /// <summary>How many pieces a landed blow is shown as — the hit count of the multi-hit strike landing right now,
    /// 1 otherwise. Set and restored around the one strike by the applier.</summary>
    public static int Split = 1;

    /// <summary>Seconds between the pieces of a multi-hit strike on the watching machine — the Ragnarok cascade.</summary>
    public const float PieceInterval = 0.2f;

    public readonly struct Scope : System.IDisposable
    {
        public void Dispose() => End();
    }

    /// <summary>Open a batch (nests). Use in a <c>using</c>; the outermost close sends.</summary>
    public static Scope Begin()
    {
        depth++;
        return default;
    }

    static void End()
    {
        if (depth <= 0) { depth = 0; return; }
        if (--depth > 0) return;
        Flush();
    }

    // Collected entries, reused so a batch allocates only the message's own arrays.
    static readonly List<GameNetwork.HitResult> mobHits = new List<GameNetwork.HitResult>(16);
    static readonly List<byte> mobDelays = new List<byte>(16);
    static readonly List<int> mobLog = new List<int>(16);
    static readonly List<NetworkIdentity> mobWho = new List<NetworkIdentity>(16);
    static readonly List<GameNetwork.PlayerHitResult> playerHits = new List<GameNetwork.PlayerHitResult>(4);
    static readonly List<byte> playerDelays = new List<byte>(4);
    static readonly List<int> playerLog = new List<int>(4);
    static readonly List<NetworkIdentity> playerWho = new List<NetworkIdentity>(4);
    static readonly Dictionary<int, NetworkConnectionToClient> audience = new Dictionary<int, NetworkConnectionToClient>(8);

    /// <summary>How a blow of <paramref name="number"/> splits into <paramref name="pieces"/> shown hits: equal pieces,
    /// the remainder on the last. Plain numbers in and out.</summary>
    public static int Piece(int number, int pieces, int index)
    {
        if (pieces <= 1) return number;
        int each = number / pieces;
        return index == pieces - 1 ? number - each * (pieces - 1) : each;
    }

    /// <summary>Collect one shown hit on a creature. <paramref name="logNumber"/> is what the combat log writes for it
    /// (the whole blow, on its first piece), or −1 for a piece the log has already counted.</summary>
    public static void AddMob(NetworkIdentity who, in GameNetwork.HitResult hit, int delayPieces, int logNumber)
    {
        mobWho.Add(who); mobHits.Add(hit); mobDelays.Add((byte)Mathf.Clamp(delayPieces, 0, 255)); mobLog.Add(logNumber);
    }

    /// <summary>Collect one shown hit on a person — the twin of <see cref="AddMob"/>.</summary>
    public static void AddPlayer(NetworkIdentity who, in GameNetwork.PlayerHitResult hit, int delayPieces, int logNumber)
    {
        playerWho.Add(who); playerHits.Add(hit); playerDelays.Add((byte)Mathf.Clamp(delayPieces, 0, 255)); playerLog.Add(logNumber);
    }

    static void Flush()
    {
        if (mobHits.Count == 0 && playerHits.Count == 0) return;
        audience.Clear();
        Gather(mobWho);
        Gather(playerWho);
        if (audience.Count > 0)
        {
            var msg = new GameNetwork.HitBatch
            {
                mobHits = mobHits.ToArray(), mobDelays = mobDelays.ToArray(), mobLog = mobLog.ToArray(),
                playerHits = playerHits.ToArray(), playerDelays = playerDelays.ToArray(), playerLog = playerLog.ToArray(),
            };
            foreach (var kv in audience)
                if (kv.Value != null && kv.Value.isReady) kv.Value.Send(msg);
        }
        mobHits.Clear(); mobDelays.Clear(); mobLog.Clear(); mobWho.Clear();
        playerHits.Clear(); playerDelays.Clear(); playerLog.Clear(); playerWho.Clear();
        audience.Clear();
    }

    // Everybody who can see any unit in the list — the union of their watchers, each once.
    static void Gather(List<NetworkIdentity> who)
    {
        for (int i = 0; i < who.Count; i++)
        {
            var id = who[i];
            if (id == null || id.observers == null) continue;
            foreach (var kv in id.observers) audience[kv.Key] = kv.Value;
        }
    }

    // ---- the watching machine: pieces waiting for their turn -------------------------------------------------------

    struct QueuedMob { public float at; public GameNetwork.HitResult hit; public int log; }
    struct QueuedPlayer { public float at; public GameNetwork.PlayerHitResult hit; public int log; }
    static readonly List<QueuedMob> queuedMob = new List<QueuedMob>(32);
    static readonly List<QueuedPlayer> queuedPlayer = new List<QueuedPlayer>(8);

    /// <summary>PLAYER'S MACHINE: hold a piece until its turn.</summary>
    public static void QueueMob(in GameNetwork.HitResult hit, int delayPieces, int log)
        => queuedMob.Add(new QueuedMob { at = Time.time + delayPieces * PieceInterval, hit = hit, log = log });

    /// <summary>PLAYER'S MACHINE: hold a piece on a person until its turn.</summary>
    public static void QueuePlayer(in GameNetwork.PlayerHitResult hit, int delayPieces, int log)
        => queuedPlayer.Add(new QueuedPlayer { at = Time.time + delayPieces * PieceInterval, hit = hit, log = log });

    /// <summary>PLAYER'S MACHINE, once a frame: draw every piece whose turn has come, in the order they were queued.
    /// Nothing is worked out — each piece is drawn exactly as the server sent it.</summary>
    public static void TickPlayback()
    {
        if (queuedMob.Count == 0 && queuedPlayer.Count == 0) return;
        float now = Time.time;
        for (int i = 0; i < queuedMob.Count; )
        {
            if (now < queuedMob[i].at) { i++; continue; }
            var q = queuedMob[i];
            queuedMob.RemoveAt(i);
            GameNetwork.DrawMobHit(q.hit, q.log);
        }
        for (int i = 0; i < queuedPlayer.Count; )
        {
            if (now < queuedPlayer[i].at) { i++; continue; }
            var q = queuedPlayer[i];
            queuedPlayer.RemoveAt(i);
            GameNetwork.DrawPlayerHit(q.hit, q.log);
        }
    }

    /// <summary>PLAYER'S MACHINE: forget every waiting piece — the connection ended or the map changed.</summary>
    public static void ClearPlayback()
    {
        queuedMob.Clear();
        queuedPlayer.Clear();
    }
}
