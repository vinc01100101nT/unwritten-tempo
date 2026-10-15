using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// SERVER ONLY: every character save this map server sends, held until the character server has actually taken it.
///
/// <para><b>Why it is one per server and not one per player.</b> A save used to live on the player's own shell,
/// so the moment they logged out — or this server shut down — a save that could not reach the character server was
/// tried once and thrown away. A character server restart cost everyone who left during it whatever they had done
/// since their last save. Held here instead, a save outlives the player it belongs to and is tried again until it
/// lands.</para>
///
/// <para><b>One document per character, the newest.</b> Each save is the whole character, so a newer one already
/// says everything an older one said. Only one is ever on the wire per character — two in flight race, and whichever
/// lands last wins — and the newest waiting one goes the instant it returns.</para>
///
/// <para><b>The disk is touched only when a send fails</b> (and at shutdown, for whatever has not landed yet): one
/// file per character, replaced by a newer save, deleted the moment it lands. So it can never grow with time — only
/// with how many players on this server have a save waiting — and it survives this server crashing during an
/// outage: a server that starts finds the files and sends them before it tells the character server it exists.</para>
///
/// <para><b>Three documents per character, each on its own.</b> The character's progress, its hotbar and its
/// auto-combat settings — the last two each have a row of their own in the character store, so the 15-second progress
/// save never rewrites them. They queue, retry and land independently; a hand-over waits for all three
/// (<see cref="Flush"/>).</para>
///
/// <para><b>A save refused for good is not retried.</b> A character that no longer exists, a save too large, or a
/// malformed one is kept in a file for a person to look at, never resent forever. A save from an older hand-over
/// (the character has since been sent to another server) is refused by the character server so it cannot roll
/// that player back; it is said in the log and dropped.</para>
/// </summary>
public static class SaveOutbox
{
    sealed class Entry
    {
        // The newest document not yet landed — the one on the wire, or the one waiting to be tried again.
        public string body;
        public Action<bool> waiters;
        // Told once THIS document — or a newer one that replaced it — is done with for good: landed, refused as from
        // an older hand-over, or refused for good and kept in a file. Never on a failure that will be tried again.
        public Action settled;
        public bool inFlight;
        // A newer document captured while one was on the wire; it goes the instant that one returns.
        public string nextBody;
        public Action<bool> nextWaiters;
        public Action nextSettled;
        public bool failed;
        public float retryAt;
    }

    static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

    // A row's entries are filed under the character id plus its suffix, which also names its file on disk, so the
    // documents of one character can never be mistaken for each other. The suffix is the row's door on the character
    // server (charserver/CharacterRows.cs) with a dot in front.
    const string HotbarSuffix = ".hotbar";
    const string AutoCombatSuffix = ".auto-combat";
    const string EmblemSuffix = ".emblem";

    static string RowSuffixOf(string key)
        => key.EndsWith(HotbarSuffix) ? HotbarSuffix : key.EndsWith(AutoCombatSuffix) ? AutoCombatSuffix
         : key.EndsWith(EmblemSuffix) ? EmblemSuffix : null;
    // The progress document is the one with no row suffix — the only one that carries a storage move.
    static bool IsProgress(string key) => RowSuffixOf(key) == null;
    static string IdOf(string key)
    {
        var suffix = RowSuffixOf(key);
        return suffix == null ? key : key.Substring(0, key.Length - suffix.Length);
    }
    static string PathOf(string key)
    {
        var suffix = RowSuffixOf(key);
        return suffix == null ? "/progress" : "/" + suffix.Substring(1);
    }
    static string Keyed(string characterId, string suffix)
        => string.IsNullOrEmpty(characterId) ? characterId : characterId + suffix;
    static int failedCount;

    // The same beat a player's own save runs on: often enough that an outage ends in seconds once the character
    // server is back, rarely enough that a down character server is not hammered by every server at once.
    const float SecondsBetweenRetries = 15f;

    /// <summary>A save that failed is still waiting. While this is true the map server does not register with the
    /// character server, so nobody can be placed from an older copy of a character than this server holds.</summary>
    public static bool HasFailed => failedCount > 0;

    public static int FailedCount => failedCount;

    static string folder;

    /// <summary>Where the unsent saves are kept: per arrivals port, because that is what makes a map server process
    /// distinct on one machine across restarts (its id is new every run).</summary>
    static string Folder => folder ??= Path.Combine(Application.persistentDataPath,
                                                     "unsent-saves-" + MapServerLink.ArrivalsPort);

