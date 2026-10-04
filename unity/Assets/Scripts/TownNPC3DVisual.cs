using UnityEngine;

/// <summary>
/// The townsfolk animation driver: a live-3D character that only ever WALKS or STANDS. It adds no VOCABULARY to
/// <see cref="Agent3DVisual"/> — walking, facing and foot-grounding are the shared behaviour every agent-driven
/// character performs identically, and a wandering townsperson has nothing further to do (no attack/jump/dash/
/// pickup/hurt). The role that DOES add one is the 3D shadow mob (<see cref="MobShadow3DVisual"/>).
///
/// <para>What it adds is the one thing only a townsperson knows: WHICH WAY TO LOOK when they are not walking. A copy
/// of somebody the server owns looks whichever way the server turned them, and somebody in conversation looks at
/// whoever is talking to them. That is ALL it adds — every rule that turns a description into a pose and a direction
/// is the shared <see cref="CharacterBrain"/>, the same one the person at the keyboard runs.</para>
///
/// <para>Kept as its own type rather than collapsed into the base because <see cref="TownNPC"/> resolves it by type
/// to freeze it when far, and because "townsfolk visual" is the thing an author adds to a townsperson. Locomotion
/// lives in the base — do NOT re-implement it here.</para>
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PathAgent3D))]
public class TownNPC3DVisual : Agent3DVisual
{
    TownNPC brain;

    /// <summary>Hand this driver the brain it belongs to. HANDED OVER rather than looked up, because the two are
    /// attached one after the other and a component resolves its siblings the instant it is attached — so this
    /// one went looking a step before the brain existed and held nothing for the rest of its life. It could not
    /// then find "look at whoever is speaking", so a townsperson stopped dead when you talked to them and never
    /// turned round; and the copy on every watching screen faced along its own catch-up glide instead of the way
    /// the server had turned it. The other order is not available: the brain resolves THIS in its own set-up.</summary>
    public void BindBrain(TownNPC townsperson) => brain = townsperson;

    /// <summary>Which way this townsperson looks this frame. Three answers, in order of authority.
    ///
    /// <para>A COPY the server owns keeps the facing it was placed at — the root is turned to the angle the server
    /// sent, and reading its own travel here would fight that with the direction of the catch-up glide.</para>
    ///
    /// <para>SOMEBODY IN CONVERSATION looks at whoever is speaking to them, and goes on looking there while they
    /// stand still — which the base answer cannot do, because it only turns a body that is walking.</para>
    ///
    /// <para>SOMEBODY WHO STANDS faces the way they were placed while nobody talks to them.</para>
    ///
    /// <para>OTHERWISE the shared answer: the line they just walked.</para></summary>
    protected override void DescribeSelf(ref CharacterBrain.Intent it)
    {
        // ONE GEAR: a townsperson strolls and never breaks into a run, so its gear is a fact rather than something
        // to work out from its speed.
        it.walkingSlow = true;
        it.gearFromSpeed = false;

        // A COPY the server owns keeps the facing it was placed at — the root was turned to the angle the server
        // sent, and reading its own travel here would fight that with the direction of the catch-up glide.
        if (brain != null && !brain.ThinksForItself) { it.facing = transform.forward; return; }

        // SOMEBODY IN CONVERSATION looks at whoever is speaking to them, and goes on looking there while they stand
        // still — which the travel line cannot do, because it only turns a body that is walking.
        var speaker = brain != null ? brain.TalkTarget : null;
        if (speaker == null)
        {
            // SOMEBODY WHO STANDS looks the way they were placed — a shopkeeper faces the square, not their door —
            // once nobody is talking to them and they are not walking back to their spot.
            if (brain != null && brain.Stands && (agent == null || !agent.Moving)) it.facing = brain.StandFacing;
            return;
        }

        Vector3 toSpeaker = speaker.position - transform.position;
        toSpeaker.y = 0f;
        if (toSpeaker.sqrMagnitude > 1e-4f) it.facing = toSpeaker;
    }
}
