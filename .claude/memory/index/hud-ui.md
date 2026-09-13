# HUD / UI

- [UI click-shield](../ui-click-shield.md) — `UiKit.ClickShield(rootRect)` transparent raycast; once per NEW root.
- [In-game DevSettings panel (P key)](../dev-settings-panel.md) — add in `DevSettings.cs` BuildKnobs(); checks `DevSettings.Blocking`.
- [HUD progression windows + Ornament Kit v1](../hud-progression-windows.md) — `ProgressionWindows.cs` on `UiKit`; `Ornament.cs`.
- [HUD hotbar = IHotbarSkill + HotbarPanel](../hotbar-ihotbarskill.md) — implement `IHotbarSkill` → auto on bar; re-bake Build UI Shell.
- [HUD build process (ONE way)](../hud-build-process.md) — GameUICanvas Resources PREFAB; wire into UIShellBuilder + Build UI Shell.
- [Window open/close motion choke](../ui-window-motion-choke.md) — `UiAnim.Toggle/Open/Close/IsOpen`, never SetActive a panel root; `UiButtonFx` in `UiKit.Btn`.
- [HUD NRE from stale baked scene (legacy Text vs TMP)](../hud-stale-baked-scene-nre.md) — GoldCounter/CalendarWeatherHUD `label` null; scene drifted from builder; fix = rebuild HUD shell via MCP, don't patch scripts.
- [Wardrobe dye Voronoi match](../wardrobe-dye-voronoi-match.md) — parts did nothing / one swatch changed many; fix = nearest-part shader match fed ALL parts, not first-within-tol dyed only.
- [Scene-bloat prevention: runtime roots tagged DontSaveInEditor](../damagepopup-pooled-clone-savable-gap.md) — project-wide sweep 2026-07-19; MEASURED: children of a flagged parent are NOT saved (root tag suffices); `SceneSaveBloatGuard` warns on saved "(Clone)" junk.
- [Chat crosses the wire](../chat-crosses-the-wire.md) — a said line and a skill call are sent to the server and echoed to everyone who can see the speaker; nothing is drawn at the keyboard that typed it.
- [Minimap runs a live second camera](../minimap-runs-a-live-second-camera.md) — no map is baked, so every map renders the world twice per client; capped at 20 Hz, and the user ruled out baking — do not offer it.
- [Bag is a TEXT LIST, not an icon grid](../bag-is-a-text-list.md) — `InventoryList.cs`; each row carries an `ItemSlot`+`SlotDragHandler` so hover/preview/click/both drags work. Sorting is a LENS; the store owns the order. Dropping goes through `InventoryStore.Drop`.
