# Birko Framework

Modular .NET framework with data access, communication, AI, and model infrastructure. General-purpose across enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, and real-time domains.

See also:
- [CLAUDE-projects.md](CLAUDE-projects.md) — Full project catalog
- [CLAUDE-maintenance.md](CLAUDE-maintenance.md) — Maintenance guidelines, new project checklist, solution registration
- [CHANGELOG.md](CHANGELOG.md) — Historical architectural changes
- [README.md](README.md) + [docs/](docs/) — User-facing documentation

Each project has its own `CLAUDE.md` at `../Birko.{ProjectName}/CLAUDE.md` with project-specific details.

## Architecture

### Store Hierarchy (Template Method Pattern)
```
AbstractStore -> AbstractBulkStore (sync)
AbstractAsyncStore -> AbstractAsyncBulkStore (async)
```

Stores use lazy-init: CRUD methods auto-call `Init()`/`InitAsync()` before first use (via `EnsureInitialized`/`EnsureInitializedAsync` with double-checked locking). Concrete stores override `*Core` methods (e.g., `CreateCoreAsync` instead of `CreateAsync`). Public methods are `virtual` on the base class.

### SQL Stores
```
DataBaseStore<DB,T> -> DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy
```
AbstractRepository -> AbstractBulkRepository (sync)
AbstractAsyncRepository -> AbstractAsyncBulkRepository (async)
```

### Settings Chain (Birko.Configuration)
```
ISettings (GetId)
  -> Settings (Location, Name)
    -> PasswordSettings (+Password)
      -> RemoteSettings (+UserName, +Port, +UseSecure)
        -> SqlSettings (+CommandTimeout, +ConnectionTimeout, abstract GetConnectionString)
          -> MSSqlSettings (+MultipleActiveResultSets, +TrustServerCertificate)
          -> MySqlSettings (+BulkInsertBatchSize)
          -> PostgreSqlSettings (+UseBinaryImport)
          -> TimescaleDBSettings (+TimeColumn, +ChunkTimeInterval)
    -> SqLiteSettings (+CommandTimeout, Path, GetConnectionString) — extends PasswordSettings
    -> CosmosDB Settings (+PartitionKeyPath, +RequestTimeout, +AllowBulkExecution, GetCosmosClientOptions)
    -> RavenDB Settings (+RequestTimeout, CreateDocumentStore)
    -> MongoDB Settings (+AuthDatabase, +ReplicaSet, GetConnectionString) — already existed
    -> RedisSettings (+Database, +KeyPrefix, GetConnectionString) — already existed
```

### Dependency Flow
```
Birko.Contracts (zero deps: ILoadable, ICopyable, IDefault, ITimestamped, IGuidEntity, ILogEntity, RetryPolicy)
  -> Birko.Configuration (Settings hierarchy, namespace Birko.Configuration)
  -> Birko.Data.Core (AbstractModel, ViewModels, Filters, Exceptions)
    -> Birko.Data.Stores (store interfaces, imports Configuration)
      -> Birko.Data.Repositories

Birko.Models.Contracts (zero deps: ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument, IContactable, IAddressable)
  -> Birko.Models (AbstractPercentage, AbstractTree, ValueData + Value Objects: Money, MoneyWithTax, Percentage, PostalAddress, Quantity)
    -> Birko.Models.Inventory / .Pricing / .Customers / .Users / .Product / .Category / .SEO (clean, no SQL attrs)
    -> Birko.Models.SQL (ModelMap<T>, IModelMapping<T>, ModelMapRegistry — fluent SQL mapping framework only, no canonical mappings)
      -> Birko.Models.Users.SQL / .Customers.SQL / .Inventory.SQL / .Pricing.SQL / .Product.SQL
         (one optional sibling per domain — pre-built IModelMapping<T> for User/Role/Tenant, Address/Customer,
          StockItem/StorageLocation/InventoryDocumentLine, Currency/Tax/PriceGroup, MeasureUnit/UnitConversion/ProductPartnerCode)

Birko.Time.Abstractions (zero deps: IDateTimeProvider, SystemDateTimeProvider, TestDateTimeProvider)
  -> Birko.Time (calendars, working hours, time zones)

Birko.Data.Patterns + Birko.Data.Tenant + Birko.Time.Abstractions
  -> Birko.Data.Composition (StoreWrapperBuilder — runtime decorator chains)

