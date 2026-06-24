using UnityEngine;

/// <summary>
/// Standalone QA playground for the baked character roster: press Play, pick a character from the on-screen
/// list, and walk it around with WASD while a tilted follow-camera tracks it. Pure test harness — it doesn't
/// touch the real <see cref="Player3DController"/> or game systems; it just spawns a
/// <see cref="BakedBillboard8"/> prefab from the <see cref="CharacterRoster"/> and drives its transform so the
/// billboard self-animates. Build the scene via <c>Tools ▸ unwritten ▸ Create Character Sandbox Scene</c>.
/// </summary>
public class CharacterSelectSandbox : MonoBehaviour
{
    public CharacterRoster roster;

    [Header("Movement")]
    public float walkSpeed = 2.6f;
    public float runMultiplier = 1.8f;   // hold Shift

    [Header("Camera")]
    public float camPitch = 35f;
    public float camYaw = 0f;
    public float camDistance = 9f;
    public float camFollow = 8f;          // lerp speed

    GameObject current;
    BakedBillboard8 currentBB;
    Transform cam;
    bool pickerOpen = true;
    Vector2 scroll;

    // Number keys → play a one-shot action for QA (cleared when you move).
    static readonly (KeyCode key, CharacterAction action)[] TestKeys =
    {
        (KeyCode.Alpha1, CharacterAction.Attack), (KeyCode.Alpha2, CharacterAction.Jump),
        (KeyCode.Alpha3, CharacterAction.Hurt),   (KeyCode.Alpha4, CharacterAction.Die),
        (KeyCode.Alpha5, CharacterAction.Cast),   (KeyCode.Alpha6, CharacterAction.Sit),
    };

    void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;
        if (roster == null) Debug.LogWarning("[unwritten] CharacterSelectSandbox has no roster assigned.");
    }

    void Update()
    {
        if (current == null) return;

        // Move on the ground plane, relative to the camera's facing (W = away/up-screen).
        float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
        Vector3 move = (CameraRig.ForwardXZ * v + CameraRig.RightXZ * h);
        if (move.sqrMagnitude > 1f) move.Normalize();
        float speed = walkSpeed * (Input.GetKey(KeyCode.LeftShift) ? runMultiplier : 1f);
        Vector3 vel = move * speed;
        bool moving = move.sqrMagnitude > 1e-4f;
        current.transform.position += vel * Time.deltaTime;

        // Drive the billboard explicitly (facing + Walk/Run from velocity). QA: number keys play a one-shot
        // action while standing still; moving cancels it.
        if (currentBB != null)
        {
            if (!moving)
                foreach (var (key, action) in TestKeys) if (Input.GetKeyDown(key)) currentBB.Play(action);
            currentBB.SetVelocity(vel);
        }
    }

    void LateUpdate()
    {
        if (cam == null) { if (Camera.main == null) return; cam = Camera.main.transform; }
        var rot = Quaternion.Euler(camPitch, camYaw, 0f);
        Vector3 focus = current != null ? current.transform.position + Vector3.up : Vector3.up;
        Vector3 desired = focus - (rot * Vector3.forward) * camDistance;
        cam.position = Vector3.Lerp(cam.position, desired, 1f - Mathf.Exp(-camFollow * Time.deltaTime));
        cam.rotation = rot;
    }

    void Spawn(CharacterRoster.Entry e)
    {
        if (e == null || e.prefab == null) return;
        if (current != null) Destroy(current);
        current = Instantiate(e.prefab, Vector3.zero, Quaternion.identity);
        currentBB = current.GetComponent<BakedBillboard8>();
        pickerOpen = false;
    }

    // ---- IMGUI picker (no prefab wiring needed for a sandbox) ----
    void OnGUI()
    {
        // Always-available toggle.
        if (GUI.Button(new Rect(10, 10, 130, 30), pickerOpen ? "Close list" : "Characters"))
            pickerOpen = !pickerOpen;
        if (current != null)
            GUI.Label(new Rect(150, 14, 400, 22), "WASD move · Shift run · pick another from the list");

        if (!pickerOpen) return;
        if (roster == null || roster.characters.Count == 0)
        {
            GUI.Box(new Rect(10, 50, 320, 60), "No roster / no characters baked.\nRun Tools ▸ unwritten ▸ Bake All Quaternius Characters.");
            return;
        }

        float w = 320, x = 10, y = 50, h = Mathf.Min(Screen.height - 70, roster.characters.Count * 64 + 10);
        GUI.Box(new Rect(x, y, w, h), "Pick a character");
        scroll = GUI.BeginScrollView(new Rect(x, y + 22, w, h - 26), scroll, new Rect(0, 0, w - 24, roster.characters.Count * 64));
        for (int i = 0; i < roster.characters.Count; i++)
        {
            var e = roster.characters[i];
            var row = new Rect(4, i * 64, w - 30, 60);
            if (GUI.Button(row, GUIContent.none)) Spawn(e);

            // thumbnail = first cell of the Idle_S strip
            if (e.previewStrip != null)
            {
                int cols = Mathf.Max(1, e.previewStrip.width / Mathf.Max(1, e.previewStrip.height));
                GUI.DrawTextureWithTexCoords(new Rect(row.x + 4, row.y + 2, 56, 56), e.previewStrip,
                    new Rect(0f, 0f, 1f / cols, 1f), true);
            }
            GUI.Label(new Rect(row.x + 70, row.y + 20, row.width - 74, 22), e.name);
        }
        GUI.EndScrollView();
    }
}
