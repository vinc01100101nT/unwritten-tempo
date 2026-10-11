---
status: "Prompts written 2026-10-02; the Home banner is made and on the website (wiki/assets/last-line-frostcap-slime-siege.jpg), the other boxes not yet."
owner: vince
last-updated: 2026-10-11
tags:
  - art-animation-pipeline
---

# Website Art Prompts — Paste-Me

One prompt for every box marked **IMAGE ART HERE** on the website prototype (`prototypes/website.html`). Every
picture is an **anime illustration in the style of classic Ragnarok Online official art**: inked lines, soft painted
colour, a bright sunny world, and a candid moment caught mid-action. No pixel art anywhere.

## Why every prompt is written out in full

ChatGPT does not hand our text to its image tool. It writes its own shorter prompt, and anything not tied to a
specific character in that prompt gets dropped. On 2026-10-02 its rewrite kept "black-faced" for the one hero whose
own description said "charcoal-black skin", and dropped it for the two heroes who were only called "unwritten" — so
they came out with ordinary skin. A word the image tool has never heard ("unwritten", "Plopling") means nothing to it,
and a rule written once at the top of a chat does not survive the rewrite.

So the rules are:

1. **ChatGPT may not change a single word.** Every prompt below starts with an order to send it word for word, and
   the session opener repeats it.
2. **Every prompt carries its own style line.** Nothing depends on an earlier message.
3. **Every hero's black skin is written into that hero's own description**, every time.
4. **Every monster is described by how it looks**, never by its in-game name alone.

## What to attach

1. **A style reference** — one or two pieces of classic Ragnarok Online official art you like (your own copies).
   GPT copies the drawing style from them, never the characters.
2. **The character reference sheet** — what each creature and outfit in our game looks like. The words in each
   prompt carry the design; the sheet backs them up.

| File in `reference/` | What is on it |
| --- | --- |
| `characters-all.png` | Everything below in one picture, for people to look at |
| `characters-1-elites.png` | The 20 pictures in the Boss folder: the 18 elites by their in-game names, and two dragons no creature uses yet |
| `characters-2-normal-mobs.png` | The 66 normal mobs by in-game name and home biome |
| `characters-3-saved-characters.png` | The 91 characters a beaten mob can turn back into, grouped as the lore groups them |
| `characters-4-3d-characters.png` | The 52 Quaternius 3D models: the outfits and hair of players, townsfolk and the fourteen shadows |
| `characters-5-palette.png` | The sprite pack's 54 colours — an optional colour hint, not needed for these prompts |

ChatGPT shrinks a large upload, so attach the **section file** a prompt needs rather than the all-in-one sheet.

## ◆ SESSION OPENER — paste once at the start of every new chat

Attach the style reference and the sheets the session will need, then send:

```text
For this whole chat, every prompt I send is final. Send it to the image generation tool exactly as I wrote it — word for word, character for character. Do not rewrite, shorten, summarise, reorder, translate, merge or "improve" a single word, do not add anything of your own, and do not replace my descriptions with your own description of the attached images. If you cannot send a prompt unchanged, tell me so instead of generating anything.

The images I attach are references only: the Ragnarok Online art shows the drawing style; the character sheets show what our creatures and outfits look like. Never draw the actual characters from the Ragnarok art.

Reply only with "Ready." for now.
```

If a picture comes out wrong, ask **"show me the exact prompt you sent to the image tool"** and compare it with
ours. If anything changed, send the prompt again and say: "You changed my prompt. Send it again, word for word."

## The building blocks

These are already written into every prompt below. Use them word for word when you write a new prompt.

**Style line** — the first paragraph after the send-order:

```text
Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright sunny warm colours, puffy white clouds and blue sky. Correct anatomy with natural proportions: every character has exactly two arms and two legs, every leg ends in a visible foot or boot, hands hold weapons by the handle. A gentle low camera angle with no fisheye or wide-angle distortion. A candid moment caught mid-action, never a posed portrait. Not pixel art, not 3D, not chibi. No text, no letters, no logos.
```

**A hero, a townsperson or a shadow** — the lore calls them "the unwritten". Write every one of them like this:

