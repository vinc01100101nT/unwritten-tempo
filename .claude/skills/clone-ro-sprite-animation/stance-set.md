# 4c. A weapon's whole stance set — combat idle, running arms, put-away and take-out

Step 4c of the `clone-ro-sprite-animation` skill (`SKILL.md` beside this file), kept apart because only stance-set work needs it.

Agreed with the user on 2026-10-10: every weapon gets the katar's four pieces, videos first, wired in only after the
user's yes on each weapon. The Katar, Dagger, Axe and Greatsword were made one at a time; the other ten (Sword, Staff,
Wand, Spear, Warhammer, Scythe, Bow, Sniper, Shuriken, Fist) in one run at the user's word — all in the game since
2026-10-11 — and their tables are all
written by ONE script, `paths/stance_sets.py` (`python paths/stance_sets.py <folder>` rewrites their `keys/` tables
and writes one filled put-away blend script per weapon into the folder; it prints each handover frame and spin). The
Fist has no model and no put-away clip: an idle and running arms only. Trial tables go in a scratch folder, never in
`keys/`: `KEYS_DIR=<folder> python point-weapon-gen.py …` reads them from there.

1. **Combat idle** `keys/<weapon>_idle.json` → `Idle<Weapon>` (one key, `"loop"`, `"smooth"`; the shared breath is
   added by the generator). The
   reference is the class's RO **battle-ready** frames: on the Thief sheet (asset 141352) they are the first three
   frames of the top-right group (front-left view) and of the row under it (back view) — wide stance, knees bent,
   lean forward, weapon arm forward at chest height, the other arm low. The sprites carry no weapon. Find `re` with
   one dry run of four tables (edge up / down / out / in) and keep a 0° one.
2. **Running arms** `keys/<weapon>_carry.json` → `Carry<Weapon>`: the idle's first key twice (`f` 0 and 4),
   `"stepLift": false`.
