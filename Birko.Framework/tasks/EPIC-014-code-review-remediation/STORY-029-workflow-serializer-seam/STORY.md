---
id: STORY-029
parent: EPIC-014
status: done
created: 2026-07-17
completed: 2026-07-17
theme: workflow-serializer-seam
origin: follow-on from CR-L416 (STORY-027, Batch DI)
affects: [Birko.Workflow.CosmosDB, Birko.Workflow.ElasticSearch, Birko.Workflow.MongoDB, Birko.Workflow.RavenDB, Birko.Workflow.SQL, Birko.Workflow.JSON, Birko.Workflow.XML]
---

# Workflow backends — unify the serialization seam (ISerializer everywhere)

## Outcome (2026-07-17) — DONE

All **seven** workflow instance models now route (de)serialization through
`Birko.Serialization.ISerializer` (injectable `ISerializer? serializer = null`) and share one wire
format:
- **JSON-string backends** (CosmosDB, ElasticSearch, MongoDB, RavenDB, SQL, JSON) default to
  `new SystemJsonSerializer()` — **camelCase**, matching the framework's deliberate convention
  (BackgroundJobs `JobSerializationHelper`, the Data.JSON store). CosmosDB/ES/Mongo/Raven were
  migrated off raw `System.Text.Json`; SQL was flipped from its CR-L416 PascalCase pin.
- **XML backend** already used `SystemXmlSerializer` (element-based, format N/A).
- Every model now **throws on a null Guid** (was `Guid ?? Guid.NewGuid()`, which diverged from the
  document id and duplicated on the next SaveAsync) and on empty/whitespace/null-deserialize payload
  (the `!` suppression is gone family-wide). ES already had the Guid guard (CR-L406).

Tests per backend: camelCase wire-format pin + injectable-serializer override + null-Guid + the
existing corrupt-record guards. All green — Cosmos 8, ES 8, Mongo 8, Raven 8, SQL 12, JSON 10, XML 8.
Verified via the round-trip tests (incl. non-empty `StateChangeRecord` history) that camelCase
round-trips. Safe: no persisted workflow data exists (Symbio uses only `Birko.Workflow` core).

Commits — source/test per repo: Cosmos f5c4f6a/092b019, ES 6c0f5e3/b1c48b1, Mongo 6cf4a45/e86f43d,
Raven 040f2c2/452e7db, plus SQL/JSON/XML (see git log). Codebase-wide sweep (below) confirmed no
other persistence layer needs migrating.

## Why this is its own story

This is **not** one of the audit's filed low findings. CR-L416 (STORY-027) only asked the
**SQL** workflow model to route its `DataJson`/`HistoryJson` (de)serialization through
`Birko.Serialization.ISerializer` instead of raw `System.Text.Json`, to match the **JSON**
reference model. Closing it exposed that the workflow-backend family is inconsistent:

| Backend | Serialization path (before this story) | Wire format |
| --- | --- | --- |
| Birko.Workflow.JSON | `ISerializer` (`SystemJsonSerializer()` default) | **camelCase** |
| Birko.Workflow.SQL | `ISerializer` (PascalCase default) — done in CR-L416 | PascalCase |
| Birko.Workflow.CosmosDB | raw `System.Text.Json` (no options) | PascalCase |
| Birko.Workflow.ElasticSearch | raw `System.Text.Json` (no options) | PascalCase |
| Birko.Workflow.MongoDB | raw `System.Text.Json` (no options) | PascalCase |
| Birko.Workflow.RavenDB | raw `System.Text.Json` (no options) | PascalCase |

