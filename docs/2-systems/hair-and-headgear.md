# Hair and Headgear

**Status: BUILT 2026-10-10, not play-tested.** It was planned by the planner, attacked by the red team, and the
user decided the open questions the same day.

## What the player gets

- Every character body is drawn bare-headed. Hair and hats are separate pieces put on top.
- **Every character wears its head the same way, with no special cases:** a hairstyle (one of 13, or none), a hair
  colour, and whatever Head item it wears. That covers players, other players, townsfolk and Shadow creatures.
- **Character creation:**
  - The body picker is unchanged.
  - Under it, a **Hair** picker steps through "No hair" and "Hair 1 of 13" … "Hair 13 of 13", using the same
    stepper as the body picker.
  - When there is hair, the Colours row gains one more swatch, painted with the same colour picker the clothes use.
  - A new character starts with no hair chosen, so each body first shows the hair it was made with.
  - An animal's head (Cow, Pug) is offered no hair.
- **Hats are items.** Fifteen Head items, one per headgear model, worn in the Head slot and seen by every other
  player.
  - They have no effect: no stats, no word, weight 0, worth 0, and they are never handed out at random.
  - The full helmets and the hood hide the hair under them.
  - Every other hat that covers the head cuts the hair where the hat sits on the head, so only the hair hanging
    below it shows and none sticks out above it.
  - The two headbands sit on the head without covering it, and leave the hair whole.
  - Which hats exist, what they do and how they are obtained is still to be planned. Until then a GM gives them.
- **Townsfolk** wear the hair their body was made with, and the hat their body had now comes from the matching Head
  item.
- **Shadow creatures** wear hair only, no headgear. Their Leather Cap has no model, so nothing shows.
- **Beards and moustaches are gone from everybody**, the wizard Kaelen included.
- **One height for everyone.** A character is sized by its body alone, so a hat or a big hairstyle never shrinks it.
  The Witch, Wizard and Elf bodies, drawn at 73–77% of a bald body before, now stand as tall as everyone else.

## The pieces

**The 13 hairstyles.** Each is cut from the first model named; the other models listed carry the identical shape.

| Hair | Cut from | Also on | Notes |
|---|---|---|---|
| 1 | BlueSoldier_Female | Casual2_Female, Doctor_Female_Old, OldClassy_Female, Soldier_Female, Suit_Female, Witch, Goblin_Female | |
| 2 | Casual_Female | Cowboy_Female, Ninja_Female, Ninja_Sand_Female | |
| 3 | Worker_Female | | |
| 4 | Knight_Golden_Female | | |
| 5 | Doctor_Female_Young | Casual3_Female, Chef_Female | |
| 6 | Kimono_Female | Pirate_Female | |
| 7 | Zombie_Female | | |
| 8 | Viking_Female | | |
| 9 | Ninja_Male_Hair | | |
| 10 | Casual_Male | Cowboy_Hair, Doctor_Male_Young, Suit_Male | |
| 11 | Chef_Male | OldClassy_Male, Doctor_Male_Old | Moustache left out |
| 12 | Casual2_Male | | |
| 13 | Viking_Male | | The mohawk alone; the beard left out |

**The 15 Head items**, each with the model it shows:

| Item | Model cut from |
|---|---|
| Army Helmet | Soldier_Male |
| Chef's Hat | Chef_Hat |
| Cowboy Hat | Cowboy_Female |
| Pointed Hat | Elf |
| Witch Hat | Witch |
| Tailed Headband | Kimono_Male |
| Headband | Kimono_Female |
| Golden Horned Helm (hides hair) | Knight_Golden_Male |
| Open Golden Helm | Knight_Golden_Female |
| Plumed Helm (hides hair) | Knight_Male |
| Ninja Hood (hides hair) | Ninja_Male |
| Top Hat | OldClassy_Male |
| Pirate Hat | Pirate_Male |
| Horned Helm | VikingHelmet |
| Hard Hat | Worker_Male |

**Bodies that needed a head.**
- The three knights and the two ninja bodies had a helmet or a hood instead of a head. Each now has the plain head
  every other body has.
  - The knights' textures have no white or skin colour, so their new head is drawn with the plain-head model's own
    material: one extra draw call on those three bodies.
- Elf, Witch and Wizard had only two of the four eye marks, because a hat covered the rest. They now have the plain
  head's four.
- Cow, Pug, both Goblins and both Zombies keep their own faces; only their hair was cut off.

## How it is built

### The bake

`HeadPieceBaker.BakeAll(true)` is run through the live editor; `BakeAll(false)` only reports what it would do. It
reads the model files and never writes them. It finds every patch of triangles that follows the head bone and cuts
out the hair, hats, beards and moustaches. It then writes:
- `2_Generated/Characters/Bodies/<Model>.prefab`: every model bare-headed.
  - The `Characters/<Model>` Addressable address now points at this bare body, under the same name.
