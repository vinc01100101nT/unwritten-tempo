/// <summary>
/// The canonical, PRE-DECLARED set of character animation actions — the contract key that ties together
/// the bake tool, the per-character <see cref="CharacterDef"/>, the baked PNG filenames (<c>Idle_N.png</c>),
/// and the runtime <see cref="BakedBillboard8"/> / gameplay lookup.
///
/// <para>It's an enum (not free text) on purpose: a name like "Run" vs "Running" vs "run" would bake fine but
/// silently miss at lookup time. As an enum, the compiler and every consumer agree by definition, renames
/// propagate, and adding an action is a deliberate one-line edit. Add new members at the END to keep any
/// serialized integer values stable.</para>
/// </summary>
public enum CharacterAction
{
    Idle,
    Walk,
    Run,
    Attack,
    Cast,
    Hurt,
    Die,
    Sit,
    Dash,
    Jump,
    // --- appended for the Quaternius pack's full embedded clip set (keep these AFTER the above) ---
    Punch,
    Shoot,
    Roll,
    Victory,
    Defeat,
    PickUp,
    StandUp,
    WalkCarry,
    RunCarry,
}

/// <summary>Action-domain helpers. <see cref="IsLocomotion"/> classifies the three actions that resolve to the
/// 1-D locomotion blend tree (Idle/Walk/Run) rather than a one-shot clip state. This is action→STRUCTURE (which
/// playback mechanism), NOT action→clip — the clip a stance plays is authored in the Stance Composer.</summary>
public static class CharacterActions
{
    public static bool IsLocomotion(CharacterAction a) =>
        a == CharacterAction.Idle || a == CharacterAction.Walk || a == CharacterAction.Run;
}
