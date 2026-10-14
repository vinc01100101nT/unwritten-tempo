using System.Collections.Generic;

/// <summary>One thing a character sold and can still buy back. A piece of gear is held AS ITSELF — the very copy that
/// left the bag, folds, grade, written stats, cards, trademark and word — so buying it back returns that piece and
/// not another of its kind. A stack is held as its item and how many.</summary>
public class SoldEntry
{
    /// <summary>This sale's own number in its character's list — what a buy-back names it by, because two sales of the
    /// same thing are two entries and an item id cannot tell them apart.</summary>
    public int id;
    public Item def;
    public int count;
    /// <summary>The piece itself, for gear; null for a stack.</summary>
    public EquipmentInstance instance;
    /// <summary>SERVER: the gold the shop paid for everything in this entry — what its buyback price is worked out
    /// from. Never sent: a player's machine is sent the finished price instead.</summary>
    public long paid;
    /// <summary>PLAYER'S MACHINE: the price to buy it back, exactly as the server sent it. Unused on the server.</summary>
    public long price;
    /// <summary>The moment it stops being kept, in world seconds (<see cref="WorldClock"/>).</summary>
    public double endsAt;

    public string CopyId => instance != null ? instance.copyId ?? "" : "";
}

/// <summary>
/// WHAT A CHARACTER HAS SOLD AND CAN STILL BUY BACK — at most <see cref="ShopMath.BuybackMostEntries"/> entries,
/// each for <see cref="ShopMath.BuybackSeconds"/>, oldest first. One list per character, shown at every shop.
///
/// <para><b>Nothing counts down.</b> Each entry holds the moment it ends; an ended one is dropped whenever the list is
/// next used — a sale, a buyback, a save, a load — so no timer runs for anybody.</para>
///
/// <para><b>The server owns it.</b> It is saved in the character's document beside the bag and the purse, so all
/// three are written by one statement and a sale can never be stored half-done. A player's machine holds the copy
/// the server last sent, and only draws it.</para>
/// </summary>
public class BuybackList
{
    readonly List<SoldEntry> entries = new List<SoldEntry>(ShopMath.BuybackMostEntries + 1);
    // The number the next sale is given. Only ever goes up, so a number is never used twice in one list.
    int nextId = 1;

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<SoldEntry> Entries => entries;

    /// <summary>Drop every entry whose moment has come. At most <see cref="ShopMath.BuybackMostEntries"/> to walk.</summary>
    public void DropEnded(double nowEpoch)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].endsAt <= nowEpoch) entries.RemoveAt(i);
    }

    /// <summary>SERVER: keep what was just sold, as an entry of its own — EVERY sale is one, never added onto an earlier
    /// sale of the same thing, so buying one back returns exactly that sale, at what that sale paid, on its own hour.
    /// When that makes one too many, the oldest is let go and handed back in <paramref name="letGo"/> so the caller can
    /// say so.</summary>
    public void Keep(Item def, int count, EquipmentInstance piece, long paid, double nowEpoch, out SoldEntry letGo)
    {
        letGo = null;
        DropEnded(nowEpoch);
        double endsAt = ShopMath.BuybackEndsAt(nowEpoch);

        entries.Add(new SoldEntry { id = nextId++, def = def, count = piece != null ? 1 : count, instance = piece, paid = paid, endsAt = endsAt });
        if (entries.Count > ShopMath.BuybackMostEntries)
        {
            letGo = entries[0];
            entries.RemoveAt(0);
        }
    }

    /// <summary>The entry with this sale's own number — what a buy-back sends, because a position would point at the
    /// wrong entry the moment one ended. Null when there is none.</summary>
    public SoldEntry Find(int saleId)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].id == saleId) return entries[i];
        return null;
    }

    public void Remove(SoldEntry e) => entries.Remove(e);

    // ---- the save ----------------------------------------------------------------------------------------

    /// <summary>SERVER: the entries still kept, as the save holds them. Null when there are none.</summary>
    public SaveData.SoldItem[] Capture(double nowEpoch)
    {
        DropEnded(nowEpoch);
        if (entries.Count == 0) return null;
        var saved = new SaveData.SoldItem[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var s = new SaveData.SoldItem { id = e.id, itemId = e.def.id, count = e.count, paid = e.paid, endsAt = e.endsAt,
                                            cards = "", roll = "", copyId = "" };
            if (e.instance != null) e.instance.ToSaved(out s.refine, out s.cards, out s.roll, out s.copyId);
            saved[i] = s;
        }
        return saved;
    }

    /// <summary>SERVER: lay a save's entries back on. An entry whose item is no longer in the game, or whose moment
    /// has come while the character was away, is skipped.</summary>
    public void Restore(SaveData.SoldItem[] saved, double nowEpoch)
    {
        entries.Clear();
        nextId = 1;
        if (saved == null) return;
        // A save from before sales had numbers carries 0: each such entry is numbered here, after the highest number the
        // save does carry, so no two entries ever share one.
        for (int i = 0; i < saved.Length; i++)
            if (saved[i] != null && saved[i].id >= nextId) nextId = saved[i].id + 1;
        for (int i = 0; i < saved.Length; i++)
        {
            var s = saved[i];
            if (s == null || s.endsAt <= nowEpoch) continue;
            var def = ItemCatalog.Resolve(s.itemId);
            if (def == null) continue;
            bool gear = def.slot != EquipSlotType.None;
            entries.Add(new SoldEntry
            {
                id = s.id > 0 && Find(s.id) == null ? s.id : nextId++,
                def = def,
                count = gear ? 1 : System.Math.Max(1, s.count),
                instance = gear ? EquipmentInstance.FromSaved(def, s.refine, s.cards, s.roll ?? "", s.copyId) : null,
                paid = s.paid,
                endsAt = s.endsAt,
            });
        }
    }

    // ---- the wire ----------------------------------------------------------------------------------------

    /// <summary>SERVER: the entries still kept, as their owner is sent them — each with its finished buyback price.</summary>
    public SoldLine[] ToWire(double nowEpoch)
    {
        DropEnded(nowEpoch);
        var lines = new SoldLine[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var line = new SoldLine { id = e.id, itemId = e.def.id, count = e.count, price = ShopMath.BuybackPrice(e.paid, e.count),
                                      endsAt = e.endsAt, cards = "", roll = "", copyId = "" };
            if (e.instance != null) e.instance.ToSaved(out line.refine, out line.cards, out line.roll, out line.copyId);
            lines[i] = line;
        }
        return lines;
    }

    /// <summary>PLAYER'S MACHINE: replace the list with what the server sent. Nothing is worked out here.</summary>
    public void TakeFromServer(SoldLine[] lines)
    {
        entries.Clear();
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            var def = ItemCatalog.Resolve(l.itemId);
            if (def == null) continue;
            bool gear = def.slot != EquipSlotType.None;
            entries.Add(new SoldEntry
            {
                id = l.id,
                def = def,
                count = l.count,
                instance = gear ? EquipmentInstance.FromSaved(def, l.refine, l.cards, l.roll ?? "", l.copyId) : null,
                price = l.price,
                endsAt = l.endsAt,
            });
        }
    }
}

/// <summary>One kept sale as its owner is sent it: the thing (with a piece of gear's own four values, so its row can
/// name it exactly), the finished price to buy it back, and the moment it stops being kept.</summary>
public struct SoldLine
{
    public int id;
    public string itemId;
    public int count;
    public int refine;
    public string cards;
    public string roll;
    public string copyId;
    public long price;
    public double endsAt;
}
