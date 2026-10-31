---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - planning-production
---

## youtube update format template

**rules:**

- all lowercase (everything: headers, text, arrows)
- no icons or emoji
- clean section headers with no formatting
- fact-check asset names before suggesting (pxltgr = environment, sscary = character billboard)
- reusable across all updates

---

## template structure

```
progress update:

new assets
- [what changed with pxltgr/sscary/etc]
- [any technical asset notes]

[system/feature being added]
- [feature detail 1]
- [feature detail 2]
- [optional: open questions or brainstorming]

next: [upcoming work]
```

---

## example (2026-06-22)

```
progress update:

new assets
- environment rebuilt with pxltgr fantasy pack
- character swapped to sscary billboards (basic animations only—premium attack pack tba :d)

weather system
- rain + snow with wet ground reflections & particle effects
- full day/night cycle (sunrise → sunset → night)
- exploring: make weather mechanically meaningful (special events, conditions?)

next: attack animations, weather → gameplay mechanics
```

---

## usage

when creating a new update:

1. gather changes from the dev session
2. fact-check all asset references in code
3. follow the structure above
4. keep each bullet short & scannable
5. use this template, not icons or fancy formatting

---

## Related

- [Roadmap to Launch](roadmap-to-launch.md) — where to pull "what actually changed" from before writing an update.
- [GPT Concept Prompts — Paste-Me](../4-art/gpt-concept-prompts.md) — the asset names the fact-check step is about.
- [Mixamo Animation Integration — Workflow](../4-art/mixamo-animation-workflow.md) — the other half of the asset pipeline updates mention.