```text
a [ROLE] whose skin is solid matte black like black paper — black face, black neck, black arms, black hands, black legs, no normal skin tone anywhere — with two small white eye marks and no mouth or nose — the face is blank black except the two eye marks, [HAIR] and [CLOTHES]
```

Their emotion is shown by the **shape of the two eye marks** — wide circles in surprise, thin lines when squinting,
slanted when angry or determined, upward arcs when laughing, closed when asleep — plus sweat drops, anger marks and
body language. They never get a mouth.

**A monster** — always its look, then the corruption touch:

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

## Sizes

ChatGPT's images come out square (1024×1024), wide (1536×1024) or tall (1024×1536). Each prompt says which to ask
for and what the website box needs; crop to the box afterwards.

> **If you ever want heroes with ordinary skin:** in each prompt, replace the hero phrase from "whose skin is solid
> matte black" up to "no mouth or nose — the face is blank black except the two eye marks" with "with [skin tone] skin".

---

## Header

### Game logo (light-theme version) — box 420 × 120

Attach the current logo (`wiki/assets/unwritten-logo.png`) instead of the style reference.

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Wide image. Redraw the attached "UNWRITTEN" logo for a light website header: the same word and the quill-pen-in-a-ring emblem, drawn as clean fantasy game-logo lettering with an inked outline, deep ink-blue letters (#22324d) with a steel-blue (#7f97bd) bevel, the quill white. Plain transparent or #f7f8fa background. No dark background, no gold, no fire or smoke. Emblem on the left, word on the right, on one line.
```

## Home

### Main key art banner — box 1920 × 600

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright sunny warm colours, puffy white clouds and blue sky. Correct anatomy with natural proportions: every character has exactly two arms and two legs, every leg ends in a visible foot or boot, hands hold weapons by the handle. A gentle low camera angle with no fisheye or wide-angle distortion. A candid moment caught mid-action, never a posed portrait. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

Wide image, to be cropped to a thin strip: every face sits inside the middle band of the image, between 25% and 75% of its height; bodies may run past it.

Composition like a classic Ragnarok Online poster: dense and crowded, every part of the frame busy, no empty areas. Heavy ink linework and dense costume detail. Three depth layers: one character huge in the foreground, so close to the camera that the frame edge cuts them off; a tangle of characters and monsters in the middle; a stone town behind. Strong diagonals, motion lines, flying pages and leaves in the air.

On a cobblestone road just outside a stone town gate, a fight against a swarm of small, weak field monsters has turned into comic chaos. FOREGROUND RIGHT, huge and close to the camera, cut off by the right edge: an archer whose skin is solid matte black like black paper — black face, black neck, black arms, black hands, black legs, no normal skin tone anywhere — with two small white eye marks narrowed in concentration and no mouth or nose — the face is blank black except the two eye marks, short purple hair and a green cloak, drawing her bow straight past the viewer — one bowstring running from the top tip of the bow to the bottom tip, pulled back to her cheek and touching the end of the arrow at her drawing hand — while a pale blue slime blob with one small dark eye sits on her head. MIDDLE LEFT: a swordsman whose skin is solid matte black like black paper — black face, black neck, black arms, black hands, black legs, no normal skin tone anywhere — with two small white eye marks slanted in determination and no mouth or nose — the face is blank black except the two eye marks, messy red hair and a red scarf over brown leather armour, holding an ordinary one-edged sword by its handle, with a plain round pommel and no spike, leaps over a low stone wall mid-swing at a brown mouse with big round cream ears that ducks just in time, while a second mouse hangs from his scarf by its teeth. MIDDLE CENTRE: a mage girl whose skin is solid matte black like black paper — black face, black neck, black arms, black hands, black legs, no normal skin tone anywhere — with two small white eye marks gone wide in shock and no mouth or nose — the face is blank black except the two eye marks, long blonde hair and a blue robe, stumbles backwards mid-spell as a sneaky yellow raccoon wearing round black goggles runs off with her pointed blue hat, her spell going wild in a burst of blue sparkles. There is exactly one raccoon and exactly one blue hat in the whole picture. A little red-and-orange butterfly monster with spotted wings flutters right in front of the swordsman's face, and one small green worm-dragon with a sleepy face naps on the wall, ignoring everything. BACKGROUND: the town wall and a tall stone tower, where townsfolk whose skin is solid matte black, with two white eye marks and no mouths (blank black faces), lean over the wall cheering and pointing. The monsters are cute, round, weak little field monsters with a small torn-paper edge and an ink blot on their side. Happy, loud, chaotic, full of life.
```

#### Short version for Leonardo.ai

Leonardo takes at most 2,000 characters and uses the prompt as written. This version is 1650 characters. Paste the
first block into the prompt box and the second into the negative prompt box; add the Ragnarok art as a style reference
and pick the widest image shape.

```text
Classic Ragnarok Online official art style, early-2000s Korean fantasy manhwa illustration, heavy ink linework, cel shading with soft painted colour, bright sunny day, blue sky, puffy white clouds. Wide crowded poster composition, every area busy, three depth layers, strong diagonals, motion lines, flying pages and leaves. Comic chaos on a cobblestone road outside a stone town gate with a tall stone tower.

Every hero and townsperson has solid matte black skin like black paper: black face, black arms, black hands, black legs. Blank black faces with only two small white eye marks, no mouth, no nose. Colour only in hair and clothes.

Foreground right, huge, cut off by the frame edge: archer girl, short purple hair, green cloak, drawing her bow past the viewer, one bowstring pulled to her cheek touching the arrow, eye marks narrowed, a pale blue slime blob sitting on her head.

Middle left: swordsman, messy red hair, red scarf, brown leather armour, leaping over a low stone wall, swinging a one-edged sword at a ducking brown mouse with big round ears; a second mouse bites his scarf; a red-orange butterfly monster flutters in his face.

Middle centre: blonde mage girl in a blue robe stumbling backwards, eye marks wide in shock, wild blue magic sparkles, while a yellow raccoon in round black goggles runs off with her pointed blue hat.

A sleepy little green dragon naps on the wall. Townsfolk with black faces cheer from the top of the town wall.

Monsters are cute, round, weak field monsters with small torn-paper edges and ink blots. Correct anatomy: two arms and two legs each, hands on weapon handles. Happy, loud, full of life.
```

```text
human skin, skin tone, tan skin, pale skin, mouth, lips, teeth, nose, pixel art, 3d render, chibi, photorealistic, text, letters, logo, watermark, extra limbs, extra arms, extra fingers, bad anatomy, two hats, two raccoons, fisheye, empty background, scary monsters, snake
```

### Story illustration — box 1200 × 500

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright sunny warm colours, puffy white clouds and blue sky. A gentle low camera angle with no fisheye or wide-angle distortion. A candid moment caught mid-action. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

Wide image. A huge open storybook lying in a sunny meadow. On the left page, a peaceful little village is drawn in soft colour. On the right page, black ink blots and smears spread across the paper, and cute round anime monsters with torn-paper edges are climbing out of the page into the real grass: a pale blue slime blob tripping over the page edge, a green slime blob with one red eye already chewing a flower, a brown mouse with big round ears peeking out. No people in the picture.
```

### Event banner (×2) and promo banner — box 500 × 240 each

Write the subject in place of `[SUBJECT]`, using the hero phrase and the monster table. The words go on the page, not
in the picture.

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright sunny warm colours. Correct anatomy with natural proportions: every character has exactly two arms and two legs, every leg ends in a visible foot or boot, hands hold weapons by the handle. A candid moment caught mid-action, never a posed portrait. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

Wide image, a small advertising banner. [SUBJECT — e.g. "an archer whose skin is solid matte black like black paper — black face, black arms, black hands, black legs, no normal skin tone anywhere — with two small white eye marks narrowed in concentration and no mouth or nose — the face is blank black except the two eye marks, short purple hair and a green cloak, caught mid-shot"]. The subject fills the left two-thirds; the right third is calm pale blue sky (#e6eefa) left empty for text.
```

### Mascot standing art — box 500 × 600

The website wants a cute mascot for one corner, like Ragnarok's Poring. Goopie (a green slime blob) is the closest
thing to the Poring on the sheet; swap in any monster from the table.

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework, clean cel shading with soft painted colour, bright warm colours. Not pixel art, not 3D. No text, no letters, no logos.

Tall image. A big cheerful mascot: a cute round anime monster, a green slime blob with one red eye, with a small ink blot on its side. Caught mid-bounce, squashed a little as it lands, looking happy. Full body, centred, a soft shadow under it, plain #f7f8fa background.
```

### Character standing art — one per weapon, 14 in all — box 900 × 1200

Pick an outfit from sheet 4 for each weapon and describe it in words in place of `[HAIR]` and `[CLOTHES]`. Fill in
the weapon, name and moment from the table — they are the lore's own words.

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. Correct anatomy with natural proportions: two arms and two legs, every leg ends in a visible boot, hands hold the weapon by the handle. A candid moment caught mid-action, never a stiff pose. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

Tall image, full body, plain #f7f8fa background, a small shadow at the feet. A hero whose skin is solid matte black like black paper — black face, black neck, black arms, black hands, black legs, no normal skin tone anywhere — with two small white eye marks and no mouth or nose — the face is blank black except the two eye marks, [HAIR] and [CLOTHES]. They hold a [WEAPON] and are [NAME], caught in this moment: [MOMENT]. Cloth and hair moving, a strong readable silhouette.
```

| Weapon | Name | Moment |
| --- | --- | --- |
| Sword | Veyra, the Last Petal | dashing through the heart of Chaos, cutting it a thousand times in a few heartbeats, petals streaming behind her |
| Greatsword | Mordane, King Beneath the Waves | rising like a raging tide, one sweep of his greatsword crashing over everything in front of him |
| Axe | Varkhaz, the Ever-Burning | charging forward and never stepping back, each axe blow catching the next like wildfire in dry grass |
| Warhammer | Tharos, the Starbreaker | leaping and bringing his hammer down hard enough to break a falling star, the earth splitting beneath it |
| Spear | Orion, the River That Remembers | hooking an enemy with his spear and dragging it, the way a river bends whatever falls into it |
| Fist | Asterion, the Light Beyond Night | standing in front of the others, then throwing one fist that lights the sky like the northern lights |
| Bow | Celestia, the Wandering Sky | drifting above the fight like a cloud nobody can catch, raining arrows down |
| Dagger | Velmora, the Poisoned Rose | stepping back after one nick of a blade dark with nightshade, letting the poison bloom |
| Katar | Nysara, the Face Without a Name | circling unseen as a mirage in the dust, then striking one crack again and again |
| Shuriken | Riven, the Red Autumn | flinging a hundred whirling blades like leaves in an autumn gale |
| Sniper | Vael, the Moonless Eye | lying still on a far hill on a moonless night, firing one shot out of the dark |
| Staff | Kaelen, the Ember Beneath Snow | setting the ground ablaze, then freezing the flames to ash under ice |
| Wand | Seraphine, Before the Dawn | kneeling over a fallen friend as the morning mist settles and they open their eyes |
| Scythe | Morwen, the Last Bloom | laying a quiet curse, watching, a black lily blooming at her feet |

### Weapon icon — 14 small boxes under the picker

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Square image. A neat grid of fourteen fantasy weapon item illustrations in classic Ragnarok Online item-art style: inked outlines, soft painted shading, each weapon on its own, all the same size, the same light from the top left, plain #f7f8fa background, no text: sword, greatsword, axe, warhammer, spear, fighting gauntlet, bow, dagger, katar, shuriken, sniper rifle, staff, wand, scythe.
```

### Biome landscape art (×14) — box 800 × 500

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework, clean cel shading with soft painted colour, bright daylight, big sky. Correct anatomy with natural proportions. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

Wide image. A painted landscape of [BIOME], seen from a hill. In one small corner, a candid moment: [MOMENT].
```

| Biome | Fill `[BIOME]` with (the wiki's World & Maps words) | Fill `[MOMENT]` with |
| --- | --- | --- |
| Grassland | open meadows with a few trees and plenty of flowers, the gentle starting country | two heroes whose skin is solid matte black — black faces, arms and legs — with white eye marks and no mouths (blank black faces), napping in the grass while a pale blue slime blob sits on one's chest |
| Forest | fallen leaves, ferns and mushrooms under tall trees | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), tripping over a mushroom |
| Highland | a near-treeless moor of short turf and boulders | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), sitting on a boulder eating lunch in the wind |
| Peaks | above the treeline: bare gravel, snow and boulders | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), clinging to a rock in a strong wind |
| Snow | a snowy pine forest, its ground always white | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), slipping on ice with arms flailing |
| Steppe | golden tussocks with sun-baked ground between them | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), chasing a hat blown away by the wind |
| Coast | sand, wet sand and shore pebbles, scattered with rocks | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), jumping back from a wave |
| Desert | open orange dunes with a few stones | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), shaking sand out of a boot |
| Volcanic | black ash, glowing lava cracks and boulder fields | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), hopping on hot ground |
| Swamp | reedy marsh and mud under willows, water standing in every hollow | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), stuck knee-deep in mud |
| Sacred | an open birch meadow in flower, high among the clouds | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), lying in the flowers watching the clouds drift past |
| Haunted | withered grass, grave dirt and old dark cobbles among dead stumps | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), tiptoeing past an old grave |
| Mist | pale moss and grey silt under willows, always in fog | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), squinting into the fog, holding a lantern up |
| Corrupted | where the blankness runs deepest and the most twisted pages gather: withered violet grass and purple-black soil, ink-smeared and torn | a hero whose skin is solid matte black — black face, arms and legs — with white eye marks and no mouth (a blank black face), peeking nervously around a rock at an ink-smeared path |

