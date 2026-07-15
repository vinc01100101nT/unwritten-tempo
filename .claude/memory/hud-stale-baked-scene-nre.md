# HUD NRE from a STALE baked scene (legacy Text) vs updated TMP code

## Symptom (2026-07-06)
Play-mode NREs every frame:
- `GoldCounter.Refresh` — `label.text` NRE (GoldCounter.cs:24)
- `CalendarWeatherHUD.Update` — `label.text` NRE (CalendarWeatherHUD.cs:48)
Both cache `label = GetComponent<TMP_Text>()` in Awake under `[RequireComponent(typeof(TMP_Text))]`.
Health/mana labels were also silently blank (`CharacterUIBinder` uses `?.GetComponent<TMP_Text>()`, so
null-safe → no crash, just no text).

## Root cause
The HUD baked into `Systems.unity` was built by an OLDER `UIShellBuilder` that made legacy
`UnityEngine.UI.Text` (+ a `Shadow`). The CURRENT `UIShellBuilder.NewText` returns `TextMeshProUGUI`, and the
whole codebase is TMP. So the SAVED SCENE drifted from the CODE: script requires `TMP_Text`,
`GetComponent<TMP_Text>()` on a legacy-`Text` object returns null → NRE.
**Diagnostic that nailed it:** dumped the offending GameObjects' component lists via `execute_code`
(`GetComponents<Component>()`) → `[RectTransform CanvasRenderer Text Shadow GoldCounter]` — legacy `Text`, no TMP.

## Fix
Do NOT patch the scripts — they're correct. REBUILD the HUD shell (it's baked author-time; the builder is the
source of truth). Ran via MCP `execute_code`: set the Systems scene active (so the new canvas lands there),
`UIShellBuilder.Build(EditorPrefs theme, scale)`, then `MarkSceneDirty`+`SaveScene`. Verified `HUD_Gold` now
carries a `TMP_Text`. Used the user's saved theme pref (`ThemeMetal3`), not the Wood default.

## Gotchas / lessons
- A baked/generated asset (HUD canvas, prefab, effect library) can DRIFT from the code that generates it — a
  builder change doesn't retro-fix already-saved scenes. When a "this should be TMP" mismatch NREs, suspect a
  stale baked scene, not the script.
- `GameObject.Find` + `new GameObject` in a builder land in the ACTIVE scene — set the target scene active
  BEFORE rebuilding or the canvas spawns in the wrong scene.
- `UIShellBuilder.Build` is a full nuke+rebuild of `GameUICanvas` (destructive) and only shows a modal on
  missing-theme; with a theme pref set it runs dialog-free, safe over MCP.
- These `Update`/event NREs only fire in Play mode → rebuild needs Edit mode first (stop Play via
  `EditorApplication.isPlaying = false`).
