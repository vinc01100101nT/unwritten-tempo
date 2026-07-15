# World authoring / generation / data tables

- [Biome-agnostic mobs = Biome.None](../biome-none-agnostic.md) — spawns any tier band; `Biome` enum APPEND-ONLY.
- [Mobs/items/maps as data tables](../mob-item-map-data-tables.md) — `MonsterDef` SO in `Resources/Monsters`, kebab `id` via `MonsterCatalog`.
- [Portal/warp color by destination](../portal-color-convention.md) — town=yellow, field=green, dungeon=blue, special=red; `ClassifyDestination`.
- [World liveliness / ambient life](../world-liveliness-ambient-npcs.md) — SIMULATION: `AmbientLife`→`TownNPC` on `PathAgent3D` + `NpcBarks`.
- [Hand-authored world content](../hand-authored-world-content.md) — user authors ART by hand; generator automates LAYOUT.
- [Maps HAND-AUTHORED via Prop Palette](../authoring-toolkit-prop-palette.md) — `PropPalette` + Authoring menu; DON'T write per-map bakers.
- [World Creator pipeline (HTML→world.json→Unity)](../world-creator-pipeline.md) — Part 1 HTML DONE; spec q176qkd22qmbk4r5t9g7fcc30n895whr.
- [Map painter pipeline](../map-painter-pipeline.md) — Phase 1 DONE (`BrushPalette`); Phase 2 HTML, Phase 3 importer.
- [⭐ MapRecipe + RecipeGenerator (the ONE generator)](../recipe-generator.md) — every map = `MapRecipe` SO; old per-kind generators DELETED.
- [Floor autotiling via RuleTiles](../floor-autotile-ruletiles.md) — `Build Floor RuleTiles` → 8 corner-correct RuleTiles.
- [Runtime material on a PREFAB → null/pink](../runtime-material-on-prefab-null-pink.md) — `new Material()` into a prefab serializes to `fileID:0`; persist as .mat first (Sway Selected Tree).