### Biome monster art (×14) — the small box under each biome card

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework, clean cel shading with soft painted colour, bright warm colours. Not pixel art, not 3D. No text, no letters, no logos.

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
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright sunny warm colours, puffy white clouds and blue sky. Correct anatomy with natural proportions: every character has exactly two arms and two legs, every leg ends in a visible foot or boot. A gentle low camera angle with no fisheye or wide-angle distortion. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

Very wide image, mostly sky and open space so text can sit over it. Every character sits inside the middle band of the image, between 30% and 75% of its height. A candid moment: [SCENE]
```

| Page | Fill `[SCENE]` with |
| --- | --- |
| News | Townsfolk crowding a town notice board covered in pinned paper. Every townsperson's skin is solid matte black — black faces, arms and legs — with two white eye marks and no mouth (a blank black face), in colourful village clothes. One short hero with spiky orange hair stands on tiptoe trying to see over a tall villager. |
| Game Guide | A hero whose skin is solid matte black — black face, arms, hands and legs — with two white eye marks and no mouth (a blank black face), brown hair and a travelling cloak, sitting cross-legged under a tree reading a huge storybook, while a pale blue slime blob peeks over their shoulder. |
| Wiki | A library of loose pages and open books; a hero whose skin is solid matte black — black face, arms, hands and legs — with two white eye marks gone wide and no mouth (a blank black face), green hair and a scholar's coat, on a ladder catching a falling stack of books. |
| Roadmap | Three heroes walking a winding road over hills toward a far city. Each hero's skin is solid matte black — black faces, arms, hands and legs — with two white eye marks and no mouth (a blank black face). One points ahead, one lags behind with a heavy bag, one walks backwards talking. |
| Download | A hero whose skin is solid matte black — black face, arms, hands and legs — with two white eye marks curved in a laugh and no mouth (a blank black face), red hair and a red scarf, leaping out of a giant book page into a bright grassy world. |

## Game Guide — Guide illustration (×4) — box 1200 × 500

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright warm colours. Correct anatomy with natural proportions: every character has exactly two arms and two legs, every leg ends in a visible foot or boot, hands hold weapons by the handle. A gentle low camera angle with no fisheye distortion. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

Wide image. A candid moment: [SCENE]
```

