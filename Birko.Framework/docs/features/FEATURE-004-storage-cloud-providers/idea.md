---
id: FEATURE-004
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Storage — Cloud providers

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-004](../../../tasks/EPIC-004-storage-cloud-providers/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Extend `Birko.Storage` with AWS S3, Google Cloud Storage, and MinIO (S3-compatible) implementations of `IFileStorage`. Pairs with the existing `Birko.Storage.AzureBlob` and `Birko.Storage` (local file system).

## Proposed shape

- Three sibling shared projects exist and are registered everywhere
- Each implements `IFileStorage` (read/write/exists/delete/list, streaming, metadata)
- xUnit tests against SDK mocks or a local LocalStack/MinIO instance

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Storage.Aws, Birko.Storage.Google, Birko.Storage.Minio]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
