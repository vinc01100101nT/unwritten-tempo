using System.Collections.Generic;

/// <summary>
/// THE ONE CHECK every bag request answers to on the server: is the stack at this position still the one the
/// player was looking at? A position is enough to name a stack right up until the bag moves between the click and
/// the request arriving, which two quick clicks are enough to do — so every request also names WHAT it expected
/// there, and one that no longer matches is dropped rather than carried out on whatever slid into that place.
///
/// <para>Naming the item is REQUIRED. An honest game always sends it; a request without it is refused, never
/// waved through.</para>
///
/// <para>Plain function on purpose: the same question is asked by using an item and by every other bag command,
/// and two copies of it are two places for one to be fixed and the other forgotten.</para>
/// </summary>
public static class BagStack
{
    public enum Verdict { Same, NoSuchStack, NothingNamed, SomethingElse }

    public static Verdict Check(List<BagEntry> bag, int index, string expectedItemId)
    {
        if (bag == null || index < 0 || index >= bag.Count) return Verdict.NoSuchStack;
        var e = bag[index];
        if (e == null || e.def == null) return Verdict.NoSuchStack;
        if (string.IsNullOrEmpty(expectedItemId)) return Verdict.NothingNamed;
        return e.def.id == expectedItemId ? Verdict.Same : Verdict.SomethingElse;
    }

    /// <summary><see cref="Check"/>, with a refusal that names an item said in the server's log under
    /// <paramref name="who"/>. True only for <see cref="Verdict.Same"/>.</summary>
    public static bool StillTheSame(Character c, int index, string expectedItemId, string who)
    {
        var bag = c != null ? c.Bag : null;
        var verdict = Check(bag, index, expectedItemId);
        if (verdict == Verdict.SomethingElse)
            GameLog.Warn(LogCat.Server, "" + who + " asked about position " + index + " expecting '"
                             + expectedItemId + "' but it now holds '" + bag[index].def.id + "' — ignored.");
        else if (verdict == Verdict.NothingNamed)
            GameLog.Warn(LogCat.Server, "" + who + " asked about position " + index
                             + " without saying which item they expected there — ignored.");
        return verdict == Verdict.Same;
    }
}
