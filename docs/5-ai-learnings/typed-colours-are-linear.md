---
status:
owner: vince
last-updated: 2026-10-28
tags:
  - ai-learnings
---

# A Colour You Typed Comes Out Nearly Black

## Reach for this when

A light, fill or tint colour you set reads as reasonable numbers (0.1, 0.2) but the picture is far darker than you
expected — "flat black at night", "the shadow light does nothing".

## The rule

This project renders in linear colour. A light or ambient colour typed as 0.10 reaches the screen as about 0.01; 0.22
as about 0.04 (roughly the value squared). Judge a lighting colour by what the engine reports it applies — read the
ambient probe or sample the screenshot — never by the number in the field.

## Why

On 2026-10-28 the night fill was (0.10, 0.12, 0.22). The user called the night "flat black"; reading the ambient probe
gave (0.010, 0.013, 0.040). A round was spent on a colour that looked sensible in the inspector and was black on
screen.

## How to apply

1. Read what the engine actually uses: `RenderSettings.ambientProbe.Evaluate(...)` for the fill, the light's
   `color * intensity` for a light, or the pixel of a screenshot.
2. When you pick a dark colour by eye, raise it until the measured value, not the typed one, is where you want it.