| Guide page | Fill `[SCENE]` with |
| --- | --- |
| The World of Unwritten | Fourteen heroes standing in a row on a giant book page, seen from behind and below, each holding a different weapon (sword, greatsword, axe, warhammer, spear, bare fists, bow, dagger, katar, shuriken, sniper rifle, staff, wand, scythe), their skin solid matte black, cloaks blowing in the wind, black ink blots creeping in at their feet. |
| Playstyles & Builds | Three friends arguing over loot at a campfire at dusk. Each friend's skin is solid matte black — black faces, arms, hands and legs — with two white eye marks and no mouth (a blank black face). One holds a sword, one a staff, one a bow, and each holds up a different glowing ring. |
| World & Maps | A hero whose skin is solid matte black — black face, arms, hands and legs — with two white eye marks and no mouth (a blank black face), spreading a big map across a table at an inn; the map shows grassland, snow, desert, a volcano and a dark ink-smeared corner, each region drawn on its own torn page. |
| Getting Started | A hero whose skin is solid matte black — black face, arms, hands and legs — with two white eye marks closed sleepily and no mouth (a blank black face), waking up on a grassy hill and stretching, while a pale blue slime blob stares at them curiously from the grass. |

## Download

### Windows and Mac icons

**No prompt.** Use the official Windows and Apple logos from their own brand pages, or leave the button without an
icon — never a generated look-alike.

