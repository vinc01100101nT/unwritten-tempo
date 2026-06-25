using UnityEngine;

/// <summary>
/// A card that slots into an equipment piece's card socket (RO-style). Insertion is
/// a DIALOG action (select gear → Insert Card → pick from a list), never drag-onto-socket.
/// A card grants a small, permanent stat bonus while its host item is equipped; the bonus
/// flows through the same derived-stat math as gear (see <see cref="EquipBonuses"/>).
/// Create via Assets ▸ Create ▸ unwritten ▸ Card.
/// </summary>
[CreateAssetMenu(fileName = "Card", menuName = "unwritten/Card")]
public class Card : ScriptableObject
{
    public string displayName = "Card";
    public Sprite icon;
    [Tooltip("One-line effect shown in the card list / tooltip, e.g. \"+20 ATK\".")]
    public string blurb;

    [Header("Bonuses (applied while the host item is equipped)")]
    public int bonusStr;
    public int bonusAgi;
    public int bonusVit;
    public int bonusInt;
    public int bonusDex;
    public int bonusLuk;

    [Header("Flat combat bonuses")]
    public int bonusAttack;
    [Tooltip("Added to crit CHANCE, in whole percent (e.g. 5 = +5%).")]
    public int bonusCritPercent;
    [Tooltip("Added to attack SPEED, in whole percent (e.g. 6 = +6%). May be negative.")]
    public int bonusAspdPercent;
}