3. **Put-away** `<Weapon>Sheathe`. **First test that the hand can reach the resting direction at all:** a static
   two-key table per candidate (wrist spot, elbow side, blade direction), built as a trial clip, then READ where the
   blade really points in it (the plan error alone does not say which way it is off). In this grip the blade is
   square to the forearm, on the thumb side, so a hand by the hip turns it forward, out or in easily and BACK only
   with the hand before the hip and the elbow back: the dagger's back-slant (blade `(0.24, -0.44, -0.87)`) came from
   wrist `[0.15, 0.40, 0.08]`, elbow side `[0, -0.4, -0.9]`; 30 other tries were 38–113° off. A shaft standing
   straight up the back is out of reach for any hand position (the doubled axe: 25–70° off); crossing inward to the
   other shoulder is reachable (an X).
   **Blend only between poses whose ARM JOINTS agree — compare them before blending** (2026-10-10, paid for on the
   sword, the sniper and the warhammer). Two still poses can each be reached exactly and still sit at opposite ends
   of the forearm's twist or the wrist's bend; blended joint by joint, the weapon then swings through the head or the
   ground half way (the sword: 27% of it in the head, its tip 20% of body height under the ground; the rifle: 56%
   under). Read each pose's arm muscles with `arm-joints.cs` (shoulder, arm down-up / front-back / twist, forearm stretch /
   twist, hand down-up / in-out) and, where two neighbours disagree, scan every ROLL of the weapon about its own
   length (`re` turned in 12 steps — a sword's or a rifle's roll does not matter to the eye) and keep the roll whose
   joints match the neighbour's. If no roll matches, the two poses are not joined by any motion of that hold: pick
   another resting place. A keyed table does not rescue it — solved pose by pose it walks into a dead end (the sword's
   slide left its line by 49 degrees). After EVERY blend, play the clip pose by pose with the item's model and read
   the most inside the body, the lowest point and the far end's biggest move in one pose (`check-put-away.cs`).
   **A weapon's reach below the fist decides its path:** the Warhammer's handle (0.64 of body height below the fist)
   cannot stand upright behind the hip or be carried round low (ground, legs) — it goes up beside the shoulder and
   over; a weapon as long as the body is tall (the bow, the rifle) is swung round the OUTSIDE of the right side,
   level, before it goes behind — swung straight there, or over the shoulder, it went through the hips or 30–57% of
   body height into the ground. Widest and cleanest is the arm straight out to the side first, as the Staff's and
   Wand's put-aways do (the user's fix for the bow, 2026-10-11: nothing of the bow enters the body that way). Each middle pose is picked from a scan by `arm-joints.cs`: the one whose joints sit
   between its two neighbours'.
   **A hand cannot press a weapon onto the back — it is between them** (2026-10-11, the user: "dont float it away").
   Scans of 600+ holds found none that leaves a spear, scythe or bow touching the back while the fist still grips it;
   with the shaft on the palm side the weapon goes INTO the body, and the chibi head sticks out 0.1 of body height
   further back than the torso, so a pole behind the head stays ~11% off the middle of the back. A table's `"hug":
   true` stops the build pushing a resting pose off the body (its capsules are wider than the real back: 0.10–0.125 of
   body height against a measured 0.065). Where the hand still cannot get closer, the resting place is the hand's
   place moved straight onto the back, and the weapon makes that small move at the handover — say how far.
   **A weapon moved to the other hand** (the bow, 2026-10-11, the user: "make the bow stance right handed"): its grip
   is mirrored across the hand's x (as a pair weapon's second copy) and turned half round its own length so its scale
   stays positive — for a thin model the same picture as a mirror; its attack clip is played mirrored by the stance
   entry's `mirrored` setting (`render-stance.cs` draws `__MIRRORCLIPS__` the same way); its key tables are the old
   ones mirrored (x negated, the two arms' keys swapped, `yaw`/`look`/`twist` negated).
   **What the game does for a stance set (2026-10-11):** a row's `putAwayMovesUpperBody` plays its put-away on the
   "DrawUpper" layer (arms, body and head) instead of arms only — needed by any table using `"twist"`;
   `spinAboutMiddle` makes the hand slide the weapon to its middle while it spins (`WeaponSocket.MiddleSlide`, which
   `render-stance.cs` and `check-put-away.cs` call too); `handoverVfx` plays an effect where the weapon leaves the
   hand and where it lands at the handover.
   `paths/pose_chain_blend.cs` is the general blend (stops = pose clip, time in it, frame, stops-there; one smooth
   curve between stops; it gives the clip its OWN length — copying another clip's settings whole left a 34-frame
   put-away with a 44-frame stop time). **A put-away is built as long as the game plays it** (0.2 s + 0.006 s per
   point of weight: weight 90 = 44 frames at 60 per second, weight 60 = 34), so its video shows the game's speed.
   **A turn of the torso that must show in game** is a key's `"twist"` (degrees to the character's right, shared by
   the spine joints over still hips; pair it with `"look"` to keep the face forward) — `yaw` turns the hips, which a
   put-away never plays. The sword's put-away uses it, so its stance row plays it on the "DrawUpper" layer
   (`putAwayMovesUpperBody`).
   **Then build it by BLENDING still poses joint by joint, not with a keyed hand path**, whenever the blade must turn
   a long way (front to back, up to across): the key-table solver turns the wrist one pose at a time and loses it —
   tip errors up to 130° and 130 wrist reversals on the axe. A blend of poses the body can really take cannot flip a
   wrist. `paths/axe_sheathe_blend.cs` runs one smooth curve through four poses (idle → hands out beside the hips →
   just behind them → the handover pose) and on to the hanging arm; `paths/dagger_sheathe_blend.cs` appends a keyed
   move (the arm held out for the dagger's spin) to a pose blend. Add a middle pose where the weapon sweeps through the body: the axes still
   cross the hips for 3 frames just before their handover. A short move that keeps the blade's direction (the katar)
   can still use a keyed hand path every 2 frames.
   **A weapon turning over in the hand is never keyed** — the wrist cannot turn a blade all the way round (the
   dagger's keyed roll was 84° off when the blade should point down). The clip only HOLDS the arm; the weapon's
   stance library row (`spinStartFrame`, `spinEndFrame` of the put-away clip, `spinTurns`, `spinLift`) makes the game
   lift the main copy off the fist and turn it about the hand bone's x through the fist point. So the held pose
   decides the plane of the spin: measure the hand's x in the built clip (palm up = a level spin, the best seen from
   the game camera) and give enough lift for the blade to pass over the forearm (dagger: 0.25 hand units). The
   take-out turns it back by itself. `render-stance.cs` and `check-clip-fit.cs` draw the spin from that row.
   **A weapon far longer than the arm cannot spin about the fist** (the Spear and the Scythe, 2026-10-10): in all 36
   holds measured its far end went through the head or the ground. It turns about its own MIDDLE — slid through the
   fist while it turns — in an upright wheel beside the raised right arm (`*PutAwayPoseWheel`, lift 0.3: nothing
   inside the body, lowest point 3.6% / 5.1% of body height above the ground). The stance row's `spinAboutMiddle`
   tells the game's hand to do it; `render-stance.cs` (from the row, or `__SPINROWS__` for a row not saved yet) and
   `check-put-away.cs` call the game's own `WeaponSocket.MiddleSlide`.
   **A put-away and a take-out play on the ARMS ONLY in game** (the Draw layer's mask), over the shared idle or the
   run, and the weapon's own standing idle fades out the moment the move starts. So build every pose of them on the
   STANDING body (lean 0, hips 0) — a pose solved on a leaning idle puts the weapon somewhere else in game than in
   the video. A leaning idle's hold is carried onto the standing body by turning its arm, blade, edge and elbow
   directions back by the idle's lean and turn (`keys/greatsword_hold_upright.json`). The body does not move with
   the arms, so a big flourish with a heavy weapon looks static (the user, on the greatsword's figure-8: "only arms
   is moving while the great sword is too heavy") — keep put-aways simple. The game scrubs the whole clip over the
   weapon's draw time (0.2 s + 0.006 s per point of weight, the same for taking out and putting away), so a clip
   longer than that plays faster — say the in-game seconds in the report; a weapon that needs longer gets more
   WEIGHT (the user's ruling: the greatsword weighs 133 for 1 s). Measure a move's flow with `tip-path.cs` (the far
   end's travel per pose, where the path changes the way it curves, arm joint reversals) — a pause shows as the
   travel dropping toward 0.
4. **Take-out** is the put-away clip played backwards, curve by curve (`reverse-clip.cs`) — never a reversed key
   table: built forward from reversed keys, the solver turned the blade the other way round and put it in the thigh.
   Its handover frame is the clip's length minus the put-away one.
5. **The resting place** is wherever the weapon IS at the put-away's handover frame, relative to the hips:
   `measure-handover.cs` prints it, so the weapon does not jump when the hand lets go. A pair weapon's other copy
   gets its own (`PAIR_HANDOVER60` → `stowPairPos` / `stowPairEul`, mirrored the way the game's hand mounts it).
   It is written to the weapon's grip only after the user approves the videos (the weapon skill's step 5b), with the
   binding's clips and frames.
   **A weapon no hand carries to its place** (the Staff and the Wand: the arm rises to the right and the weapon moves
   to the back by itself, with the game's Blink Step effect when wired) gets a DESIGNED place from `place-on-body.cs`
   (the model's middle at a point of the body, its length along a direction, its thin side away from the body).
   **A resting place designed first (the katar's X on the lower back) is the other way round: the hand must deliver
   to it.** Measure the jump at each hand's handover — how far the model moves and how far it turns between its
   in-hand pose and the saved place — and fix the HAND, never the place: moving the place to a hand that arrives
   wrong leaves the weapon resting wrong. The right katar jumped 12.2% of body height and 52° (2026-10-10), nearly
   all of it a roll about the blade: its keys carried no edge (`re`), so the solver chose one. With the edge and
   blade set to the resting directions at keys 11–15 and the wrist moved 85% of the way to where the resting place
   needs it, it is 0.8% and 1°; at 92% the wrist ran out of bend and the miss came back at 5.9% and 12°. The left
   katar's 1.6% and 8° was left as it was (every variant tried was worse). The resting directions are read in the
   CLIP's pose at that frame, never in the standing idle: the right katar's saved place points `(-0.77, -0.63, -0.09)`
   at its handover frame and `(-0.82, -0.57, -0.01)` standing in Idle2 (that one measured on the Knight body).
   The saved `KatarSheathe` is a splice, not its table built whole — read the note in `keys/katar_sheathe.json`.
6. **Videos** (`render-stance.cs` + `make-stance-videos.py`): `<weapon>_idle`, `<weapon>_run` (the run with the
   carry arms laid over it, in place), `<weapon>_sheathe`, `<weapon>_unsheathe` (the weapon moved onto the hips at
   the handover; a pair's other copy at its own handover, from `pairStowAt` and `STOW_PAIR_*`). A job named
   `<weapon>_<kind>` lands in `D:\kimodo\out\ro_refs\videos\<weapon>\<weapon>_<kind>.mp4`; an imported attack with no
   key table (the bow's) is rendered here too, as `<weapon>_attack`. Pull frames back out of each video and compare them before sending. The script prints each
   elbow's bend from the very poses it draws; quote THAT number, never one measured on the clip alone. (2026-10-10:
   its arm filter matched "Arm" but not "Forearm", so the run videos kept the run's own 90° elbow under a carry pose
   measured straight on the clip; the user saw the bend twice before the filter was found.)