So there are two axes of inconsistency:
1. **Seam:** only JSON + SQL use `ISerializer`; the other four call `System.Text.Json` directly (no
   injectable override, can't share converters / naming conventions).
2. **Wire format:** the JSON backend is the lone **camelCase** outlier because
   `SystemJsonSerializer`'s *parameterless* default is camelCase; everyone else is PascalCase
   (raw `System.Text.Json`'s default = property names as-declared).

The PascalCase on the four raw backends is **incidental** (the out-of-the-box default), not a
designed choice — none of them ever adopted the abstraction.

## Scope

Extend the `ISerializer` seam to the **four raw backends** so all six workflow models share one
injectable serialization path, mirroring what SQL/JSON already do:

- For each of **CosmosDB / ElasticSearch / MongoDB / RavenDB** models:
  - `ToInstance` / `FromInstance` / `UpdateFromInstance` take `ISerializer? serializer = null`,
    defaulting to a static `SystemJsonSerializer`.
  - Add the `Birko.Serialization` projitems import to each backend's `.Tests` project (no consumer
    csproj changes needed — nothing consumes these backends yet; Symbio uses only `Birko.Workflow`
    core with no persistence backend).
  - Add a wire-format-pin test + an injectable-serializer test to each.
- While in each model, also fold in the **`Guid ?? System.Guid.NewGuid()` fabrication** analogue
  (the ES CR-L406 pattern — only ES was filed+fixed): CosmosDB/JSON/MongoDB/RavenDB `ToInstance`
  still mint a random InstanceId on a null Guid, diverging from the document and duplicating on the
  next SaveAsync upsert. Throw on a null Guid instead.

## Codebase sweep (2026-07-17) — where else is a serializer used directly?

Swept the whole framework for raw `System.Text.Json` / `XmlSerializer` / `JsonConvert` usage to check
whether the "hardcoded serializer instead of the `ISerializer` abstraction" anti-pattern exists
beyond the workflow backends. Result — **the four workflow backends above are the only genuine
persistence-domain-data offenders.** Everything else is legitimate:

- **Already uses the abstraction (the intended pattern):**
  - `Birko.BackgroundJobs` — `JobSerializationHelper` (shared across ALL job backends) wraps
    `SystemJsonSerializer`. **camelCase.**
  - `Birko.Data.JSON` store (`AbstractJsonStore`/`AbstractAsyncJsonStore`) — injectable `ISerializer`,
    defaults to `SystemJsonSerializer`. **camelCase** (indented).
  - `Birko.Workflow.JSON` + `Birko.Workflow.SQL` (SQL done under CR-L416).
- **Legitimately direct (abstraction would be wrong or irrelevant) — leave as-is:**
  - AI providers (`Birko.AI` / `Birko.AI.Providers` — Claude/OpenAI/Gemini/Ollama/Azure/etc.),
    `Birko.Communication.NFC|IR|GraphQL` transports: these serialize to **external API wire
    contracts** dictated by the remote service, not a Birko-chosen format.
  - `Birko.Serialization*` implementations themselves (`SystemJsonSerializer`,
    `SystemXmlSerializer`, `NewtonsoftJsonSerializer`) — they ARE the abstraction.
  - `Birko.Data.CosmosDB/Serialization/CosmosGuidIdSerializer` — a custom Cosmos serializer.
  - `Birko.Storage/Local/LocalFileStorage` — its private `FileMetadata` sidecar via a
    **source-generated** `JsonSerializerContext` (a deliberate AOT/trimming choice, not user data).
  - `Birko.Data.Sync.Tenant/TenantSyncProvider` — serializes only to feed a **SHA256 change-hash**
    (never stored/round-tripped).
  - `Birko.DesignTokens/Model.cs` — design-token config model.

So STORY-029's scope (the 4 raw workflow backends) is **complete** — no other persistence layer needs
migrating.

## Wire-format decision (REVISED after the sweep)

**Recommended: unify all six workflow backends to camelCase** — because that is the framework's
**deliberate** convention wherever the `ISerializer` abstraction is actually used for persistence
(`BackgroundJobs` `JobSerializationHelper`, the `Birko.Data.JSON` store, and the `Birko.Workflow.JSON`
backend are all camelCase). The PascalCase on Cosmos/ES/Mongo/Raven is **incidental** (raw
`System.Text.Json`'s default), not a designed choice.

Concretely: default every workflow backend's fallback serializer to the plain
`new SystemJsonSerializer()` (camelCase). This changes the wire format of the four raw backends
PascalCase→camelCase **and means revisiting `Birko.Workflow.SQL`**, which CR-L416 pinned to PascalCase
purely to preserve its *incidental* raw format — flip it to camelCase here so the whole family (all six
+ BackgroundJobs + Data.JSON store) shares one convention.

This is **safe**: verified no persisted workflow data exists anywhere (the only possible consumer,
Symbio, wires no workflow persistence backend), so no migration is required regardless of format.

> Supersedes the earlier PascalCase proposal, which was based on majority-of-*incidental*-format
> rather than the framework's *deliberate* convention. Confirm at execution time.

## Success criteria

- All six workflow instance models (JSON, SQL, CosmosDB, ElasticSearch, MongoDB, RavenDB) route
  (de)serialization through `Birko.Serialization.ISerializer` with an injectable override.
- All six share one wire format (camelCase per the revised decision above — matching the framework's
  deliberate `ISerializer` convention; includes flipping the CR-L416 SQL backend from PascalCase).
- Each backend's `ToInstance` throws a clear error on a null Guid (no random-InstanceId fabrication)
  and on empty/whitespace/null-deserialize `DataJson` (the corrupt-record guard already applied to
  ES/JSON/Mongo/Raven/SQL under STORY-027).
- Each backend's `.Tests` project has a wire-format-pin + injectable-serializer test; all green.
- One commit per repo (polyrepo), `Co-Authored-By` trailer.

## Notes

- STORY-027 CR-L415/L416 (SQL) already applied the seam to SQL with a PascalCase default and a
  `FromInstance_PreservesPascalCaseWireFormat` pin — use it as the template.
- The store-level `SaveAsync`/`FindBy*` concurrency + schema tests remain integration-tier
  (STORY-028); this story is model-layer only.
