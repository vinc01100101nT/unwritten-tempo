using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SERVER ONLY: every storage move a character has started and the character server has not yet answered — at most one
/// per character.
///
/// <para><b>A move rides the character's own save.</b> Storing takes the thing out of the live bag at once; taking holds
/// it back until the character server has written it down. Every save written while a move is waiting carries it (the
/// outbox keeps only the newest document, so an older one that carried it may never be sent); the character server
/// applies a move id once however many documents carry it. A TAKE also adds the thing to the bag of every document that
/// carries it, so the bag that is written holds what storage gave up.</para>
///
/// <para><b>Filed by character, not on the shell.</b> A save outlives the player who made it (<see cref="SaveOutbox"/>),
/// so what to do when its answer comes back must too: put the thing back in a document that was refused, even after
/// the player has gone.</para>
/// </summary>
public static class StorageMoves
{
    sealed class Pending
    {
        public StorageMoveWire move;
        public NetPlayer shell;
        public Item def;
        // The piece itself, for gear: the very copy that left the bag, or the one storage is handing back.
        public EquipmentInstance piece;
    }

    static readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();

    /// <summary>Is a move of this character's still waiting for the character server?</summary>
    public static bool InFlight(string characterId)
        => !string.IsNullOrEmpty(characterId) && pending.ContainsKey(characterId);

    /// <summary>Start a move. The caller has already taken a stored thing out of the live bag; a taken one is added
    /// when the move lands.</summary>
    public static void Begin(string characterId, NetPlayer shell, StorageMoveWire move, Item def, EquipmentInstance piece)
    {
        if (string.IsNullOrEmpty(characterId) || move == null) return;
        pending[characterId] = new Pending { move = move, shell = shell, def = def, piece = piece };
    }

    /// <summary>The move this character's next save must carry, or null.</summary>
    public static StorageMoveWire For(string characterId)
        => !string.IsNullOrEmpty(characterId) && pending.TryGetValue(characterId, out var p) ? p.move : null;

    /// <summary>Make <paramref name="save"/> say what the move does to the bag: a take adds the thing to it. A store
    /// changes nothing here — the thing already left the live bag the document was captured from.</summary>
    public static void Compose(SaveData save, StorageMoveWire move)
    {
        if (save == null || move == null || move.IsStore) return;
        AddToSavedBag(save, move.itemId, move.count, move.refine, move.cards, move.roll, move.copyId);
    }

    /// <summary>Add a thing to a document's bag the way the live bag adds one: a piece of gear is its own entry at the
    /// end; a stack joins the first stack of the same item, or becomes a new entry at the end.</summary>
    public static void AddToSavedBag(SaveData d, string itemId, int count, int refine, string cards, string roll, string copyId)
    {
        if (d == null || string.IsNullOrEmpty(itemId) || count <= 0) return;
        copyId ??= "";
        int n = d.bagItemIds != null ? d.bagItemIds.Length : 0;
        if (copyId.Length == 0 && d.bagCounts != null)
            for (int i = 0; i < n; i++)
            {
                bool stack = d.bagCopyIds == null || i >= d.bagCopyIds.Length || string.IsNullOrEmpty(d.bagCopyIds[i]);
                if (d.bagItemIds[i] == itemId && stack && i < d.bagCounts.Length) { d.bagCounts[i] += count; return; }
            }
        d.bagItemIds = Grown(d.bagItemIds, n, itemId);
        d.bagCounts = Grown(d.bagCounts, n, copyId.Length > 0 ? 1 : count);
        d.bagRefines = Grown(d.bagRefines, n, copyId.Length > 0 ? refine : 0);
        d.bagCards = Grown(d.bagCards, n, copyId.Length > 0 ? cards ?? "" : "");
        d.bagRolls = Grown(d.bagRolls, n, copyId.Length > 0 ? roll ?? "" : "");
        d.bagCopyIds = Grown(d.bagCopyIds, n, copyId);
    }

    /// <summary>Take back out of a document's bag exactly what <see cref="AddToSavedBag"/> put in: that many off the first
    /// stack of the item (the entry goes when nothing is left), or the entry that is that very piece of gear. What a
    /// refused TAKE needs — storage kept the thing, so the bag that is written must not hold it as well.</summary>
    public static void RemoveFromSavedBag(SaveData d, string itemId, int count, string copyId)
    {
        if (d == null || d.bagItemIds == null || string.IsNullOrEmpty(itemId) || count <= 0) return;
        copyId ??= "";
        for (int i = 0; i < d.bagItemIds.Length; i++)
        {
            string copy = d.bagCopyIds != null && i < d.bagCopyIds.Length ? d.bagCopyIds[i] ?? "" : "";
            if (d.bagItemIds[i] != itemId || copy != copyId) continue;
            if (copyId.Length == 0 && d.bagCounts != null && i < d.bagCounts.Length && d.bagCounts[i] > count)
            {
                d.bagCounts[i] -= count;
                return;
            }
            d.bagItemIds = Without(d.bagItemIds, i);
            d.bagCounts = Without(d.bagCounts, i);
            d.bagRefines = Without(d.bagRefines, i);
            d.bagCards = Without(d.bagCards, i);
            d.bagRolls = Without(d.bagRolls, i);
            d.bagCopyIds = Without(d.bagCopyIds, i);
            return;
        }
    }

