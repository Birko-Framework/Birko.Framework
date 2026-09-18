---
id: EPIC-010
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Data.RavenDB]
---

# Birko.Data.RavenDB — Index ergonomics

## Area of concern

Final remaining RavenDB index management enhancement — attribute-driven index definitions (Option B from the original design). Bulk-deploy and Map/Reduce query helpers are already done.

## Success criteria

- Decorating a model with index attributes auto-discovers and deploys the index
- Existing `IndexDefinition` / `AbstractIndexCreationTask` paths still work unchanged
- Tests cover discovery + deployment + idempotency
