---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - art-animation-pipeline
---

# GPT Concept Prompts — Paste-Me (working copy)

Root doc grouping the two GPT image-generation prompt sets — **Props** (Part 1) and **Weapons** (Part 2). Source of truth = **Asset Concept Style Guide v2.1 (TRELLIS 2)**; these are the GPT-image-only distillations. Each part has its **own Style Preamble** — paste the relevant one at the top of a session, then send one **line per image**.

> Tip: save whichever Style Preamble you're using as a ChatGPT Custom Instruction / Project so you don't re-paste it. Square (1:1) image, plain grey background, generate a few variants, pick the cleanest silhouette.

**Shared pipeline (downstream of every prompt):** GPT concept → **Trellis 2** (res 1024 / decimation 250k / texture 2048 / guidance 7.5,7.5,1 / steps 14) → Export → Blender cleanup / retopo → Unity import (`Tools ▸ unwritten ▸ Props ▸ Optimize + Import GLB Prop…`). Weapons additionally get a `WeaponGrip` + a pass in `Tools ▸ unwritten ▸ Weapons ▸ Weapon Grip Tuner` to seat them in the hand.

---

# Part 1 — Props

## ◆ STYLE PREAMBLE (PROPS) — paste once per session

> Stylized hand-painted fantasy game asset concept, Ragnarok Online / Torchlight / Warcraft charm, for an isometric MMORPG. Single centered object on a clean 3/4 hero angle, entire asset visible, sitting on a closed base with a soft ground-contact shadow. Pure flat light-grey studio background, bright soft studio lighting, low contrast, no harsh sun or deep shadows. Chunky exaggerated proportions, strong readable silhouette, clear material separation (stone reads as stone, metal as metal, water as water). Warm fantasy palette: stone grey, cream, copper, muted iron, hints of blue. Detail in the painted texture (stone cracks, wear, moss) not in extra geometry. No landscape, no sky, no background props, no thin rods/chains/ropes. Production blueprint for 3D reconstruction.

## ◆ PROP LINES — send one per image

### Tier 1 — Landmarks (generate first; these set the style anchor)

1. **Central fountain** — a grand circular three-tier stone fountain, the centerpiece of a fantasy town plaza. Wide low octagonal basin of carved pale stone, a stepped central column rising to two stacked shrinking bowls, gentle water spilling between tiers and pooling below. Decorative carved stone edging, subtle moss and water stains, a few copper accents. Chunky, solid, symmetrical, monumental but not tall. Closed stone base.
2. **Hero statue / monument** — a heroic fantasy monument: an armored guardian knight standing tall on a chunky tiered stone pedestal, one hand resting on a downward sword. Weathered bronze/copper figure with green patina, cream-grey carved stone plinth with simple relief panels and stepped base. Dignified, symmetrical, strong silhouette readable from a thumbnail. No thin protruding spikes or banners. Closed base. *(Riskiest of the set — humanoid + sword edge. If Trellis mangles it, simplify the pose.)*
3. **Stone well** — a quaint fantasy village well: a round chunky fieldstone wall waist-high, a sturdy timber A-frame holding a small shingled roof and a rope drum with a crank handle, a wooden bucket resting on the rim. Warm timber + grey stone + muted iron banding. Cozy, solid, slightly storybook. Keep the rope thick and short. Closed stone base.

### Tier 2 — Core buildings (the town's bulk; reuse 2–3× each)

