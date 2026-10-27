---
name: seamless-world-ground-on-server
description: In the seamless world the server loads ground only near players and awake spawn areas, so anything placed at map load stands on nothing; and the old zone world is gone (2026-10-28) — what that changed
metadata:
  type: project
---

**Ground on the server.** The seamless world's server loads squares only around players and awake spawn areas. Anything built when the map loads stands on no ground: Lastline's 16 townsfolk were built at 21:25:08, the first square arrived at 21:26:58, and no player ever saw them. Fix: `SeamlessWorld.HoldGround(rect, then)` keeps a rectangle's ground loaded for good and runs `then` once it is in; townsfolk use it. Any new thing placed in the world at map load must do the same.

**The old zone world is deleted (2026-10-28, the user's call).** 77 maps, their ground, minimaps and painter files are gone; the build ships only Systems + world. The in-game atlas and the old layout file are EMPTY, so: the map window opens the map you are in (no world view), Keeper trips list nothing, `check-map-ids` only proves the seamless world, the start map and the music table. The world builder reads Lastline from `1_Authored/Prefabs/Towns/Lastline.prefab`. The 18 Summoners are kept in the townsfolk list, parked on map names that no longer exist, until their places in the world are built; all 230 creatures are kept, their old spawn areas deleted.

Still open (2026-10-28): Keepers are looked up per MAP, which breaks once a second town is in the world; teleport price is by doors, which the world does not have.

Related: [[world-builder]], [[world-look-and-time-of-day]].
