---
status: "The standard for every generated picture (Leonardo.ai, GPT Image 2.5 Sunburst). Made so far: the Home key art banner, the Home story illustration, and the Getting Started and Playstyles & Builds guide pictures (all in unity/Assets/3_Data/Resources/UI/KeyArt/), and 13 of the 14 weapon icons (wiki/assets/weapon-icons/). Everything else is still to make."
owner: vince
last-updated: 2026-10-19
tags:
  - art-animation-pipeline
---

# Art Prompts — the Standard

Every generated picture for the game and the website is made in **Leonardo.ai** with the prompts below, so they all
look like one game. Six pictures are made a day, across three Leonardo accounts, until every box on the website and
every skill icon in the game has its picture.

The look: **hand-drawn anime fantasy art like early-2000s Korean fantasy online game official art** — inked lines,
clean cel shading with soft painted colour, a bright warm world, and a candid moment caught mid-action. Never pixel
art, never 3D, never chibi.

## The hard rules — every prompt, every time

1. **No Ragnarok anything.** Never the name, never one of its monsters, never "like the Poring", never "Ragnarok
   style". The style is described in plain words; the attached reference carries the rest. Every prompt says that
   every design is original and that no game names, logos or text appear.
2. **No negative prompt.** This model has no negative prompt box. Everything unwanted is said as a plain "no …"
   inside the prompt itself.
3. **Every prompt is complete on its own.** It carries its own style paragraph; nothing depends on an earlier one.
4. **Every person's black skin is written out in that prompt**, every time (the skin paragraph below).
5. **Every monster is described by how it looks**, never by its in-game name — the generator has never heard of
   "Plopling".
6. **No people in skill icons.** An icon is an emblem, so it never needs a face or a hand.
7. **A weapon's skill icons are built from its Kashira's theme** — the petal and the wind for the Sword's Veyra, the
   flame for the Axe's Varkhaz. Every icon in that weapon's set is made of that theme, so the whole set feels like
   one person (see Skill icons below).
8. **Keep each prompt under 2,000 characters**, Leonardo's limit.

## Leonardo settings — the same every time

| Setting | Value |
| --- | --- |
| Model | GPT Image 2.5 Sunburst |
| Style | Dynamic |
| Quality | High |
| Image Dimensions | 16:9, Small 1376 × 768 (9:16 only for the tall boxes marked so below) |
| Number of generations | 1 |
| Image reference | exactly one picture — see below |

**The one image reference.** Leonardo takes only one. Attach the Home key art banner,
`unity/Assets/3_Data/Resources/UI/KeyArt/last-line-frostcap-slime-siege.jpg`, as the style reference for every
picture, unless a prompt below names a different one. That keeps every new picture matching the art already made.

**Character and monster sheets** in `docs/4-art/reference/` are for writing the words of a prompt — what a creature
or an outfit looks like. They are not attached, because the one reference slot belongs to the style picture.

| File in `reference/` | What is on it |
| --- | --- |
| `characters-all.png` | Everything below in one picture |
| `characters-1-elites.png` | The 20 pictures in the Boss folder: the 18 elites by their in-game names, and two dragons no creature uses yet |
| `characters-2-normal-mobs.png` | The 66 normal mobs by in-game name and home biome |
| `characters-3-saved-characters.png` | The 91 characters a beaten mob can turn back into, grouped as the lore groups them |
| `characters-4-3d-characters.png` | The 52 3D models: the outfits and hair of players, townsfolk and the fourteen shadows |
| `characters-5-palette.png` | The sprite pack's 54 colours |

## How each box crops a 1376 × 768 picture

The website fills each box from the middle of the picture and cuts off what does not fit. That is why each prompt
says where faces and action must sit.

