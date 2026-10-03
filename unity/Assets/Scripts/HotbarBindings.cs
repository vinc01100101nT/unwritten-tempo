using System.Collections.Generic;

/// <summary>
/// What one hotbar slot holds, as the one string it is saved and sent as: "" for nothing, a skill's own id, an item
/// as "item:&lt;item id&gt;", or one exact piece of gear as "gear:&lt;copy id&gt;". The kinds are told apart by these
/// prefixes and nothing else, so every reader asks here rather than guessing from the text.
/// </summary>
public static class HotbarEntry
{
    public const string ItemPrefix = "item:";
    public const string GearPrefix = "gear:";

    public static string Item(string itemId) => string.IsNullOrEmpty(itemId) ? "" : ItemPrefix + itemId;
    public static string Gear(string copyId) => string.IsNullOrEmpty(copyId) ? "" : GearPrefix + copyId;

    public static bool IsItem(string entry, out string itemId) => Strip(entry, ItemPrefix, out itemId);
    public static bool IsGear(string entry, out string copyId) => Strip(entry, GearPrefix, out copyId);

    /// <summary>The skill id this entry holds, or "" when it holds an item, gear or nothing.</summary>
    public static string SkillOf(string entry)
        => string.IsNullOrEmpty(entry) || entry.StartsWith(ItemPrefix, System.StringComparison.Ordinal)
                                      || entry.StartsWith(GearPrefix, System.StringComparison.Ordinal) ? "" : entry;

    static bool Strip(string entry, string prefix, out string rest)
    {
        rest = "";
        if (string.IsNullOrEmpty(entry) || !entry.StartsWith(prefix, System.StringComparison.Ordinal)) return false;
        rest = entry.Substring(prefix.Length);
        return rest.Length > 0;
    }
}

/// <summary>
/// The player's HOTBAR — three rows of nine slots, any of which holds a skill, a usable item or one exact piece of
/// gear (<see cref="HotbarEntry"/>). Row 1 answers F1–F9, row 2 the number keys 1–9, and row 3 whatever keys the
/// player assigns in Settings (<see cref="GameKeys"/>). One bar per character, whatever weapon is in hand: a skill of
/// another weapon simply cannot be cast until that weapon is held.
///
/// <para>One slot per thing: putting a skill, item or piece somewhere new vacates its old slot. Ultimates sit in any
/// slot like everything else — the rule that keeps them apart is their shared cooldown, not a seat of their own.
/// Pure data, owned by <see cref="Character"/>; the server decides what may stay on it
/// (<see cref="Character.KeepOnlyBindable"/>) and keeps it in a row of its own in the character store, written only
/// when it changes.</para>
/// </summary>
public sealed class HotbarBindings
{
    public const int Rows = 3, PerRow = 9;
    public const int SlotCount = Rows * PerRow;

    /// <summary>The row (0 = the F-key row, 1 = the number row, 2 = the player's own keys) slot
    /// <paramref name="slot"/> sits in.</summary>
    public static int RowOf(int slot) => slot / PerRow;

    /// <summary>Where slot <paramref name="slot"/> sits in its row, 0..8.</summary>
    public static int ColumnOf(int slot) => slot % PerRow;

    readonly string[] slots = new string[SlotCount];

    /// <summary>Changes on every change, so the server can tell a bar that needs writing down from one that does not.</summary>
    public int Version { get; private set; }

    public HotbarBindings()
    {
        for (int i = 0; i < slots.Length; i++) slots[i] = "";
    }

    public static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

    /// <summary>What slot <paramref name="slot"/> holds, "" when empty or out of range.</summary>
    public string Get(int slot) => IsValidSlot(slot) ? slots[slot] : "";

    /// <summary>The skill in slot <paramref name="slot"/>, "" when it holds no skill.</summary>
    public string SkillAt(int slot) => HotbarEntry.SkillOf(Get(slot));

    public bool IsEmpty(int slot) => string.IsNullOrEmpty(Get(slot));

