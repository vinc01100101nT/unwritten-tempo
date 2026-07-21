using System.Collections.Generic;

/// <summary>
/// The WEAPON-bound half of the mutation model (design doc §Sourcing = hybrid): mutations baked onto a specific
/// weapon that reshape ITS skills and vanish the moment you unequip it — the "loot surprise," distinct from the
/// ring's build-wide voices. A weapon lists affix ids (<see cref="Item.weaponAffixIds"/>); this maps each id to a
/// reshaper and folds the EQUIPPED weapon's affixes into a <see cref="CastMods"/>.
///
/// <para>By design weapon affixes own the effects ring VOICES don't — CONTROL (slow/root/stun/push/pull) and raw
/// damage here; targeting (penetrate/bounce/homing), persistence (lingering/trail) and combo need delivery/zone
/// systems and land later (see the design doc's "Weapon affix library"). NEVER an execute/repeat/expand affix —
/// those are ring voices.</para>
/// </summary>
public static class WeaponAffixes
{
    enum Kind { Slow, Root, Stun, Push, Pull, RawDamage, ManaLeech, Refund, Reflect }

    struct Affix
    {
        public string DisplayName;
        public Kind kind;
        public float power;   // Slow: speed factor · Push/Pull: knockback speed · RawDamage: +damage fraction
        public float secs;    // duration (Slow/Root/Stun/Push/Pull); unused for RawDamage
        public Affix(string name, Kind k, float p, float s) { DisplayName = name; kind = k; power = p; secs = s; }
    }

    // The authored affix pool — id → what it does. Weapons reference these by id (like grantedSkillIds).
    static readonly Dictionary<string, Affix> Catalog = new Dictionary<string, Affix>
    {
        { "affix.frostbite",  new Affix("Frostbite",  Kind.Slow,      0.5f, 2.5f) },
        { "affix.entangling", new Affix("Entangling", Kind.Root,      0f,   1.5f) },
        { "affix.concussive", new Affix("Concussive", Kind.Stun,      0f,   1.0f) },
        { "affix.repelling",  new Affix("Repelling",  Kind.Push,      12f,  0.25f) },
        { "affix.gravity",    new Affix("Gravity Well", Kind.Pull,    10f,  0.25f) },
        { "affix.honed",      new Affix("Honed",      Kind.RawDamage, 0.4f, 0f) },
        { "affix.siphon",     new Affix("Siphon",     Kind.ManaLeech, 0.15f, 0f) },
        { "affix.thrifty",    new Affix("Thrifty",    Kind.Refund,    0.25f, 0f) },
        { "affix.thornmail",  new Affix("Thornmail",  Kind.Reflect,   0.3f,  0f) },
    };

    /// <summary>Look up an affix id's display name (for tooltips), or null if unknown.</summary>
    public static string NameOf(string affixId)
        => affixId != null && Catalog.TryGetValue(affixId, out var a) ? a.DisplayName : null;

    /// <summary>One read-only reference row per authored affix (id, name, kind, human effect) — the data source for
    /// the editor Affix &amp; Ring Voice Table. Reads the live <see cref="Catalog"/> and reuses <see cref="Behavior"/>,
    /// so the reference view can never drift from the real affix data. Editor/author-time only (allocs a list).</summary>
    public readonly struct AffixRow
    {
        public readonly string Id, Name, Kind, Effect;
        public AffixRow(string id, string name, string kind, string effect) { Id = id; Name = name; Kind = kind; Effect = effect; }
    }

    public static List<AffixRow> AllAffixes()
    {
        var rows = new List<AffixRow>(Catalog.Count);
        foreach (var kv in Catalog)
            rows.Add(new AffixRow(kv.Key, kv.Value.DisplayName, kv.Value.kind.ToString(), Behavior(kv.Value)));
        return rows;
    }

    /// <summary>Sum the equipped weapon's EQUIP-PASSIVE affixes (always-on while worn) — read by
    /// <see cref="Character"/> on equip. These are steady-state (mana leech / cost refund / thorns), not per-cast
    /// reshapers, so they live as Character state instead of <see cref="CastMods"/>.</summary>
    public static void GetPassives(Character ch, out float manaLeech, out float refund, out float reflect)
    {
        manaLeech = 0f; refund = 0f; reflect = 0f;
        if (ch == null) return;
        var weapon = ch.GetEquipped(EquipSlotType.Weapon);
        var ids = weapon != null ? weapon.weaponAffixIds : null;
        if (ids == null) return;
        for (int i = 0; i < ids.Count; i++)
        {
            if (!Catalog.TryGetValue(ids[i], out var a)) continue;
            switch (a.kind)
            {
                case Kind.ManaLeech: manaLeech += a.power; break;
                case Kind.Refund:    refund += a.power; break;
                case Kind.Reflect:   reflect += a.power; break;
            }
        }
    }

