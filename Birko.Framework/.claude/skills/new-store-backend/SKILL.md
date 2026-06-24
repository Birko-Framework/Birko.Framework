---
name: new-store-backend
description: Add a new persistence backend (database, file format, cache, queue) to Birko.Framework — `Birko.Data.X` for general stores, `Birko.Workflow.X` for workflow persistence, `Birko.BackgroundJobs.X` for job persistence. Use when the user says "add a new store", "novy store backend", "novy persistence backend", "novy datovy store", "implement Birko.Data.Foo", "add Foo as a workflow backend", or similar requests to plug a new storage platform into one of the abstract store hierarchies. Encodes the Store Hierarchy (AbstractStore → AbstractBulkStore template-method pattern), the *Core override convention, the Settings descendant chain, the bulk Update/Delete filter overrides, and the required xUnit + FluentAssertions test scaffold. Use [[new-birko-subproject]] first to create the project skeleton, then this skill to fill in the store implementation. Reference implementations: `Birko.Data.ElasticSearch` (async/bulk), `Birko.Data.JSON` (file-based), `Birko.Data.XML` (file-based with System.Xml.Serialization quirks), `Birko.Data.InMemory` (simplest store — thread-safe ConcurrentDictionary, no persistence, no settings class).
---

# Birko Framework — New Store Backend

Implement a new persistence backend that plugs into one of Birko's abstract store hierarchies. The skill works for three families:

- **General data store** — `Birko.Data.X` (CRUD on arbitrary entities). Hierarchy: `AbstractStore → AbstractBulkStore` + async variants.
- **Workflow persistence** — `Birko.Workflow.X` (store workflow state). Hierarchy: same abstracts, plus `IWorkflowStore`.
- **Background job persistence** — `Birko.BackgroundJobs.X` (store queued jobs). Hierarchy: same abstracts, plus `IJobQueue`.

## Authoritative references — READ THESE FIRST when invoked