    /// <summary>Send this character's newest save. <paramref name="whenDone"/> is told whether THIS document (or a
    /// newer one sent in its place) reached the store on its first attempt. A save that fails is still kept and
    /// tried again — the caller is simply told it has not landed yet.</summary>
    public static void Submit(string characterId, string body, Action<bool> whenDone)
        => SubmitKey(characterId, body, whenDone, null);

    /// <summary>The same, and <paramref name="whenSettled"/> is told when this document — or a newer one sent in its
    /// place — is done with for good, on whichever attempt. What a save carrying "everything changed since the last one
    /// landed" needs: it may forget those changes only then. A newer document always carries everything an older one
    /// did, so both are told when the newer one settles.</summary>
    public static void Submit(string characterId, string body, Action<bool> whenDone, Action whenSettled)
        => SubmitKey(characterId, body, whenDone, whenSettled);

    /// <summary>Send this character's newest HOTBAR, the same way a save is sent — kept, retried and landed on its own.</summary>
    public static void SubmitHotbar(string characterId, string body, Action<bool> whenDone)
        => SubmitKey(Keyed(characterId, HotbarSuffix), body, whenDone, null);

    /// <summary>Send this character's newest AUTO-COMBAT SETTINGS, the same way — kept, retried and landed on their own.</summary>
    public static void SubmitAutoCombat(string characterId, string body, Action<bool> whenDone)
        => SubmitKey(Keyed(characterId, AutoCombatSuffix), body, whenDone, null);

    /// <summary>Send this character's newest cape EMBLEM, the same way — kept, retried and landed on its own.</summary>
    public static void SubmitEmblem(string characterId, string body, Action<bool> whenDone)
        => SubmitKey(Keyed(characterId, EmblemSuffix), body, whenDone, null);

    static void SubmitKey(string characterId, string body, Action<bool> whenDone, Action whenSettled)
    {
        if (string.IsNullOrEmpty(characterId) || body == null) { whenDone?.Invoke(false); return; }
        if (!entries.TryGetValue(characterId, out var e)) { e = new Entry(); entries[characterId] = e; }

        if (e.inFlight)
        {
            e.nextBody = body;
            e.nextWaiters += whenDone;
            e.nextSettled += whenSettled;
            return;
        }

        // A newer document replaces one that was waiting to be tried again: it says everything the old one did — so
        // whoever was waiting for the old one to settle is told when this one does.
        e.body = body;
        e.waiters += whenDone;
        e.settled += whenSettled;
        Send(characterId, e);
    }

    /// <summary>Make sure whatever is waiting for this character has landed, then say so. Answers at once, with
    /// yes, when nothing is waiting. Asked before a character is let go to another machine, so the one taking
    /// over reads the newest copy rather than one this server never managed to send.</summary>
    public static void Flush(string characterId, Action<bool> whenDone)
    {
        // EVERY DOCUMENT, and the answer only once all four have answered: whoever takes over reads the rows and the
        // progress together, so none may still be in the air.
        bool allOk = true;
        int waiting = 4;
        void One(bool ok)
        {
            allOk &= ok;
            if (--waiting == 0) whenDone?.Invoke(allOk);
        }
        FlushKey(characterId, One);
        FlushKey(Keyed(characterId, HotbarSuffix), One);
        FlushKey(Keyed(characterId, AutoCombatSuffix), One);
        FlushKey(Keyed(characterId, EmblemSuffix), One);
    }

    /// <summary>Make sure the hotbar waiting for this character, if any, has landed, then say so — yes at once when
    /// nothing is waiting.</summary>
    public static void FlushHotbar(string characterId, Action<bool> whenDone)
        => FlushKey(Keyed(characterId, HotbarSuffix), whenDone);

    /// <summary>The same for the auto-combat settings.</summary>
    public static void FlushAutoCombat(string characterId, Action<bool> whenDone)
        => FlushKey(Keyed(characterId, AutoCombatSuffix), whenDone);

    /// <summary>The same for the cape emblem.</summary>
    public static void FlushEmblem(string characterId, Action<bool> whenDone)
        => FlushKey(Keyed(characterId, EmblemSuffix), whenDone);

