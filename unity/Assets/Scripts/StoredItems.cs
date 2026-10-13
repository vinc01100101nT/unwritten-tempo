using System;
using System.Collections.Generic;

/// <summary>One stored thing as it crosses the wire and as the character server reads it back: the item, how many, and
/// — for a piece of gear — the piece's own four values (<see cref="EquipmentInstance.ToSaved"/>), so the very piece that
/// went in is the one that comes out. A count of 0 on a single changed row means that row is gone.</summary>
[Serializable]
public struct StoredLine
{
    public string itemId;
    public int count;
    public int refine;
    public string cards;
    public string roll;
    public string copyId;
}

/// <summary>One stored thing, resolved: its item and, for gear, the piece itself.</summary>
public class StoredEntry
{
    public Item def;
    public int count;
    /// <summary>The piece itself, for gear; null for a stack.</summary>
    public EquipmentInstance instance;

    public string CopyId => instance != null ? instance.copyId ?? "" : "";
}

/// <summary>
/// AN ACCOUNT'S STORAGE AS ONE MACHINE HOLDS IT — on the server, the copy it last read from the character server and kept
/// up to date with each move that landed; on a player's machine, exactly what the server last sent, drawn and nothing
/// else. The character server's table is the truth; this is a copy of it.
/// </summary>
public class StorageList
{
    readonly List<StoredEntry> entries = new List<StoredEntry>();
    // Stored things whose item is no longer in the game: not drawn, but they still take up room.
    int unknown;

    /// <summary>Oldest first, as they were put in.</summary>
    public IReadOnlyList<StoredEntry> Entries => entries;

    /// <summary>True once a list has arrived at all.</summary>
    public bool Known { get; private set; }

    /// <summary>How many things are stored — what the limit counts.</summary>
    public int Rows => entries.Count + unknown;

    /// <summary>Forget everything — a new character signed in, or a read failed.</summary>
    public void Clear() { entries.Clear(); unknown = 0; Known = false; }

    /// <summary>Lay <paramref name="lines"/> on, starting at row <paramref name="startsAt"/>: the first chunk of a list
    /// (startsAt 0) replaces what was held.</summary>
    public void Take(StoredLine[] lines, int startsAt)
    {
        if (startsAt <= 0) { entries.Clear(); unknown = 0; }
        Known = true;
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
        {
            var e = Resolve(lines[i]);
            if (e != null) entries.Add(e); else unknown++;
        }
    }

    /// <summary>One row changed: replaced where it is, added at the end when new, gone when its count is 0.</summary>
    public void TakeRow(StoredLine line)
    {
        int at = IndexOf(line.itemId, line.copyId);
        if (line.count <= 0) { if (at >= 0) entries.RemoveAt(at); return; }
        var e = Resolve(line);
        if (e == null) return;
        if (at >= 0) entries[at] = e; else entries.Add(e);
    }

    /// <summary>The stored thing named by its item id and, for gear, the piece's own id — never by a position, which
    /// would point at the wrong row the moment another changed. Null when there is none.</summary>
    public StoredEntry Find(string itemId, string copyId)
    {
        int at = IndexOf(itemId, copyId);
        return at >= 0 ? entries[at] : null;
    }

    /// <summary>Would storing <paramref name="def"/> (the piece <paramref name="copyId"/> for gear) start a new row,
    /// rather than join a stack already stored?</summary>
    public bool NeedsNewRow(Item def, string copyId)
        => !string.IsNullOrEmpty(copyId) || def == null || Find(def.id, "") == null;

    /// <summary>SERVER: the line for <paramref name="e"/> as it now stands.</summary>
    public static StoredLine LineOf(StoredEntry e)
    {
        var l = new StoredLine { itemId = e.def.id, count = e.count, cards = "", roll = "", copyId = "" };
        if (e.instance != null) e.instance.ToSaved(out l.refine, out l.cards, out l.roll, out l.copyId);
        return l;
    }

    int IndexOf(string itemId, string copyId)
    {
        copyId ??= "";
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.def != null && e.def.id == itemId && e.CopyId == copyId) return i;
        }
        return -1;
    }

    static StoredEntry Resolve(StoredLine l)
    {
        var def = ItemCatalog.Resolve(l.itemId);
        if (def == null) return null;
        bool gear = def.slot != EquipSlotType.None;
        var piece = gear ? EquipmentInstance.FromSaved(def, l.refine, l.cards, l.roll ?? "", l.copyId) : null;
        return new StoredEntry { def = def, count = gear ? 1 : Math.Max(1, l.count), instance = piece };
    }
}

/// <summary>One move into or out of storage, as it rides a progress save (the character server's <c>Storage.Check</c>
/// reads these names). An empty <see cref="moveId"/> is no move at all: JSON from this game always carries the object.</summary>
[Serializable]
public class StorageMoveWire
{
    public string moveId = "";
    /// <summary>"store" or "take".</summary>
    public string kind = "";
    public string itemId = "";
    public int count;
    public int refine;
    public string cards = "";
    public string roll = "";
    public string copyId = "";
    /// <summary>How many things storage may hold (<see cref="KeeperMath.StorageLimit"/>).</summary>
    public int limit;

    public bool IsStore => kind == "store";
}

/// <summary>What one progress save sends the character server: the character, the quest rows that changed since the
/// last save landed, which hand-over it belongs to, and the storage move it carries (empty when none).</summary>
[Serializable]
public class ProgressEnvelope
{
    public SaveData progress;
    public QuestRowWire[] quests;
    public int handover;
    public StorageMoveWire storage;
}

/// <summary>One town a Keeper can send you to, as its owner is sent it: the map, what players call it, and the finished
/// price.</summary>
public struct KeeperTrip
{
    public string map;
    public string name;
    public long price;
}
