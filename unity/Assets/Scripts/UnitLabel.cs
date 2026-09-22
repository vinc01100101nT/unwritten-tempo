using UnityEngine;

/// <summary>
/// WHAT A UNIT IS CALLED — the one answer, for a creature, a person or a villager.
///
/// <para>Written down once because it was already written twice: the readout at the top of the screen worked it out
/// one way, and the text client another, and a third reader would have been a third answer. A name is a fact about
/// the unit, not about whichever thing happens to be drawing it.</para>
///
/// <para>Called when a readout BINDS to a unit — never per frame — so the component lookup inside costs nothing
/// that matters.</para>
/// </summary>
public static class UnitLabel
{
    /// <summary>The name to show for this unit. Never null; an unnamed object falls back to a tidied object name
    /// rather than to an empty label, because a blank plate reads as a bug.</summary>
    public static string Of(Health unit)
    {
        if (unit == null) return "";

        // A creature carries its authored name on its plate, whether or not that plate is drawing it.
        // perf-ok: on bind only, never per frame.
        var plate = unit.GetComponent<MobNameplate>();
        if (plate != null && !string.IsNullOrEmpty(plate.displayName)) return plate.displayName;

        return Tidy(unit.name);
    }

    /// <summary>The name with the level beside it — "Poring Lv 12" — or the bare name when there is no level. The
    /// level is the server's (<see cref="NetMob.Level"/>); a readout re-asks this only when that number changes.</summary>
    public static string WithLevel(string name, int level) => level > 0 ? name + " Lv " + level : name;

    // The object names the game builds bodies under, turned back into the name a person would recognise. A watched
    // person's body is "Player_<their name>" and a creature's is "Monster_<its name>", sometimes with Unity's own
    // clone suffix on the end.
    static string Tidy(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string n = raw;
        if (n.EndsWith("(Clone)")) n = n.Substring(0, n.Length - "(Clone)".Length);
        if (n.EndsWith(" ")) n = n.TrimEnd();
        if (n.StartsWith("Player_")) n = n.Substring("Player_".Length);
        else if (n.StartsWith("Monster_")) n = n.Substring("Monster_".Length);
        return n;
    }
}
