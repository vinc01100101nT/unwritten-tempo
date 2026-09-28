---
name: built-game-crashes-on-quit-engine-diagnostics
description: The built Mac client segfaults as it finishes closing, inside Unity's own teardown; cause is engine diagnostics + Addressables bundles, not our code.
metadata:
  type: project
---

**Symptom:** closing the built client (Mac, Unity 6000.4.11f1) ends in a macOS crash report — EXC_BAD_ACCESS at
address 0 on the main thread, under an NSTimer. Player.log looks orderly up to "CodeReloadManager destroyed".

**Root cause (measured 2026-09-28 by symbolizing against Unity's own UnityPlayer.dSYM for this version, in
`PlaybackEngines/MacStandaloneSupport/Variations/macos_arm64_player_nondevelopment_mono/`):**
`AssetBundleManager::~AssetBundleManager` → `AssetBundleAnalytics::UnregisterAnalyticsEvents` reads through
`GetIInsights()`, which is already null that late in `RuntimeCleanup`. The event was registered because
**engine diagnostics was on** (`InsightsSettings.m_EngineDiagnosticsEnabled: 1` in
`ProjectSettings/UnityConnectSettings.asset`) and the game loads **Addressables bundles** every run (mobs,
characters). It runs after every C# quit hook, so no change to our scripts can reach it.

**Fix applied 2026-09-28, not yet confirmed by a close:** engine diagnostics switched off (set through the editor,
one-line diff). If it still crashes, check the new report's top frame; next test is quitting right after launch.

No earlier fix exists anywhere in git, memory or handoffs, despite the user remembering one. Related: [[unity-mcp-bridge]].
