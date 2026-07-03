using System.Collections.Generic;

/// <summary>
/// SINGLE SOURCE OF TRUTH for "which Quaternius embedded clip plays a given <see cref="CharacterAction"/>" on a
/// live 3D character. Both the editor AnimatorController builder (<c>Use3DCharacter</c>) and the runtime
/// <see cref="Character3DPose"/> read this one map, so a state named here and the state the pose CrossFades to can
/// never drift. Clip names are the Quaternius take SUFFIXES (the armature "CharacterArmature|" prefix stripped).
///
/// <para>Idle/Walk/Run are the locomotion blend and aren't one-shot states, but they're listed so
/// <see cref="Character3DPose"/> can length-match every clip. Some game actions have no bespoke Quaternius clip
/// (there's no "cast" — Shoot_OneHanded stands in); those mappings are the best-fit choice, change them here once.</para>
/// </summary>
public static class Character3DClipMap
{
    /// <summary>CharacterAction → Quaternius clip suffix. The one place the 3D mapping is authored.</summary>
    public static readonly Dictionary<CharacterAction, string> ClipFor = new Dictionary<CharacterAction, string>
    {
        { CharacterAction.Idle,     "Idle" },
        { CharacterAction.Walk,     "Walk" },
        { CharacterAction.Run,      "Run" },
        { CharacterAction.Attack,   "SwordSlash" },
        { CharacterAction.Cast,     "Shoot_OneHanded" },   // no native cast clip — closest one-handed gesture
        { CharacterAction.Hurt,     "RecieveHit" },        // (sic) Quaternius spells it "Recieve"
        { CharacterAction.Die,      "Death" },
        { CharacterAction.Jump,     "Jump" },
        { CharacterAction.Roll,     "Roll" },
        { CharacterAction.Dash,     "Roll" },
        { CharacterAction.PickUp,   "PickUp" },
        { CharacterAction.Sit,      "SitDown" },
        { CharacterAction.Punch,    "Punch" },
        { CharacterAction.Shoot,    "Shoot_OneHanded" },
        { CharacterAction.Victory,  "Victory" },
        { CharacterAction.Defeat,   "Defeat" },
        { CharacterAction.StandUp,  "StandUp" },
        { CharacterAction.WalkCarry,"Walk_Carry" },
        { CharacterAction.RunCarry, "Run_Carry" },
    };

    /// <summary>The three actions that resolve to the 1-D locomotion blend tree (not a one-shot CrossFade state).</summary>
    public static bool IsLocomotion(CharacterAction a) =>
        a == CharacterAction.Idle || a == CharacterAction.Walk || a == CharacterAction.Run;
}
