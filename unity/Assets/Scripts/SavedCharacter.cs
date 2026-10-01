using UnityEngine;

/// <summary>What a saved character does once it has turned back.</summary>
public enum SavedBehaviour
{
    NeverFights,
    Ally,
    Rebel,
}

/// <summary>Who a character was in the artist's book.</summary>
public enum CharacterGroup
{
    VillageFolk,
    Court,
    Tribe,
    Warriors,
    BeastFolk,
    Mages,
    StorybookVillains,
    Constructs,
    NinjaClan,
}

/// <summary>How much gold saving a never-fights character pays, as a tier (see <see cref="SavedCharacterRules.GoldMultiplier"/>).</summary>
public enum GoldTier
{
    None,
    Ordinary,
    Helpless,
    Wealthy,
}

/// <summary>
/// One character from the artist's book — the page a mob really was, as he drew it. A beaten mob can turn back into one
/// (see <see cref="SavedCharacters"/>). Assets live in <c>Resources/SavedCharacters</c>, loaded by
/// <see cref="SavedCharacterCatalog"/>. Design: docs/3-world/saved-characters.md.
/// </summary>
[CreateAssetMenu(fileName = "SavedCharacter", menuName = "unwritten/Saved Character", order = 2)]
public class SavedCharacter : ScriptableObject
{
    [Tooltip("Stable id, e.g. 'character.villager'. What the server sends and what everything references. Never shown.")]
    public string id = "character.new";
    [Tooltip("The name every readout shows for this character. Can be renamed freely.")]
    public string displayName = "Character";

    [Tooltip("Its sprite sheet: 16px cells, one column per direction (down, up, left, right), the walk in the top rows.")]
    public Texture2D sheet;
    [Tooltip("Its portrait — the face drawn beside its name when you point at it or fight it.")]
    public Texture2D faceset;

    [Tooltip("What it does once it has turned back: stays and leaves, fights for its saver, or fights everyone.")]
    public SavedBehaviour behaviour = SavedBehaviour.NeverFights;
    [Tooltip("Who it was in the artist's book. A rebel's reason for fighting comes from this.")]
    public CharacterGroup group = CharacterGroup.VillageFolk;
    [Tooltip("How much gold saving it pays, times the mob's own gold range. Only a never-fights character pays gold.")]
    public GoldTier goldTier = GoldTier.None;

    [Tooltip("The id of the elite that always turns into this character. Empty = a normal may turn into it at random.")]
    public string eliteId = "";

    [Header("Measured art (written when the asset is made, from the walk frames of its sheet)")]
    [Tooltip("How many pixels up from the bottom of a cell its feet stand — the lowest painted row of its walk frames. " +
             "The picture is pinned to the ground there. -1 = never measured: the mobs' feet line is used.")]
    public float footPx = -1f;
    [Tooltip("How far the tallest walk frame's crown sits above its feet line, in world units at scale one.")]
    public float crownAtScaleOne;
    [Tooltip("How wide the widest walk frame's painted part is, in world units at scale one.")]
    public float paintedWidthAtScaleOne;
}
