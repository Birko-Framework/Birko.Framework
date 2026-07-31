---
id: TASK-060
parent: STORY-047
status: todo
priority: P2
assignee: ai
created: 2026-07-18
affects: [Birko.Data.MongoDB.Tests, Birko.Data.CosmosDB.Tests, Birko.Data.RavenDB.Tests]
---

# Run & review the live null-filter parser tests

## Scope

Execute the env-var-gated live null-filter tests against real backends and review the results.

## Steps

1. Bring up backends (docker):
   - MongoDB: `docker run -p 27017:27017 mongo` → `BIRKO_MONGO_HOST=localhost`
   - RavenDB: `docker run -p 8080:8080 ravendb/ravendb` → `BIRKO_RAVEN_URL=http://localhost:8080`
   - Cosmos: Azure Cosmos emulator (linux image or Azure) → `BIRKO_COSMOS_CONNECTION=<conn string>`
2. Run each suite with its env var set:
   - `dotnet test Birko.Data.MongoDB.Tests --filter MongoFilterMatrixLiveTests`
   - `dotnet test Birko.Data.CosmosDB.Tests --filter CosmosFilterMatrixLiveTests`
   - `dotnet test Birko.Data.RavenDB.Tests --filter RavenFilterMatrixLiveTests`
   Each runs the full shape matrix (STORY-047) and reports per-shape OK / DIVERGE / THROW on failure.
3. If any fail, determine whether it is a real semantic divergence (e.g. Cosmos `= null` vs `IS_NULL`) or a
   serialization/indexing nuance, and record the finding; fix or document as appropriate.
4. Follow-up (optional, per STORY-047): extend the live matrix beyond null, and adopt a real Skipped
   mechanism (`Xunit.SkippableFact` / Testcontainers).

## Done when

- The three live null tests pass against real backends (or divergences are documented with a decision).
- Any provider-specific null/missing behaviour is written up in the relevant sub-project CLAUDE.md.