Birko.Data.Core
  -> Birko.Data.Tagging (ITaggable, Tag, EntityTag, ITagService, TagServiceBase)

Birko.Data.Patterns (FieldType, FieldDescriptor, ISchemaBuilder, ICollectionBuilder, IIndexBuilder, IIndexManager, IndexDefinition, ISoftDeletable, IAuditable, ISpecification, IUnitOfWork, PagedResult)
  -> Birko.Data.Migrations (IMigrationContext, IDataMigrator, IContextualMigration, IMigration, IMigrationRunner, IMigrationStore)
    -> Birko.Data.Migrations.SQL (SqlMigrationContext — reuses AbstractConnector), .MongoDB, .ElasticSearch, .RavenDB, .CosmosDB, .InfluxDB, .TimescaleDB

Birko.AI.Contracts (zero deps: ILlmProvider, Message, ContentBlock, Tool, AgentOptions, LlmProviderFactory)
  -> Birko.AI (LlmProviderBase, Agent base, AgentFactory (registration-based), default tools)
    -> Birko.AI.Providers (Claude, OpenAI, Gemini, Ollama, AzureOpenAI, etc. + ProviderRegistration)
    -> Birko.AI.Agents (CodingAgent, language agents, media agents + AgentRegistration)
    -> Birko.AI.Orchestration (ITaskDispatcher, ImplementationPlan, StepDependencyAnalyzer)
  -> Birko.AI.Resilience (ProviderRateLimiter, ProviderCircuitBreaker, CostTrackingService, TrackedLlmProvider)

Birko.Communication.OAuth (IOAuthClient, OAuthClient, OAuthSettings)
  -> Birko.Communication.OAuth.Providers (GitHubOAuthProvider — pre-configured device flow)

Birko.Communication.GraphQL (IGraphQLClient, GraphQLClient, GraphQLSettings — queries, mutations, subscriptions over HttpClient + ClientWebSocket)

Birko.Communication.gRPC (GrpcSettings, GrpcChannelPool, GrpcClientFactory, GrpcAuthenticationInterceptor, GrpcException — client over Grpc.Net.Client)
  -> Birko.Communication.gRPC.Server (GrpcServerSettings, AddBirkoGrpc, GrpcServerAuthenticationInterceptor — server over Grpc.AspNetCore; mirrors REST / REST.Server split)

Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy, JobProcessor, JobScheduler)
  -> 8 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .Redis, .CosmosDB

Birko.Workflow (WorkflowBuilder, WorkflowEngine, guards, actions, Mermaid/DOT)
  -> 7 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .CosmosDB
