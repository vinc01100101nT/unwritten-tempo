# A Screen Effect Reads the Camera, Not Only Its Settings

**Reach for this when:** a full-screen effect (outlines, edge ink, fog, grain) looks different through a second camera, a preview or a picture-in-picture, even though both use the same effect with the same settings.

## The rule

A screen-space effect whose settings are in PIXELS also depends on how much of the view one pixel covers:
tan(half view angle) ÷ picture height. Two cameras with the same settings but different view angles or
different picture sizes produce different results. To make a second camera look the same as the main one, match
that per-pixel share too. Then back the camera off along its line of sight so the framing stays the same.

## Why

2026-09-29: the character-creation preview drew far too many ink lines. Round one matched the preview's picture
size to its box, so the lines were the same 2.19 pixels thick as in the world, and the fix was reported at 90%.
They still looked overdone. The ink finds creases by comparing depth a fixed number of pixels apart, and the
preview's pixel covered 3.1 times more of the view than the world camera's pixel (30° over 265 pixels, against 35°
over 972). That produced about three times as many crease lines. Matching the pixel SIZE was only half of it.
Cost: two rounds, with the user seeing the same bug come back.

## How to apply

1. List every input the effect's shader reads, not only its exposed settings: the screen size, the depth and its
   range, the camera's view angle.
2. Compute tan(fov/2) ÷ pixel height for both cameras. If they differ, set the second camera's view angle so they
   match. Then move it back along its line of sight by tan(old half-angle) ÷ tan(new half-angle), measured from the
   point where that line crosses the subject.
3. Prove it by rendering the second camera to a file and looking at it, before reporting.
