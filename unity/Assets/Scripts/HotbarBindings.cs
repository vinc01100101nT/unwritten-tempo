using System.Collections.Generic;

/// <summary>
/// The player's HOTBAR layout — which active skill (by stable id) sits in each of the nine slots (keys 1–9).
/// Class-agnostic: any known skill (see <see cref="SkillCatalog"/>) can be bound to any slot, by drag or
/// click, in the Skills (K) menu. A skill occupies at most ONE slot — binding it somewhere new vacates its
/// old slot. Pure data, owned by <see cref="Character"/>, persisted via <see cref="Capture"/>/<see cref="Restore"/>.
/// The central key router + the visual bar both read this (see <see cref="HotbarPanel"/>).
/// </summary>
public sealed class HotbarBindings
{
    public const int SlotCount = 9;   // keys 1–9

    // slot → skill id ("" = empty). Fixed-length; never reallocated after construction.
    readonly string[] slots = new string[SlotCount];

    // The ONE equipped Ultimate (its own dedicated slot, separate from keys 1–9). "" = no ult equipped.
    string ultSkillId = "";

    /// <summary>The skill id in the dedicated ULT slot ("" = none).</summary>
    public string Ult => ultSkillId ?? "";

    /// <summary>Equip an ultimate into the dedicated slot (replaces whatever was there). Returns true if it changed.</summary>
    public bool BindUlt(string id)
    {
        if (string.IsNullOrEmpty(id) || ultSkillId == id) return false;
        ultSkillId = id;
        return true;
    }

    /// <summary>Clear the ult slot. Returns true if it had something.</summary>
    public bool ClearUlt()
    {
        if (string.IsNullOrEmpty(ultSkillId)) return false;
        ultSkillId = "";
        return true;
    }

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

    /// <summary>Drop-binding from a drag: routes by the skill's TIER so an Ultimate always lands in the
    /// dedicated ULT slot and a base skill never can. <paramref name="slot"/> &lt; 0 means the ULT cell was the
    /// drop target. THE single place the tier→slot rule lives, so drag and click-to-bind agree. Returns true if
    /// anything changed.</summary>
    public bool BindFromDrag(int slot, string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var def = SkillCatalog.Get(id);
        bool isUlt = def != null && def.IsUltimate;
        if (isUlt) return BindUlt(id);          // ultimates always route to the dedicated slot, never keys 1–9
        if (slot < 0) return false;             // a base skill can't sit in the ULT cell
        return Bind(slot, id);
    }

    /// <summary>Auto-place a freshly learned skill: an Ultimate goes to the dedicated ULT slot, everything else
    /// to the first empty numbered slot. The single entry point every unlock/grant path calls, so an ultimate
    /// never lands on keys 1–9. Returns true if it bound anywhere (false = already bound / bar full).</summary>
    public bool AutoBind(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var def = SkillCatalog.Get(id);
        if (def != null && def.IsUltimate) return BindUlt(id);
        return BindToFirstEmpty(id) >= 0;
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

    /// <summary>Replace the ENTIRE bar — numbered keys 1–9 AND the dedicated ULT slot — with exactly this weapon kit
    /// (V3: the kit is rolled per weapon COPY). Clears every numbered slot AND the ult first, then fills from index 0;
    /// any Ultimate in the list routes to the ULT slot, passives are skipped (they hold no slot), everything else
    /// goes to numbered keys. Unknown/duplicate ids are skipped. Pass null/empty to clear the whole bar (a weapon was
    /// removed). Returns true if anything changed.</summary>
    public bool SetWeaponKit(IReadOnlyList<string> ids)
    {
        bool changed = false;
        for (int i = 0; i < slots.Length; i++)
            if (!string.IsNullOrEmpty(slots[i])) { slots[i] = ""; changed = true; }
        if (ClearUlt()) changed = true;   // the ult is part of the weapon's kit — it leaves with the weapon too

        if (ids != null)
        {
            int next = 0;
            for (int k = 0; k < ids.Count && next < slots.Length; k++)
            {
                var id = ids[k];
                var def = SkillCatalog.Get(id);
                if (def == null || SlotOf(id) >= 0 || id == ultSkillId) continue;   // unknown or already placed (dedupe)
                // A rolled kit can contain PASSIVES; they take no slot (they run on their trigger via PassiveEvents,
                // gated by PassiveLoadout). Binding one to a key would give the player a slot that can never fire.
                if (def.IsPassive) continue;
                if (def.IsUltimate) { if (BindUlt(id)) changed = true; continue; }
                slots[next++] = id; changed = true;
            }
        }
        return changed;
    }

    // ---- save / load ----------------------------------------------------------
    // The ult id rides as one extra trailing element (index SlotCount), so the save shape stays a flat string[]
    // and older length-9 saves simply load with no ult.
    public string[] Capture()
    {
        var arr = new string[slots.Length + 1];
        System.Array.Copy(slots, arr, slots.Length);
        arr[slots.Length] = ultSkillId ?? "";
        return arr;
    }

    public void Restore(string[] saved)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var id = (saved != null && i < saved.Length && saved[i] != null) ? saved[i] : "";
            // Drop ids that are no longer a known skill (e.g. retired legacy skills from an old save) so a
            // stale binding can't linger as a dead slot — the pool is the composed SkillCatalog now.
            slots[i] = SkillCatalog.IsKnown(id) ? id : "";
        }
        string ult = (saved != null && saved.Length > slots.Length && saved[slots.Length] != null) ? saved[slots.Length] : "";
        ultSkillId = SkillCatalog.IsKnown(ult) ? ult : "";
    }
}