### Launcher screenshot — box 1000 × 640

**No prompt — take it from the real launcher** once it has the new look.

## Media

### Wallpapers

```text
Send the text below to the image tool exactly as written, word for word. Do not change a single word.

Hand-drawn anime illustration in the style of classic Ragnarok Online official art from the early 2000s: confident ink linework with varied line weight, clean cel shading with soft painted colour, bright sunny warm colours. Correct anatomy with natural proportions: every character has exactly two arms and two legs, every leg ends in a visible foot or boot, hands hold weapons by the handle. A gentle low camera angle with no fisheye distortion. Not pixel art, not 3D, not chibi. No text, no letters, no logos.

[SIZE] image. A candid moment: [SCENE]
```

| Wallpaper | `[SIZE]` | Fill `[SCENE]` with |
| --- | --- | --- |
| Key art | Wide | The composition and scene paragraphs from the Home key art prompt, without its middle-band paragraph. |
| A Kashira | Wide | A [WEAPON] standing upright in the ground on a windy hill at sunset, petals and storybook pages blowing past it. No people. |
| A shadow | Wide | A tall figure whose skin is solid matte black and smeared with dripping ink, with two white eye marks and no mouth (a blank black face), in dark tattered armour, turning to look over its shoulder while standing on a torn book page. |
| Phone | Tall | The composition and scene paragraphs from the Home key art prompt, redrawn tall: the archer huge at the bottom, the swordsman and the mage above her, the tower and cheering townsfolk at the top. |

