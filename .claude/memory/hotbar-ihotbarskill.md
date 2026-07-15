# HUD hotbar = `IHotbarSkill` + `HotbarPanel` (built 2026-06-18)

The on-screen action bar is **data-driven**: any player active that implements **`IHotbarSkill`**
(`unity/Assets/Scripts/IHotbarSkill.cs`) auto-appears on the bar, ordered by `SlotOrder`. No hotbar edits
needed to add a skill.

## To add a new active to the bar
1. Make the skill component implement `IHotbarSkill`: `SlotOrder`, `ShortLabel`, `KeyLabel`, `Icon`
   (Sprite, nullable → text fallback), `ManaCost`, `Affordable`, `CooldownRemaining`, `CooldownFraction01`
   (1=just used→0=ready), `Tint`, and `bool Activate()`.
2. The keypress path and the hotbar **click** both call `Activate()` — keep all target/MP/cooldown rules
   inside it so both paths behave identically. Track a `float lastDuration` (the cooldown actually applied)
   so the radial sweep can compute `remaining/lastDuration`.
3. Assign `icon` at bake in `Build25DSpike.BuildPlayer` via `LoadSkillIcon("Spell/Name")`, or drag one onto
   the component's `icon` field in the Inspector. Icons live in `Assets/Art/NinjaAdventure/Ui/Skill Icon/`
   (Spell/Meteo/Job/Items folders; 24×24, **Sprite/Multiple** → `LoadSkillIcon` forces Single).

## How it renders
`HotbarPanel.cs` (runtime, on GameUICanvas) finds the `Player3DController`, collects its `IHotbarSkill`s, and
decorates the **6 empty `HUD_Hotbar` cells** baked by `UIShellBuilder.BuildHud` (first N = skills; extras stay
empty). Each slot = icon/label + hotkey (TL) + MP cost (BR, reddens when broke) + radial cooldown sweep
(1×1 white sprite, Filled/Radial360) + countdown number + a Button → `Activate()`. Builds once, then per-frame
only updates fillAmount + the second-counter (alloc-light).

## Re-bakes
- **Build UI Shell** — REQUIRED to get `HotbarPanel` onto `GameUICanvas.prefab` (it's a UI component).
- **Setup Systems Scene** — only for the ICONS (re-runs `BuildPlayer`); without it the bar works but shows
  text labels.

Current bar: **Bash(1)·Charge(2)·Guard(3)·Warcry(4)·Bolt(Q)** (ordered by `SlotOrder` 0/1/2/3/4).
Bash/Charge/Guard/Warcry/Caster all implement `IHotbarSkill`. Guard = defensive stance (`Player3DGuard` +
`GuardShieldFX` + `Character.BeginGuard`). Warcry = point-blank AoE (`Player3DWarcry` damages + knocks back
via `MonsterAI3D.Knockback`→`PathAgent3D.Knockback`, a CC-driven decelerating shove). Was the M1 "make the
kit legible" item.
