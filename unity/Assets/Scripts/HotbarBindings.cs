/// <summary>
/// The player's HOTBAR layout — which active skill (by stable id) sits in each of the nine slots (keys 1–9).
/// Class-agnostic: any UNLOCKED skill (see <see cref="SkillRoster"/>) can be bound to any slot, by drag or
/// click, in the Skills (K) menu. A skill occupies at most ONE slot — binding it somewhere new vacates its
/// old slot. Pure data, owned by <see cref="Character"/>, persisted via <see cref="Capture"/>/<see cref="Restore"/>.
/// The central key router + the visual bar both read this (see <see cref="HotbarPanel"/>).
/// </summary>
public sealed class HotbarBindings
{
    public const int SlotCount = 9;   // keys 1–9

    // slot → skill id ("" = empty). Fixed-length; never reallocated after construction.
    readonly string[] slots = new string[SlotCount];

    public HotbarBindings()
    {
        for (int i = 0; i < slots.Length; i++) slots[i] = "";
    }

    public static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

    /// <summary>The skill id bound to <paramref name="slot"/>, or "" if empty / out of range.</summary>
    public string Get(int slot) => IsValidSlot(slot) ? slots[slot] : "";

    public bool IsEmpty(int slot) => string.IsNullOrEmpty(Get(slot));

    /// <summary>The slot a skill currently occupies, or −1 if unbound.</summary>
    public int SlotOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < slots.Length; i++) if (slots[i] == id) return i;
        return -1;
    }

    public bool IsBound(string id) => SlotOf(id) >= 0;

    public int FirstEmptySlot()
    {
        for (int i = 0; i < slots.Length; i++) if (string.IsNullOrEmpty(slots[i])) return i;
        return -1;
    }

    /// <summary>Bind <paramref name="id"/> to <paramref name="slot"/>. The skill is removed from any slot it
    /// already occupied (one slot per skill), and whatever was in this slot is replaced (becomes unbound).
    /// Returns true if anything changed.</summary>
    public bool Bind(int slot, string id)
    {
        if (!IsValidSlot(slot) || string.IsNullOrEmpty(id)) return false;
        int old = SlotOf(id);
        if (old == slot) return false;                 // already here — no-op
        if (old >= 0) slots[old] = "";                 // vacate its previous slot (move, not duplicate)
        slots[slot] = id;
        return true;
    }

    /// <summary>Bind to the first empty slot (the click-to-bind / auto-bind-on-unlock path). No-op if the
    /// skill is already bound or the bar is full. Returns the slot used, or −1.</summary>
    public int BindToFirstEmpty(string id)
    {
        if (string.IsNullOrEmpty(id) || IsBound(id)) return -1;
        int slot = FirstEmptySlot();
        if (slot < 0) return -1;
        slots[slot] = id;
        return slot;
    }

    /// <summary>Swap the contents of two slots (drag-to-rearrange on the bar). Moving a skill onto an empty
    /// slot just relocates it (the empty "" rides back). No-op for an invalid or identical pair.</summary>
    public bool Swap(int a, int b)
    {
        if (!IsValidSlot(a) || !IsValidSlot(b) || a == b) return false;
        var t = slots[a]; slots[a] = slots[b]; slots[b] = t;
        return true;
    }

    /// <summary>Clear a slot (unbind whatever is there). Returns true if it had something.</summary>
    public bool Clear(int slot)
    {
        if (!IsValidSlot(slot) || string.IsNullOrEmpty(slots[slot])) return false;
        slots[slot] = "";
        return true;
    }

    /// <summary>Drop a skill from the bar entirely (by id), wherever it sits.</summary>
    public bool Unbind(string id)
    {
        int slot = SlotOf(id);
        if (slot < 0) return false;
        slots[slot] = "";
        return true;
    }

    // ---- save / load ----------------------------------------------------------
    public string[] Capture()
    {
        var arr = new string[slots.Length];
        System.Array.Copy(slots, arr, slots.Length);
        return arr;
    }

    public void Restore(string[] saved)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var id = (saved != null && i < saved.Length && saved[i] != null) ? saved[i] : "";
            // Drop ids that are no longer a known skill (e.g. retired legacy skills from an old save) so a
            // stale binding can't linger as a dead slot — the pool is the composed SkillCatalog now.
            slots[i] = SkillRoster.IsKnownId(id) ? id : "";
        }
    }
}
