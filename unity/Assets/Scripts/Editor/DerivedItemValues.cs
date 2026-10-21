using System.Collections.Generic;
using System.Text;
using UnityEditor;

/// <summary>
/// THE ITEM NUMBERS NOBODY TYPES — every piece of gear's value and every creature material's tier and value are worked
/// out from the rule and written into the item files, so the saved number can never drift from the rule:
/// <list type="bullet">
/// <item>gear: what a brand-new piece is worth (<see cref="CraftingMath.NewPieceWorth"/>) — the same rule every copy's
/// own worth follows, so a piece is worth the same to a tooltip, a sale and a purchase;</item>
/// <item>a creature's own material: the tier its creature's kind and grade give, and the value that pays what its
/// creature's level, tier, grade and folds say (<see cref="CreatureMaterials.Derive"/>) — and its icon, the box drawn
/// in its tier's colour (<see cref="BoxIconPath"/>), so no material's box is ever picked by hand.</item>
/// </list>
///
/// <para>It runs by itself whenever an item or a creature file is SAVED — the user's own save is the action that
/// writes, and the worked-out files are saved with it. <c>DerivedItemValues.Sync(false)</c> through the live editor
/// only reports what is out of step. <c>tools/check-creature-materials.py</c> names any saved number that disagrees.
/// The player's machine only reads the saved numbers; it never works one out.</para>
/// </summary>
public class DerivedItemValues : AssetModificationProcessor
{
    const string ItemsFolder = "/Resources/Items/";
    const string MonstersFolder = "/Resources/Monsters/";
    // tools/check-creature-materials.py reads this line.
    const string BoxIconPath = "Assets/1_Authored/Art/Icons/Items/Box {0}.png";

    static bool syncing;

    static string[] OnWillSaveAssets(string[] paths)
    {
        if (syncing) return paths;
        bool touches = false;
        foreach (var p in paths)
            if (p.EndsWith(".asset") && (p.Contains(ItemsFolder) || p.Contains(MonstersFolder))) { touches = true; break; }
        if (!touches) return paths;

        var changed = new List<string>();
        syncing = true;
        try { Apply(true, null, changed); }
        finally { syncing = false; }
        if (changed.Count == 0) return paths;
        var all = new HashSet<string>(paths);
        foreach (var c in changed) all.Add(c);
        var result = new string[all.Count];
        all.CopyTo(result);
        return result;
    }

    /// <summary>Work every derived number out and, with <paramref name="write"/>, write and save what is out of step.
    /// Returns what changed (or would).</summary>
    public static string Sync(bool write)
    {
        var said = new StringBuilder();
        var changed = new List<string>();
        int n = Apply(write, said, changed);
        // ONLY WHAT THIS CHANGED IS SAVED: saving everything would also write whatever else happens to be unsaved in
        // the editor — on 2026-10-20 that was a font atlas filled during a play session.
        if (write)
            foreach (var path in changed)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
        said.Append(write ? "wrote " : "would write ").Append(n).Append(" item(s)");
        return said.ToString();
    }

    static int Apply(bool write, StringBuilder said, List<string> changedPaths)
    {
        // The versions first: a version's material is priced from the level it copies from its original.
        int n = CreatureVersions.Apply(write, said, changedPaths);
        foreach (var d in MonsterCatalog.All)
        {
            if (d == null || d.material == null || d.material.materialKind != MaterialKind.Creature) continue;
            if (!CreatureMaterials.Derive(d, out int step, out int value)) continue;
            n += Set(d.material, step, value, write, said, changedPaths);
            n += SetIcon(d.material, BoxIcon(CreatureMaterials.TierOfStep(step)), write, said, changedPaths);
        }

        var dust = MaterialCatalog.TemperDust;
        var oil = MaterialCatalog.QuenchOil;
        int newPiece = (int)System.Math.Min(int.MaxValue,
            CraftingMath.NewPieceWorth(dust != null ? dust.value : 0, oil != null ? oil.value : 0));
        foreach (var it in ItemCatalog.All)
            if (it != null && it.slot != EquipSlotType.None)
                n += Set(it, it.materialStep, newPiece, write, said, changedPaths);
        return n;
    }

    static readonly UnityEngine.Sprite[] boxIcons = new UnityEngine.Sprite[(int)Rarity.Legendary + 1];

    static UnityEngine.Sprite BoxIcon(Rarity tier)
    {
        int t = (int)tier;
        if (boxIcons[t] == null)
            boxIcons[t] = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(string.Format(BoxIconPath, tier));
        return boxIcons[t];
    }

    static int SetIcon(Item it, UnityEngine.Sprite box, bool write, StringBuilder said, List<string> changedPaths)
    {
        if (box == null)
        {
            said?.Append(it.displayName).Append(": no box icon found for its tier\n");
            return 0;
        }
        if (it.icon == box) return 0;
        said?.Append(it.displayName).Append(": icon ").Append(it.icon != null ? it.icon.name : "none").Append(" -> ")
             .Append(box.name).Append('\n');
        if (write)
        {
            it.icon = box;
            // editor-write-ok: runs only inside the user's own save of an item or creature file, or a Sync(true) they ran.
            EditorUtility.SetDirty(it);
            changedPaths?.Add(AssetDatabase.GetAssetPath(it));
        }
        return 1;
    }

    static int Set(Item it, int step, int value, bool write, StringBuilder said, List<string> changedPaths)
    {
        if (it.materialStep == step && it.value == value) return 0;
        said?.Append(it.displayName).Append(": step ").Append(it.materialStep).Append(" -> ").Append(step)
             .Append(", value ").Append(it.value).Append(" -> ").Append(value).Append('\n');
        if (write)
        {
            it.materialStep = step;
            it.value = value;
            // editor-write-ok: runs only inside the user's own save of an item or creature file, or a Sync(true) they ran.
            EditorUtility.SetDirty(it);
            changedPaths?.Add(AssetDatabase.GetAssetPath(it));
        }
        return 1;
    }
}