| Box | Box size | What of the picture shows |
| --- | --- | --- |
| Key art banner | 1920 × 600 | the middle 56% of the height (22% cut off top and bottom); stretched 1.4×, so a little soft |
| Story and Guide pictures | 1200 × 500 | the middle 75% of the height (about 12% cut off top and bottom) |
| Event and promo banners | 500 × 240 | the middle 86% of the height (7% cut off top and bottom) |
| Page header banners | 1920 × 360 | the middle 34% of the height (33% cut off top and bottom); stretched 1.4×, so a little soft |
| Biome landscapes, concept sheets | 800 × 500, 1600 × 1000 | the middle 89% of the width (about 5% cut off each side) |
| Desktop wallpapers | 2560 × 1440 | all of it; stretched 1.9×, so soft |

## The building blocks

Already written into every prompt below. Use them word for word in any new prompt.

**Style paragraph** — first in every picture prompt:

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs per person, hands hold things by the handle. Gentle low camera, no fisheye.
```

**Skin paragraph** — in every prompt that has a person in it (the lore calls them "the unwritten"):

```text
Every person has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. Their faces are blank black with only two small white eye marks, no mouth, no nose.
```

Their feelings show in the **shape of the two eye marks** — wide circles in surprise, thin lines when squinting,
slanted when angry or determined, upward arcs when laughing, closed when asleep — plus sweat drops, anger marks and
body language. They never get a mouth.

**Middle-band paragraph** — in every wide box that crops top and bottom (pick the band from the crop table):

```text
Wide image. Keep every face and all the action in the middle band of the picture; the top and bottom edges hold only sky and ground.
```

**A monster** — its look, then the paper touch:

```text
a cute round anime monster: [LOOK FROM THE TABLE], with a small torn-paper edge and an ink blot on its side
```

| Monster | Look (read from its sprite) |
| --- | --- |
| Flutterby | a little red-and-orange butterfly with spotted wings, a cream face and curly antennae |
| Hisser | a coiled red snake with an orange belly and a pale grey crown |
| Pilfer | a sneaky yellow raccoon wearing round black goggles |
| Plopling | a pale blue slime blob with one small dark eye |
| Squeak | a brown mouse with big round cream ears |
| Wyrmlet | a small green worm-dragon with a sleepy face and a striped belly |
| Burrowbo | a grey-green mole with a blue snout |
| Dewdrop | a blue water-drop slime with a wobbly frown |
| Grumbear | a grumpy red-brown bear cub with angry eyebrows |
| Mudpup | a pale blue axolotl with frilly gills |
| Adder | a red snake with one white eye |
| Emberwyrm | a smiling orange worm with rosy cheeks |
| Sunscale | a dark green lizard with an orange face and a curled tail |
| Willo | a small orange-red flame with a cute face |
| Bloodshot | a fiery orange teardrop with a single eye in its middle |
| Cinder | an angry little flame spirit |
| Cyclo | a red one-eyed blob with two small horns |
| Horngore | a scowling red demon head with horns |
| Sucker | a red octopus with a round sucker mouth |
| Goopie | a green slime blob with one red eye |
| Grubble | a round green beetle shell covered in red spots |
| Lovebite | a red heart with a fanged mouth |
| Nightsqueak | a dark grey rat with big cream ears |
| Bandit Boss (elite) | a big red-orange bear-like brute with a striped head, small tusks and clawed hands |
| Gloop King (elite) | a large pale blue slime with one huge eye |
| Frost Ronin (elite) | an icy blue armoured beetle warrior with a yellow crescent crest, holding two curved blades |
| Blood Ronin (elite) | the same armoured beetle warrior in red, holding two curved blades |
| Inferno Mask (elite) | a floating orange fire mask with three dark eye holes |
| Venom Gloop (elite) | a large green slime with one red slit eye |

For any other creature, look at its card on sheet 1 or 2 and describe it the same way: colour, animal or shape, and
one feature.

---

## Skill icons (in the game)

Every skill's icon is a painted emblem. **A weapon's whole skill set is made in ONE picture** — a grid of 6 columns
and 4 rows (24 cells) on one 1376 × 768 image — and cut apart afterwards, because only six pictures are made a day.
Each icon comes out about 180 pixels square, well above the 48-pixel hotbar slot. A weapon with more than 24 skills
takes a second picture.

**Every icon in a weapon's set is built from its Kashira's theme** (the lore's own table):

| Weapon | Kashira | Style | Theme | Element |
| --- | --- | --- | --- | --- |
| Sword | Veyra, the Last Petal | Blossom Wind | petal | Wind |
| Greatsword | Mordane, King Beneath the Waves | Raging Tide | wave | Water |
| Axe | Varkhaz, the Ever-Burning | Wildfire | flame | Fire |
| Warhammer | Tharos, the Starbreaker | Earthrend Descent | the ground splitting | Earth |
| Spear | Orion, the River That Remembers | Bending River | river current | Water |
| Fist | Asterion, the Light Beyond Night | Northern Lights | aurora | Neutral |
| Bow | Celestia, the Wandering Sky | Passing Cloud | cloud | Wind |
| Dagger | Velmora, the Poisoned Rose | Nightshade Bloom | nightshade flower | Poison |
| Katar | Nysara, the Face Without a Name | Veiled Mirage | dust | Ghost |
| Shuriken | Riven, the Red Autumn | Autumn Gale | leaf | Wind |
| Sniper | Vael, the Moonless Eye | Dark Moon | the moonless night | Dark |
| Staff | Kaelen, the Ember Beneath Snow | Frozen Ashes | ash and frost | Fire |
| Wand | Seraphine, Before the Dawn | Morning Mist | mist | Holy |
| Scythe | Morwen, the Last Bloom | Black Lily | lily | Undead |

**Writing a new weapon's icon sheet:** copy the Sword sheet below, swap the "look" sentence for that Kashira's theme
and a background in the weapon's colour, and write each icon as what the skill does, drawn out of that theme.

### Sword — Veyra, Blossom Wind (22 skills, one picture)

The sword's weapon colour in the game is red, so every sword icon sits on deep crimson. The 22 skills in grid order:
Thousand Petals, Held Breath, Howling Wind, Single Petal, Eye of the Storm, Last Petal / Stripped Bough, Second Bloom,
Tailwind, Still Air, Bare Branch, Crossed Branches / Piercing Gust, Rising Breeze, Scattered Petals, Petal Storm,
Parting Clouds, Squall / Bending Reed, Headwind, Returning Wind, Deep Roots. The last two cells stay empty.

```text
Fantasy game skill icon sheet, hand-painted in the style of the attached reference, like early-2000s Korean fantasy online game skill icons: ink outlines, cel shading, soft painted colour, one bold emblem per icon, readable as a thumbnail. Original designs only. No text, no numbers, no people, no hands, no faces.

