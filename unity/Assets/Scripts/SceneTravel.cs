/// <summary>
/// Carries the desired arrival spawn across a scene load. A <see cref="Portal"/>
/// sets <see cref="Target"/> before loading the next scene; that scene's
/// Bootstrap reads it to place the player at the matching <see cref="SpawnPoint"/>,
/// then clears it. A plain static class — no GameObject, so it survives the load.
/// </summary>
public static class SceneTravel
{
    public static string Target;
}