- `2_Generated/Characters/HeadPieces/`: the 13 hairstyles and the 15 headgear pieces.
  - Each piece has its own colour strip of a few exact colours, so it never pulls in a whole model's texture.
  - The strips are uncompressed on purpose, so their colours stay exact (ASSETS.md rule 1).
  - Re-baking edits each piece in place, so the items that show it keep their link.
- `3_Data/Resources/HeadPieces.asset`: the hairstyles in creation order, and for every body the hair it was made
  with, in that body's colour.

`CharacterDyePartsBaker.BakeAll()` now reads the bare bodies, so a hair's colour is never offered as a clothes
colour.

### On the body

`HeadSocket` sits on every body, beside `WeaponSocket`, and puts the hair and the worn Head item's model on the head.
It is added by:
- your own avatar (`Player3DBody.Configure`)
- another player's body (`NetPlayer.EnsureBody`)
- a townsperson (`NetNpc`)
- a Shadow (`MobShadow3DVisual`)
- the creation preview (`CharacterPreview`)
- the Mob Table preview, which uses its `Wear` and `PaintHair` steps.

It does nothing outside Play or on the map server.

- **Fixed to the head, not bent with the body.** Measured with 200 people: hair that bends with the body cost
  22.6 ms a frame (9.6 → 32.3 ms), while the same hair fixed to the head bone cost nothing measurable (9.0 ms). Every
  piece that follows the head bone at least 85% of the way is fixed to it, at that body's own resting offset for the
  head.
  - Only the Ninja Hood, which hangs over the shoulders (26% head), bends with the body. It has one copy of its mesh
    per body it is worn on, shared by everyone wearing it there.
- **Hair colour.** The chosen colour replaces the hair's own colour, and its other shade moves with it by
  brightness, through the same colour swap the clothes use.
- **Headgear** is drawn in its own colours.
- **A covering hat cuts the hair** (`HairCut`). The bake measures how low each covering hat comes down, counting
  only its points over the head itself, in 24 directions around the head (`HeadPiece.cutRim`). Hair higher than
  that in its own direction is cut away exactly, triangles crossing the line included, so a tilted hat cuts along
  its own tilt and a wide brim's tips do not cut too low.
  - The cut mesh is made once per (hair, hat) pair, the first time that pair is worn, and shared by everyone wearing
    it. The slowest measured cut took 5.6 ms. Making all 13 x 10 cuts at author time instead would have shipped about
    26 MB of hair.
  - The two headbands are marked as not covering the hair.
- **A full helmet or the hood** hides the hair by not putting it on at all, so a Shadow's revive cannot switch it
  back on.
- **The clothes colouring reads the body renderer only** (`CharacterDye3D`). Reading a piece too would have saved a
  wrong body name into the character.
- **Height reads the body alone** (`Character3DBody.MeasureHeight`, `Character3DPose.Measure`). A speech bubble
  still clears a worn hat (`HeadSocket.TopAbove`).

### Saving and sending

- **The save** carries `hairStyle` ("none" or a hairstyle id) and `hairColour` ("#RRGGBB").
  - Both empty means a character made before hairstyles. They wear the hair their body was made with, in the colour
    they gave that part at creation (`HeadPieces.Resolve`). The nine existing characters read this way; nothing in
    their saves was rewritten.
  - Loading a save, or choosing a character, always overwrites the hair, empty included, so the previous
    character's hair never stays on.
- **The character server:**
  - A new character must name a hairstyle its body may wear, and a colour when it has hair.
  - The list and progress replies return the hair.
  - The look-keeping merge copies the hair only where one is stored (`jsonb_strip_nulls`).
  - The allowed hairstyles are exported beside the body list by `accounts/scripts/sync-roster.mjs`, into
    `accounts/data/character-models.json`.
- **Other players** receive the hair the server works out in `NetPlayer.DressOnServer`, in one value
  (`HeadPieces.Pack`). Their Head item arrives in the worn list they already receive.
- **Townsfolk** rows carry `hairStyle`, `hairColour` and `headgear` (a Head item id), sent to watchers by `NetNpc`.
- **Shadow creatures** carry `hairStyle` and `hairColour` on their creature entry.
- **The shared character string** moved to `unwritten-character-v22` with this work; both sides say `unwritten-character-v26` today.

### Head items made of nothing

A worn piece whose material is None is worn for its look alone. It rolls no armour word and is never handed out at
random (`EquipmentInstance.RollWord`, `ItemCatalog`). Every armour piece made before this has a material, so no
existing item changed.

### Checks

- `tools/check-head-pieces.py` (proved by `tools/prove-head-pieces-check.py`) checks that:
  - every body address points at its bare body;
  - every body has a hair row;
  - every hairstyle and Head item model is a head piece;
  - every townsperson's hair and hat exist.
- `tools/check-shadow-looks.py` accepts a bare body as a roster entry and checks a Shadow's hairstyle and hair
  colour.

## Not built

- Choosing or changing hair after creation.
- Editing a creature's hair in the Mob Table: its preview shows the hair but has no control for it.
- Where the hats come from, and what they do.