**Town houses — 5 variants (distinct silhouettes so rows don't repeat).** Send each with the style preamble + the fountain/statue/well as style anchor. :warning: Keep doors/windows as *shallow recessed insets* — if GPT draws a big black open doorway it punches a hole in the Trellis mesh; add "shallow recessed door, no visible interior" if needed.

4a. **House A — single-storey cottage** — squat stone-and-timber cottage, steep peaked shingled roof, one chimney, window boxes, a small wooden door under a short awning. Wide low footprint. Closed stone foundation.

4b. **House B — two-storey townhouse** — taller narrow timber-framed house, jettied (overhanging) upper floor, blue tiled gable roof, shuttered windows, stone ground floor. Vertical silhouette to contrast House A. Closed foundation.

4c. **House C — L-shaped corner house** — an L-footprint house for street corners, two wings meeting at a stair-stepped roof, a chimney at the joint, half stone / half plaster walls, balcony over the door. Closed foundation.

4d. **House D — wide family house** — broad two-storey home, long horizontal hipped roof, central dormer window, double chimney, flower boxes and a low front porch. The "big house" of a residential row. Closed foundation.

4e. **House E — tall slim row house** — narrow tall three-storey house with a steep front-facing gable, exposed timber beams, small attic window at the peak, plaster + stone. Slots between bigger houses to break up the line. Closed foundation.

5. **Modular half-timber wall section** — a straight run of fantasy cottage wall built as a repeatable façade piece. Cream rough-plaster panels with chunky grey fieldstones half-set into the plaster (irregular blocky stones, scattered, not a full stone wall), split into bays by dark timber corner posts and beams (half-timber framing). A grey ashlar stone-block base course runs along the bottom as a solid foundation, and a warm terracotta barrel-tile roof eave overhangs the top with a soft shadow underneath. Warm palette: cream plaster, grey stone, dark timber, orange-red clay tile. Chunky and flat-fronted, strong horizontal silhouette. Keep any door/window as a *shallow recessed inset, no visible interior*. Detail (plaster texture, stone edges, tile ridges) painted in, not extra geometry. Closed stone base.
6. **Shop building** — open-front ground floor + a hanging sign bracket; reads as a shop. Cloth awning encouraged.
7. **Inn / tavern** — the biggest hub building, the landmark players walk toward; warm, inviting, hanging lanterns.
8. **Civic hall / town office** — grander stone, columns, official tone; the "capital" building.

### Tier 3 — Civic / commerce dressing

9. **Hanging shop signboard** — bracket-mounted wooden sign; make blank + icon variants (sword / hammer / potion).
10. **Market stall** — striped cloth awning over a wooden counter with a few goods.
11. **Notice / job board** — wooden posts + pinned papers; the quest-hub object.
12. **Street lamp post** — standing wrought-iron lantern on a stone foot (pairs with night lighting).

### Tier 4 — Street clutter (cheap, high "lived-in" payoff)

13. **Wooden crate**
14. **Barrel** (new-style)
15. **Signpost** — directional arrow post
16. **Bench** — public seating
17. **Banner / flag** — kingdom identity, recolorable
18. **Planter / potted shrub** — tidy civic greenery

### Tier 5 — Grand capital (defer until the core town reads well)

19. **Stone archway / grand town gate**
20. **Castle facade** — north landmark

### Tier 6 — Trees (worldgen greenery; recolor + rescale for variety)

> :warning: Keep foliage as **chunky solid canopy masses**, never individual leaves or thin twigs — thin geometry punches holes / floating shards in the Trellis mesh. Add "solid rounded canopy, no gaps, thick trunk" if GPT draws a sparse tree. Closed root base on all.

21. **Oak tree** — a broad stylized fantasy oak, thick gnarled trunk flaring into surface roots, a wide rounded multi-lobed canopy of solid deep-green foliage clumps. Sturdy, storybook, casts a big friendly shade. The town's default shade tree. Closed root base.
22. **Pine / conifer** — a tall conical evergreen, straight tapering trunk, stacked tiered rings of blue-green needle clumps narrowing to a point. Reads clearly from a thumbnail as a pine. Good for northern/hill biomes. Closed root base.
23. **Birch (slim accent tree)** — a slender pale-barked tree with dark flecks, a light airy but still solid golden-green canopy. Vertical accent to break up rounded oaks. Closed root base.
24. **Willow (droop tree)** — a soft weeping willow, short thick trunk, a wide mound of draping muted sage-green foliage sweeping toward the ground. Keep the drapes as solid curtain masses, not stringy strands. Waterside mood. Closed root base.
25. **Fruit / blossom tree** — a small cheerful orchard tree, chunky trunk, a rounded canopy dotted with warm blossoms or fruit (pink or apple-red). Adds color to gardens and plazas. Closed root base.
26. **Dead / bare tree** — a bare weathered tree, twisted grey trunk and a few thick stubby branches, no leaves. Chunky branches only, no fine twigs. Spooky/autumn dressing. Closed root base.
27. **Palm tree** — a tall curved trunk with segmented bark and a crown of broad solid fronds fanning out. Coastal/oasis biome. Keep fronds as thick paddles. Closed root base.
28. **Stump** — a wide chopped tree stump, visible growth rings on top, surface roots, a little moss. Cheap filler + seat/prop. Closed base.

### Tier 7 — Plants & shrubs (ground clutter; scatter for a lived-in world)

29. **Round bush / shrub** — a chunky rounded shrub, solid dense green foliage mass, faint texture of leaf clumps. The default filler greenery; recolor for seasons. Closed soil base.
30. **Fern cluster** — a low spray of broad stylized fern fronds, deep forest-green, arching from a central crown. Keep fronds thick and solid. Forest floor / shade. Closed base.
31. **Tall grass tuft** — a chunky clump of stylized long grass blades fanning upward, warm green fading to straw at the tips. Grouped as a solid tuft, not stringy. Scatter across fields. Closed base.
32. **Cattails / reeds** — a stand of upright reed stalks with brown seed-heads, blue-green blades. Waterside / marsh dressing. Keep stalks thick. Closed muddy base.
33. **Cactus** — a chunky rounded barrel/paddle cactus, ribbed blue-green body, tiny stub spines painted not modeled, a small flower on top. Desert biome. Closed sandy base.
34. **Mushroom cluster** — a group of oversized storybook mushrooms, fat rounded caps (red-with-white-spots + a couple muted browns), thick stubby stems. Fantasy forest whimsy. Closed mossy base.
35. **Ivy / vine clump** — a low mound of climbing vine with broad solid leaves, for draping on walls and ruins. Keep as a connected leafy mass, no dangling strings. Closed base.

### Tier 8 — Flowers (color pops; small, recolorable, scatter in clusters)

36. **Wildflower cluster** — a small chunky patch of mixed stylized wildflowers (red, yellow, blue, white blooms) on solid green stems and leaves. The default flower scatter; recolor freely. Closed soil base.
37. **Tulip bed** — a tidy row/clump of rounded tulip blooms on straight stems, cheerful primary colors, broad leaves at the base. Civic garden / planter filler. Closed soil base.
38. **Rose bush** — a compact bush studded with plump red or pink rose blooms among solid dark-green leaves. Keep thorns painted, not modeled. Garden centerpiece. Closed soil base.
39. **Sunflower** — a single tall sunflower, thick sturdy stalk, big rounded golden-petaled head with a dark center, broad leaves. Cheerful vertical accent. Closed soil base.
40. **Lily pads / water flower** — a small cluster of flat round lily pads on water with one or two white/pink lotus blooms. Pond dressing (pairs with the willow + well). Flat closed water base.
41. **Potted flower** — a chunky terracotta pot brimming with a rounded mound of mixed blooms. Doorstep / windowsill dressing for the town houses. Closed pot base.

### Tier 8b — Window-box plants (drop into house window ledges; 3 variants, same box so they read as a set)

> :warning: **The whole point of these is to NOT be leaf-blobs.** Trellis bakes thin scattered leaves as opaque poly cards with no transparency, so they render as flat blobs. Force **solid, chunky, connected foliage masses with a clear rounded silhouette** — never individual thin leaves, stray petals, or dangling strands. Add "solid rounded foliage mass, thick connected leaves, no thin gaps" if GPT draws a sparse or leggy plant. Keep the **same long wooden window-box planter** across all three so the mesh + scale match.

42. **Window box A — full leaves (no flowers)** — a long low wooden window-box planter packed with a solid rounded mound of lush deep-green foliage, broad chunky leaves spilling gently over the front edge. Pure healthy greenery, no blooms. Solid connected leaf mass, no thin individual leaves or dangling strands. Closed wooden box base.
43. **Window box B — leaves with a few flowers** — the same long wooden window-box planter full of that solid green foliage, now dotted with a light scatter of small cheerful blooms (mixed red / yellow / white) tucked among the leaves. Mostly leaves, occasional flower pops. Chunky solid foliage and blooms. Closed wooden box base.
44. **Window box C — full bloom** — the same wooden window-box planter overflowing with a dense colorful crown of flowers (red, pink, yellow, blue) crowded above a solid base of green leaves — a lush, cheerful blooming display. Flowers dominate. Every bloom and leaf clump reads as a solid rounded mass, no thin stems or stray petals. Closed wooden box base.

### Props pipeline note

Generate **Tier 1 first**, eyeball all three together in Unity as a set — confirm they read as one cohesive town before committing to the rest.

---

# Part 2 — Weapons

## ◆ STYLE PREAMBLE (WEAPONS) — paste once per session

> Stylized hand-painted fantasy game WEAPON concept, Ragnarok Online / Torchlight / Warcraft charm, for an isometric MMORPG. A SINGLE weapon, isolated and centered on a clean 3/4 hero angle, the whole weapon visible with its **grip/handle clearly readable** — it will be placed in a character's hand. Present it upright or laid on a gentle diagonal with the handle toward the lower end. Pure flat light-grey studio background, bright soft studio lighting, low contrast, no harsh sun or deep shadows. Chunky exaggerated proportions, strong readable silhouette, clear material separation (forged steel, warm wood, leather wrap, glowing gem). Warm fantasy palette: muted iron, bright steel, copper/bronze, warm timber, leather brown, one jewel accent. **Give every edge and shaft real THICKNESS** — no razor-thin blades, no wire-thin spear shafts, bowstrings, chains, or straps; blades are broad, hafts are stout, guards and pommels are chunky. Detail in the painted texture (nicks, wear, runes, wood grain) not in fragile geometry. No hand, no scenery, no background props. Production blueprint for 3D reconstruction.

> :warning: **Weapon Trellis gotcha — thin parts vanish or shatter.** Trellis loses or fragments anything wire-thin. Weapons are full of it: blade edges, spear shafts, bowstrings, wand tips. Force thickness everywhere. Specific fixes to append when GPT draws it too thin:
>
> - **Blades** — "broad blade with visible edge thickness, not a razor sheet."
> - **Shafts / hafts** (spear, staff, axe, scythe) — "stout thick shaft, gripped like a haft, not a stick."
> - **Bow** — draw it **stringless** and add "string painted on / omitted, thick carved limbs" — a taut thin string never survives the mesh.
> - **Firearm** — "chunky barrel and stock, no thin ramrod or trigger wires."
> - Keep the whole thing **one connected solid** — no floating gems, no detached crossguard tips.

## ◆ WEAPON LINES — send one per image

### :crossed_swords: Might — physical, forged steel, chunky (scales off Atk)

1. **One-hand sword** *(WeaponType.Sword)* — a knightly arming sword, broad straight double-edged blade with a wide central fuller, a chunky straight crossguard, leather-wrapped grip, round steel pommel. The Swordsman baseline — clean, heroic, balanced.
2. **Two-hand greatsword** *(TwoHandSword)* — a massive zweihänder, wide heavy blade with a long ricasso, big swept crossguard, a long two-hand leather grip, heavy counterweight pommel. Slow, monumental bruiser silhouette.
3. **One-hand axe** *(Axe)* — a stout bearded hand-axe, thick single crescent steel head with iron banding, chunky short wooden haft, leather grip wrap. Reckless berserker feel.
4. **Two-hand battle-axe** *(TwoHandAxe)* — a huge double-bitted battle-axe, two broad crescent blades, a thick reinforced haft with iron langets and an end spike. Anti-large cleaver.
5. **Mace** *(Mace)* — a heavy flanged mace, chunky faceted steel head, short stout haft, warm gold/bronze accents for a holy read. Blunt stagger/CC weapon.
6. **Spear / halberd** *(Spear)* — a stout polearm, a broad leaf-shaped spearhead **with real width and thickness**, a thick wooden shaft bound in iron langets, optionally a small side axe-blade for a halberd read. Reach controller — keep the shaft stout, never a stick.

### :dagger: Finesse — crit, lean but not thin (scales off Atk-crit)

7. **Dagger** *(Dagger)* — a fantasy assassin's dagger, a broad tapering blade with clear edge thickness, a small chunky guard, tightly wrapped grip, jeweled pommel. Poison/bleed knife.
8. **Katar** *(Katar)* — a punch-dagger, a wide triangular blade riding over an H-frame handle held crosswise in the fist, all chunky steel. Crit assassin's fist-blade.
9. **Bow** *(Bow)* — a recurve hunter's bow, thick carved wooden limbs curving to reinforced tips, a leather-wrapped central grip, subtle rune inlay. **Stringless / string painted on** — thick limbs carry the silhouette.
10. **Firearm** *(Firearm)* — a fantasy flintlock (pick a chunky pistol *or* a long musket), thick carved wooden stock, stout iron barrel, brass fittings, an oversized decorative hammer/lock. No thin ramrod or wire trigger.

### :crystal_ball: Arcane — magic, wood + crystal (scales off MAtk / MP)

11. **Caster staff** *(Staff)* — a tall wizard's staff, a stout gnarled wooden shaft (thick, not a stick), a chunky faceted crystal or orb cradled in carved wooden claws at the top, iron banding. The MAtk nuker's focus.
12. **Support wand** *(short Staff)* — a short one-hand focus wand, a thick carved wooden handle, a glowing gem head set in a chunky metal cradle, soft warm light. The healer's tool — cozy, not menacing.
13. **Scythe** *(needs a new WeaponType — see mapping)* — a fantasy reaping scythe, a broad curved blade **with real thickness**, a stout wooden snaith bound in iron braces, a subtle sickly-green rune glow. The curse/DoT necromancer weapon.
14. **Focus orb / grimoire** *(needs a new WeaponType)* — an arcane focus: either a chunky crystal orb held in a clawed metal stand, or a thick spellbook with heavy metal corners and a gem clasp. The control mage's channel — one solid connected object, no floating orb.

## Weapon-type mapping (keep the art honest)

Concept art is decoupled from code, but only some of these are wieldable **today**. Real `WeaponType` members: **Sword, TwoHandSword, Dagger, Axe, TwoHandAxe, Mace, Spear, Staff, Bow, Katar, Knuckle, Firearm**.

- **Wand** → author as a **short Staff**; there is no separate `Wand` type.
- **Scythe · Focus-orb · Grimoire/Tome** → need a **new `WeaponType` + a new attack-animation bucket** before they can be equipped and swung. Fine to generate the concept + mesh ahead of that — just know it's not hand-ready until the type + swing clip exist.
- **Knuckle** (fists) usually needs no held mesh — skip, or generate simple knuckle-guards.

## Quality / variants

Recolor + escalate one hero mesh per type across grades (the existing **Iron Sword → Bow** pattern): a Raw or
F-grade piece is plain steel and wood; the grades above it add brighter metal, inlay, and a soft emissive glow.
Keep the silhouette identical so the same grip tuning carries across every grade of that weapon.

### Weapons pipeline note

After import, make each weapon wieldable: add a `**WeaponGrip**` to the prefab root and seat it in the hand with `Tools ▸ unwritten ▸ Weapons ▸ Weapon Grip Tuner`. Generate one family at a time and eyeball them together in Unity before committing to the rest.

---

## Related

- [Mixamo Animation Integration — Workflow](mixamo-animation-workflow.md) — the sibling pipeline: characters and their clips, rather than props and weapons.
- [Weapon Roster — 14 Identities](../2-systems/active-skill-system-v3/weapon-roster-12-identities.md) — the fourteen weapons the Part 2 prompts have to look like.
- [Story & Setting (Canon)](../3-world/story-setting-canon.md) — what a weapon *is* in the fiction, which the concept art should carry.
- [World Creator — Spec](../3-world/world-creator-spec.md) — the maps these props get placed into.
- [YouTube Update Format Template](../1-planning/youtube-update-template.md) — where new assets get announced, asset names fact-checked first.
