using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The ONE recipe for a townsperson, used by the machine that owns the world and by every machine that only
/// watches it. There used to be one copy of it, on the director that populates a town, because every machine
/// populated its own — so every player walked through a different town, with different people standing in
/// different places saying different things. The world is one world now, so the people in it are built one way
/// and the machine that owns them says where they stand.
///
/// <para><b>The picture is the only optional part.</b> A machine with no screen still needs a townsperson's
/// collision capsule, its routing footprint, its height and its walking speed — those are facts about the
/// person, and a server that skipped them would run somebody different from the one on screen. Only the rigged
/// model is left out, exactly as the live-3D creatures already leave it out.</para>
/// </summary>
public static class Townsperson
{
    /// <summary>Build <paramref name="personName"/> at <paramref name="at"/>, in <paramref name="map"/>, wearing
    /// <paramref name="modelName"/>. Hand over a <paramref name="model"/> on a machine that draws people, and null on
    /// one that does not.
    ///
    /// <para>The model goes on FIRST when there is one: the animation driver and the brain added below both
    /// resolve the body's pieces as they wake, and a body handed over after them is one neither ever finds.</para></summary>
    public static GameObject Build(string modelName, string personName, Vector3 at, Scene map, GameObject model)
    {
        var spec = CharacterSpec.Active;

        // The MOVER must live on an UNSCALED root: a CharacterController silently refuses to translate when its
        // transform is scaled down to character size (~0.1) — MEASURED, its move came out 0 at scale 0.098 but works
        // at scale 1. So (the same split the player uses) the unscaled root carries the controller and the brain,
        // and the scaled visual model rides it as a child.
        // Named for the person, so the server's log and the hierarchy say who it is.
        var root = new GameObject("Townsfolk_" + (string.IsNullOrEmpty(personName) ? modelName : personName));
        // A runtime spawn — never bake a stray copy into a saved scene.
        root.hideFlags = HideFlags.DontSaveInEditor;
        // The town that made them is the town they live in — never whichever map was loaded last.
        MapManager.PutInMap(root, map);
        root.transform.position = at;

        if (model != null)
        {
            // The scaled model child, its animator, the pose sink and the stance choke point — built by the SHARED
            // character-body recipe, the same one the player and the live-3D creatures use, so there is one
            // definition of "a character body" rather than one per unit type.
            Character3DBody.Build(root, model, spec.animatorController, spec.bodyHeight);
        }
        else
        {
            // HOW TALL THIS PERSON IS, stated rather than measured off a model this machine does not have. Anything
            // that measures a unit by its body reads this, and the machine with no screen is the one that decides
            // where a blow lands and how far anybody can reach.
            UnitBody.Set(root, spec.bodyHeight);
        }

        // The shared mover on the UNSCALED root (it adds a CharacterController itself), then the ONE character rig —
        // the same capsule and routing footprint the player and the live-3D creatures get.
        var agent = root.AddComponent<PathAgent3D>();
        Character3DBody.ApplyRig(root, agent);
        agent.moveSpeed = spec.WalkGearSpeed;
        // Spread apart from other units WHILE WALKING, but townsfolk chase nobody — no avoidIgnore target.
        agent.avoidMask = UnitCrowd.Mask;
        // Don't drift while paused: the player is on the Crowd layer, so idle separation would shove a standing
        // townsperson away when the player steps beside them (sliding in the Idle pose). Hold ground when halted.
        agent.separateWhenIdle = false;
        // EVERYBODY WALKS THROUGH THEM, on every machine. This is a fact about a townsperson's BODY, not about who
        // decides where it walks — left with the brain's set-up, a copy the server owns would never be registered
        // and would stand there as a solid pillar the player collides with and climbs onto.
        UnitCrowd.Register(root);

        // The animation driver and the wander brain, both on the root, added last so they resolve their siblings.
        // Then the two are introduced to each other EXPLICITLY: a component resolves its siblings the instant it is
        // attached, so whichever goes on first cannot find the other, and the pair only works in one direction on
        // its own. The brain finds the driver because it goes on second; the driver is told about the brain here.
        var driver = root.AddComponent<TownNPC3DVisual>();
        var brain = root.AddComponent<TownNPC>();
        driver.BindBrain(brain);
        return root;
    }
}
