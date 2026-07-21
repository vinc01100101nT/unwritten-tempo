using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's ACTIVE timed self-buffs, for the on-screen buff bar (<see cref="BuffBarPanel"/>). A presentation
/// mirror: each entry is written at the same instant the mechanical buff is applied (in <see cref="SkillEffectApplier"/>),
/// carrying the SOURCE SKILL's icon + name + its window — so the bar shows the same icon the hotbar uses, counting down.
/// Owned by <see cref="Character"/> (<c>Character.Buffs</c>); runtime-only, cleared on death.
///
/// <para>Keyed by <see cref="Buff.name"/> so re-casting a buff REFRESHES its cell (never stacks a duplicate). Entries
/// are allocated only on a NEW buff (rare, on cast — not a hot path); the per-frame read in the bar is zero-alloc.</para>
/// </summary>
public class PlayerBuffs
{
    public class Buff
    {
        public Sprite icon;
        public string name;
        public string description;   // short "what it does" line for the hover card (from the source effect)
        public float startTime;
        public float endTime;
        public Color tint;

        public float Remaining => Mathf.Max(0f, endTime - Time.time);
        public float Fraction01 => endTime > startTime ? Mathf.Clamp01((endTime - Time.time) / (endTime - startTime)) : 0f;
    }

    readonly List<Buff> active = new List<Buff>();

    /// <summary>The live buff entries (already-expired ones are swept by <see cref="Prune"/>). Read-only for the bar.</summary>
    public IReadOnlyList<Buff> Active => active;

    /// <summary>Fired when the SET of buffs changes (a new buff, or a prune) — the bar rebuilds its cells on this.</summary>
    public event Action OnChanged;

    /// <summary>Apply/refresh a display buff: <paramref name="seconds"/> from now, under <paramref name="name"/>
    /// (re-casting the same skill just extends its cell). No-op for a non-positive window or a null name.</summary>
    public void Add(Sprite icon, string name, float seconds, Color tint, string description = null)
    {
        if (seconds <= 0f || string.IsNullOrEmpty(name)) return;
        float now = Time.time;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].name != name) continue;
            active[i].icon = icon;
            active[i].description = description;
            active[i].startTime = now;
            active[i].endTime = now + seconds;
            active[i].tint = tint;
            OnChanged?.Invoke();
            return;
        }
        active.Add(new Buff { icon = icon, name = name, description = description, startTime = now, endTime = now + seconds, tint = tint });
        OnChanged?.Invoke();
    }

    /// <summary>Drop any expired entries; returns true if the set changed (the bar rebuilds when it does). Cheap
    /// fixed loop over the small list — safe to call every frame.</summary>
    public bool Prune()
    {
        bool changed = false;
        for (int i = active.Count - 1; i >= 0; i--)
            if (Time.time >= active[i].endTime) { active.RemoveAt(i); changed = true; }
        if (changed) OnChanged?.Invoke();
        return changed;
    }

    /// <summary>Wipe all buffs (on death/revive).</summary>
    public void Clear()
    {
        if (active.Count == 0) return;
        active.Clear();
        OnChanged?.Invoke();
    }
}
