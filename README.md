# Roguelike MMO

A 2D MMORPG × Roguelike. This repo currently holds the first interactive prototype:
the **Level-Up Skill Draft** — a roguelike-style skill draft (2 guaranteed + 1 random
wildcard each level) grafted onto a persistent character, where choices steer the
player through a skill graph toward an advanced job class.

> The project name `roguelike-mmo` is a placeholder — rename it freely.

## Concept docs

The design lives in Tempo (cloud, org-scoped):

- **Skill Progression System — Design Doc** (the full mechanic)
- **PRD — Level-Up Skill Draft (Prototype)**
- Tracking issue **VIN-1**

## Run it

```bash
npm install
npm run dev
```

Then open the printed local URL (default http://localhost:5173). The skill draft is the
whole app.

## Layout

```
src/
  App.tsx                 # renders the skill-draft experience
  main.tsx                # entry
  styles/globals.css      # Tailwind v4 + theme
  pages/
    SkillDraftPage.tsx    # the screen: draft cards + tree + build panel (holds state)
  skill-draft/
    graph.ts              # the shared skill-graph dataset + draw algorithm
    SkillCard.tsx         # a draftable option card (+ hover tooltip)
    SkillTree.tsx         # the tree; connectors computed from real node positions
    Tooltip.tsx           # floating skill helper
    BuildPanel.tsx        # owned skills + "heading toward" class
mockups/
  level-up-screen.html    # the original static HTML mockup (reference)
```

## Tech

Vite + React + TypeScript + Tailwind CSS v4.
