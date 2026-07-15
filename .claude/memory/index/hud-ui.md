# HUD / UI

- [UI click-shield](../ui-click-shield.md) — `UiKit.ClickShield(rootRect)` transparent raycast; once per NEW root.
- [In-game DevSettings panel (P key)](../dev-settings-panel.md) — add in `DevSettings.cs` BuildKnobs(); checks `DevSettings.Blocking`.
- [HUD progression windows + Ornament Kit v1](../hud-progression-windows.md) — `ProgressionWindows.cs` on `UiKit`; `Ornament.cs`.
- [HUD hotbar = IHotbarSkill + HotbarPanel](../hotbar-ihotbarskill.md) — implement `IHotbarSkill` → auto on bar; re-bake Build UI Shell.
- [HUD build process (ONE way)](../hud-build-process.md) — GameUICanvas Resources PREFAB; wire into UIShellBuilder + Build UI Shell.
- [Window open/close motion choke](../ui-window-motion-choke.md) — `UiAnim.Toggle/Open/Close/IsOpen`, never SetActive a panel root; `UiButtonFx` in `UiKit.Btn`.
- [HUD NRE from stale baked scene (legacy Text vs TMP)](../hud-stale-baked-scene-nre.md) — GoldCounter/CalendarWeatherHUD `label` null; scene drifted from builder; fix = rebuild HUD shell via MCP, don't patch scripts.
- [Wardrobe dye Voronoi match](../wardrobe-dye-voronoi-match.md) — parts did nothing / one swatch changed many; fix = nearest-part shader match fed ALL parts, not first-within-tol dyed only.