Open these in `C:\Source\Birko\Framework\Birko.Framework\` before generating anything. If anything below contradicts them, **follow the files**.

- `CLAUDE.md` § "Architecture / Store Hierarchy" — diagrams the `AbstractStore → AbstractBulkStore` chain and the `AsyncDataBaseStore<DB,T>` SQL variant.
- `CLAUDE.md` § "Conventions" — the contract every store must implement and the `*Core`-override rule.
- `CLAUDE.md` § "Reference Implementations" — points to ElasticSearch (async/bulk), JSON (file), XML (file with `System.Xml.Serialization` quirks), InMemory (simplest store, `ConcurrentDictionary`, no persistence).
- `C:\Source\Birko\Framework\Birko.Data.Stores\CLAUDE.md` — the abstract base classes the new store inherits from.
- `C:\Source\Birko\Framework\Birko.Data.ElasticSearch\CLAUDE.md` + `C:\Source\Birko\Framework\Birko.Data.ElasticSearch\Stores\` — **canonical async/bulk reference**. Mirror its file layout, async patterns, bulk batching, and `*Core` overrides.
- `C:\Source\Birko\Framework\Birko.Data.JSON\CLAUDE.md` + `C:\Source\Birko\Framework\Birko.Data.JSON\Stores\` — **canonical file-based reference**. Use this when the new backend is a single-file format.
- `C:\Source\Birko\Framework\Birko.Data.InMemory\CLAUDE.md` + `C:\Source\Birko\Framework\Birko.Data.InMemory\Stores\` — **simplest reference** (thread-safe `ConcurrentDictionary`, no persistence; `*Core` overrides only, no settings class). Start here when the new backend has no connection/file (in-memory, mock, or a cache-like store), or just to see the minimal shape of a complete `IBulkStore`/`IAsyncBulkStore` implementation.
- `C:\Source\Birko\Framework\Birko.Configuration\` — settings hierarchy (`ISettings → Settings → PasswordSettings → RemoteSettings → SqlSettings → MSSqlSettings/MySqlSettings/PostgreSqlSettings`). Pick the right ancestor before adding a new settings descendant.

## Prerequisite: project skeleton

The project files (`.shproj`, `.projitems`, `CLAUDE.md`, `README.md`, `License.md`, `.gitignore`, registration in `.slnx` / `.code-workspace` / `Birko.Framework.csproj`) come from [[new-birko-subproject]]. **Run that skill first** to scaffold the empty `Birko.Data.X` (or `.Workflow.X` / `.BackgroundJobs.X`) project, then return here to fill in the store classes.

If the project already exists, skip to "Inputs to gather" below.

## Inputs to gather from the user

1. **Family** — General data store / Workflow / BackgroundJobs. Determines which `IStore` interfaces to implement and which optional helpers (Bulk, IndexManagement, ViewModel, Views) make sense.
2. **Platform type** —
   - **Remote service** (database, search engine, cache) → settings descend from `RemoteSettings` (or `SqlSettings` if SQL).
   - **File on disk** (JSON, XML, YAML, binary) → settings descend from `Settings` directly (or `PasswordSettings` if encrypted).
   - **Embedded** (SQLite, LiteDB) → settings descend from `PasswordSettings`.
3. **Native bulk semantics** — does the platform have a native bulk-update / bulk-delete by filter (SQL `UPDATE … WHERE`, MongoDB `$set`, ES `UpdateByQuery`)? If yes, override `Update(filter, PropertyUpdate<T>)` + `Delete(filter)` for native performance. If no, fall back to the base class's default (read → mutate → write).
4. **Sync, async, or both?** — Default **both**. Birko's convention is to ship a sync `XStore : AbstractBulkStore` AND an async `AsyncXStore : AbstractAsyncBulkStore` pair.
5. **Companion `.ViewModel` project?** — Some platforms ship a `.ViewModel` sibling (e.g. `Birko.Data.ElasticSearch.ViewModel`). Only if the platform has natural read-model translation (e.g. ES `_source` → projection).
6. **Companion `.Views` project?** — Some platforms ship a `.Views` sibling for the Aggregation framework (`Birko.Data.ElasticSearch.Views`). Add if the platform supports server-side aggregation.
7. **Health check** — per `CLAUDE-maintenance.md` § "Health Check Requirements", an external-service backend **must** ship a health check. Pick `Birko.Health.Data` / `.Redis` / `.Azure`, or propose `Birko.Health.X`.

## Per-class checklist

### 1. Settings descendant — `XSettings`

Lives in `Birko.Configuration` (or in the new `Birko.Data.X` project if the settings are platform-specific enough). Inherit from the right ancestor:

- Remote DB → `: RemoteSettings` (gives you `UserName`, `Password`, `Port`, `UseSecure`, `Location` = host).
- Remote SQL → `: SqlSettings` (adds `CommandTimeout`, `ConnectionTimeout`, abstract `GetConnectionString()`).
- File-based → `: Settings` (gives you `Location` = path, `Name`).
- File-based + encrypted → `: PasswordSettings`.

Add platform-specific knobs as new properties with **protected setters** (per the Conventions section). Implement `GetConnectionString()` if the platform has one.

### 2. Sync store — `XStore<T> : AbstractBulkStore<T>`

In `C:\Source\Birko\Framework\Birko.Data.X\Stores\XStore.cs`:

- `public class XStore<T> : AbstractBulkStore<T>, ISettingsStore<XSettings> where T : class`
- Constructor takes the platform's native client / connection / file path. Do **not** open it in the constructor — `Init()` does that.
- Override `Init()` to open the connection / load the file / ensure the index exists. **Idempotent** — calling `Init()` twice must be safe (the base lazy-init protects you, but defensive coding still matters).
- Override `Destroy()` to close the connection / flush + release the file handle.
- Override the **`*Core` methods only**, not the public CRUD:
  - `CreateCore(T entity)`
  - `ReadCore(Expression<Func<T, bool>>? filter, int? skip, int? limit)`
  - `UpdateCore(T entity)`
  - `DeleteCore(T entity)`
  - `CountCore(Expression<Func<T, bool>>? filter)`
  - **Bulk variants** — `CreateCore(IEnumerable<T>)`, `UpdateCore(IEnumerable<T>)`, `DeleteCore(IEnumerable<T>)`.
- **Filter-based bulk** — for platforms with native support, override:
  - `public override int Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)` — translate `PropertyUpdate<T>` to the native operation (SQL `UPDATE … SET`, MongoDB `$set`, ES `UpdateByQuery` script).
  - `public override int Delete(Expression<Func<T, bool>> filter)` — translate to the native `DELETE … WHERE`.
  - Leave the `Action<T>` overload alone — the base class handles it with read-mutate-write fallback.
- **NEVER override** the public `Create` / `Read` / `Update` / `Delete` / `Count` methods. The base class wraps them with `EnsureInitialized()` + dispatches to `*Core`. Overriding the public method bypasses lazy-init.

### 3. Async store — `AsyncXStore<T> : AbstractAsyncBulkStore<T>`

Same as above, but every method is `*CoreAsync` returning `Task<…>` with a `CancellationToken` parameter. Mirror the sync version's overrides.

### 4. Repository pair (if the platform deserves one)

Optional. `Birko.Data.Repositories` already provides generic repositories on top of stores. Only add `XRepository` / `AsyncXRepository` if the platform has natural repository-level abstractions (e.g. document collections with custom semantics).

### 5. ConnectorBase (SQL only)

If the new store is a SQL dialect: subclass `AbstractConnector` in `C:\Source\Birko\Framework\Birko.Data.SQL\`. Override `GetSqlFunctionName()` for dialect-specific function names, parameter syntax, identifier quoting. **Reuse the connector from the migration context** (`Birko.Data.Migrations.SQL.SqlMigrationContext` takes an `AbstractConnector` — same instance the store uses).

## Tests

Per `CLAUDE-maintenance.md` § "Test Requirements" — every new public method needs xUnit + FluentAssertions coverage.

In `C:\Source\Birko\Framework.Tests\Birko.Data.X.Tests\`:

- `XStoreTests.cs` — CRUD round-trip, filter-based bulk Update/Delete, lazy-init (calling CRUD without explicit `Init()`), edge cases (null filter, empty result, concurrent access if relevant).
- `AsyncXStoreTests.cs` — same as above, async variant. Include cancellation tests (`CancellationTokenSource.CancelAfter(…)`).
- `XSettingsTests.cs` — connection string generation, default values, validation.
- **Integration tests** — if the platform has a Docker image (most do), use Testcontainers or the platform's in-process embedded mode. **Do NOT mock the database** — per [[feedback_no_mocks]] (if that memory exists in the user's profile) and Birko's convention, integration tests hit a real instance.

## Health check (if external service)

Per `CLAUDE-maintenance.md` § "Health Check Requirements":

1. Implement `IHealthCheck` in `Birko.Health.Data` (or `.Redis` / `.Azure` / new `.X`).
2. Lightweight probe: `SELECT 1`, `PING`, `_cluster/health?level=cluster&local=true`, etc.
3. Dual constructors: `Func<T>` factory + singleton instance.
4. Three-level status: **Healthy** (OK), **Degraded** (slow, configurable threshold), **Unhealthy** (exception).
5. Include `latencyMs` in result `Data` dictionary.
6. Add unit tests (constructor validation, factory exception, cancellation).
7. Update `docs/health.md`, health examples, and the Health tab in `Program.cs`.
8. Register the health check project in `.slnx`, `.code-workspace`, and `Birko.Framework.csproj` if it's new.

## Workflow / BackgroundJobs specifics

If `family = Workflow`:

- Implement `IWorkflowStore` (not just `IStore`).
- Reference implementations: `Birko.Workflow.SQL`, `Birko.Workflow.ElasticSearch`, `Birko.Workflow.MongoDB`, `.RavenDB`, `.JSON`, `.XML`, `.CosmosDB` (7 backends).

If `family = BackgroundJobs`:

- Implement `IJobQueue` + `IJobStore`.
- Reference implementations: 8 backends (`.SQL`, `.ElasticSearch`, `.MongoDB`, `.RavenDB`, `.JSON`, `.XML`, `.Redis`, `.CosmosDB`).
- XML backend has the quirk that `System.Xml.Serialization` has no native `Dictionary` support — wrap job metadata in `SerializableMetadata` like `Birko.BackgroundJobs.XML` does.

## Documentation updates (per [[feedback_update_docs]])

- `README.md` — add the new backend to the storage matrix.
- `docs/stores.md` (or `docs/workflow.md` / `docs/background-jobs.md`) — document the platform, settings, connection-string format, native bulk semantics, health check.
- Root `CLAUDE.md` § "Dependency Flow" — add the new project to the appropriate branch of the diagram.
- Root `CLAUDE.md` § "Recent Updates" — add a dated entry summarizing what the new backend offers. Use [[roll-changelog]] when the section gets long.

## After implementing

1. **Build** — `dotnet build C:\Source\Birko\Framework\Birko.Framework\Birko.Framework.slnx`.
2. **Run tests** — `dotnet test C:\Source\Birko\Framework.Tests\Birko.Data.X.Tests\Birko.Data.X.Tests.csproj`.
3. **Run [[verify-birko-conventions]]** to catch nullable warnings, missed `*Core` overrides, missing tests, hard-coded paths.
