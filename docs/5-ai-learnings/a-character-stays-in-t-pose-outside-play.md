# A character stays in its T-pose when you pose it outside Play

**Reach for this when** you pose a Unity humanoid character from an editor script (to take a picture, to measure
where a hand ends up mid-swing) and it stays with its arms straight out, or a bone you expected to move reports
the same position before and after.

## The rule

Outside Play mode, don't rely on the Animator to pose a character for you. Set the joints yourself: read the pose
with `HumanPoseHandler`, overwrite its muscle values with the clip's curves at the time you want, and write it
back. Then prove it took by measuring a bone's position before and after, before you trust any picture.

## Why

2026-10-08, checking new weapon models in a character's hand. Two normal ways of posing a character did nothing
inside a preview scene: `Animator.Play` followed by `Animator.Update(0)`, and a `PlayableGraph` playing the clip with
`Evaluate()`. The right hand bone stayed at exactly (1.202, 1.885, 0.040) through all three calls. The first round of
pictures came back with every character in a T-pose and had to be thrown away. Setting the muscles directly matched
all 95 of the clip's muscles on the first try, and the next pictures showed the real idle and attack poses.

## How to apply

1. `var hph = new HumanPoseHandler(avatar, rig.transform); var hp = new HumanPose(); hph.GetHumanPose(ref hp);`
2. For each `AnimationUtility.GetCurveBindings(clip)` binding whose type is `Animator`, turn its name into a muscle
   name (`LeftHand.` → `Left `, `RightHand.` → `Right `, `.` → space), look it up in `HumanTrait.MuscleName`, and set
   `hp.muscles[i] = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(time)`. Count the matches.
3. `hph.SetHumanPose(ref hp); hph.Dispose();`
4. Measure a hand or foot bone before and after. If it did not move, nothing above worked.

The full script, in use, is in the `import-weapon-models` skill.
