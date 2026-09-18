---
id: FEATURE-009
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: done
---

# Birko.Communication — Remaining protocols

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-009](../../../tasks/EPIC-009-communication-protocols/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Two remaining communication subsystems: gRPC support (client + server) and an OAuth2 authorization server (the client-side OAuth flows already exist in `Birko.Communication.OAuth`).

## Proposed shape

- `Birko.Communication.gRPC` sibling project with client + server primitives
- `Birko.Security.OAuth.Server` exposes token endpoint, authorization endpoint, client registration, consent management
- OAuth server persists clients + tokens via `Birko.Data.Stores`

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Communication.gRPC, Birko.Security.OAuth.Server]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
