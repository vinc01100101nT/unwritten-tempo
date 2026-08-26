using UnityEngine;

/// <summary>
/// Press-to-talk (F) for ambient townsfolk. Press F near a <see cref="TownNPC"/> and the camera eases to a side-on
/// two-shot of the player + that NPC, both turn to face each other, the HUD hides, and a bottom-screen dialog box
/// (<see cref="DialogBox"/>) opens with a random, time-of-day-appropriate conversation. Page it with the box's
/// NEXT/CLOSE button or the Right-Arrow key; only <b>Esc</b> cancels early (WASD does not). When it closes, the
/// camera eases back and the HUD returns.
///
/// <para>This is the 3D press-to-talk system the dormant <see cref="InteractPrompt"/>/<see cref="DialogueUI"/> were
/// left for. It finds NPCs via <see cref="TownNPC"/>'s registry (FindObjectsByType can't see them — DontSaveInEditor).
/// Self-attached by <see cref="Player3DController"/> (idempotent); while a chat is active <see cref="Talking"/> is
/// true and the controller freezes movement.</para>
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("How close (world units) the player must be to a townsperson to start talking.")]
    public float interactRange = 1.3f;

    /// <summary>True while a conversation is active — <see cref="Player3DController"/> reads this to freeze movement.</summary>
    public static bool Talking { get; private set; }

    Player3DController player;
    TownNPC partner;
    Canvas gameHud;

    void Awake() { player = GetComponent<Player3DController>(); }

    void Update()
    {
        if (player == null) return;

        if (Talking)
        {
            // Esc cancels (WASD does NOT — the player is frozen). If the partner despawned, close too.
            bool cancelled = Hotkeys.CancelDown();
            if (cancelled || partner == null)
            {
                if (cancelled) Hotkeys.MarkCancelHandled();
                if (DialogBox.Instance != null) DialogBox.Instance.Close();   // fires onClosed → EndTalk
                else EndTalk();
                return;
            }

            // Keep both turned to each other and the camera framed on the side (they're frozen, but track anyway).
            player.FaceWorldPoint(partner.transform.position);
            FrameCamera();
            return;
        }

        // Not talking: F starts a chat with the nearest townsperson in range.
        if (Hotkeys.Down(KeyCode.F))
        {
            var npc = TownNPC.Nearest(transform.position, interactRange);
            if (npc != null) StartTalk(npc);
        }
    }

    void StartTalk(TownNPC npc)
    {
        partner = npc;
        Talking = true;
        npc.BeginTalk(transform);
        player.FaceWorldPoint(npc.transform.position);
        FrameCamera();
        HideHud(true);
        // The box drives NEXT/CLOSE + Right-Arrow itself; when it closes it calls EndTalk to unwind the cinematic.
        DialogBox.Open(npc.SpeakerName, npc.DialogueLines(), EndTalk);
    }

    // Invoked as the DialogBox's onClosed (natural CLOSE / past-last-line) AND on Esc/despawn via DialogBox.Close.
    // Idempotent: unwinds the cinematic once.
    void EndTalk()
    {
        if (!Talking) return;
        Talking = false;
        if (partner != null) partner.EndTalk();
        partner = null;
        if (CameraFollow3D.Main != null) CameraFollow3D.Main.ExitCinematic();
        HideHud(false);
    }

    // Hide/show the whole game HUD (one canvas) + the perf overlay for a clean cinematic.
    void HideHud(bool hide)
    {
        if (gameHud == null)
        {
            var go = GameObject.Find("GameUICanvas") ?? GameObject.Find("GameUICanvas(Clone)");
            if (go != null) gameHud = go.GetComponent<Canvas>();
        }
        if (gameHud != null) gameHud.enabled = !hide;
        // PerfHud compiles out of non-development builds entirely, so the call must go with it — the same
        // condition the class itself carries. Without this the game does not build for release OR for a server.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        PerfHud.SetHidden(hide);
#endif
    }

    // Side-on two-shot: camera perpendicular to the player↔NPC line, on whichever side the camera is already on (so
    // it doesn't swing 180° through the pair), pulled back + raised to frame both. Sized off the NPC's height so it
    // stays right at the world's small character scale.
    void FrameCamera()
    {
        var cam = CameraFollow3D.Main;
        if (cam == null || partner == null) return;

        Vector3 a = transform.position, b = partner.transform.position;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 axis = b - a; axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f) axis = CameraRig.ForwardXZ;   // overlapping → any axis
        axis.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, axis);              // horizontal perpendicular
        if (Vector3.Dot(side, cam.transform.position - mid) < 0f) side = -side;   // keep the camera's current side

        float h = partner.HeadHeight; if (h < 1e-3f) h = 0.3f;
        float sep = Vector3.Distance(a, b);
        float dist = Mathf.Max(h * 3.2f, sep * 1.2f + h * 2f);

        Vector3 camPos = mid + side * dist + Vector3.up * (h * 1.1f);
        Vector3 lookAt = mid + Vector3.up * (h * 0.55f);
        cam.EnterCinematic(camPos, lookAt);
    }

    void OnDisable()
    {
        if (Talking)
        {
            if (DialogBox.Instance != null) DialogBox.Instance.Close(); else EndTalk();
        }
    }
}