    // The array `a` with entry `at` taken out; untouched when it has no such entry.
    static T[] Without<T>(T[] a, int at)
    {
        if (a == null || at < 0 || at >= a.Length) return a;
        var b = new T[a.Length - 1];
        for (int i = 0, j = 0; i < a.Length; i++) if (i != at) b[j++] = a[i];
        return b;
    }

    // The array `a` with exactly `n` entries (a short one padded with the default), plus `last`.
    static T[] Grown<T>(T[] a, int n, T last)
    {
        var b = new T[n + 1];
        for (int i = 0; i < n; i++)
        {
            if (a != null && i < a.Length) b[i] = a[i];
            else if (typeof(T) == typeof(string)) b[i] = (T)(object)"";
        }
        b[n] = last;
        return b;
    }

    // ---- the character server's answer ------------------------------------------------------------------

    /// <summary>A progress save for <paramref name="characterId"/> landed. When it carried the waiting move, that move is
    /// now written down: a taken thing goes into the live bag, the player is told, and the move is done.</summary>
    public static void Landed(string characterId, string sentBody)
    {
        if (!pending.TryGetValue(characterId, out var p) || !Carries(sentBody, p.move)) return;
        pending.Remove(characterId);
        var m = p.move;
        GameLog.Info(LogCat.Server, "storage " + m.kind + " of " + m.count + " " + m.itemId
                     + (m.copyId.Length > 0 ? " (copy " + m.copyId + ")" : "") + " for character " + characterId + " written down");

        var shell = p.shell;
        var c = shell != null ? shell.ServerCombatUnit as Character : null;
        if (shell == null || c == null) return;
        string what = p.piece != null ? ItemTooltip.CopyTitle(p.piece) : m.count + " " + p.def.displayName;
        if (m.IsStore)
        {
            shell.StorageChanged(p.def, m.count, p.piece, true);
            shell.KeeperSaid("Stored " + what + ".", true);
            return;
        }
        // NOT AN ARRIVAL: the thing was already this account's, and the save that just landed already holds it in the
        // bag — so it is laid in directly, never through the door that announces something new.
        PutInLiveBag(c, p.def, m.count, p.piece);
        shell.StorageChanged(p.def, m.count, p.piece, false);
        shell.PushBagToOwner(alsoTheBar: true);
        shell.KeeperSaid("Took " + what + " from storage.", true);
    }

    /// <summary>A progress save for <paramref name="characterId"/> was refused (422) because the storage move it carried
    /// failed a check — nothing at all was written. Returns the same document with the move taken out and its bag put
    /// back to what is true without it (a stored thing back in, a taken thing back out): the document to send instead.
    /// Null when the document cannot be read. <paramref name="moveId"/> is the move that was refused, so a newer
    /// document still waiting can have the same move taken out of it (<see cref="WithoutMove"/>).</summary>
    public static string Refused(string characterId, string sentBody, string reply, out string moveId)
    {
        moveId = "";
        ProgressEnvelope env;
        try { env = JsonUtility.FromJson<ProgressEnvelope>(sentBody); }
        catch { env = null; }
        if (env == null || env.progress == null) return null;
        var m = env.storage;
        moveId = m != null ? m.moveId ?? "" : "";
        string reason = ReasonOf(reply);
        GameLog.Warn(LogCat.Server, "storage " + (m != null ? m.kind + " of " + m.count + " " + m.itemId : "move")
                     + " for character " + characterId + " was refused (" + reason + ") — sending the save again without it");
        Revert(env);

        // THE PLAYER, IF STILL HERE, gets their thing back too — the first refusal of this move only.
        if (m != null && pending.TryGetValue(characterId, out var p) && p.move.moveId == moveId)
        {
            pending.Remove(characterId);
            var shell = p.shell;
            var c = shell != null ? shell.ServerCombatUnit as Character : null;
            if (c != null)
            {
                if (m.IsStore) { PutInLiveBag(c, p.def, m.count, p.piece); shell.PushBagToOwner(alsoTheBar: true); }
                shell.KeeperSaid(Sentence(reason), false);
                shell.ForgetStorageCopy();
            }
        }
        return JsonUtility.ToJson(env);
    }

