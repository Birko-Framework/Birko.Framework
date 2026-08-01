---
id: FEATURE-001
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Web.Components — UI polish

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-001](../../../tasks/EPIC-001-web-components-ui-polish/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Remaining ergonomic improvements to the Shadow DOM web component catalogue: a `bare` attribute for inline-form usage, a benchmark-gated migration of `b-editable-table` cells onto the new bare form components, `size` variants on three components that still lack them, and the display/disclosure gaps surfaced by the Birko.Web Playground (chart sizing, a `b-accordion`).

## Proposed shape

- `bare` attribute lands consistently on all form controls
- `b-editable-table` migration decision is made (with benchmark data either way)
- `b-pagination` / `b-dropdown-menu` / `b-breadcrumb` each ship a `size` variant when a concrete consumer use case appears
- Display & disclosure components closed (STORY-028): `b-chart` unitless-height fix, a framework-native `b-accordion`

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Web.Components]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
