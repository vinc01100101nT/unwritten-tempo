---
name: persistent-systems-layer
description: Global stuff (HUD/cursor/player/camera/data) lives in a persistent Systems.unity loaded additively under swappable maps — NO DontDestroyOnLoad
metadata: 
  node_type: memory
  type: project
  originSessionId: e226cc1a-d6ed-46e7-967f-4bda6658acd8
---

The game uses a **persistent Systems scene + additive map loading** (the "app-shell / `layout.tsx` wrapping `<Outlet/>`" pattern), so there is **no `DontDestroyOnLoad` anywhere**. Built 2026-06-14, replacing the earlier short-lived DDOL-singleton approach.

**`Systems.unity`** is loaded once and **never unloaded**. It holds everything global: `GameUICanvas` (HUD), `GameSystems` (the `GameCursor` host), the `Player` (avatar + movement + commander + attacker + pathing), `Character` (data), `DialogueCanvas` (`DialogueUI`) + `InteractPromptCanvas` (`InteractPrompt`) — both global singletons, one persistent **Main Camera** (`CameraFollow2D`), `EventSystem`, and **`GameRoot`** (the boot component). Maps (`Field`, `Town`) hold ONLY tilemaps + `SpawnPoint`s + `Portal`s + NPCs/monsters — no camera/player/HUD/dialogue.

**2026-06-16: dialogue + interact prompt moved INTO Systems.** They used to be baked per-map (duplicated in `Field` + `Town`), so generator-made towns got neither and two live `.Instance` singletons could collide during additive swaps. Now `Build Dialogue UI`/`Build Interact Prompt` save `Resources/DialogueCanvas.prefab` + `Resources/InteractPromptCanvas.prefab` (and leave NO scene instance), `SystemsSceneBuilder` instantiates both into Systems (now required prefabs), and `Tools ▸ unwritten ▸ Remove Per-Map Dialogue + Prompt` (`UICentralizer.cs`) strips the old baked copies from loaded maps (undoable). Migration order: Build Dialogue UI → Build Interact Prompt → Setup Systems Scene → Remove Per-Map Dialogue + Prompt per map.

Flow: `GameRoot.Start` → `MapManager.LoadMap(firstMap)` loads a map **additively** under Systems. `Portal` → `MapManager.Travel(target, spawnId)` loads the target additively, makes it active, unloads the old map (never Systems). `MapManager.OnSceneLoaded` makes the map active + strips any camera/audiolistener/eventsystem a map still carries (so only the Systems ones run) + binds the persistent camera's bounds to the map's ground tilemap. `PersistentPlayer.OnSceneLoaded` teleports the avatar to the `SpawnPoint` and re-aims the camera.

Key files: `MapManager.cs`, `GameRoot.cs`, `PersistentPlayer/PersistentUI/GameSystems/Character.cs` (now plain singletons, no DDOL/auto-spawn), `Bootstrap.cs` (stands down when `GameSystems.Instance != null`), `DepthSortRuntime` (bakes props into the tilemap's own scene).

**URP one-global-light gotcha:** URP's 2D renderer allows only ONE global light. Per-map `Global Light 2D` objects caused "More than one global light on layer Default" during additive portal swaps (two maps briefly overlap). Fixed by moving the single global light into `Systems.unity` (`Tools ▸ unwritten ▸ Fix 2D Lighting`, in `LightingFix.cs`; `SystemsSceneBuilder` also creates it on build) and stripping per-map globals. Light2D config is set via SerializedObject (`m_LightType` = `(int)Light2D.LightType.Global`, `m_ApplyToSortingLayers` = all) so it's URP-version-robust. Non-global lights (torches) stay per-map.

**Setup is one-click and ORDER MATTERS:** Build Player → **Setup Global Systems** (makes `Resources/Player.prefab` + `GameSystems.prefab`) → **Setup Systems Scene** (`SystemsSceneBuilder.cs`: assembles `Systems.unity`, sets it Build index 0 + `EditorSceneManager.playModeStartScene`). In-editor, pressing Play in any map boots Systems first then loads that map (the open map is remembered by `SystemsPlayMode.cs` via SessionState `unwritten.bootMap`). Related: [[project-layout]], [[editor-tools-menu]], [[ysort-depth]].