    /// <summary>The save document <paramref name="body"/> with the move <paramref name="moveId"/> taken out of it and its
    /// bag put back to what is true without that move — or <paramref name="body"/> itself when it carries no such move.
    /// Null when it cannot be read. For a newer document still waiting behind one whose move was just refused: sent as
    /// it is, the character server would be asked the same move again after the player had already been given their
    /// thing back (a store) or told it was gone (a take) — and a yes that second time would double the thing or lose it.</summary>
    public static string WithoutMove(string body, string moveId)
    {
        if (string.IsNullOrEmpty(moveId) || body == null || !body.Contains(moveId)) return body;
        ProgressEnvelope env;
        try { env = JsonUtility.FromJson<ProgressEnvelope>(body); }
        catch { env = null; }
        if (env == null || env.progress == null) return null;
        if (env.storage == null || env.storage.moveId != moveId) return body;
        Revert(env);
        return JsonUtility.ToJson(env);
    }

    // THE DOCUMENT IS PUT BACK TO WHAT IS TRUE WITHOUT ITS MOVE: a thing that was going into storage is in the bag
    // again, and a thing that was coming out of it — which this document's bag was given — is taken back out, because
    // storage still holds it. The move itself is emptied.
    static void Revert(ProgressEnvelope env)
    {
        var m = env.storage;
        if (m != null && !string.IsNullOrEmpty(m.moveId))
        {
            if (m.IsStore) AddToSavedBag(env.progress, m.itemId, m.count, m.refine, m.cards, m.roll, m.copyId);
            else RemoveFromSavedBag(env.progress, m.itemId, m.count, m.copyId);
        }
        env.storage = new StorageMoveWire();
    }

    /// <summary>A progress save for <paramref name="characterId"/> will never be written — the character has been handed
    /// to another server since (<paramref name="putBack"/> false: that server's copy is the truth now), or the character
    /// server refused the document for good (<paramref name="putBack"/> true). The move it carried is let go, so the next
    /// one is not held up for ever; a thing that was on its way INTO storage, and so is in neither place, goes back into
    /// the live bag when the player is still here. Returns the id of the move let go ("" when none), so a newer document
    /// still waiting can have it taken out (<see cref="WithoutMove"/>).</summary>
    public static string Abandoned(string characterId, string sentBody, bool putBack)
    {
        if (!pending.TryGetValue(characterId, out var p) || !Carries(sentBody, p.move)) return "";
        pending.Remove(characterId);
        var m = p.move;
        GameLog.Warn(LogCat.Server, "storage " + m.kind + " of " + m.count + " " + m.itemId + " for character " + characterId
                     + " was let go — the save that carried it will never be written");
        var shell = p.shell;
        var c = shell != null ? shell.ServerCombatUnit as Character : null;
        if (!putBack || c == null) return m.moveId;
        if (m.IsStore) { PutInLiveBag(c, p.def, m.count, p.piece); shell.PushBagToOwner(alsoTheBar: true); shell.SaveSoon(); }
        shell.KeeperSaid("That could not be moved just now.", false);
        shell.ForgetStorageCopy();
        return m.moveId;
    }

    /// <summary>Lay a thing this character already owns into their live bag: gear as its own entry, a stack into the
    /// first stack of the same item. NOT an arrival — nothing is announced.</summary>
    public static void PutInLiveBag(Character c, Item def, int count, EquipmentInstance piece)
    {
        if (c == null || def == null || count <= 0) return;
        if (piece != null) { c.Bag.Add(new BagEntry { def = def, count = 1, instance = piece }); return; }
        for (int i = 0; i < c.Bag.Count; i++)
        {
            var e = c.Bag[i];
            if (e != null && e.def == def && !e.IsEquip) { e.count += count; return; }
        }
        c.Bag.Add(new BagEntry { def = def, count = count });
    }

    // Did this document carry the move? Its id is a uuid, so finding it in the text cannot be a coincidence.
    static bool Carries(string body, StorageMoveWire m)
        => body != null && m != null && !string.IsNullOrEmpty(m.moveId) && body.Contains(m.moveId);

    [System.Serializable]
    class Reply { public string error; }

    static string ReasonOf(string reply)
    {
        try { var r = JsonUtility.FromJson<Reply>(reply); return r != null && !string.IsNullOrEmpty(r.error) ? r.error : "refused"; }
        catch { return "refused"; }
    }

    // What the player is told, from what the character server said.
    static string Sentence(string reason)
    {
        switch (reason)
        {
            case "storage is full":              return "Storage is full.";
            case "that is no longer in storage": return "That is no longer in storage.";
            case "that piece is already stored": return "That piece is already in storage.";
            default:                             return "That could not be moved: " + reason + ".";
        }
    }
}