```

### Reference Implementations
- **ElasticSearch** store — reference for async/bulk operations
- **JSON** store — reference for file-based storage
- **XML** store — reference for file-based storage with `System.Xml.Serialization` (note: no native `Dictionary` support — use wrapper types)
- **InMemory** store (`Birko.Data.InMemory`) — simplest possible store (thread-safe `ConcurrentDictionary`, no persistence); the canonical test double / prototyping backend

## Usage in Consumer Solutions

When using Birko.Framework projects in your solution, create **one or more aggregator library projects** that bundle the `Birko.*` shared projects you need (e.g. `FisData.Birko`, `Symbio.Birko`, or split by layer like `{Solution}.Birko.Core` + `{Solution}.Birko.Edge` + `{Solution}.Birko.Ai`). Your other projects reference the aggregator(s) instead of importing `.projitems` directly. Default to a single aggregator; split only when concrete pain shows up — bloated binaries, leaky transitive deps, or unused-heavy-dependency pull-ins (camera, AI, hardware). This avoids compilation and transitive reference issues that arise when multiple projects import overlapping sets of shared projects independently.

Use `$(BirkoSrc)` (resolved from a root `Directory.Build.props`) for all `Import Project="…\Birko.X\Birko.X.projitems"` paths instead of hard-coded absolutes. The property reads `/p:BirkoSrc=…` first, then the `BIRKO_SRC` environment variable, then defaults to the `Birko\Framework` checkout relative to the consumer repo (the recommended layout nests the framework under a `Birko\Framework` bucket with consumers under a sibling `Birko\Consumers`, so the default resolves `..\..\Framework`; flat-sibling checkouts would use `..`). TypeScript bundlers consuming `Birko.Web.*` sources resolve a **separate `Birko\Web` bucket** — the frontend libs (`Birko.Web.Core` / `.Components` / `.Shell`) live there, apart from the .NET `Birko\Framework`. Their `build.js` walks up to find `Birko\Web` (or honors `BIRKO_SRC`). So the two builds resolve sibling buckets: `Birko\Framework` for MSBuild, `Birko\Web` for esbuild. See [README — Usage in Consumer Solutions](README.md#usage-in-consumer-solutions) for the full pattern.

## Conventions
- All stores implement: `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`
- All repositories implement: `IRepository`, `IAsyncRepository`, `IBulkRepository`, `IAsyncBulkRepository`
- Bulk stores support filter-based Update/Delete: `Update(filter, PropertyUpdate<T>)`, `Update(filter, Action<T>)`, `Delete(filter)`
- Use `PropertyUpdate<T>` for native platform operations (SQL SET, MongoDB $set, ES UpdateByQuery); use `Action<T>` for complex mutations
- New platform stores should override `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` for native performance
- On a bulk store, `Read(filter)` returns the **collection** (`IEnumerable<T>`), not a single entity: the bulk `Read(filter, orderBy, limit, offset)` overload hides the inherited single-result `Read(filter)` from member lookup (C# only considers the most-derived type that declares the method name). Use `ReadFirst(filter)` / `ReadFirstAsync(filter)` (on `IBulkReadStore<T>` / `IAsyncBulkReadStore<T>`) for a single result, or cast to `IReadStore<T>` / `IAsyncReadStore<T>`
- Concrete stores override `protected *Core` methods (e.g., `CreateCoreAsync`, `ReadCore`), **NOT** the public CRUD methods. The base class handles lazy-init in the public wrapper
- Use protected setters for properties that derived classes need to modify
- `RemoteSettings` should be passed via `base.SetSettings()`, not constructed inline

## Task tracking — this repo is the polyrepo family's aggregator

The Birko family is a **polyrepo** (every `Birko.*` sub-project is its own git repo); this
repo is its **aggregator** (the `.slnx`, the shared CLAUDE docs — and the cross-cutting plan).
This is the aggregator override the generic `tasks` skill's shape detection defers to:

- **Cross-cutting epics** (work spanning several `Birko.*` sub-projects) live in **this repo's
  `tasks/`**, with the affected sub-projects listed in the EPIC's `affects:` frontmatter
  (e.g. `affects: [Birko.AI, Birko.Data.Core]`).
- **Single-sub-project work** stays in that sub-repo's own `tasks/` (the default
  walk-up-to-`.git` rule already lands there) — don't track it here.
- Cross-cutting `docs/features/` and `docs/specs/` follow the same split: family-wide at this
  aggregator, per-project in each sub-repo (a cross-cutting story regens specs per affected
  project, driven by `affects:`).

## Skills shipped by this repo

`.claude/skills/` is the home of the Birko-specific skills. They **build on top of the generic
project-lifecycle-skills set** (never the reverse — the generic skills know only a "stack
scaffolder" hook, not Birko). Project-local ones (new-birko-subproject, new-store-backend,
verify-birko-conventions, the roll-changelog shadow) auto-load only inside this repo; the
consumer-facing ones (birko-new-project, new-birko-web-page, new-birko-web-component,
design-agent) are shared user-level via [install-skills.ps1](install-skills.ps1) (junctions —
edit here, live immediately).

## Code Style
- **Guard clauses:** Use early returns instead of wrapping entire method bodies in if blocks. Prefer `if (x == null) return;` over `if (x != null) { ... }`.
- **No nullable warnings:** All new code must compile without CS8600–CS8605, CS8618, CS8625. Use proper null checks, `!` only when provably safe, or `?` annotations.

## Testing
- All test projects use **xUnit + FluentAssertions**
- Every new public functionality must have corresponding tests in `Birko.{ProjectName}.Tests`
- Test both success and failure cases; include edge cases and boundary conditions
- Each test project has its own `CLAUDE.md` describing scope and conventions
- See [CLAUDE-maintenance.md](CLAUDE-maintenance.md) for test requirements on new projects and health check patterns

## Recent Updates

The rolling per-change log now lives entirely in [CHANGELOG.md](CHANGELOG.md) (newest-first). Add new architectural / behavioral change notes here as `### Title (YYYY-MM-DD)` entries; when this section grows past ~5–8 entries, roll the oldest into CHANGELOG.md (the project-local `/roll-changelog` skill does this). Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`, not here.