    static void FlushKey(string characterId, Action<bool> whenDone)
    {
        if (string.IsNullOrEmpty(characterId) || !entries.TryGetValue(characterId, out var e)
            || (e.body == null && !e.inFlight))
        {
            whenDone?.Invoke(true);
            return;
        }
        if (e.inFlight)
        {
            if (e.nextBody != null) e.nextWaiters += whenDone;
            else e.waiters += whenDone;
            return;
        }
        e.waiters += whenDone;
        Send(characterId, e);
    }

    /// <summary>Try the failed saves again, each on its own beat. Called every frame on the server; does nothing
    /// at all while nothing has failed.</summary>
    public static void Tick()
    {
        if (failedCount == 0) return;
        float now = Time.time;
        foreach (var kv in entries)
        {
            var e = kv.Value;
            if (e.failed && !e.inFlight && e.body != null && now >= e.retryAt) Send(kv.Key, e);
        }
    }

    static void Send(string characterId, Entry e)
    {
        e.inFlight = true;
        string sending = e.body;
        ServerHttp.Send("PUT", MapServerLink.CharacterServerUrl + "/server/characters/" + IdOf(characterId) + PathOf(characterId),
                        sending, MapServerLink.ServerSecret,
            (ok, reply, error, status) => Landed(characterId, e, sending, ok, reply, error, status));
    }

    static void Landed(string characterId, Entry e, string sent, bool ok, string reply, string error, long status)
    {
        e.inFlight = false;
        var told = e.waiters;
        e.waiters = null;
        // Settled unless it failed in a way that is tried again — then it stays with the body that will be retried.
        Action settledNow = null;

        if (ok)
        {
            e.body = null;
            Recovered(characterId, e);
            settledNow = e.settled; e.settled = null;
            // A storage move this document carried is now written down (see StorageMoves).
            if (IsProgress(characterId)) StorageMoves.Landed(IdOf(characterId), sent);
        }
        else if (status == 422 && IsProgress(characterId))
        {
            // THE STORAGE MOVE IT CARRIED FAILED A CHECK, and the character server wrote NOTHING — the move took the
            // whole save back with it. The same document goes again with the move taken out (and a stored thing put
            // back in its bag, a taken one taken back out), so the progress still lands. A newer document waiting behind
            // it carries the same move: it goes instead, with that move taken out of it too — the character server is
            // never asked a move twice once the player has been told it was refused — and inherits who was waiting.
            string again = StorageMoves.Refused(IdOf(characterId), sent, reply, out string refusedMove);
            if (e.nextBody != null)
            {
                e.nextBody = StorageMoves.WithoutMove(e.nextBody, refusedMove) ?? e.nextBody;
                e.body = null;
                e.nextWaiters = told + e.nextWaiters;
                told = null;
            }
            else if (again != null)
            {
                e.body = again;
                e.waiters = told;
                told = null;
                Send(characterId, e);
            }
            else
            {
                string kept = KeepRefused(characterId, sent);
                GameLog.Error(LogCat.Server, "a save for character " + characterId + " was refused over its storage move ("
                                  + reply + ") and could not be read back to send again — kept in " + kept + ".");
                StorageMoves.Abandoned(IdOf(characterId), sent, putBack: true);
                e.body = null;
                Recovered(characterId, e);
                settledNow = e.settled; e.settled = null;
            }
        }
        else if (status == 409)
        {
            // AN OLDER HAND-OVER. The character has been sent to another server since this was captured, and the
            // character server refuses it so it cannot put that player back to an older state. Never resent.
            GameLog.Warn(LogCat.Server, "a save for character " + characterId + " was refused — they have been "
                             + "handed to another server since. Dropped so it cannot roll them back. " + reply);
            // A storage move it carried goes with it: the server they are on now holds the bag as it really is.
            if (IsProgress(characterId)) StorageMoves.Abandoned(IdOf(characterId), sent, putBack: false);
            e.body = null;
            Recovered(characterId, e);
            settledNow = e.settled; e.settled = null;
        }
        else if (status == 400 || status == 404 || status == 413)
        {
            // REFUSED FOR GOOD. Resending would be refused the same way for ever, so it is kept for a person.
            string kept = KeepRefused(characterId, sent);
            GameLog.Error(LogCat.Server, "a save for character " + characterId + " was refused for good ("
                              + status + " " + reply + ") — kept in " + kept + " for a person to look at.");
            // A storage move it carried will never be written: a thing on its way into storage goes back in the bag.
            if (IsProgress(characterId))
            {
                string letGo = StorageMoves.Abandoned(IdOf(characterId), sent, putBack: true);
                // A newer document waiting behind this one carries the same move, and must not ask it again.
                if (e.nextBody != null) e.nextBody = StorageMoves.WithoutMove(e.nextBody, letGo) ?? e.nextBody;
            }
            e.body = null;
            Recovered(characterId, e);
            // SETTLED TOO: what it carried is in that file for a person, and sending it again would be refused the
            // same way for ever — the very loop a save of "everything changed since" would otherwise never leave.
            settledNow = e.settled; e.settled = null;
        }
        else
        {
            // THE CHARACTER SERVER COULD NOT TAKE IT RIGHT NOW. Kept, on disk too, and tried again on the beat.
            if (!e.failed) { e.failed = true; failedCount++; }
            e.retryAt = Time.time + SecondsBetweenRetries;
            WriteToDisk(characterId, e.body);
            GameLog.Warn(LogCat.Server, "could not write character " + characterId + " down yet — " + error
                             + ". Kept, and tried again every " + SecondsBetweenRetries + " seconds until it lands.");
        }

        // THE NEWER ONE GOES NOW, before anybody is told — a waiter may ask for another save the moment it hears
        // back, and that request must queue behind this one rather than race it.
        if (e.nextBody != null)
        {
            e.body = e.nextBody;
            e.waiters = e.nextWaiters;
            // A body that failed and is being replaced hands its settle-waiters to the newer one, which carries all of it.
            e.settled += e.nextSettled;
            e.nextBody = null;
            e.nextWaiters = null;
            e.nextSettled = null;
            Send(characterId, e);
        }

        settledNow?.Invoke();
        told?.Invoke(ok);

        if (e.body == null && !e.inFlight && e.waiters == null) entries.Remove(characterId);
    }

