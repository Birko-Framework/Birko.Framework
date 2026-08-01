---
id: FEATURE-007
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Telemetry — Additional exporters

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-007](../../../tasks/EPIC-007-telemetry-exporters/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Expand `Birko.Telemetry` beyond the existing OTLP exporter — add Prometheus (metrics scraping), Seq (structured log shipping), and Grafana LGTM stack (Loki/Tempo/Mimir with optional dashboard provisioning).

## Proposed shape

- Three sibling shared projects exist and are registered
- Each hooks into `Birko.Telemetry`'s metric / log / trace pipeline via DI extensions
- Grafana exporter optionally provisions dashboards via the Grafana HTTP API

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Telemetry.Prometheus, Birko.Telemetry.Seq, Birko.Telemetry.Grafana]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
