/// <summary>
/// Carries the desired arrival spawn across a scene load. A <see cref="Portal3D"/>
/// sets <see cref="Target"/> before loading the next scene; <see cref="PersistentPlayer3D"/>
/// reads it to place the player at the matching <see cref="SpawnPoint"/>,
/// then clears it. A plain static class — no GameObject, so it survives the load.
/// </summary>
public static class SceneTravel
{
    public static string Target;

    /// <summary>Where a signing-in player left off, carried the same one-shot way <see cref="Target"/> is.
    /// A door says WHICH spawn point to arrive at; a resumed session says the exact spot, because standing
    /// where you logged out is the whole point of recording it. Cleared the moment it is used, so it can
    /// never quietly place a later arrival.</summary>
    public static string ResumeScene;
    public static UnityEngine.Vector3 ResumePosition;

    public static bool HasResume => !string.IsNullOrEmpty(ResumeScene);

    public static void ForgetResume() { ResumeScene = null; }
}
