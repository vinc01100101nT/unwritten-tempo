using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The SINGLE OWNER of the player's combo chains — the data sibling of <see cref="ComboRunner"/> (which executes)
/// and the source the Combo Composer edits. Owned by <see cref="Character"/> as <c>Character.Combos</c> and persisted
/// alongside the hotbar (see <see cref="Capture"/>/<see cref="Restore"/>), exactly like <see cref="HotbarBindings"/>
/// — a plain data class, not a component, so there's ONE authoritative copy of "which skill is in which chain".
///
/// <para>Enforces the design's core constraint: a skill belongs to <b>at most one</b> chain, globally (a committed
/// skill leaves solo use — <see cref="SkillRunner.Activate"/> reads <see cref="IsCommitted"/> here). Nothing else
/// stores commitment, so it can't drift.</para>
///
/// <para>The position-ramp multipliers and inter-step gap are GLOBAL tuning (not per-player save data), so they live
/// as <c>static</c> knobs here — one owner, read by <see cref="ComboRunner"/>, never hardcoded at the roll site.</para>
/// </summary>
public class ComboBook
{
    /// <summary>Max chains a player can field (matches the size of the combo-safe hotkey set in the composer).</summary>
    public const int MaxChains = 6;

    readonly List<ComboChain> chains = new List<ComboChain>();

    public IReadOnlyList<ComboChain> Chains => chains;
    public int ChainCount => chains.Count;

    // ---- global tuning (static — the SINGLE knob, tunable by a dev tool/console) ---------------------------

    /// <summary>Position damage ramp by slot (0..3). Default 1× / 1.5× / 2.5× / 4× — the finisher hits hardest.</summary>
    public static float[] SlotMultipliers = { 1f, 1.5f, 2.5f, 4f };

    /// <summary>Seconds between combo steps — the CC-vulnerability window AND what makes an all-instant chain readable.</summary>
    public static float StepGapSeconds = 0.15f;

    public static float SlotMultiplier(int i) => (SlotMultipliers != null && i >= 0 && i < SlotMultipliers.Length)
        ? Mathf.Max(0f, SlotMultipliers[i]) : 1f;
    public static float StepGap => Mathf.Max(0f, StepGapSeconds);

    // ---- commitment / uniqueness (the SSOT the whole feature reads) ----------------------------------------

    /// <summary>True if <paramref name="id"/> is committed to ANY chain (so it can't be cast solo / re-committed).</summary>
    public bool IsCommitted(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int c = 0; c < chains.Count; c++) if (chains[c] != null && chains[c].Contains(id)) return true;
        return false;
    }

    /// <summary>The chain index holding <paramref name="id"/>, or −1 — for the composer's "already used" feedback.</summary>
    public int ChainHolding(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int c = 0; c < chains.Count; c++) if (chains[c] != null && chains[c].Contains(id)) return c;
        return -1;
    }

    /// <summary>May <paramref name="id"/> be placed into <paramref name="into"/>? False only if it already lives in a
    /// DIFFERENT chain (re-placing within the same chain is allowed).</summary>
    public bool CanAssign(string id, ComboChain into)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int c = 0; c < chains.Count; c++)
        {
            var ch = chains[c];
            if (ch == null || ch == into) continue;
            if (ch.Contains(id)) return false;
        }
        return true;
    }

    // ---- composer mutations --------------------------------------------------------------------------------

    /// <summary>Create a new empty chain (capped at <see cref="MaxChains"/>); returns it, or null if full.</summary>
    public ComboChain AddChain()
    {
        if (chains.Count >= MaxChains) return null;
        var c = new ComboChain { skillIds = new string[ComboChain.Slots] };
        chains.Add(c);
        return c;
    }

    public void RemoveChain(ComboChain c) { if (c != null) chains.Remove(c); }

    /// <summary>Put <paramref name="id"/> into <paramref name="chain"/>'s <paramref name="slot"/>, enforcing global
    /// uniqueness (and once-per-chain). Returns false if the skill is committed elsewhere.</summary>
    public bool AssignSlot(ComboChain chain, int slot, string id)
    {
        if (chain == null || slot < 0 || slot >= ComboChain.Slots || string.IsNullOrEmpty(id)) return false;
        if (!CanAssign(id, chain)) return false;
        for (int s = 0; s < ComboChain.Slots; s++) if (chain.skillIds[s] == id) chain.skillIds[s] = "";   // once per chain
        chain.skillIds[slot] = id;
        return true;
    }

    public void ClearSlot(ComboChain chain, int slot)
    {
        if (chain != null && slot >= 0 && slot < ComboChain.Slots) chain.skillIds[slot] = "";
    }

    public void SetHotkey(ComboChain chain, KeyCode key) { if (chain != null) chain.hotkey = key; }

    // ---- save / load ---------------------------------------------------------------------------------------

    /// <summary>Encode as one string per chain: "&lt;hotkeyKeyCodeInt&gt;|id0,id1,id2,id3" (empty slot = "").</summary>
    public string[] Capture()
    {
        var arr = new string[chains.Count];
        for (int c = 0; c < chains.Count; c++)
        {
            var ch = chains[c];
            var ids = new string[ComboChain.Slots];
            for (int s = 0; s < ComboChain.Slots; s++) ids[s] = ch.Id(s);
            arr[c] = ((int)ch.hotkey) + "|" + string.Join(",", ids);
        }
        return arr;
    }

    public void Restore(string[] saved)
    {
        chains.Clear();
        if (saved == null) return;
        for (int i = 0; i < saved.Length && chains.Count < MaxChains; i++)
        {
            var line = saved[i];
            if (string.IsNullOrEmpty(line)) continue;

            KeyCode key = KeyCode.None;
            string idsPart = line;
            int bar = line.IndexOf('|');
            if (bar >= 0)
            {
                if (int.TryParse(line.Substring(0, bar), out int k)) key = (KeyCode)k;
                idsPart = line.Substring(bar + 1);
            }

            var parts = idsPart.Split(',');
            var ch = new ComboChain { hotkey = key, skillIds = new string[ComboChain.Slots] };
            for (int s = 0; s < ComboChain.Slots; s++)
            {
                string id = s < parts.Length ? parts[s] : "";
                ch.skillIds[s] = SkillCatalog.IsKnown(id) ? id : "";   // drop retired skills, like Hotbar.Restore
            }
            chains.Add(ch);
        }
    }
}
