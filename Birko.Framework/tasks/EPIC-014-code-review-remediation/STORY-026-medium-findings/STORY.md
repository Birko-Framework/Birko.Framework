---
id: STORY-026
parent: EPIC-014
status: in-progress
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: medium
finding-count: 275
finding-ids: CR-M001 …
---

# Medium findings

## Progress

**160 / 275 triaged** (as of 2026-07-13). Verify-first paid off repeatedly: several test-gap findings
were already resolved by test projects created since the audit, and CR-M006 / CR-M033 / CR-M053 / CR-M127 were false positives / already-resolved.

> **Plan (decided 2026-07-13):** finish the pure-logic/offline sweep first (highest value-per-effort,
> lowest risk), THEN tackle the deferred pile below as a dedicated integration-test effort. Deferring
> is a conscious sequencing choice, not abandonment — these are logged so they aren't lost.

### ⏳ Deferred pile — handle after the offline sweep

All deferrals share one root cause: the framework's tests are pure-logic/offline (fake `DbCommand`,
`InMemory`, lazy clients), and these findings need more than that. Grouped by what would unblock them:

- **Needs a real `DbConnection` (SQLite — runs in-process, no Docker; verifiable locally):**
  CR-M135 (SQL store CRUD / RunCommand / SqlUnitOfWork / isLock / cancellation), CR-M144 (SqLite bulk
  retry), CR-M145 (SqLite test-gap), CR-M150 (SQL.View.Migrations round-trip), CR-M152 (SQL.ViewModel
  test-gap), CR-M154 (SQL.Views store/manager round-trip). A SQLite-backed harness closes these and
  end-to-end-validates the already-committed SQL fixes (M134/M137/M142/M147/M148).
- **Needs the async-interface change (self-contained refactor):** CR-M101 (thread `CancellationToken`/
  async through `IMigrationRunner`/`IMigrationStore`/`IDataMigrator` + the ~7 backend implementers),
  which then unblocks CR-M108 (Migrations.InfluxDB sync-over-async) and CR-M109 (Flux count semantics).
- **Needs Docker / Testcontainers (genuinely CI-tier, NOT verifiable in this environment):** CR-M089
  (ES store CRUD/scroll/bulk/aggregation), and the live-DB paths of CR-M136/M138/M139/M146/M149
  (MSSql/PostgreSQL/SqLite connector + Auto-mode view cache).
- **Refactor / larger design change:** CR-M140 & CR-M151 (extract the duplicated view-SELECT builder
  into a shared helper), CR-M153 (SQL.Views GroupBy needs real GROUP-BY metadata on `Tables.View` +
  connector changes — overlaps M151). CR-M141/M143 (MSSql.View / PostgreSQL.View test-gaps → new
  projects) fit the SQLite/Docker tiers above depending on provider.

### Batch 36 — MessageQueue core + InMemory CR-M199/M200/M201/M202 (4 closed; offline)

