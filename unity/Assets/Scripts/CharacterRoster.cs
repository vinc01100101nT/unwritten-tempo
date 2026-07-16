using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The registry of every baked character built by the batch baker — name, ready-to-spawn
/// <see cref="BakedBillboard8"/> prefab, and a preview sprite (the Idle/South first frame). The runtime
/// character-select sandbox reads this to populate its picker; gameplay can also use it to look a character
/// up by name. The batch baker creates/updates one asset at <c>Assets/1_Authored/Art/Characters/CharacterRoster.asset</c>.
/// </summary>
[CreateAssetMenu(fileName = "CharacterRoster", menuName = "unwritten/Character Roster", order = 31)]
public class CharacterRoster : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string name;
        public GameObject prefab;     // a BakedBillboard8 prefab
        public Texture2D previewStrip; // the Idle_S strip; the picker draws its first cell as a thumbnail
    }

    public List<Entry> characters = new List<Entry>();

    /// <summary>Find an entry by (case-insensitive) name, or null.</summary>
    public Entry Find(string n)
    {
        if (string.IsNullOrEmpty(n)) return null;
        foreach (var e in characters)
            if (e != null && string.Equals(e.name, n, System.StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    /// <summary>Add or replace an entry by name (used by the batch baker to keep one row per character).</summary>
    public void Upsert(string n, GameObject prefab, Texture2D previewStrip)
    {
        var e = Find(n);
        if (e == null) { e = new Entry { name = n }; characters.Add(e); }
        e.prefab = prefab;
        e.previewStrip = previewStrip;
    }
}