### Concept art

Each row is the part after the style line: send it with the send-order line and the style line in front of it,
exactly as in the prompts above.

| Box | Size | Text after the style line |
| --- | --- | --- |
| Monster art — Grassland | Square | `A creature design sheet of one cute round anime monster, [LOOK FROM THE TABLE], with a small torn-paper edge: front, side and back views plus three small expression sketches (happy, angry, sleepy), plain #f7f8fa background.` |
| Monster art — Snow | Square | The same with a Snow monster's look. |
| Monster art — elite | Square | The same with an elite's look, bigger and more menacing, with torn paper at its edges. |
| Character concept sheet | Wide | `A character design sheet of one hero whose skin is solid matte black like black paper — black face, black neck, black arms, black hands, black legs, no normal skin tone anywhere — with two small white eye marks and no mouth or nose — the face is blank black except the two eye marks, [HAIR] and [CLOTHES]: front, side and back views and a row of eye-mark expressions, plain #f7f8fa background.` |
| Town concept art | Wide | `The first town on a sunny afternoon, seen from a gentle low angle: a plaza with a fountain, stone cottages and market stalls. A candid crowd moment: a merchant chasing a brown mouse monster that stole an apple, while heroes laugh at it. Every person's skin is solid matte black — black faces, arms, hands and legs — with two white eye marks and no mouth (a blank black face), in colourful clothes.` |
| Item art sheet | Wide | `A catalogue page of fantasy weapons and armour laid out in neat rows, item illustrations with inked outlines and soft painted shading, plain #f7f8fa background.` |