    /// <summary>Append a live line per affix baked on the EQUIPPED weapon (name + what it does), but only when
    /// the affix can act on <paramref name="def"/>. Reads the equipped weapon, so a tooltip always reflects the
    /// current weapon. Rich-text bold on numbers.</summary>
    public static void AppendSummary(Character ch, System.Text.StringBuilder sb, SkillDef def)
    {
        if (ch == null) return;
        var weapon = ch.GetEquipped(EquipSlotType.Weapon);
        var ids = weapon != null ? weapon.weaponAffixIds : null;
        if (ids == null) return;
        bool hitsMobs = SkillReshape.HitsMobs(def);
        bool damagesMobs = SkillReshape.DamagesMobs(def);
        for (int i = 0; i < ids.Count; i++)
        {
            if (!Catalog.TryGetValue(ids[i], out var a)) continue;
            bool applies;
            switch (a.kind)
            {
                case Kind.RawDamage:
                case Kind.ManaLeech: applies = damagesMobs; break;   // need a damaging hit
                case Kind.Refund:
                case Kind.Reflect:   applies = true; break;          // always-on equip passives
                default:             applies = hitsMobs; break;       // control needs a mob-hit
            }
            if (!applies) continue;
            sb.Append("\n• <b>").Append(a.DisplayName).Append("</b> <color=#FFB060>(weapon)</color> — ").Append(Behavior(a));
        }
    }

    static string Behavior(Affix a)
    {
        switch (a.kind)
        {
            case Kind.Slow:      return "slows to <b>" + UnityEngine.Mathf.RoundToInt(a.power * 100f) + "%</b> for <b>" + a.secs.ToString("0.#") + "s</b> on hit";
            case Kind.Root:      return "roots for <b>" + a.secs.ToString("0.#") + "s</b> on hit";
            case Kind.Stun:      return "stuns for <b>" + a.secs.ToString("0.#") + "s</b> on hit";
            case Kind.Push:      return "knocks enemies back on hit";
            case Kind.Pull:      return "pulls enemies in on hit";
            case Kind.RawDamage: return "<b>+" + UnityEngine.Mathf.RoundToInt(a.power * 100f) + "%</b> damage";
            case Kind.ManaLeech: return "restores <b>" + UnityEngine.Mathf.RoundToInt(a.power * 100f) + "%</b> of damage as MP";
            case Kind.Refund:    return "refunds <b>" + UnityEngine.Mathf.RoundToInt(a.power * 100f) + "%</b> of the MP cost";
            case Kind.Reflect:   return "reflects <b>" + UnityEngine.Mathf.RoundToInt(a.power * 100f) + "%</b> of damage taken";
            default:             return a.kind.ToString();
        }
    }

    /// <summary>Fold the equipped weapon's baked affixes into <paramref name="mods"/> (weapon layer, applied
    /// BEFORE the ring's voices — see the design's fold order). No-op with no weapon or no affixes. Zero-alloc.</summary>
    public static void AddOutgoing(Character ch, ref CastMods mods)
    {
        if (ch == null) return;
        var weapon = ch.GetEquipped(EquipSlotType.Weapon);
        var ids = weapon != null ? weapon.weaponAffixIds : null;
        if (ids == null) return;
        for (int i = 0; i < ids.Count; i++)
        {
            if (!Catalog.TryGetValue(ids[i], out var a)) continue;
            switch (a.kind)
            {
                case Kind.Slow:
                    if (mods.slowFactor <= 0f || a.power < mods.slowFactor) mods.slowFactor = a.power;
                    mods.slowSecs = UnityEngine.Mathf.Max(mods.slowSecs, a.secs);
                    break;
                case Kind.Root: mods.rootSecs = UnityEngine.Mathf.Max(mods.rootSecs, a.secs); break;
                case Kind.Stun: mods.stunSecs = UnityEngine.Mathf.Max(mods.stunSecs, a.secs); break;
                case Kind.Push:
                    mods.pushSpeed = UnityEngine.Mathf.Max(mods.pushSpeed, a.power);
                    mods.pushSecs = UnityEngine.Mathf.Max(mods.pushSecs, a.secs);
                    break;
                case Kind.Pull:
                    mods.pullSpeed = UnityEngine.Mathf.Max(mods.pullSpeed, a.power);
                    mods.pullSecs = UnityEngine.Mathf.Max(mods.pullSecs, a.secs);
                    break;
                case Kind.RawDamage: mods.rawDamageMult += a.power; break;
            }
        }
    }
}