- **M199** (test-gap + bug) `RetryPolicy` had no tests; added `RetryPolicyTests`, and the large-attempt boundary test surfaced the CR-M078-style overflow (`(long)Math.Pow(...)` wraps negative) → fixed to compute in double + saturate at MaxDelay.
- **M200** (already resolved, verify-first) `InMemoryChannel.RemoveSubscriber` already captures+cancels+**disposes** the CTS (CR-H119) and `StartDispatching` is guarded on `DispatchCts == null`; existing `InMemoryChannelDisposalTests` covers it. No change.
- **M201** delayed `InMemoryProducer.SendAsync` was fire-and-forget swallowing faults → observe the detached task's fault (OnlyOnFaulted continuation) + document delayed delivery as best-effort. Offline tests (delivers; cancelled delayed send doesn't throw or deliver).
- **M202** `InMemoryConsumer.RejectAsync(requeue:true)` silently discarded (destination unknown) → `_pendingAck` now stores `(destination, message)` so requeue writes back to the channel. Offline tests (requeue redelivers; no-requeue doesn't).
- Suites green: Birko.MessageQueue.Tests 71, Birko.MessageQueue.InMemory.Tests 11.
- **Deferred (live broker):** MQTT M204 (resubscribe-on-reconnect) / M207 Redis poll-loop / M208 Redis consumer-name / M209 Redis test-gap need a live broker; MQTT M203/M205 need a new `.MQTT.Tests` project; M206 (Redis CancellationToken gate) is offline and can go in a follow-up with the existing Redis.Tests.

### Batch 35 — Helpers + Localization CR-M195/M196/M197/M198 (4 closed; offline)

- **M195** `PathValidator.SanitizePath` stripped `../` tokens in a single pass, so nested/overlapping sequences (e.g. `....//` → `../`) survived → loop the replacements to a fixpoint. Regression asserts no traversal token survives.
- **M196** (test-gap) added the two highest-risk untested Helpers to Birko.Helpers.Tests: `CsvParser` (quoting/escaping/CRLF/trailing-row/custom-delimiter) + `PathHelper.IsUnderDirectory` (containment + sibling-prefix). Remaining helper types left for a later coverage sweep.
- **M197** `InMemoryTranslationProvider.GetSupportedCultures` threw `CultureNotFoundException` on a bogus culture name → guard+filter like the Json/Resx providers. Regression asserts no throw + bad entry filtered.
- **M198** (design/doc) Localization.Data `DatabaseTranslationProvider` sync members block on async store I/O → documented the deadlock risk on UI/single-threaded-sync-context threads + steer to the async API (GetSupportedCultures has no async counterpart → cache it).
- Suites green: Birko.Helpers.Tests 88, Birko.Localization.Tests 140.

### Batch 34 — Birko.EventBus cluster CR-M184…M190 (7 closed; whole EventBus cluster done)

All EventBus test coverage lives in one `Birko.EventBus.Tests` project (compiles every EventBus projitems), so no new projects.
- **M184** parallel-dispatch Stop mode didn't halt other handlers (all tasks eagerly launched) → linked CTS cancelled on first failure; original exception rethrown via ExceptionDispatchInfo (not a follow-on OCE).
- **M185** Continue/Stop catch blocks were empty, no logger → added `Action<IEvent,Exception>? OnHandlerError` on the options, invoked in both modes, sequential + parallel.
- **M186** `DomainEventPublished` dropped the source event's OccurredAt/EventId (EventBase stamped now/new-Guid) → ctor now propagates both, restoring time-ordered + dedup-by-EventId replay.
- **M187** (design) DistributedEventBus RetryPolicy/DeadLetterOptions never consumed → documented they're transport-delegated (CR-H114 fault-driven re-delivery) rather than wiring bus-level retry (conflicts with the model) or removing (breaking).
- **M188** Dispose blocked on `UnsubscribeAsync().GetAwaiter().GetResult()` → implement IAsyncDisposable (await), sync Dispose uses `sub.Dispose()`.
- **M189** OutboxProcessor reflection-publish surfaced the opaque TargetInvocationException message → unwrap at the Invoke site (`Task.FromException(inner)`) + `Unwrap` in the ProcessBatch catch, so LastError carries the real cause.
- **M190** Outbox test-gap already covered by the existing Outbox/ tests; also fixed a **pre-existing compile break** — `InMemoryOutboxStoreTests` still called the old 2-arg `MarkFailedAsync` after the CR-H115 `maxAttempts` param landed (the whole EventBus.Tests project didn't build); updated to `maxAttempts: 5`.
- Suite green: Birko.EventBus.Tests 84.

### Batch 33 — Birko.Data.XML CR-M182/M183 (2 closed; offline file-based bugs)

- **M182** async `EnsureDataLoadedAsync` gated on `_items.Count == 0`, so a legitimately-empty store re-read the disk on every read/count/aggregate → added a `_loaded` flag on `AbstractAsyncXmlStore` (set after LoadDataAsync, reset in DestroyAsync). Offline regression: empty store loads once across N reads; destroy forces reload. (Sync path loads in ctor — async-only.)
- **M183** `XmlStore.SaveData` + `AsyncXmlStore.SaveDataAsync` did File.Delete-then-write, so a mid-write failure destroyed the existing file → write to `.tmp` then `File.Move(overwrite:true)`, cleanup temp + rethrow on failure. Offline regression: a serializer throwing on the 2nd write leaves the 1st file byte-identical, no temp left behind.
- Suite green: Birko.Data.XML.Tests 16.

### Batch 32 — Birko.Data.Tagging CR-M172 + Birko.Data.Tenant CR-M175 (2 closed; cleanup + design doc)

- **M172** `TagService.SetEntityTagsAsync`'s add loop called `AttachTagAsync`, which re-queried `GetEntityTagLinksAsync` on each iteration (N+1) even though the diff already proved the link absent → call `CreateEntityTagAsync` directly with the same payload. Offline regression: adding 3 tags issues exactly 1 link query (call-counter on the in-memory service).
- **M175** (design/"other") the static `Tenant.Current` singleton coexists with DI-scoped `ITenantContext` as a second source of truth — a store/repo accidentally built with the static fallback in a DI app silently degrades to unfiltered cross-tenant access. Documented the footgun on `Tenant`/`Tenant.Current` (non-DI use only; resolve a scoped context in DI apps). Chose docs over throwing (which would break legitimate console/tool construction).
- Suite green: Birko.Data.Tagging.Tests 11. (Tenant M175 is comment-only.)

### Batch 31 — Birko.Data.ViewModel CR-M179/M180/M181 (3 closed; offline bugs + test-gap)

- **M179** the async bulk repo inlined `CreateModelInstance()+MapToModel` / `CreateInstance()+LoadFrom` instead of the base helpers → bulk `ReadAsync` never called StoreHash, so change-tracking stayed empty and a later single-item UpdateAsync always saw a "changed" model (no-op skip defeated). Routed ReadAsync through `LoadInstance` and CreateAsync/UpdateAsync/DeleteAsync(IEnumerable) through `LoadModelInstance`. (Sync bulk repo already used these correctly.)
- **M180** none of the bulk mutators (sync + async: Create / Update×3 / Delete×2) had the `if (ReadMode) throw` guard the single-item paths enforce → a ReadMode repo could still mutate via the bulk API. Added `AccessViolationException("Repository is in Read Mode")` to all twelve.
- **M181** `Birko.Data.ViewModel.Tests` exists (created since the audit — CR-H110 delegate tests); augmented with `BulkViewModelRepositoryReadModeTests` (M179 hash-priming + M180 ReadMode enforcement). Also dropped two redundant projitems imports (Contracts/Configuration come transitively) to clear the MSB4011 warnings.
- Suite green: Birko.Data.ViewModel.Tests 9.

### Batch 30 — Birko.Data.Tenant CR-M173/M174 (2 closed; offline bugs)

- **M173** the tenant bulk wrappers (sync + async Delete/Update) validated `All(BelongsToCurrentTenant)` then passed the same lazy source to the inner store → double-enumeration (authorized set could differ from persisted set) → materialize once.
- **M174** `AsyncTenantStoreWrapper.SaveAsync` create branch ignored the CreateAsync return and relied on the inner store writing back `data.Guid` → `return await CreateAsync(...)` (mirrors the sync wrapper).
- Suite green: Birko.Data.Tenant.Tests 16.
- **Skipped this pass:** the Sync-backend cluster CR-M157–M171 (Sync test-gap needs an async-provider test double; Sync.CosmosDB/ElasticSearch/RavenDB/Sql need live stores; Sync.Json/Xml + M163 need new file-based test projects) — folded into the deferred/infra pass. Swept the core offline projects (Tenant here; Tagging/ViewModel next).

### Batch 29 — Birko.Data.Sync CR-M155/M156 (2 closed; offline bugs)

- **M155** Preview/PreviewAsync's bare catch masked OperationCanceledException as a fake conflict → added `catch (OperationCanceledException) { throw; }` before the broad catch in both. Offline test (store read throws OCE → Preview rethrows).
- **M156** the bidirectional both-exist branch had a dead `if (winner == "conflict")` (GetWinner never returns "conflict") → removed the unreachable block (behavior-preserving; real conflict detection noted as a tracked feature). Offline test (both-exist → Update, 0 conflicts).
- Suite green: Birko.Data.Sync.Tests 29.
- **Note:** the remaining SQL-cluster findings (M136, M138–M146, M149–M154) are deferred as infra-heavy — new test projects (M141/M143/M152/M154), live-DB fixes (M136/M138/M139/M144/M146/M149), refactors (M140/M151), and M153 (SQL.Views GroupBy needs real GROUP BY metadata on the View model + connector changes — not a contained fix). The clean offline SQL bugs (M137/M142/M147/M148) are done.

### Batch 28 — Birko.Data.SQL.View CR-M147/M148 (2 closed; offline logic bugs)

- **M147** View ctor overwrote an explicit `name` with the concatenated table names (inverted guard) → derive from tables only when `name` is null/empty.
- **M148** `GetViewField` cast the lambda body to `UnaryExpression` unconditionally (InvalidCastException for a plain MemberExpression) and returned `null!` (NRE at callers) → handle both body shapes + throw descriptively.
- Both fixed in Birko.Data.SQL.View and tested in the existing Birko.Data.SQL.Tests (which compiles the SQL.View projitems — no new project). Suite green: SQL.Tests 284.

### Batch 27 — SQL providers CR-M137/M142 (2 closed; offline type-mapping bugs)

- **M137** MSSqlConnector mapped DbType.Object/Binary to bare `BINARY` (→ BINARY(1), truncating blobs to 1 byte) → `VARBINARY(MAX)`. Test in existing MSSql.Tests.
- **M142** PostgreSQLConnector.FieldDefinition post-processed the composed definition with `String.Replace` to inject SERIAL → emit the SERIAL pseudo-type directly at type-emit time. Test in existing PostgreSQL.Tests.
- Suites green: SQL.MSSql.Tests 17, SQL.PostgreSQL.Tests 14. Remaining SQL-cluster findings (M136, M138–M141, M143–M155: live-DB bugs, base-builder refactor, SQL.View ctor/nullable bugs, and new SQL.View/.MSSql.View/.PostgreSQL.View test projects) continue next.

### Batch 26 — Birko.Data.SQL CR-M134 (1 closed; M135 deferred)

- **M134** the three connector reader paths (RunReaderCommand, external-transaction reader, async RunReaderCommandAsync) fell through to ExecuteReader after InitException handled a createCommand failure (which returns, not rethrows, when an OnException handler is set) → track a `faulted` flag + `yield break` before ExecuteReader. Code-review verified (SQL.Tests fakes only DbCommand, not a DbConnection).
- **M135 deferred** — store CRUD / RunCommand / SqlUnitOfWork / isLock / CancellationToken need a real DbConnection (SQLite integration) or a substantial fake-ADO connection harness that isn't set up; out of scope for the pure-logic pass, left open.

### Batch 25 — RavenDB.ViewModel + Repositories CR-M132/M133 (2 closed)

- **M132** RavenDB.ViewModel repos had no test project → new **Birko.Data.RavenDB.ViewModel.Tests** (ctor store-type validation + unwrapping RavenDBStore getter via a fake wrapper). Registered in .slnx + .code-workspace.
- **M133** Repositories test-gap → Repositories.Tests (already existed) augmented with AbstractRepositoryFallbackTests (null-store fallbacks + AbstractBulkRepository type-mismatch guard).
- Suites green: RavenDB.ViewModel.Tests 4, Repositories.Tests 10.

### Batch 24 — Birko.Data.RavenDB CR-M130/M131 (2 closed)

- **M130** async bulk insert ignored the CancellationToken → `_documentStore.BulkInsert(token: ct)` + a per-item `ThrowIfCancellationRequested`.
- **M131** thin test coverage → RavenDB.Tests (IndexManager + LazyInit since the audit) augmented with Settings (GetId/LoadFrom/CreateDocumentStore) + RavenDbUnitOfWork state-machine tests. Store CRUD / facet aggregation / Commit stay integration-tier.
- Suite green: Birko.Data.RavenDB.Tests 30. (RavenDB.ViewModel M132 + Repositories M133 — both need new test projects — are the next batch.)

### Batch 23 — Birko.Data.Processors CR-M127/M128/M129 (3 closed)

- **M127** ZIP nested-folder entry → already resolved by the CR-H076 Zip Slip flatten (`Path.GetFileName`), so a `folder/data.csv` entry extracts into `_extractPath` (existing) not a missing subdir. Added a regression test.
- **M128** CSV `ProcessStreamAsync` is fake-async (runs the sync parser) → documented via `<remarks>` (the finding's accepted minimum) rather than a full IAsyncEnumerable rewrite.
- **M129** XmlProcessor async path read Text/CDATA via the synchronous `reader.Value` (can throw under `Async=true`) → new `ProcessNodeAsync` uses `await reader.GetValueAsync()`; Element/EndElement delegate to the sync `ProcessNode`.
- Suite green: Birko.Data.Processors.Tests 36.

### Batch 22 — Birko.Data.Patterns CR-M124/M125/M126 (3 closed)

- **M124** Sluggable bulk wrappers' foreach-then-pass double-enumerated `data` → materialize once (async CreateAsync + sync Create/Update; UpdateAsync-async was already fixed under CR-H075; SoftDelete/Audit/Timestamp use the safe lazy-Select pattern).
- **M125** versioned wrapper skipped the version check on a null read (silent lost-update) → treat a missing row as a conflict (ConcurrentUpdateException) + doc the best-effort read-check-write contract. Sync + async.
- **M126** Patterns test-gap → Patterns.Tests (already existed) augmented with the M124 enumeration regression, VersionedStoreWrapperTests (M125), and PagedResult boundary math.
- Suite green: Birko.Data.Patterns.Tests 22.

### Batch 21 — MongoDB.ViewModel + MongoDB.Views CR-M121/M122/M123 (3 closed)

- **M121** MongoDB.ViewModel repos had no test project → new **Birko.Data.MongoDB.ViewModel.Tests** (ctor store-type validation + unwrapping MongoDBStore getter via a fake wrapper). Registered in .slnx + .code-workspace.
- **M122** MongoDB.Views Auto-mode fallback was dead (`catch (MongoCommandException)` never fires — a missing view returns an empty cursor) → explicit `ViewExistsAsync` check (ListCollectionNamesAsync name filter); Auto falls through to on-the-fly when the view is absent, Persistent still queries it. Code-review verified.
- **M123** MongoDB.Views `.projitems`/`.shproj` had a non-hex SharedGUID (`…mgoview0001`) → replaced with a real GUID in both.
- Suites green: MongoDB.Views.Tests 6, MongoDB.ViewModel.Tests 4.

### Batch 20 — Birko.Data.MongoDB store CR-M117 … M120 (4 closed)

- **M117** `MongoDBStore` public `Read(Guid)`/`Read()` overrides bypassed lazy-init (straight `Collection.Find`) and were redundant → deleted; base routes through `ReadCore` with EnsureInitialized.
- **M118** `AsyncMongoDBStore.ReadAsync(Guid)` override bypassed init + the cancellation gate → deleted; base routes through `ReadCoreAsync`.
- **M119** `SaveAsync` upsert branch skipped `EnsureInitializedAsync` → added it before the native `ReplaceOneAsync`.
- **M120** store test-gap → augmented with Settings.GetConnectionString variants + GetId + IndexManager.ValidateScope (made internal); change-stream mapping already covered.
- Suite green: Birko.Data.MongoDB.Tests 42.

### Batch 19 — Migrations RavenDB + TimescaleDB CR-M114/M116 (2 closed; Migrations cluster done bar the async findings)

- **M114** RavenDB `ParseFilterToRql`/`UpdateDocuments` interpolated values as `'{s}'` (injection/escaping) → parameterized via `$pN`/`$uN` `IndexQuery.QueryParameters`; `ParseFilterToRql` now returns `(rql, parameters)`. Offline tests updated for the parameterized output + a quote-value case.
- **M116** TimescaleDB test-gap → already covered by since-the-audit TimescaleDBMigrationSqlTests (BuildCompressionPolicySql/BuildContinuousAggregateSql DDL, CR-H070/H071).
- Suites green: Migrations.RavenDB.Tests 11, Migrations.TimescaleDB.Tests 4.

**Migrations cluster (CR-M101 … M116) status:** M102–M107, M110–M116 done; **M101 / M108 / M109 remain deferred** as the InfluxDB/interface async batch (CancellationToken + async through IMigrationRunner/IMigrationStore/IDataMigrator + the InfluxDB sync-over-async & Flux-count).

### Batch 18 — Migrations MongoDB + SQL CR-M112/M113/M115 (3 closed)

- **M112** Mongo `CopyData` ignored `transformJson` → extracted `BuildCopyPipeline` that prepends the transform stage(s) (single or array) before `$merge`. Offline-tested.
- **M113** Mongo migrations test-gap → augmented with `BuildCopyPipeline` + `ParseFilter` pure tests (store/live up-down stay integration-tier).
- **M115** SQL migrations test-gap → store/runner/schema already covered by SQLite tests (since the audit); added `ParseFilterToWhere` coverage ($gt/$gte/$lt/$lte/$ne mapping, quoted identifiers + @pN parameterization, injection-safety).
- Suites green: Migrations.MongoDB.Tests 7, Migrations.SQL.Tests 24. M114 (RavenDB RQL) + M116 (TimescaleDB test-gap) are the next batch.

### Batch 17 — Migrations.InfluxDB CR-M110/M111 (2 closed; M108/M109 deferred)

- **M110** CopyData/BulkInsert coerced every non-string field via `Convert.ToDouble` (corrupting bool/int/long, throwing on DateTime/byte[]) and dropped `_time` → new `ApplyValue` helper branches on runtime type (string→tag, bool/int/double→matching Field, else string field, no throw); `_time` preserved. Offline tests via ToLineProtocol.
- **M111** `ConvertFilterToFluxPredicate` returned JSON verbatim as a Flux delete predicate → rejects JSON with NotSupportedException (Flux predicate still passes through); `RemoveMigration` escapes `migration.Name` (new `EscapeFluxString`). Offline tests.
- **M108 deferred** — the sync `IDataMigrator` interface + async-only SDK means the sync-over-async can't be removed without adding async members to the shared migration interfaces (the CR-M101 async/CancellationToken work); do it there.
- **M109 deferred** — the Flux `count()` semantics fix needs a live InfluxDB to validate against multi-field measurements; do it with the M108 async rework.
- Suite green: Migrations.InfluxDB.Tests 17.

### Batch 16 — Migrations cluster CR-M102 … CR-M107 (6 closed; M101 deferred)

Birko.Data.Migrations (core) + .Migrations.CosmosDB + .Migrations.ElasticSearch. All test projects already existed.

- **M102** core runner test-gap → Migrations.Tests augmented (RegisterMigrations dedup/sort, Migrate/Rollback guards, GetPending/Applied, EnsureInitialized, GetMigrationsToExecute Up/Down range incl. the Down boundary).
- **M103** CosmosDB `RenameField` bare `catch {}` → catches only CosmosException BadRequest/NotFound; rethrows throttling/auth/service errors.
- **M104** CosmosDB `ParseFilterToSql` interpolated raw `c.{name}` → bracket-quoted `c["{name}"]` (escaped); tests updated + injection case.
- **M105** ES `GetAppliedVersions` `Size(1000)` + Ascending truncated the newest → Descending + Size(10000) (GetCurrentVersion/Max no longer under-reports).
- **M106** ES Record/Remove didn't refresh → `.Refresh(Refresh.True)` so applied-version reads are immediately consistent.
- **M107** ES test-gap → already covered by PainlessSourceTests (pure Painless builder); store round-trip is integration-tier.
- **M101 deferred** — adding CancellationToken to InitializeAsync/MigrateAsync/RollbackAsync + IMigrationStore ripples across ~7 backend implementers; its own batch.
- Suites green: Migrations.Tests 14, Migrations.CosmosDB.Tests 11, Migrations.ElasticSearch.Tests 2.

### Batch 15 — InfluxDB cluster CR-M094 … CR-M097 (4 closed)

Birko.Data.InfluxDB + .InfluxDB.ViewModel.

- **M094** `AsyncInfluxDBStore.SaveAsync` update branch wrote the point directly (no `EnsureInitializedAsync`, no `ExecuteWithRetryAsync`) unlike every other CRUD path → route it through `UpdateAsync`.
- **M095** thin store tests → InfluxDB.Tests 9 → 25 (IsTransientException, FormatFluxInterval [made internal], ModelToPoint via subclass, InfluxDbUnitOfWork state machine). MapRecordToModel/live CRUD left to an integration tier.
- **M096** the `Destroy`/`DestroyAsync` overrides on both InfluxDB repos called base (destroys the store) AND `Drop`/`DropAsync` (same store) → dropped the bucket twice. Removed the overrides; kept `Drop`/`DropAsync` as distinct API.
- **M097** (test-gap) new **Birko.Data.InfluxDB.ViewModel.Tests** (5): ctor type-guard, IsHealthy-without-client, + the M096 structural regression. Registered in .slnx + .code-workspace.
- Suites green: InfluxDB.Tests 25, InfluxDB.ViewModel.Tests 5.

### Batch 14 — EventSourcing docs + JSON + Localization CR-M093/M098/M099/M100 (4 closed)

Low-friction scattered fixes, all offline-verified (existing test projects).

- **M093** EventSourcing README documented a fictional API (`EventStore<T>`, `EventSourcedRepository<T>`, `CreatedEvent<T>`, `EventStream`/`EventSnapshot`, `GetAtTime`) → rewritten to the real wrapper/extension surface (matches CLAUDE.md).
- **M098** JSON sync bulk `CreateCore` overwrote caller Guids (`item.Guid = Guid.NewGuid()`) and used `_items.Add` (throws on dup) → `??=` + upsert indexer, matching the single-item / async-bulk paths.
- **M099** Localization `LocalizedOrderByHelper.GetNonLocalizedOrderBy` was dead code (comment-only loop, always returned null; no callers) → deleted.
- **M100** Localization sync+async bulk `Create/Update/Delete(IEnumerable<T>)` enumerated the source twice → materialize once at entry.
- Suites green: JSON.Tests 14, Localization.Tests 70.

### Batch 13 — ElasticSearch cluster CR-M088 … CR-M092 (4 closed, 1 deferred)

Birko.Data.ElasticSearch (+ .ViewModel, .Views).

- **M088** `EnumerableExtensions.MultiMatch/MoreLikeThis` were broken (Compile()-a-parameter-expression) but are query-DSL markers parsed by name → kept as markers with throw-only bodies + doc.
- **M090** ES.ViewModel `ElasticSearchRepository.Count/ClearCache` used `(Store as ElasticSearchStore<T>)` (null for a wrapped store → Count 0 / ClearCache no-op) → route through the unwrapping `ElasticSearchStore` property; fixed in the reference (non-ViewModel) repo too. New Birko.Data.ElasticSearch.ViewModel.Tests proves the unwrap mechanism.
- **M091** ES.Views store vs manager resolved the index name by different rules → shared `ElasticSearchViewIndexResolver` (Persistent+Name → name, else PrimarySource.Name); both delegate to it.
- **M092** (test-gap) ES.Views.Tests already existed; augmented with resolver tests.
- **M089** (ES store CRUD/scroll/bulk/aggregation test-gap) — **deferred, left open**: needs a mocked/Testcontainers ElasticClient tier, out of scope for pure-logic batches.
- New test project registered in .slnx + .code-workspace. Suites green: ES.Tests 78, ES.Views.Tests 7, ES.ViewModel.Tests 3.

### Batch 12 — CosmosDB cluster CR-M084 … CR-M087 (4 closed)

Birko.Data.CosmosDB + Birko.Data.CosmosDB.Views. All bugs verified via pure-logic unit tests (no live emulator).

- **M084** `CosmosAggregationHelper` emitted dotted `c.Field` identifiers by raw interpolation → bracket-quoted via a `FieldRef` helper (`c["Field"]`, embedded quotes/backslashes escaped) in SELECT/aggregate/GROUP BY.
- **M085** `CosmosDBIndexManager` stored single-field indexes as `/name/?` but Exists/Drop/GetInfo compared against the raw name (never matched) and Drop pushed the raw name into ExcludedPaths unconditionally → extracted `NormalizeIncludedPath`/`NormalizeFieldPath`/`IncludedPathMatches`/`FieldPathMatches`/`PolicyContainsIndex`/`RemoveIncludedIndex`; Create/Exists/Drop/GetInfo now agree, and Drop only excludes the path it actually removed.
- **M086** `CosmosFilterTranslator.TranslateValue` fell back to raw `ToString()` for DateTime (culture-dependent, unquoted, invalid) and enums (unquoted member name) → DateTime/DateTimeOffset now ISO-8601 quoted, enums emit numeric value.
- **M087** (test-gap) Birko.Data.CosmosDB.Views.Tests already existed; augmented with TranslateValue coverage (doubles as M086's tests).
- Suites green: CosmosDB.Tests 43 (+11), CosmosDB.Views.Tests 7 (+5). Remaining Data.* backend medium findings (CR-M088 …) still to triage.

**The entire Communication cluster (CR-M036 … CR-M075, 40 findings across Bluetooth, Camera, GraphQL,
gRPC, Hardware, IR, Modbus, Network, NFC, OAuth, REST, REST.Server, SOAP, SSE, WebSocket) is closed.**

### Batch 11 — Core foundation CR-M076 … CR-M083 (8 closed)

Configuration, Contracts, CQRS, Data.Composition, Data.Core.

- **3 confirmed bugs fixed:** **M076** `Settings.LoadFrom(ISettings)` hard-cast → type-guard (foreign
  ISettings no longer throws InvalidCastException); **M078** `RetryPolicy.GetDelay` `(long)Math.Pow`
  overflow to a negative TimeSpan at high attempts → compute in double + saturate at MaxDelay before
  cast; **M082** `AbstractModel.CopyTo()` / `AbstractLogModel.CopyTo()` returned `clone!` (null) with no
  arg → guard-clause `return this` (honors non-null ICopyable<T>).
- **Already resolved (verify-first):** **M077** Birko.Configuration.Tests and **M080** Birko.CQRS.Tests
  existed (created since the audit); M080's covariant-dispatch regression was already present. Configuration.Tests
  augmented with GetId + LoadFrom(ISettings) tests.
- **New test projects (3):** **Birko.Contracts.Tests** (10 — M079: RetryPolicy incl. the M078 overflow
  boundary + jitter window), **Birko.Data.Core.Tests** (17 — M083: ExpressionParameterReplacer, filters,
  CopyTo null-handling, ViewModel PropertyChanged), **Birko.Data.Composition.Tests** (8 — M081:
  StoreWrapperBuilder.Build<T> branch/gating/ordering + EventSourcing ctor arity, over the InMemory store).
  Registered in .slnx + .code-workspace.
- Suites green: Contracts 10, Configuration 11, Data.Core 17, Composition 8 (CQRS unchanged). The Data.*
  storage-backend medium findings (CR-M084 …) remain.

### Batch 10 — WebSocket CR-M073 … CR-M075 (3 closed; completes Communication)

- **M073** `WebSocketPort` Write/Open/Close/ReadWorker used `.Wait()`/`.Result` → now
  `ConfigureAwait(false).GetAwaiter().GetResult()` (original exception propagates, no context deadlock).
- **M074** `WebSocketAuthenticationService` had a `Dispose()` but not `: IDisposable` (DI never disposed
  it → leaked `ReaderWriterLockSlim`) → implements the interface.
- **M075** WebSocket.Tests → 33: server start/stop/**restart** lifecycle (real loopback HttpListener on
  a free port) + auth reject/allow + the IDisposable regression.

### Batch 9 — SSE cluster CR-M067 … CR-M072 (6 closed; all bugs)

- **M067** rate-limit `_connectionHistory` grew unbounded → prune + evict fully-aged-out keys each request.
- **M068** `SseResponse.Denied` dropped its reason → added `Reason` property, assigned in `Denied`.
- **M069** query-token parsing broke on leading `?` and `=` in value → `TrimStart('?')` + `Split('=', 2)`.
- **M070** `SseClient.ConnectAsync` leaked prior CTS/task → tears down the prior session first.
- **M071** `SseClient` was a no-I/O stub → implemented real HTTP SSE streaming (injectable HttpClient,
  ResponseHeadersRead, line read → `ProcessEventLine`, reconnect via Last-Event-ID).
- **M072** `CreateComment` emitted `data: : text` → distinct `Comment` property → real `: text` line.
- SSE.Tests → 19 (mock-handler streaming test proves the client now actually receives events).

### Batch 8 — REST / REST.Server / SOAP CR-M059 … CR-M066 (8 closed)

- **REST (M059/M060/M061)** — `Credentials` now backed by the client's `HttpClientHandler` (was a
  dead auto-property); sync overloads documented as convenience shims (prefer `*Async`); CLAUDE.md
  rewritten to the real thin `RestClient` API (the fictional `AsyncRestClient`/`RestRequest`/
  `GetData<T>`/`RestException`/retry/cache surface removed).
- **REST.Server (M062/M063)** — `RestAuthenticationService : IDisposable` (had a `Dispose()` but
  didn't implement the interface → lock leak). REST.Server.Tests exists + IDisposable regression.
- **SOAP (M064/M065/M066)** — client cache → `ConcurrentDictionary`+`GetOrAdd` (was Dictionary
  check-then-act); `SoapServer` gained a synchronous `Stop()` core so `Dispose` no longer blocks on
  `StopAsync().GetAwaiter().GetResult()`. SOAP.Tests exists + cache/server regressions.
- Suites green: REST 19, REST.Server 7, SOAP 7.

### Batch 7 — Communication test-gaps + NFC polling CR-M051/M056/M057/M058 (4 closed)

- **NFC polling (M056)** — added an `INfcTransport.PollingError` event; all three transports
  (Serial/Http/Hid) now wrap the poll loop in try/catch (surface non-cancellation faults + stop),
  track the poll Task, and `StopPollingAsync` awaits it. Regression tests fire `PollingError` on a
  faulting transport.
- **NFC tests (M057)** — NfcReaderPort pipeline/lifecycle, SerialNfcTransport ParseResponse/DetectTagType
  (via reflection), HttpNfcTransport endpoints (mocked handler). NFC.Tests → 121.
- **IR tests (M051)** — SamsungAcProfile, InfraredPort (Write %4, HandleReceivedTiming fallback),
  Http/SerialIrTransport; RC5/RawProtocol already covered. IR.Tests → 102.
- **OAuth tests (M058)** — PollDeviceTokenAsync (pending/slow_down/expired/timeout), real refresh-token
  grant + fallback, ExchangeCodeAsync confidential branch, real 401-resend. OAuth.Tests → 54.

### Batch 6 — Communication ports/protocols CR-M047 … CR-M055 (8 closed; M051/IR deferred)

- **gRPC M047** — `WithAuth` copies inbound headers into a new `Metadata` instead of mutating the
  caller's, so reused `CallOptions` no longer accumulate duplicate auth headers.
- **Hardware Serial M048/M049** — ctor guard-throws on null settings (was a latent NRE); all
  `ReadData` access serialized on a dedicated `_readLock` (atomic RemoveReadData also fixes a `-1`
  crash). New **Hardware.Tests** (M050, 23) covers GetID + buffer logic.
- **Modbus M052** — MBAP transaction id validated against the response; mismatch throws instead of
  accepting a stale frame. Regression test added.
- **Network M054** — `Udp.Write` guards `_remoteEndPoint` (CS8604 + latent NRE). New **Network.Tests**
  (M055, 25). M053 (RemoveReadData TOCTOU) was already atomic (CR-H026/H027) — not reproducible.
- Suites green: gRPC 25, Modbus 71, Hardware 23, Network 25.
- **Deferred:** CR-M051 (IR test gaps) — next batch with NFC/OAuth test-gaps.

### Batch 5 — Communication.Camera + .GraphQL CR-M043 … CR-M046 (4 findings, all closed)

- **Camera M043** — `CaptureFrameAsync` kills the ffmpeg process tree in a `finally` on cancel/fail
  (Process.Dispose doesn't terminate the child), so no orphaned ffmpeg holds the camera.
- **Camera M044** — arguments now go through `ProcessStartInfo.ArgumentList` (extracted `BuildArguments`),
  unquoted defaults; tests assert a space-containing device is one token and injection is impossible.
- **GraphQL M045** — removed the `_requestLock` SemaphoreSlim that serialized every request on a shared
  client; regression test proves two concurrent requests are in flight at once.
- **GraphQL M046** — extracted `HandleMessageAsync` from the receive loop and unit-tested the
  next/complete/error/wrong-id/payload-errors dispatch (frame reassembly was already covered).
- Suites green: Camera 15, GraphQL 58.

### Batch 4 — Communication.Bluetooth cluster CR-M036 … CR-M042 (7 findings, all closed)

- **M036** — `Read()` now checks availability + `GetRange` under one `lock(ReadData)` (was a TOCTOU);
  platform-agnostic, tested.
- **M037** — Linux discovery wraps the timeout CTS, linked CTS, and bluetoothctl `Process` in `using`
  (compile-verified with `DefineConstants=LINUX`).
- **M038** — a `_reconnecting` guard stops `Open()` resetting `_reconnectAttempts` mid-reconnect, so
  `MaxReconnectAttempts` now actually bounds the retry chain (was effectively infinite). Residual
  thread-supervision documented inline.
- **M039** — `OpenWindows` checks `connectTask.Wait(timeout)` and throws on timeout (WINDOWS-gated;
  code-review verified — WinRT branch isn't compiled off a Windows TFM).
- **M040** — CLAUDE.md rewritten to the real API; README examples corrected (the fictional
  `BLEServer`/`BLEScanner`/etc. types are gone).
- **M041** — dead RSSI branch removed; Linux service-filtered discovery throws `NotSupportedException`
  instead of silently returning all devices (Windows placeholders left documented).
- **M042** — `Birko.Communication.Bluetooth.Tests` already existed; augmented with `Read()` + `GetID`
  tests (12, hardware-free).
- **Note:** the Windows WinRT branches (M038/M039 Windows parts) are code-review-only — they don't
  compile off a Windows TFM here; the Linux branch was compile-checked (a pre-existing `sizeof`/`unsafe`
  issue there is unrelated to these findings and left untouched).

### Batch 3 — Caching cluster CR-M030 … CR-M035 (6 findings, all closed)

- **MemoryCache** (CR-M030): per-key stampede locks are now reference-counted and retired by their
  last releaser — the timer no longer evicts locks, closing the GetOrAdd↔WaitAsync eviction race.
- **HybridCache** (CR-M031): write-through `SetAsync` always awaits the L1 write in a `finally`, so
  the L1 write is never an orphaned fire-and-forget when L2 faults with fallback disabled. Tests
  expanded (CR-M032): L1 TTL capping, WriteThrough=false, L2-failure fallback for
  Remove/Exists/RemoveByPrefix/Clear.
- **RedisCache** (CR-M034): every method observes the `CancellationToken` (+ the RemoveByPrefix loop);
  offline regression tests exploit the lazy connection. CR-M033 (dead `KeyTimeToLiveAsync`) was already
  gone (fixed with CR-H014); CR-M035's test project already existed — expanded with the cancellation tests.
- Caching suites green: Caching 25, Hybrid 34, Redis 12.

### Batch 2 — BackgroundJobs cluster CR-M015 … CR-M029 (15 findings, all closed)

Across all 8 job-queue backends. Bugs fixed + regression-tested; audit `Status` flipped to `done`.
- **Atomic dequeue claim** ported from the SQL backend's proven `ClaimToken` conditional-update +
  re-read-verify pattern to the doc stores: **MongoDB** (CR-M020, per-document `$set` atomic),
  **RavenDB** (CR-M021, single-winner even under last-write-wins), **ElasticSearch** (CR-M016,
  refresh-window residual documented — handlers must be idempotent). `ClaimToken` added to each model.
- **File backends** JSON (CR-M018) / XML (CR-M029): `DequeueAsync` read-claim-update serialized with
  a `SemaphoreSlim` (mirrors `InMemoryJobQueue`); cross-process still unsupported by design (documented).
- **Redis:** `GetByStatusAsync` orders before truncating (CR-M023); every method + `RedisJobLockProvider`
  observe the `CancellationToken` (CR-M024); the dequeue claim (ZREM + hash flip) is now fully inside
  the Lua script, closing the orphan window (CR-M025).
- **SQL lock provider:** connection no longer leaks on `OpenAsync` failure (CR-M026); non-Postgres
  backends get real locks (MSSql `sp_getapplock`, MySQL `GET_LOCK`) or return `false` instead of a
  false success (CR-M027, SQLite).
- **Test projects:** created `Birko.BackgroundJobs.{JSON,ElasticSearch,RavenDB,MongoDB}.Tests`
  (CR-M017/M019/M022 + M020 coverage); CosmosDB/SQL already existed (CR-M015/M028) — SQL expanded with
  lock-provider + FailAsync-boundary tests. All 8 BackgroundJobs suites green (JSON 6, XML 7, Redis 7,
  SQL 17, CosmosDB 3, Mongo 6, ES 6, Raven 6).

### Batch 1 — Birko.AI cluster CR-M001 … CR-M014 (14 findings, all closed)

Across Birko.AI, .Agents, .Contracts, .Orchestration, .Providers, .Resilience.

- **Bugs fixed + regression-tested:** CR-M001 (async `HandleResponse`), CR-M002/CR-M008
  (`CancellationToken` threaded through `ILlmProvider` + all 16 providers + `LlmProviderBase` retry
  loops/SSE + `TrackedLlmProvider` + the agent run loop + `Tool.ExecuteAsync`), CR-M003
  (`LlmStreamingResponse` made disposable, disposed via `await using`), CR-M007 (symmetric
  `ToDictionary`/`FromDictionary`), CR-M010 (`SendWithRetryAsync` disposes the buffered response),
  CR-M011 (rate-limit re-check loop), CR-M012 (`GetRetryAfter` covers token/daily windows), CR-M013
  (serialized circuit-breaker persistence + `FlushPersistenceAsync`).
- **Not reproducible:** CR-M006 (`Merge` already copies `OnLlmResponseReceived`).
- **Test-gap:** CR-M005 → new `Birko.AI.Agents.Tests` (18 tests). CR-M004 / CR-M009 / CR-M014 →
  their `.Tests` projects already existed (created since the audit) and were expanded with regression
  tests for the fixes above.
- New AI-cluster test count: **59** (AI.Tests 11, Agents.Tests 18, Providers.Tests 17,
  Orchestration.Tests 3, Resilience.Tests 10). Remaining medium findings CR-M076 … CR-M275 are still
  to triage (verify-first, one project cluster at a time).

## User story

As a maintainer, I want the **medium**-severity code-review findings triaged and the worthwhile
ones fixed, so quality issues below the high bar don't accumulate.

## Scope

The 275 medium findings `CR-M001 …` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). **Unverified** —
these are reviewer claims that were not individually adversarially re-checked, so confirm each is
real before fixing.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Mxxx` entry (verify-first), copying its ID/Title → title, Path → file:line, Detail → context,
Fix → approach, Acceptance → derive + add a regression test. Flip each finding's `Status` in the
audit (`done` / `wontfix`) as it's triaged.
