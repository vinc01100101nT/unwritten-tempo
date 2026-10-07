---
name: server-never-draws
description: "HARD LAW 2026-10-07 — the game server only sends data (state, events, which effect fired); it never draws and never sends \"draw X\" commands. Every picture is chosen and drawn by the client from its own data."
metadata:
  node_type: memory
  type: feedback
  originSessionId: d92be9ac-67f4-43c3-bdc8-db6e333ce473
  modified: 2026-10-06T19:43:32.870Z
---

The game server sends a snapshot of DATA and nothing else: positions, numbers, states, which event happened, which
authored effect fired. It never draws, never runs picture code, and never sends a message whose meaning is "render
this picture here" with picture settings in it. Each client reads the data and decides, from its OWN copy of the
skill / cue / asset data, what to draw.

**Why:** the user, 2026-10-07, after I first fixed a missing afterimage by having the server send an "afterimage here,
this long, this colour" message: "server must never, NEVER EVER... DRAW. IT IS CLIENT'S OBLIGATION TO DRAW. SERVER MUST
JUST SEND DATA". The mirror of RULE NOCLIENTMATH (client never computes a game number).

**How to apply:** when a picture is missing on clients because the code that triggers it runs on the server, do not
forward the picture — pass the data that identifies the event (e.g. `RelocationLook`: skill id + effect index; a
flight's thrown weapon + skill id + binding index; a tick's `TrueDamageCause`) and let each client read the look from
its own data. Sound counts as drawing (user, 2026-10-07).

**How it is enforced (sweep of 2026-10-07):** every drawing door (`Vfx`, `DamagePopup`, `Sfx`, `BattleNotice`,
`GroundDrop`) asks `GameServer.CaughtDrawing(door)`, which on the server writes ONE error per door to the server log
with the stack that reached it — an error line there is a bug to fix, never noise. Scene parts that only draw or sound
remove themselves on the server (`GameServer.RemovesPresentation(this)` as Awake's first line). Server-made creatures
never get a flat picture: `MobSpawner.Clone` makes them under an inactive parent and `MobBody.DropFlatPicture` strips
them before any Awake runs; the body is fitted from the art's authored numbers (`MobBody.FlatArt`). Skill sequences
run the same schedule on both sides; only `VfxSequenceRunner.Draws` machines draw. Creature death is
`DeathDissolve.Expire` (server: timing only) vs `Play` (screens). Yellow totals are summed on the server
(`StrikeTotals`) and sent as a finished number.

**Still on the server:** the idle player rig in the always-loaded scene (Character3DVisual / StanceController). It
serves only itself: a server-held person's fighter has no rig, so `StatusController.FlinchSeconds` on the server takes
its no-rig fallback (0.3 s), never this rig's hurt clip. (Claimed the opposite once, from reading the function without
checking who `isPlayer` is on a server — wrong.)

**A creature's melee swing lands at its contact moment**, exactly like a person's: begun in `MonsterAI3D` (intent
raised at the start), landed `ContactAfter(beat)` = beat × `CharacterSpec.hitTiming` later by `LandSwingIfDue`, which
re-asks alive / hostile / in reach. The server sends that moment (`NetMob.netActionConnectsAt`) and a flat creature's
bite is started early enough to slam in exactly then (`AttackLunge.Bite(aimedAt, connectsIn)`). Holding the NUMBER on
screen until the slam was tried first and rejected by the user (2026-10-07): the health had already gone — the
damage itself must land at the slam. See [[afterimage-player-model-and-bakemesh-scale]], [[the-game-is-always-online]].