    static void Recovered(string characterId, Entry e)
    {
        if (e.failed) { e.failed = false; failedCount--; }
        DeleteFromDisk(characterId);
    }

    // ---- the disk ---------------------------------------------------------------------------------------------

    /// <summary>At start-up: every save a previous run of this server could not send, put back in the queue to be
    /// sent before this server registers.</summary>
    public static void LoadFromDisk()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var path in Directory.GetFiles(Folder, "*.json"))
            {
                string characterId = Path.GetFileNameWithoutExtension(path);
                if (entries.ContainsKey(characterId)) continue;
                entries[characterId] = new Entry { body = File.ReadAllText(path), failed = true, retryAt = 0f };
                failedCount++;
            }
            if (failedCount > 0)
                GameLog.Warn(LogCat.Server, failedCount + " save(s) a previous run could not send were found in "
                                 + Folder + " — sending them before this server registers.");
        }
        catch (Exception ex)
        {
            GameLog.Error(LogCat.Server, "could not read the unsent saves in " + Folder + " — " + ex.Message);
        }
    }

    /// <summary>At shutdown: everything not yet landed, written to disk so the next start sends it. A save that
    /// does land before the process ends deletes its own file.</summary>
    public static void WriteAllToDisk()
    {
        foreach (var kv in entries)
        {
            var newest = kv.Value.nextBody ?? kv.Value.body;
            if (newest != null) WriteToDisk(kv.Key, newest);
        }
    }

    static void WriteToDisk(string characterId, string body)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string final = Path.Combine(Folder, characterId + ".json");
            string temp = final + ".tmp";
            File.WriteAllText(temp, body);
            if (File.Exists(final)) File.Delete(final);
            File.Move(temp, final);
        }
        catch (Exception ex)
        {
            GameLog.Error(LogCat.Server, "could not keep character " + characterId + "'s unsent save on disk — "
                              + ex.Message + ". It is still held in memory and still being retried.");
        }
    }

    static void DeleteFromDisk(string characterId)
    {
        try
        {
            string final = Path.Combine(Folder, characterId + ".json");
            if (File.Exists(final)) File.Delete(final);
        }
        catch (Exception ex)
        {
            GameLog.Warn(LogCat.Server, "could not remove character " + characterId + "'s sent save from disk — "
                             + ex.Message + ". It would be sent again on the next start, harmlessly.");
        }
    }

    static string KeepRefused(string characterId, string body)
    {
        try
        {
            string dir = Path.Combine(Folder, "refused");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, characterId + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, body);
            return path;
        }
        catch (Exception ex)
        {
            return "(nowhere — " + ex.Message + ")";
        }
    }
}