    /// <summary>The slot an entry currently occupies, or −1.</summary>
    public int SlotOf(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return -1;
        for (int i = 0; i < slots.Length; i++) if (slots[i] == entry) return i;
        return -1;
    }

    /// <summary>Is this skill anywhere on the bar.</summary>
    public bool IsBound(string skillId) => !string.IsNullOrEmpty(HotbarEntry.SkillOf(skillId)) && SlotOf(skillId) >= 0;

    public int FirstEmptySlot()
    {
        for (int i = 0; i < slots.Length; i++) if (string.IsNullOrEmpty(slots[i])) return i;
        return -1;
    }

    /// <summary>Put <paramref name="entry"/> in <paramref name="slot"/>, moving it out of any slot it was in and
    /// replacing whatever was there. Returns true if anything changed.</summary>
    public bool Bind(int slot, string entry)
    {
        if (!IsValidSlot(slot) || string.IsNullOrEmpty(entry)) return false;
        int old = SlotOf(entry);
        if (old == slot) return false;
        if (old >= 0) slots[old] = "";
        slots[slot] = entry;
        Version++;
        return true;
    }

    /// <summary>Put <paramref name="entry"/> in the first empty slot. Returns the slot, or −1 when it is already on
    /// the bar or the bar is full.</summary>
    public int BindToFirstEmpty(string entry)
    {
        if (string.IsNullOrEmpty(entry) || SlotOf(entry) >= 0) return -1;
        int slot = FirstEmptySlot();
        if (slot < 0) return -1;
        slots[slot] = entry;
        Version++;
        return slot;
    }

    /// <summary>Swap two slots (drag to rearrange); onto an empty slot is a move.</summary>
    public bool Swap(int a, int b)
    {
        if (!IsValidSlot(a) || !IsValidSlot(b) || a == b) return false;
        if (slots[a] == slots[b]) return false;
        var t = slots[a]; slots[a] = slots[b]; slots[b] = t;
        Version++;
        return true;
    }

    /// <summary>Empty a slot. Returns true if it held something.</summary>
    public bool Clear(int slot)
    {
        if (!IsValidSlot(slot) || string.IsNullOrEmpty(slots[slot])) return false;
        slots[slot] = "";
        Version++;
        return true;
    }

    // ---- save / load ----------------------------------------------------------

    /// <summary>All 27 slots, in order.</summary>
    public string[] Capture()
    {
        var arr = new string[slots.Length];
        System.Array.Copy(slots, arr, slots.Length);
        return arr;
    }

    /// <summary>Replace every slot. A skill the game no longer has is dropped; whether an item or a piece is still
    /// owned is the server's question (<see cref="Character.KeepOnlyBindable"/>), asked before this.</summary>
    public void Restore(string[] saved)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var entry = saved != null && i < saved.Length && saved[i] != null ? saved[i] : "";
            string skill = HotbarEntry.SkillOf(entry);
            slots[i] = skill.Length > 0 && !SkillCatalog.IsKnown(skill) ? "" : entry;
        }
        Version++;
    }

    /// <summary>Lay down a bar saved before the bar had three rows (before save v18), read exactly the way the old bar
    /// read it: its first four numbered skills and, as the LAST entry, the ultimate in its own seat — whatever length the
    /// array was saved at. The four keep their keys (the number row's 1–4) and the ultimate goes beside them, on 5.</summary>
    public void RestoreLegacy(string[] old)
    {
        const int OldNumbered = 4;
        for (int i = 0; i < slots.Length; i++) slots[i] = "";
        if (old != null && old.Length > 0)
        {
            int numbered = System.Math.Min(OldNumbered, old.Length - 1);
            for (int i = 0; i < numbered; i++)
                if (SkillCatalog.IsKnown(old[i]) && SlotOf(old[i]) < 0) slots[PerRow + i] = old[i];
            string ult = old[old.Length - 1];
            if (SkillCatalog.IsKnown(ult) && SlotOf(ult) < 0 && string.IsNullOrEmpty(slots[PerRow + 4]))
                slots[PerRow + 4] = ult;
        }
        Version++;
    }
}
