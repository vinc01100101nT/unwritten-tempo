---
name: art-prompts
description: ALWAYS use when writing a prompt for a generated picture — website art, key art, banners, guide pictures, wallpapers, concept art, weapon icons, or a weapon's skill icon sheet — for Leonardo.ai (GPT Image 2.5 Sunburst) or any image generator. Also when slicing a returned skill icon sheet into the game. Points to the one standard every picture follows.
---

# Art prompts

**The standard is `docs/4-art/website-art-prompts.md`. Read the whole doc before writing any prompt** — it holds
the style paragraph, the skin paragraph, the Leo settings and limits, the crop table for every website box, the
monster looks, the Kashira theme table, and a finished prompt for every box. This skill only says where it is and
what can never be broken; the doc is the rule.

## Never broken, even before the doc is open

- No Ragnarok anything in a prompt: not the name, not its monsters, not "like the Poring".
- No negative prompt — Leo's model has no box for one. Say "no …" inside the prompt.
- Under 2,000 characters per prompt (Leo's limit). Count it.
- One image per generation and one reference picture; six pictures a day across three accounts, so batch: a
  weapon's whole skill set goes in ONE sheet.
- Every person's black skin is written into the prompt every time; monsters are described by look, never by name.
- A weapon's skill icons are built from its Kashira's theme (the doc's table).
- Prompts go to the user in ` ```text ` blocks, ready to paste.
- **A fix for a bad result goes into every prompt it applies to, in the same turn.** When a picture comes back
  wrong and the fix is not about that one picture (a weapon pasted on like a sticker, a weapon leaning on something
  that is not beside the hero), sweep every prompt that could make the same mistake and fix them all, then say
  which ones changed. Fixing only the prompt that failed is how the same bad picture comes back next week.

## After a sheet comes back

Cut it into one 128 × 128 picture per skill in `unity/Assets/1_Authored/Art/Icons/Skills/`, named by skill id, and
set each skill's **icon** in the Skill Composer (or its `icon` field). Saving a skill re-exports the wiki copies.

## Keeping it true

When a new rule is agreed, write it into the doc, not here — this file stays a pointer.
