using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// "IF EQUIPPED" — the lines under a piece of gear's tooltip in the bag that say what would change if you put it on:
/// the piece it replaces, then each stat that moves, before → after and by how much, green up and red down.
///
/// <para><b>The server works every number out.</b> It holds the character, so it derives the sheet as if the piece were
/// worn (<see cref="Character.DeriveWith"/>) against the sheet as it is, and sends the finished lines. A player's machine
/// asks when the pointer rests on a piece, keeps the answer until the character's sheet or bag next changes, and shows
/// the lines exactly as sent.</para>
/// </summary>
public static class EquipPreview
{
    // ---- the server's side ----------------------------------------------------------------------------------

    /// <summary>SERVER: the finished lines for wearing <paramref name="inst"/> in <paramref name="slot"/>, as
    /// <paramref name="c"/> stands now.</summary>
    public static string Text(Character c, EquipmentInstance inst, EquipSlotType slot)
    {
        var sb = new StringBuilder();
        var equipped = c.GetEquippedInstance(slot);
        if (equipped != null && equipped.def != null && equipped != inst)
            sb.Append("\n\n" + ItemTooltip.DimTag + "Replaces </color>" + ItemTooltip.MainTag)
              .Append(ItemTooltip.CopyTitle(equipped)).Append("</color>");

        var before = c.CurrentDerived;
        var after = c.DeriveWith(slot, inst);
        sb.Append("\n" + ItemTooltip.DimTag + "If equipped:</color>");
        AppendDelta(sb, "MELEE ATK", before.meleeMax, after.meleeMax);
        AppendDelta(sb, "RANGED ATK", before.rangedMax, after.rangedMax);
        AppendDelta(sb, "M.ATK", before.matkMax, after.matkMax);
        AppendDelta(sb, "DEF", before.def, after.def);
        AppendDelta(sb, "M.DEF", before.mdef, after.mdef);
        AppendDelta(sb, "HIT", before.hit, after.hit);
        AppendDelta(sb, "FLEE", before.flee, after.flee);
        AppendDelta(sb, "Max HP", before.maxHP, after.maxHP);
        AppendDeltaPct(sb, "CRIT", before.critChance, after.critChance);
        return sb.ToString();
    }

    // Green/red "LABEL  before → after (+delta)" rows. Unchanged rows are skipped.
    static void AppendDelta(StringBuilder sb, string label, int before, int after)
    {
        if (before == after) return;
        int d = after - before;
        string color = d > 0 ? ItemTooltip.GoodHex : ItemTooltip.BadHex;
        sb.Append("\n" + ItemTooltip.DimTag).Append(label).Append("</color>  ")
          .Append(before).Append(" → ").Append(after)
          .Append(" <color=").Append(color).Append('>').Append(d > 0 ? "+" : "").Append(d).Append("</color>");
    }

    static void AppendDeltaPct(StringBuilder sb, string label, float before, float after)
    {
        int b = Mathf.RoundToInt(before * 100f), a = Mathf.RoundToInt(after * 100f);
        if (b == a) return;
        int d = a - b;
        string color = d > 0 ? ItemTooltip.GoodHex : ItemTooltip.BadHex;
        sb.Append("\n" + ItemTooltip.DimTag).Append(label).Append("</color>  ")
          .Append(b).Append("% → ").Append(a).Append("% <color=").Append(color).Append('>')
          .Append(d > 0 ? "+" : "").Append(d).Append("%</color>");
    }

    // ---- the player's machine: what the server sent ---------------------------------------------------------

    static readonly Dictionary<(string, EquipSlotType), string> sent = new Dictionary<(string, EquipSlotType), string>();
    static readonly Dictionary<(string, EquipSlotType), float> askedAt = new Dictionary<(string, EquipSlotType), float>();

    // An ask with no answer after this long is forgotten, so resting on the piece again asks again.
    const float AskAgainAfter = 5f;

    /// <summary>Goes up every time an answer arrives or the kept answers are dropped, so a tooltip waiting on one
    /// redraws only then.</summary>
    public static int Version { get; private set; }

    /// <summary>PLAYER'S MACHINE: the lines the server sent for wearing copy <paramref name="copyId"/> in
    /// <paramref name="slot"/>; false before they arrive.</summary>
    public static bool TryGetSent(string copyId, EquipSlotType slot, out string text) =>
        sent.TryGetValue((copyId ?? "", slot), out text);

    /// <summary>PLAYER'S MACHINE: ask the server, unless the answer is already here or was asked for moments ago.</summary>
    public static void Ask(string copyId, EquipSlotType slot)
    {
        if (string.IsNullOrEmpty(copyId)) return;
        var key = (copyId, slot);
        if (sent.ContainsKey(key)) return;
        float now = Time.unscaledTime;
        if (askedAt.TryGetValue(key, out float at) && now - at < AskAgainAfter) return;
        var me = GameNetwork.MyPlayer;
        if (me == null) return;
        me.AskEquipPreview(copyId, slot);
        askedAt[key] = now;
    }

    /// <summary>PLAYER'S MACHINE: keep what the server sent.</summary>
    public static void TakeSent(string copyId, EquipSlotType slot, string text)
    {
        if (string.IsNullOrEmpty(copyId)) return;
        sent[(copyId, slot)] = text ?? "";
        askedAt.Remove((copyId, slot));
        Version++;
    }

    /// <summary>PLAYER'S MACHINE: drop every kept answer — the character's sheet or bag changed, so each one may now be
    /// wrong, and the next hover asks again.</summary>
    public static void ForgetSent()
    {
        if (sent.Count == 0 && askedAt.Count == 0) return;
        sent.Clear();
        askedAt.Clear();
        Version++;
    }
}
