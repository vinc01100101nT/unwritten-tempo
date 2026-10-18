using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using System.Text;

/// <summary>
/// WHAT A CREATURE IS WEARING, as one piece of text that crosses the wire: each worn piece's item id, its grade,
/// and how many times it has been folded. The SERVER writes it; every watching machine reads it back and works
/// nothing out.
/// </summary>
public static class WornList
{
    /// <summary>One worn piece as read back from the text.</summary>
    public struct Entry
    {
        public Item def;
        public Quality quality;
        public int folds;
    }

    const char PieceGap = '\n';
    const char FieldGap = '\t';

    static readonly List<EquipmentInstance> scratch = new List<EquipmentInstance>(8);

    static readonly System.Comparison<EquipmentInstance> ByBodyOrder = (a, b) =>
        EquipSlots.Order(a.def.slot).CompareTo(EquipSlots.Order(b.def.slot));

    /// <summary>SERVER: what <paramref name="gear"/> wears, as text, pieces in body order. "" when nothing is worn.
    /// Built only when the worn set changes, never per frame.</summary>
    public static string Encode(UnitEquipment gear)
    {
        if (gear == null) return "";
        gear.CopyWornInto(scratch);
        for (int i = scratch.Count - 1; i >= 0; i--)
            if (scratch[i] == null || scratch[i].def == null) scratch.RemoveAt(i);
        if (scratch.Count == 0) return "";
        scratch.Sort(ByBodyOrder);

        var sb = new StringBuilder();
        for (int i = 0; i < scratch.Count; i++)
        {
            var inst = scratch[i];
            if (sb.Length > 0) sb.Append(PieceGap);
            sb.Append(inst.def.id).Append(FieldGap)
              .Append(((int)inst.quality).ToString(CultureInfo.InvariantCulture)).Append(FieldGap)
              .Append(inst.upgrade.ToString(CultureInfo.InvariantCulture));
        }
        scratch.Clear();
        return sb.ToString();
    }

    /// <summary>Read <paramref name="text"/> back into <paramref name="result"/>, which is cleared first. A piece whose
    /// item this build does not have is skipped.</summary>
    public static void Decode(string text, List<Entry> result)
    {
        if (result == null) return;
        result.Clear();
        if (string.IsNullOrEmpty(text)) return;

        foreach (var piece in text.Split(PieceGap))
        {
            var fields = piece.Split(FieldGap);
            if (fields.Length < 3) continue;
            var def = ItemCatalog.Resolve(fields[0]);
            if (def == null) continue;
            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int q)) continue;
            if (q < (int)Quality.Raw || q > (int)Quality.A) continue;
            int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int folds);
            result.Add(new Entry { def = def, quality = (Quality)q, folds = Mathf.Max(0, folds) });
        }
    }
}
