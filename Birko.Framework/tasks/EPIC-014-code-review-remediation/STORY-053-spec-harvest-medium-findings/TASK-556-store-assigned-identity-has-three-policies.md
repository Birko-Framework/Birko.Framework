---
id: TASK-556
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [SH-M313, SH-M314]
pr: null
github-issue: null
jira-key: null
---

# Store-assigned identity follows three different policies across backends — one of them overwrites the caller's Guid

## Context

Spawned from [[TASK-163]] (2026-10-11) while verifying `SH-M313` / `SH-M314`. Those two were filed against the
in-memory store (`Guid.Empty` survives `data.Guid ??= Guid.NewGuid()`, is stored under the empty key, and
`CreateCore` then returns `Guid.Empty` — the same value it returns for `null` data; the `Read(Guid)` override then
refuses `Guid.Empty`, so the row is unreachable by id). **Confirmed, and the shared root cause is wider:** "give
this entity an identity" has no single producer (rule 16), and the ~55 sites that do it follow three policies:

| Policy | Where (measured 2026-10-11) |
|---|---|
| Assign only when `null` — **`Guid.Empty` is kept as the id** | `??=` in 40 sites: in-memory, JSON, XML, SQL single-item `CreateCore` (`DataBaseStore.cs:197`, `AsyncDataBaseStore.cs:265`), MongoDB, RavenDB single-item, CosmosDB, ElasticSearch single-item, InfluxDB, EventSourcing wrappers, ViewModel repositories |
| Assign when `null` **or** `Guid.Empty` | ElasticSearch bulk (`ElasticSearchStore.cs:232`), CosmosDB async (`AsyncCosmosDBStore.cs:329`) |
| **Always overwrite**, even a caller-supplied Guid | SQL bulk create on every provider (`SQLiteStore.cs:47`, `MSSqlStore.cs:68`, `MySQLStore.cs:62`, `PostgreSQLStore.cs:65` + async twins), RavenDB bulk (`RavenDBStore.cs:399,412`, async `:452,468`) |

The third row is the serious one: a caller that sets Guids to link entities (parent/child, idempotent import) and
bulk-creates them gets **different** ids stored on the framework's default provider — and the same call keeps the ids
on JSON / in-memory, so tests pass. ⚠ **That row is a recorded decision, not an accident:** `PostgreSQLStore.cs:61-64`
cites CR-L190 ("bulk create assigns a fresh server-side Guid to every row, intentionally discarding any caller-supplied
Guid … use the single-item path to preserve a known id"). CR-L190 aligned the SQL providers with *each other*; it did
not look at the portable backends or at RavenDB's bulk path, which keep or overwrite differently. So this task
**reopens CR-L190 on the record** — keep it (and make every backend's bulk create overwrite, documented on
`IBulkStore.Create`) or reverse it — rather than quietly changing it.

`AbstractStore.Save` already treats `Guid.Empty` as "new" (`AbstractStore.cs:239`), which is the policy the first row
contradicts: `Save(entity with Guid.Empty)` → `Create` → stored under `Guid.Empty`.

## Acceptance criteria

- [ ] One producer — e.g. `AbstractModel.EnsureGuid()` (assign when `null` or `Guid.Empty`, keep any other value) —
      used at every site above; no store re-implements the rule
- [ ] CR-L190 decided on the record: bulk create either keeps a caller-supplied Guid on **every** backend (SQL and
      RavenDB then can hit a duplicate key — test it, consistently with [[TASK-163]]'s in-memory "create never
      replaces") or overwrites it on every backend, documented on `IBulkStore.Create` / `IAsyncBulkStore.CreateAsync`
- [ ] In-memory `Read(Guid)` and the base `Read(Guid)` agree on `Guid.Empty` once nothing can be stored under it
- [ ] A per-provider contract test: create with `null`, with `Guid.Empty`, and with a caller Guid (single and bulk);
      red-verified, including the bulk-overwrite case on SQLite
- [ ] CHANGELOG migration entry for the bulk-create behaviour change
- [ ] `/specs regen store-crud-contract`

## Out of scope

- `Update` / `Save` of an entity that is not stored — [[TASK-555]].

## Human test plan

N/A — a library contract with no UI surface; fully covered by the per-provider contract tests.

## Implementation plan

_Populated by `/tasks plan TASK-556` — leave empty until then._
