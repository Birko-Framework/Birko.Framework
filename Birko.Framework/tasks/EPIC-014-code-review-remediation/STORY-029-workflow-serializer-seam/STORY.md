---
id: STORY-029
parent: EPIC-014
status: planned
created: 2026-07-17
theme: workflow-serializer-seam
origin: follow-on from CR-L416 (STORY-027, Batch DI)
affects: [Birko.Workflow.CosmosDB, Birko.Workflow.ElasticSearch, Birko.Workflow.MongoDB, Birko.Workflow.RavenDB, Birko.Workflow.JSON]
---

# Workflow backends — unify the serialization seam (ISerializer everywhere)

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

## Wire-format decision (proposed)

**Recommended: unify all six to PascalCase.** Default every backend's fallback serializer to
`new SystemJsonSerializer(new JsonSerializerOptions())` (PascalCase = System.Text.Json's own
default), and **flip the JSON backend** camelCase→PascalCase. This:
- changes only **one** backend's wire format (JSON), since the other five are already PascalCase;
- gives all six the same seam **and** the same wire format;
- is **safe** — verified no persisted workflow data exists anywhere (the only possible consumer,
  Symbio, wires no workflow persistence backend), so no migration is required.

Alternative (rejected unless a reason surfaces): unify everyone to camelCase — would change four
backends' wire format instead of one. Confirm the PascalCase choice at execution time before
touching the JSON backend, since the JSON model is the audit's cited reference.

## Success criteria

- All six workflow instance models (JSON, SQL, CosmosDB, ElasticSearch, MongoDB, RavenDB) route
  (de)serialization through `Birko.Serialization.ISerializer` with an injectable override.
- All six share one wire format (PascalCase per the decision above).
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
