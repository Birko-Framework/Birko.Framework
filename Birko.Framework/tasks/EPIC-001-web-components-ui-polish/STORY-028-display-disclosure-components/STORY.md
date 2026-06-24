---
id: STORY-028
parent: EPIC-001
status: in-progress
created: 2026-06-19
---

# Display & disclosure components

## User story

As a developer, I want the display/layout component gaps surfaced by the Birko.Web Playground ([[TASK-038]]) closed, so the catalogue covers fixed-height charts and a reusable disclosure group without consumer workarounds.

## Behaviour

- `b-chart` coerces a unitless `height` to `px` and never stretches past its configured height ([[TASK-039]])
- A shared `coerceCssLength` helper + `BaseComponent.lengthAttr` in `Birko.Web.Core` closes the same unitless-length bug across `b-skeleton` and the data viewers ([[TASK-041]])
- A framework-native `b-accordion` (collapsible / disclosure group) ships, tokenized, accessible, and keyboard-operable ([[TASK-040]])
- All surfaced while building the playground gallery; existing component behaviour is unchanged otherwise
