---
id: EPIC-001
status: in-progress
created: 2026-05-28
owner: ai
affects: [Birko.Web.Components]
---

# Birko.Web.Components — UI polish

## Area of concern

Remaining ergonomic improvements to the Shadow DOM web component catalogue: a `bare` attribute for inline-form usage, a benchmark-gated migration of `b-editable-table` cells onto the new bare form components, `size` variants on three components that still lack them, and the display/disclosure gaps surfaced by the Birko.Web Playground (chart sizing, a `b-accordion`).

## Success criteria

- `bare` attribute lands consistently on all form controls
- `b-editable-table` migration decision is made (with benchmark data either way)
- `b-pagination` / `b-dropdown-menu` / `b-breadcrumb` each ship a `size` variant when a concrete consumer use case appears
- Display & disclosure components closed (STORY-028): `b-chart` unitless-height fix, a framework-native `b-accordion`
