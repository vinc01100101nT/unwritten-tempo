# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- **Effort**: model ALWAYS Opus 4.8 (never assess/print model). Assess EFFORT only; auto-GO Low/Medium; PAUSE >Medium until user GO. Tag `**EFFORT: X**`.
- **Checkpoint**: OVERWRITE this file (never append); prune landed; keep short.

## 🎨 DESIGN (parallel, no code): "unwritten" brand sheet + StatPanel stat icons
GPT generates the logo/sheet; I give feedback + draft GPT prompts. **All decisions saved in Skill System doc §15** (`q17as16xh2gt1g058kemazjar588tvc8`): ring nodes = the 10 derived stats (logo == §5b ten-stat set); color by 3 archetype families (🔴Might Def/HP/Atk · 🔵Magic MAtk/MP/CastSpd · 🟢Finesse Accuracy/Evasion/Critical/AtkSpd; gold=neutral path); icons silhouette-first; fixing MATK vs Critical sparkle clash. ⏭️ User pastes GPT's next iteration → check crit icon + colors landed. Presentation only, no code/math impact.

## 🎯 ACTIVE: HUD window trio (Inventory/Equipment/Character) — Unity uGUI port
GPT⇄Claude relay; spec+visual LOCKED. Doc `q172d54qewwwxxr2aq5s29wk0x898958` (decisions only, no transcript). Building in numbered steps, user GO-gates each; user compiles (no local Play loop) + pastes errors.

**Step 1 DONE (data + pure derive):** `Rarity.cs` (enum + `Rarities` gold-Legendary style table), `Card.cs` (SO), `Item.cs` extended (rarity/category/cardSlots/weight/zeny + per-stat & crit/aspd bonuses), `EquipmentInstance.cs` (+`EquipBonuses` alloc-free aggregate), `DerivedSnapshot.cs`. **`Character.cs` refactored**: `equipped` now `Dictionary<slot,EquipmentInstance>`; `P()` = allocated + gear (effective stat → gear bonuses flow into ALL derived/combat stats); flat bonuses cached in `equipBonuses` (perf win); crit/aspd take equip %; keystone `DeriveWith(slot,hypo)→DerivedSnapshot` (swaps cached bonus, runs same getters, restores) + `CurrentDerived`; `GetEquipped`→Item kept (combat untouched) + `GetEquippedInstance`/`SetEquipped(instance)` added.

**Step 2 DONE (shared widgets):** `ItemSlot.cs` extended — renders channels (rarityBorder/upgradeBadge/cardPips ◇□ neutral/selectRing+hoverGlow cyan), holds `EquipmentInstance`, `SetInstance`/`SetSelected`; all refs OPTIONAL (old canvas unaffected). `ItemTooltip.cs` — rarity header + full stat lines + instance (+N/cards) + `ShowForPreview(inst,slot)` green/red delta. `WindowFrame.cs` — remember-pos (PlayerPrefs) + click-to-front, composes w/ existing `WindowDragHandle`. **2b demo**: `Tools ▸ unwritten ▸ UI ▸ Build HUD Widgets` (`Editor/HudWidgetBuilder.cs` + runtime `HudWidgetDemo.cs`) — self-contained demo canvas, QUICKFIX-tagged (delete after Step 3).

**Step 3 DONE (the three windows):** `EquipSlotType` migrated in-place (int-stable rename Body→Armor/OffHand→Shield/Trinket→Accessory1 + added Accessory2/Garment/Costume×3; Boots legacy; combat block now reads Shield). New: `UiKit.cs` (runtime uGUI kit + Window + rich Slot + Btn), `InventoryStore.cs` (runtime bag model, seeds Resources/Items, Equip/Unequip/Consume via Character), `ProgressionWindows.cs` (host: builds+wires all 3 windows + C/I/E/Esc toggles + card-insert dialog + hover delta preview). `ItemStarterSet.cs` extended (rarity/cards/weight/zeny/category + Dawnbreaker legendary). `ItemSlot.cs` got `clickOverride`/`previewOnHover`/`previewSlot` hooks. Editor `ProgressionWindowsBuilder.cs` = `Tools ▸ unwritten ▸ UI ▸ Build Progression Windows` (+ seeds 4 starter Cards in Resources/Cards).

**INTEGRATED + legacy removed:** `UIShellBuilder` now adds `ProgressionWindows` (+`InventoryStore` via RequireComponent) to the persistent GameUICanvas and ENSURES starter Items+Cards; DELETED the old Bag panel, Character panel (+equip column), item `BuildTooltip`, and dropped `BagSeeder`+`StatPanel` components + the I/C `PanelToggle` bindings. **Stat allocation moved INTO the new Character window** (StatPoints + 6 +rows calling `Character.AddStat`). `ProgressionWindows` is now a singleton. `BagSeeder.cs`/`StatPanel.cs` are orphaned files (harmless). ⚠️ USER MUST **re-run Build UI Shell** to regenerate the GameUICanvas prefab; delete any old standalone `ProgressionWindowsCanvas` (now redundant — singleton self-disables the dupe).

⏭️ **NEXT options**: (a) GPT's visual skin pass — apply the brand frame/flourish art once GPT exports the ornament kit (currently solid-color placeholder chrome); (b) equipment save persistence (M3); (c) delete dead `BagSeeder.cs`/`StatPanel.cs`. Channel rule still: border=rarity ONLY, +N=gold, ◇=neutral, cyan=select/hover.

⏭️ Verify: Unity recompile clean (~10 new/edited scripts); load a Systems-scene map → `Build Progression Windows` → Play → I/E/C toggle; equip from bag (double/right-click), hover bag gear for ATK→delta, right-click paperdoll to unequip, select gear + Insert Card.

## 💤 PENDING: Map Painter Phase 3 + Prontera
ONE map → JSON → Unity import ([[map-painter-pipeline]]). Phase 1+2 done; **Phase 3 importer NOT STARTED** — read `map.json` → bake (ground→GroundBlend, objects→`PropPlacement`+yaw, walls→`BuildPerimeterWall`). BLOCKED on a real painted `map.json` to lock schema.

## Gotchas
- **No local Play loop** — user presses Play/Bake/Generate to verify; I can't compile here (paste recompile errors).
- World ground standard: Y0 floor, Ground layer; custom shaders (WetGround/GroundBlend/FoliageWind) need Always-Included before a real build.
