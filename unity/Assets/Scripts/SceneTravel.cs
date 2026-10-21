/// <summary>
/// Carries the desired arrival spawn across a scene load. A <see cref="Portal3D"/>
/// sets <see cref="Target"/> before loading the next scene; <see cref="PersistentPlayer3D"/>
/// reads it to place the player at the matching <see cref="SpawnPoint"/>,
/// then clears it. A plain static class — no GameObject, so it survives the load.
/// </summary>
public static class SceneTravel
{
    /// <summary>Walk through a door into a map the SAME process is holding. Nothing crosses the wire and nothing is
    /// written down — this is what a door has always been, and it stays that way for every door that does not
    /// leave the process. It is started by the server's word rather than by the
    /// player's own machine only because the machine cannot know which side of the line a map is on.
    /// <para>With <paramref name="teleport"/> (a door, a Butterfly Wing) it looks like a teleport
    /// (<see cref="TeleportLook"/>): within the same map the screen fades through black, into another the loading
    /// screen fades in.</para></summary>
    public static void ArriveLocally(string map, string spawnId, bool teleport)
    {
        // THE MAP THEY ARE ALREADY IN — a save point in the map somebody died in. Loading it again would stand a
        // second copy of the map beside the first; they are only placed.
        if (map == MapManager.CurrentMap)
        {
            // A DOOR WITHIN ONE MAP already has the loading screen fading in over the teleport: they are placed
            // behind it, and it fades away again.
            if (teleport && LoadingScreen.Showing)
            {
                PlaceAgain(spawnId);
                if (CameraFollow3D.Main != null) CameraFollow3D.Main.SnapToTarget();
                LoadingScreen.Hide();
                return;
            }
            if (teleport)
            {
                TeleportLook.CoverWithFade(() =>
                {
                    // Left the map while the screen went dark: that landing belongs to a map no longer here.
                    if (MapManager.CurrentMap != map) return;
                    PlaceAgain(spawnId);
                    if (CameraFollow3D.Main != null) CameraFollow3D.Main.SnapToTarget();
                });
                return;
            }
            LoadingScreen.Hide();
            PlaceAgain(spawnId);
            return;
        }

        // ANOTHER MAP: the curtain goes up — a move the server started, a Keeper's trip or a wing, was not pressed
        // for here — and stays up until it has loaded and they stand in it, then the new map fades in.
        // A teleport loads the map only once the screen is black.
        if (teleport)
        {
            TeleportLook.CoverWithCurtain(() =>
            {
                LoadingScreen.HideWhenMapArrives();
                MapManager.Travel(map, spawnId);
            });
            return;
        }
        LoadingScreen.Show();
        LoadingScreen.HideWhenMapArrives();
        MapManager.Travel(map, spawnId);
    }

    static void PlaceAgain(string spawnId)
    {
        Target = spawnId;
        if (PersistentPlayer3D.Instance != null) PersistentPlayer3D.Instance.ReturnToSavedPlace();
    }

    /// <summary>The next arrival in <paramref name="map"/> stands at <paramref name="positionInMap"/> exactly — a save
    /// point, sent by the server. Carried the same one-shot way a signing-in player's last position is.</summary>
    public static void ArriveAtSpot(string map, UnityEngine.Vector3 positionInMap)
    {
        ResumeScene = map;
        ResumePosition = positionInMap;
    }

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
