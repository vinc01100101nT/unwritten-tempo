using UnityEngine;

/// <summary>
/// Marks a creature as called up by a summoner (<see cref="CreatureSummons"/>) for this life. While it is: it follows
/// its summoner, it never summons, it pays no experience and drops nothing, and when it dies it goes back to its
/// summoner to be reused rather than respawning on its own. When its summoner dies it vanishes at once.
///
/// <para>Added once per creature object and kept through every reuse; <see cref="Release"/> clears it.</para>
/// </summary>
public class SummonedCreature : MonoBehaviour
{
    /// <summary>Who called it up. Null once released.</summary>
    public CreatureSummons summoner;

    /// <summary>Which of the summoner's groups it fills — the elite's nearest normal, the next, or the one after.</summary>
    public int group;

    /// <summary>Called by <see cref="Health.Die"/> after the death fade: hand the body back to its summoner.</summary>
    public void OnFadedOut()
    {
        if (summoner != null) summoner.Bank(this);
        else CreatureSummons.Hide(gameObject);
    }
}