Wide image: 24 equal square cells in 6 columns and 4 rows, thin off-white #f7f8fa gaps. Each icon: rounded square, thin dark ink border, deep crimson background, light from top left. Every icon is made of pink blossom petals and swirling pale wind, plus a slim silver sword where a blade is needed.

Row 1, left to right: 1 petal whirlwind spinning around a blade; 2 five glowing petals held still in a wind bubble; 3 a gust shaped like a howling wolf head; 4 one big petal on a heavy downward sword stroke, white impact flash; 5 a calm blue eye inside a petal spiral, blue light drops rising; 6 one last petal on a huge white-gold sword arc.
Row 2: 7 a bare blossom branch beside a chipped falling blade; 8 a blossom blooming again from a fallen petal; 9 a blade riding three wind ribbons; 10 petals frozen in still air around an upright sword; 11 a leafless branch against a cracked red sky, one petal left; 12 two blossom branches crossed in an X, bound by wind.
Row 3: 13 a sword thrust inside a spear-shaped gust; 14 a breeze spiralling upward, petals growing as they rise; 15 a slash scattering petals in all directions, wind broken in shards; 16 a petal tornado cut by two crossing slashes; 17 clouds parting, a golden beam down a sword edge; 18 a sword in slanting rain and sharp wind, petals torn.
Row 4: 19 a bending reed, a sword flicking a blow aside in a white spark; 20 wind blowing petals back against a small glowing green leaf; 21 a gust circling back, five slash marks returning to the centre; 22 a sword planted in earth, glowing roots spreading below; 23 and 24 empty off-white cells.
```

---

## Header

### Game logo (light-theme version) — box 420 × 120

Attach the current logo (`wiki/assets/unwritten-logo.png`) as the reference instead of the style picture.

```text
Wide image. Redraw the attached "UNWRITTEN" logo for a light website header: the same word and the quill-pen-in-a-ring emblem, drawn as clean fantasy game-logo lettering with an inked outline, deep ink-blue letters (#22324d) with a steel-blue (#7f97bd) bevel, the quill white. Plain #f7f8fa background. No dark background, no gold, no fire or smoke, no other words. Emblem on the left, word on the right, on one line.
```

## Home

### Main key art banner — box 1920 × 600 — MADE

`last-line-frostcap-slime-siege.jpg`. It is also the style reference for everything else.

### Story illustration — box 1200 × 500 — MADE

`storybook-ink-monsters.jpg`, made from this prompt:

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs per person, hands hold things by the handle. Gentle low camera, no fisheye.

Wide image. Keep every face and all the action in the middle band of the picture; the top and bottom edges hold only sky and ground.

A huge open storybook lying in a sunny meadow under a blue sky with puffy white clouds. The left page shows a peaceful little village drawn in soft colour. On the right page, black ink blots and smears spread across the paper, and cute round monsters with small torn-paper edges and ink blots on their sides climb out of the page into the real grass: a pale blue slime blob tripping over the page edge, a green slime blob with one red eye already chewing a flower, a brown mouse with big round cream ears peeking out. No people. Calm, magical, a little mischievous.
```

### Event banner (×2) and promo banner — box 500 × 240 each

Write the subject in place of `[SUBJECT]`, with the skin paragraph's words and the monster table. The headline goes
on the page, not in the picture.

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs per person, hands hold things by the handle. Gentle low camera, no fisheye.

Every person has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. Their faces are blank black with only two small white eye marks, no mouth, no nose.

Wide image, a small advertising banner. [SUBJECT, caught mid-action]. The subject fills the left two-thirds and stays inside the middle band of the height; the right third is calm pale blue sky (#e6eefa) left empty.
```

### Mascot standing art — box 500 × 600 — 9:16

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework, clean cel shading with soft painted colour, bright warm colours. An original design, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters.

Tall image. A big cheerful mascot: a cute round green slime blob with one red eye and a small ink blot on its side. Caught mid-bounce, squashed a little as it lands, looking happy. Full body, centred, a soft shadow under it, plain #f7f8fa background.
```

### Floating mascot — box 240 × 280

The small monster that bobs in the corner of every page. Same prompt as the mascot standing art, with "Tall image"
replaced by "Square image, small and simple, readable at a tiny size". Cut it out of its plain background afterwards.

### Character standing art — one per weapon, 14 in all — box 900 × 1200 — 9:16

Pick an outfit from sheet 4 and describe it in place of `[HAIR]` and `[CLOTHES]`. Fill in the weapon and the moment
from the table — the lore's own words. The Kashira's name never goes into the prompt.

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. All characters are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs, every leg ends in a visible boot, hands hold the weapon by the handle. Not a stiff pose.

The hero has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. The face is blank black with only two small white eye marks, no mouth, no nose.

Tall image, full body, plain #f7f8fa background, a small shadow at the feet. The hero has [HAIR] and wears [CLOTHES], holds a [WEAPON], and is caught in this moment: [MOMENT]. Cloth and hair moving, a strong readable silhouette.
```

| Weapon | Moment |
| --- | --- |
| Sword | dashing through a storm of darkness, cutting it a thousand times in a few heartbeats, petals streaming behind her |
| Greatsword | rising like a raging tide, one sweep of his greatsword crashing over everything in front of him |
| Axe | charging forward and never stepping back, each axe blow catching the next like wildfire in dry grass |
| Warhammer | leaping and bringing his hammer down hard enough to break a falling star, the earth splitting beneath it |
| Spear | hooking an enemy with his spear and dragging it, the way a river bends whatever falls into it |
| Fist | standing in front of the others, then throwing one fist that lights the sky like the northern lights |
| Bow | drifting above the fight like a cloud nobody can catch, raining arrows down |
| Dagger | stepping back after one nick of a blade dark with nightshade, letting the poison bloom |
| Katar | circling unseen as a mirage in the dust, then striking one crack again and again |
| Shuriken | flinging a hundred whirling blades like leaves in an autumn gale |
| Sniper | lying still on a far hill on a moonless night, firing one shot out of the dark |
| Staff | setting the ground ablaze, then freezing the flames to ash under ice |
| Wand | kneeling over a fallen friend as the morning mist settles and they open their eyes |
| Scythe | laying a quiet curse, watching, a black lily blooming at her feet |

### Weapon icon — 14 small boxes under the weapon picker — 13 MADE, Fist missing

Attach `wiki/assets/weapon-icons/sword.png` as the reference instead of the style picture.

```text
One fantasy weapon item illustration in exactly the same style, size, outline, shading and lighting as the attached weapon icon: a pair of fighting gauntlets with reinforced knuckles. An original design, nothing from any existing game. Centred, alone, plain #f7f8fa background. No text, no letters, no hands, no people.
```

### Biome landscape art (×14) — box 800 × 500

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework, clean cel shading with soft painted colour, bright daylight, big sky. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Gentle low camera, no fisheye.

Every person has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. Their faces are blank black with only two small white eye marks, no mouth, no nose.

Wide image. Keep everything important away from the far left and right edges. A painted landscape of [BIOME], seen from a hill. In one small corner, a candid moment: [MOMENT].
```

| Biome | `[BIOME]` (the wiki's World & Maps words) | `[MOMENT]` |
| --- | --- | --- |
| Grassland | open meadows with a few trees and plenty of flowers, the gentle starting country | two heroes napping in the grass while a pale blue slime blob sits on one's chest |
| Forest | fallen leaves, ferns and mushrooms under tall trees | a hero tripping over a mushroom |
| Highland | a near-treeless moor of short turf and boulders | a hero sitting on a boulder eating lunch in the wind |
| Peaks | above the treeline: bare gravel, snow and boulders | a hero clinging to a rock in a strong wind |
| Snow | a snowy pine forest, its ground always white | a hero slipping on ice with arms flailing |
| Steppe | golden tussocks with sun-baked ground between them | a hero chasing a hat blown away by the wind |
| Coast | sand, wet sand and shore pebbles, scattered with rocks | a hero jumping back from a wave |
| Desert | open orange dunes with a few stones | a hero shaking sand out of a boot |
| Volcanic | black ash, glowing lava cracks and boulder fields | a hero hopping on hot ground |
| Swamp | reedy marsh and mud under willows, water standing in every hollow | a hero stuck knee-deep in mud |
| Sacred | an open birch meadow in flower, high among the clouds | a hero lying in the flowers watching the clouds drift past |
| Haunted | withered grass, grave dirt and old dark cobbles among dead stumps | a hero tiptoeing past an old grave |
| Mist | pale moss and grey silt under willows, always in fog | a hero squinting into the fog, holding a lantern up |
| Corrupted | where the blankness runs deepest and the most twisted pages gather: withered violet grass and purple-black soil, ink-smeared and torn | a hero peeking nervously around a rock at an ink-smeared path |

### Biome monster art (×14) — the small box under each biome card

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework, clean cel shading with soft painted colour, bright warm colours. All creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters. No people.

Wide image, plain #f7f8fa background. A candid group snapshot of cute round anime monsters squabbling, bouncing and photobombing each other, each with a small torn-paper edge and an ink blot: [MONSTERS]. The elite looms bigger behind them.
```

Fill `[MONSTERS]` with the looks from the monster table, joined by commas:

| Biome | Monsters |
| --- | --- |
| Grassland | Flutterby, Hisser, Pilfer, Plopling, Squeak, Wyrmlet · elites: Bandit Boss, Gloop King |
| Forest | Canewick, Capling, Coily, Crawlet, Hootie, Mossnail, Pando, Rascal, Shootling · elites: Autumn Stalk, Elder Stalk |
| Highland | Blinkbat, Digger, Mossgore, Peeper, Scaly, Tarantix, Webber · elites: Mossmaw, Oculord |
| Peaks | Hornscale, Iris · elite: Karasu |
| Snow | Burrowbo, Dewdrop, Grumbear, Mudpup · elite: Frost Ronin |
| Steppe | Chomper, Emberhoot, Viper · elite: Golden Bandit |
| Coast | Gillie, Gnasher, Kappi, Newtie, Octi, Rivershell, Shellby, Snapper · elites: Deep Maw, Great Croak |
| Desert | Adder, Emberwyrm, Sunscale, Willo · elite: Blood Ronin |
| Volcanic | Bloodshot, Cinder, Cyclo, Horngore, Sucker · elite: Inferno Mask |
| Swamp | Bittersweet, Inky, Moldy, Swampfang · elite: Toadmaw |
| Sacred | Skywing, Wisp · elite: Oni Tengu |
| Haunted | Blazeskull, Drizzle, Emberlurk, Glowcoil, Squirt · elite: Abyss Maw |
| Mist | Frostcap, Frostskull, Frostwick · elite: Wraithmother |
| Corrupted | Goopie, Grubble, Lovebite, Nightsqueak · elite: Venom Gloop |

### In-game screenshot (×4 on Home, ×3 on Media)

**No prompt — take these in the game.** A generated picture here would show players a game that does not exist.

## Page header banners — box 1920 × 360 each

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours, blue sky with puffy white clouds. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs per person, hands hold things by the handle. Gentle low camera, no fisheye.

Every person has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. Their faces are blank black with only two small white eye marks, no mouth, no nose.

Wide image. Every face and all the action sit inside the middle third of the height; the top third is only sky and the bottom third only ground. A candid moment: [SCENE]
```

| Page | `[SCENE]` |
| --- | --- |
| News | Townsfolk in colourful village clothes crowding a town notice board covered in pinned paper. One short hero with spiky orange hair stands on tiptoe trying to see over a tall villager. |
| Game Guide | A hero with brown hair and a travelling cloak sitting cross-legged under a tree reading a huge storybook, while a pale blue slime blob peeks over their shoulder. |
| Wiki | A library of loose pages and open books; a hero with green hair and a scholar's coat, eye marks gone wide, on a ladder catching a falling stack of books. |
| Roadmap | Three heroes walking a winding road over hills toward a far city. One points ahead, one lags behind with a heavy bag, one walks backwards talking. |
| Download | A hero with red hair and a red scarf, eye marks curved in a laugh, leaping out of a giant book page into a bright grassy world. |

## Game Guide — Guide illustration (×4) — box 1200 × 500 — 2 MADE

**Getting Started** (`start.jpg`) and **Playstyles & Builds** (`playstyles.jpg`) are made. Still to make:

**The World of Unwritten**

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs per person, hands hold things by the handle. Gentle low camera, no fisheye.

Every person has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. Their faces are blank black with only two small white eye marks, no mouth, no nose.

Wide image. Keep every face and all the action in the middle band of the picture; the top and bottom edges hold only sky and ground.

Seen from behind and slightly below: fourteen heroes standing in a row on a giant open book page that stretches like a field to the horizon, each holding a different weapon: sword, greatsword, axe, warhammer, spear, fighting gauntlets, bow, dagger, katar, shuriken, long rifle, staff, wand, scythe. Colourful hair and travelling clothes, cloaks blowing in the wind. Black ink blots creep across the page toward their feet. Bright sky with puffy white clouds, loose pages flying in the wind. Heroic and hopeful.
```

**World & Maps**

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs per person, hands hold things by the handle. Gentle low camera, no fisheye.

Every person has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. Their faces are blank black with only two small white eye marks, no mouth, no nose.

Wide image. Keep every face and all the action in the middle band of the picture; the top and bottom edges hold only the table and the inn's ceiling beams.

A cosy inn at midday, sunlight through the window. A hero with short silver hair and a blue travelling coat spreads a big map across a wooden table, eye marks wide with excitement. The map is drawn in pictures only, no writing: green grassland, snowy pines, orange desert dunes, a smoking volcano, and one dark ink-smeared corner, each region on its own torn page pinned together. A pale blue slime blob sits on the corner of the map, holding it flat. A mug and a lantern on the table.
```

## Download

### Windows and Mac icons

**No prompt.** Use the official Windows and Apple logos from their own brand pages, or leave the button without an
icon — never a generated look-alike.

### Launcher screenshot — box 1000 × 640

**No prompt — take it from the real launcher.**

## Media

### Wallpapers

Desktop wallpapers use 16:9; the phone wallpaper uses 9:16.

```text
Hand-drawn anime fantasy illustration in the style of the attached reference, like early-2000s Korean fantasy online game official art: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. All characters and creatures are original designs, nothing taken from any existing game, film or book. No game names, no logos, no text, no letters, no signs. Correct anatomy: two arms and two legs per person, hands hold things by the handle. Gentle low camera, no fisheye.

Every person has solid matte black skin like black paper: black face, black neck, black arms, black hands, black legs. Their faces are blank black with only two small white eye marks, no mouth, no nose.

[SIZE] image. A candid moment: [SCENE]
```

| Wallpaper | `[SIZE]` | `[SCENE]` |
| --- | --- | --- |
| Key art | Wide | Attach the key art banner as the reference and write: "The same scene as the attached picture, redrawn to fill the whole frame." |
| A weapon | Wide | A [WEAPON] standing upright in the ground on a windy hill at sunset, the things of its Kashira's theme (from the Skill icons table) blowing past it. No people. |
| A shadow | Wide | A tall figure smeared with dripping ink, in dark tattered armour, turning to look over its shoulder while standing on a torn book page. |
| Phone | Tall | Attach the key art banner as the reference and write: "The same scene as the attached picture, redrawn tall: the archer huge at the bottom, the swordsman and the mage above her, the tower and cheering townsfolk at the top." |

### Concept art

Each row is the part after the style paragraph (and after the skin paragraph when a person is in it).

| Box | Size | Text after the style paragraph |
| --- | --- | --- |
| Monster art — Grassland | 1:1 | `A creature design sheet of one cute round anime monster, [LOOK FROM THE TABLE], with a small torn-paper edge: front, side and back views plus three small expression sketches (happy, angry, sleepy), plain #f7f8fa background.` |
| Monster art — Snow | 1:1 | The same with a Snow monster's look. |
| Monster art — elite | 1:1 | The same with an elite's look, bigger and more menacing, with torn paper at its edges. |
| Character concept sheet | Wide | `A character design sheet of one hero with [HAIR] and [CLOTHES]: front, side and back views and a row of eye-mark expressions, plain #f7f8fa background.` |
| Town concept art | Wide | `The first town on a sunny afternoon, seen from a gentle low angle: a plaza with a fountain, stone cottages and market stalls. A candid crowd moment: a merchant chasing a brown mouse monster that stole an apple, while heroes laugh at it.` |
| Item art sheet | Wide | `A catalogue page of fantasy weapons and armour laid out in neat rows, item illustrations with inked outlines and soft painted shading, plain #f7f8fa background.` |
