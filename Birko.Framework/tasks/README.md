# Tasks — Birko.Framework

> ⚠ **Feature drift (3 groups, 109 items)** — **all three re-measured 2026-09-19**, and two grew
> since the 2026-09-16 pass.
> **DV9 ×74** — 199 tasks carry `feature: FEATURE-014` and its `decisions.md` names 128 TASK ids, so
> the ledger does not know about **74** of its own tasks (×64 on 2026-09-16, ×63 on 2026-09-09, ×62
> and ×59 earlier that week, ×31 on 2026-09-04). Measured rather than incremented, per § TASK-283.
> The reverse gap is unchanged: **3** ids in the ledger are not FEATURE-014 tasks ·
> **DV5 ×31** (every task in `_loose/` has no epic *and* no feature, so none appears in a feature row
> — was ×19; the 12 added are the `_loose` intake since 2026-09-16, TASK-468 among them) ·
> **DV3 ×4** (TASK-285/286/287/288 sit under EPIC-014 with `feature: null` while every sibling links
> to FEATURE-014 — a broken back-link; unchanged).
> Run `/roadmap --check` for the full audit, or `/tasks audit --fix` for the safe ones.
>
> ⚠ **DV7 was NOT recomputed in this pass and is not being reported as clean.** Spec staleness needs a
> `git diff` per area against `generated-at` — a cost this dashboard refresh did not pay. Note the
> reason has changed since the last pass, which cited areas globbing *sibling repos*: [[TASK-457]]
> consolidated the framework into one repo on 2026-09-18, so `generated-at` can now observe a source
> change for the first time and the measurement is newly *worth* taking. The last measured value was
> **DV7 ×3** (`filter-expression-translation`, `bulk-filter-operations`,
> `unit-of-work-and-transactions`), owned by [[TASK-251]]. Do not carry the previous pass's "very
> likely ×1" forward as a number — it was arithmetic on a stale total then, and the monorepo has since
> moved the baseline underneath it.
> DV8/DV10/DV11 were clean at the last measurement: all 25 mapped areas exist on disk and carry
> `shaped-by-derived: true`.
>
> ℹ **Known false positive, left as-is:** EPIC-018 reads `in-progress` with all 4 tasks `done`. It is
> an area-of-concern epic giving `Birko.Web.Core` an owner, and closing it would recreate the orphaning
> it exists to prevent — the reasoning is in its own `EPIC.md` (§ *Why this epic stays `in-progress`
> with no open tasks*), which is why this dashboard no longer restates it.

_Generated 2026-09-19 (`/tasks triage`, chained from `/tasks cancel TASK-235`). Run `/tasks triage` to refresh. **Do not hand-edit** — changes will be overwritten._

## Counts

| Status       | Epics | Stories | Tasks |
|--------------|-------|---------|-------|
| planned      | 10    | 24      | —     |
| todo         | —     | —       | 153   |
| in-progress  | 7     | 9       | 0     |
| review       | —     | —       | 0     |
| blocked      | —     | —       | 1     |
| done         | 1     | 23      | 169   |
| cancelled    | 0     | 0       | 4     |

`todo` by priority: 32× P1 · 98× P2 · 23× P3.  
> ℹ Recounted from the files on each refresh rather than incremented — a breakdown that sums correctly
> is not thereby correct (before TASK-315 this line carried a P0 that had just been closed, and was
> one P3 short, while still totalling right). No P0 is open.
>
> ⚠ **The review queue is empty for the first time since it was introduced** — the 11 tasks this
> dashboard listed on 2026-09-17 were all signed off on 2026-09-19 (TASK-035, TASK-038, TASK-091,
> TASK-118, TASK-201 and the rest). That is a drained queue, not a dropped section: `review` is
> rendered in the counts table at 0 rather than omitted, so the distinction stays visible.
>
> ⚠ **[[TASK-235]] was cancelled 2026-09-19, not completed** — its subject is
> `FisData.Stock.Angular.Server`, and Symbio is FisData's successor, so the net10 migration it was
> `blocked` on will never land. That is the only `blocked` task gone; **TASK-148 remains the single
> blocked entry**. The file is kept, per the tree's never-delete rule, because the process lesson in
> its § Context (sweep consumers by ownership, not by path glob) outlives the consumer.

## In progress now

_None_

## Tree

- **EPIC-001** Birko.Web.Components — UI polish — in-progress (9/13 tasks done)
  - [x] TASK-053 b-range: vertical orientation (equalizer-style slider) · FEATURE-001
  - STORY-001 bare attribute for inline form usage — in-progress (1/1 done)
    - [x] TASK-001 Add `bare` attribute to all form controls · FEATURE-001
  - STORY-002 b-editable-table migration to bare components — in-progress (1/1 done)
    - [x] TASK-002 Benchmark + migrate b-editable-table to bare components · FEATURE-001
  - STORY-003 size attribute coverage — planned (0/1 done)
    - [ ] TASK-003 size attribute on b-pagination, b-dropdown-menu, b-breadcrumb · FEATURE-001
  - STORY-023 Form-associated custom elements (ElementInternals) — in-progress (2/5 done)
    - [x] TASK-035 Make form controls form-associated via ElementInternals · FEATURE-001
    - [ ] TASK-132 `b-form`: `required` on a checkbox / switch is inert — an unchecked toggle counts as filled · FEATURE-001
    - [ ] TASK-133 `b-form`: a `radio` field's value is never collected, and a `required` radio group can never validate · FEATURE-001
    - [ ] TASK-134 Decide whether `b-form.validate()` adopts the remaining validity flags, starting with `typeMismatch` · FEATURE-001
    - [x] TASK-136 `b-form.validate()` surfaces a control's own verdict — on a whitelist, not `checkValidity()` · FEATURE-001
  - STORY-028 Display & disclosure components — done (3/3 done)
    - [x] TASK-039 b-chart: coerce/validate a unitless `height` (avoid endless SVG stretch) · FEATURE-001
    - [x] TASK-040 Add a `b-accordion` (collapsible / disclosure group) component · FEATURE-001
    - [x] TASK-041 Extract a shared `coerceCssLength` helper and fix the unitless-length bug across components · FEATURE-001
  - STORY-050 Visible help text on form controls — in-progress (1/1 done)
    - [x] TASK-091 `description` — a persistent help-text row on the form controls · FEATURE-001
- **EPIC-002** Birko.Data.Redis — planned (0/1 tasks done)
  - [ ] TASK-004 Implement Birko.Data.Redis · FEATURE-002
- **EPIC-003** Birko.Caching.NCache — planned (0/1 tasks done)
  - [ ] TASK-005 Implement Birko.Caching.NCache · FEATURE-003
- **EPIC-004** Birko.Storage — Cloud providers — planned (0/3 tasks done)
  - STORY-004 AWS S3 storage — planned (0/1 done)
    - [ ] TASK-006 Implement Birko.Storage.Aws · FEATURE-004
  - STORY-005 Google Cloud Storage — planned (0/1 done)
    - [ ] TASK-007 Implement Birko.Storage.Google · FEATURE-004
  - STORY-006 MinIO (S3-compatible) — planned (0/1 done)
    - [ ] TASK-008 Implement Birko.Storage.Minio · FEATURE-004
- **EPIC-005** Birko.Messaging — Provider expansion — planned (0/5 tasks done)
  - STORY-007 Email providers (SendGrid + Mailgun) — planned (0/2 done)
    - [ ] TASK-009 Implement Birko.Messaging.SendGrid · FEATURE-005
    - [ ] TASK-010 Implement Birko.Messaging.Mailgun · FEATURE-005
  - STORY-008 SMS via Twilio — planned (0/1 done)
    - [ ] TASK-011 Implement Birko.Messaging.Twilio · FEATURE-005
  - STORY-009 Push notifications (Firebase + APNs) — planned (0/2 done)
    - [ ] TASK-012 Implement Birko.Messaging.Firebase · FEATURE-005
    - [ ] TASK-013 Implement Birko.Messaging.Apple · FEATURE-005
- **EPIC-006** Birko.MessageQueue — Provider expansion — planned (0/5 tasks done)
  - STORY-010 RabbitMQ (AMQP) — planned (0/1 done)
    - [ ] TASK-014 Implement Birko.MessageQueue.RabbitMQ · FEATURE-006
  - STORY-011 Kafka — planned (0/1 done)
    - [ ] TASK-015 Implement Birko.MessageQueue.Kafka · FEATURE-006
  - STORY-012 Cloud queue providers (Azure Service Bus + AWS SQS) — planned (0/2 done)
    - [ ] TASK-016 Implement Birko.MessageQueue.Azure · FEATURE-006
    - [ ] TASK-017 Implement Birko.MessageQueue.Aws · FEATURE-006
  - STORY-013 MassTransit adapter — planned (0/1 done)
    - [ ] TASK-018 Implement Birko.MessageQueue.MassTransit · FEATURE-006
- **EPIC-007** Birko.Telemetry — Additional exporters — planned (0/3 tasks done)
  - STORY-014 Prometheus exporter — planned (0/1 done)
    - [ ] TASK-019 Implement Birko.Telemetry.Prometheus · FEATURE-007
  - STORY-015 Seq log exporter — planned (0/1 done)
    - [ ] TASK-020 Implement Birko.Telemetry.Seq · FEATURE-007
  - STORY-016 Grafana LGTM stack exporter — planned (0/1 done)
    - [ ] TASK-021 Implement Birko.Telemetry.Grafana · FEATURE-007
- **EPIC-008** Birko.Health — Queue + cloud health checks — planned (0/4 tasks done)
  - STORY-017 Message queue health checks — planned (0/2 done)
    - [ ] TASK-022 RabbitMqHealthCheck · FEATURE-008
    - [ ] TASK-023 KafkaHealthCheck · FEATURE-008
  - STORY-018 Cloud queue health checks — planned (0/2 done)
    - [ ] TASK-024 AzureServiceBusHealthCheck · FEATURE-008
    - [ ] TASK-025 AwsSqsHealthCheck · FEATURE-008
- **EPIC-010** Birko.Data.RavenDB — Index ergonomics — planned (0/1 tasks done)
  - [ ] TASK-028 Attribute-driven RavenDB index definitions (Option B) · FEATURE-010
- **EPIC-011** Birko.Framework — Test coverage gaps — planned (0/7 tasks done)
  - [ ] TASK-052 Adopt a web unit-test runner for Birko.Web.* (migrate backport-smoke) · FEATURE-011
  - STORY-021 Redis-dependent tests — planned (0/2 done)
    - [ ] TASK-029 Birko.BackgroundJobs.Redis.Tests · FEATURE-011
    - [ ] TASK-030 Birko.Caching.Redis.Tests · FEATURE-011
  - STORY-022 Phase 4 lower-priority tests — planned (0/3 done)
    - [ ] TASK-031 Birko.Models.* validation tests · FEATURE-011
    - [ ] TASK-032 Birko.Data.*.ViewModel CRUD tests · FEATURE-011
    - [ ] TASK-033 Birko.Configuration + Birko.Contracts DTO tests · FEATURE-011
  - STORY-047 Review filter-parser behaviour on live document databases — planned (0/1 done)
    - [ ] TASK-060 Run & review the live null-filter parser tests · FEATURE-011
- **EPIC-012** Birko.MessageQueue.MQTT — v5 features — planned (0/1 tasks done)
  - [ ] TASK-034 MQTT v5 topic aliases + user properties · FEATURE-012
- **EPIC-013** Reference consumers — integration smoke harness + Web playground — in-progress (3/4 tasks done)
  - [x] TASK-037 Replace the TUI example with an extracted backend integration smoke-harness consumer · FEATURE-013
  - [x] TASK-038 Birko.Web playground: component gallery + live token editor + theme-CSS export · FEATURE-013
  - [x] TASK-228 `Birko.Sandbox` is not a git repository — the smoke harness and the only dependency manifest exist on one disk · FEATURE-013
  - [ ] TASK-307 The playground's token editor has no editor for `rgba()`/`hsla()` tokens, and none for lengths · FEATURE-013
- **EPIC-014** Code review — audit remediation — in-progress (116/203 tasks done)
  - [x] TASK-058 SqLiteConnector emits invalid AUTOINCREMENT DDL for non-primary-key increment fields (dual-key models) · FEATURE-014
  - [x] TASK-131 Per-sub-repo `docs/specs/` trees — the aggregator's staleness guard cannot fire · FEATURE-014
  - [ ] TASK-144 `RuleSpecification` and `RuleExpressionConverter` are two translators of one rule model · FEATURE-014
  - [ ] TASK-146 Nothing pins that the async repository has no connector-bypassing read · FEATURE-014
  - [ ] TASK-150 `char?`, `TimeSpan` and `DateTimeOffset` have no column mapping — they now fail loudly instead of quietly · FEATURE-014
  - [x] TASK-196 `x.Col.Date == value` matched zero rows on every input, every column, every day · FEATURE-014
  - [x] TASK-197 `TimeOnly` had no column mapping — and after [[TASK-112]] it took the whole entity down · FEATURE-014
  - [x] TASK-204 An index that could not be built took the entity's whole read surface with it — permanently · FEATURE-014
  - ~~TASK-205 A qualified `Table.Column` is emitted unquoted while `FROM "Table"` is quoted — PostgreSQL folds them apart~~ · FEATURE-014
  - [ ] TASK-208 DECISION: which of `Birko.Data.SQL.View` the spec map should cover — two fixes have now landed in the excluded part · FEATURE-014
  - [x] TASK-210 `MongoDB.Driver 3.2.0` pulls two vulnerable transitive packages, and nothing reports it · FEATURE-014
  - [x] TASK-211 On-the-fly views are broken on PostgreSQL — and the error is swallowed, so they return an empty result · FEATURE-014
  - [x] TASK-216 A filtered DELETE / UPDATE qualifies its `WHERE` with a bare table name, so every filtered write fails on PostgreSQL · FEATURE-014
  - [ ] TASK-217 `Update(Table, values, conditions)` builds its SET list from every column, so a partial update cannot work · FEATURE-014
  - [ ] TASK-226 Per-sub-repo `docs/specs/` trees for the 4 single-repo areas (and the 64 unspecced projects) · FEATURE-014
  - [x] TASK-227 `generated-at` always names the commit *before* the spec it stamps, so staleness is measured from too early · FEATURE-014
  - [x] TASK-229 Two shared projects `using` a driver they do not declare — and three sources disagree about whose job it is · FEATURE-014
  - [x] TASK-230 The remaining vulnerable transitives — 7 advisories across 37 of 246 projects · FEATURE-014
  - [x] TASK-231 `Birko.EventBus.Outbox.SQL` shipped complete but registered nowhere — unbuilt, untested, invisible · FEATURE-014
  - [x] TASK-232 DECISION: the lock contract meant three different things — split the durations, keep session semantics · FEATURE-014
  - [ ] TASK-233 DECISION: the CosmosDB span-`Contains` rewrite may now be redundant — the SDK fixed it upstream · FEATURE-014
  - [x] TASK-234 38 more shared projects use an external package they never declare · FEATURE-014
  - [x] TASK-236 A per-backend verdict on locking for the six job backends without a provider · FEATURE-014
  - [x] TASK-237 `RecurringJobScheduler` duplicates every job per worker — wire leader election · FEATURE-014
  - [x] TASK-238 Seven `Birko.Data.Sync.*` projitems carry a `ProjectReference` to another `.projitems` · FEATURE-014
  - [ ] TASK-239 Packages declared that .NET 10 already provides — `NU1510`, the mirror image of TASK-234 · FEATURE-014
  - [x] TASK-240 A transaction boundary that async writes actually honour, stated per provider · FEATURE-014
  - [x] TASK-241 RavenDB never sets the document id from the entity Guid — delete is a silent no-op and update duplicates · FEATURE-014
  - [x] TASK-242 Every bulk write escaped the transaction boundary, and on three providers it did so silently · FEATURE-014
  - [x] TASK-243 On MySQL, a store's first operation inside a boundary silently commits that boundary · FEATURE-014
  - [x] TASK-244 Lazy schema-ensure runs before the store publishes its transaction boundary · FEATURE-014
  - [x] TASK-245 Index DDL every provider accepts — MySQL rejected the clause, PostgreSQL could not resolve the columns · FEATURE-014
  - [x] TASK-246 A migration's `.Unique()` silently builds a NON-unique index on every SQL provider · FEATURE-014
  - [x] TASK-247 `SqlSchemaBuilder`'s raw-SQL fallbacks emit index DDL that two providers reject · FEATURE-014
  - [x] TASK-248 MySQL cannot index an unbounded `string` column — and that is the canonical documented pattern · FEATURE-014
  - [x] TASK-249 Four close-gate findings on TASK-245 — including a second injection sink its own rule pointed at · FEATURE-014
  - [x] TASK-250 A spec source glob is not a git pathspec, so the staleness check never saw 124 files · FEATURE-014
  - [ ] TASK-251 Regen the three wide-surface spec areas DV7 still reports · FEATURE-014
  - [x] TASK-252 Six latent per-provider gaps found while closing the index-DDL thread · FEATURE-014
  - [x] TASK-253 The migration hypertable emitters carry the same identifier defect — and one bypasses the DDL funnel · FEATURE-014
  - [x] TASK-254 A hypertable conversion that cannot succeed now bricks the store instead of degrading · FEATURE-014
  - [x] TASK-255 `BuildContinuousAggregateSql` still hardcodes `time` — CR-H070 unfixed in the method next door · FEATURE-014
  - [x] TASK-256 PostgreSQL's binary `COPY` cannot bind a UTC `DateTime`, and the test suite is green because its fixture avoids it · FEATURE-014
  - [x] TASK-257 On MSSql an unlengthed `string` column becomes `TEXT`, so **no predicate on it works** · FEATURE-014
  - [x] TASK-258 `retryWhenOwned` claims to preserve each provider's retry policy, and nothing asserts that it does · FEATURE-014
  - [x] TASK-259 `SqlSchemaBuilder` publishes its connection onto a process-wide cached connector and never clears it · FEATURE-014
  - [x] TASK-260 `CreateContinuousAggregate` takes two raw SQL fragments that cannot be contained · FEATURE-014
  - [x] TASK-261 `GetChunkInterval` reads a catalogue column TimescaleDB removed in 2.0 · FEATURE-014
  - [x] TASK-262 The migration emitters' identifier rules assume this framework created the object — twice over · FEATURE-014
  - [x] TASK-263 There is no way to persist an instant with its offset — the timezone-aware column type is mapped but unreachable · FEATURE-014
  - [x] TASK-264 A migration's declared column metadata is dropped on the way to the connector · FEATURE-014
  - [x] TASK-265 On MySQL a `[UniqueField]` or `[PrimaryField]` unlengthed string emitted `LONGTEXT`, so the table could not be created · FEATURE-014
  - [x] TASK-266 Index keys that are still wrong after TASK-257: a `byte[]` column, and a composite too wide for the key limit · FEATURE-014
  - [x] TASK-267 The project-local `verify-conventions` did not run at the close gate, again · FEATURE-014
  - [ ] TASK-268 Two small SQL field-mapping gaps found while typing MSSql's string columns · FEATURE-014
  - [x] TASK-269 Nothing reports a column whose stored type no longer matches what the model declares · FEATURE-014
  - [x] TASK-270 `DataBase.GetConnector` shares one connector process-wide, and three separate features have put per-caller state on it · FEATURE-014
  - [ ] TASK-271 The TimescaleDB migration emitters bypass the connector for a reason that no longer exists · FEATURE-014
  - [ ] TASK-272 An entity cannot say which schema it lives in · FEATURE-014
  - [x] TASK-273 `CompositeIndex` cannot express a filter predicate, so a unique index over a NULLABLE column is unusable on MSSql · FEATURE-014
  - [x] TASK-274 The second index lane dropped `Sparse` in all six builders — and three of them created no index at all · FEATURE-014
  - [x] TASK-275 `[UniqueField]` on a nullable column is an inline constraint, so on MSSql it rejects the second ordinary row — and no predicate can be attached to it · FEATURE-014
  - [ ] TASK-276 One test in `Birko.Data.SQL.Tests` fails about 10% of full-suite runs, and its identity was never captured · FEATURE-014
  - [x] TASK-277 A write to a missing table reports SUCCESS on EVERY provider — `OnException` swallows it and `DoInit` does nothing · FEATURE-014
  - [x] TASK-278 On SQL Server every limited read emits invalid T-SQL — `ReadFirstAsync` and paging both fail · FEATURE-014
  - [x] TASK-279 `BuildCompressionPolicySql` keeps CR-H070's `orderByColumn = "time"` — the half of the remedy that was a compatibility artefact · FEATURE-014
  - [x] TASK-280 `IsHypertable` and `GetChunkInterval` ignore the schema half of the qualified name TASK-262 taught them to accept · FEATURE-014
  - [x] TASK-281 A continuous aggregate cannot be created or refreshed inside a transaction — so it may never have worked through the runner at all · FEATURE-014
  - ~~TASK-282 The TimescaleDB migration emitters bypass the connector — an option TASK-259 reopened and nobody owns~~ · FEATURE-014
  - [x] TASK-283 A throwing `OnIndexCreationFailed` subscriber defeats TASK-204's degrade and bricks the entity · FEATURE-014
  - [x] TASK-284 An empty `startOffset` silently widens a refresh policy to all of history — and the escape hatch that relies on it is untested · FEATURE-014
  - [x] TASK-285 A `COUNT` of a missing table throws while a `SELECT` of the same table returns empty — the one read that answers 500
  - [x] TASK-286 Date every `CREATE TABLE`, so "created and then missing" stops being unprovable
  - [x] TASK-287 The count path swallows TASK-286's escape annotation, so the instrument is blind where it is needed
  - [x] TASK-288 A table that vanishes under an initialised store never comes back — every write 500s until restart
  - [x] TASK-289 A throwing `OnSchemaEscapeDetected` subscriber reopens TASK-285 and destroys TASK-286's annotation · FEATURE-014
  - [x] TASK-290 Name the mechanism behind the schema-ensure escape · FEATURE-014
  - [x] TASK-291 `EnsureSchemaAndReport` rewraps a cancellation as a bare `Exception`, so a client that hung up becomes a 500 · FEATURE-014
  - [x] TASK-292 The per-store transaction door remembers a schema-ensure that was rolled back · FEATURE-014
  - [x] TASK-293 The escape "anomaly" is decided by a substring of the statement, so it fabricates anomalies · FEATURE-014
  - [x] TASK-294 A count that hits lock contention is a 500, while a count of a missing table is `0` · FEATURE-014
  - [x] TASK-295 The escape and heal apparatus is SQLite-only: three providers record no created tables · FEATURE-014
  - [x] TASK-296 SQLite connection pooling serves a stale schema image, so a freshly created table reads as missing · FEATURE-014
  - [ ] TASK-298 A migration can declare a column default, and no connector emits one · FEATURE-014
  - [ ] TASK-299 `FieldDescriptor`'s three index properties are read by nothing, in any backend · FEATURE-014
  - [ ] TASK-300 A skill instruction is not an enforcement mechanism — only a hook cannot be skipped · FEATURE-014
  - [ ] TASK-301 `Birko.EventBus.Outbox.SQL` is in the build and in no documentation index · FEATURE-014
  - [ ] TASK-302 The SQL test suites have leaked ~90,000 temp directories, and every teardown swallows the failure · FEATURE-014
  - [x] TASK-303 A composite `PRIMARY KEY (a, b)` cannot be declared at all, and TimescaleDB needs one · FEATURE-014
  - [ ] TASK-304 `AbstractDatabaseModel`'s `[UniqueField]` on `Guid` forbids the composite key TASK-303 just enabled · FEATURE-014
  - [ ] TASK-305 The default `RetryPolicy` is `None`, so every retry path in the SQL layer is inert — decide whether it should be · FEATURE-014
  - [ ] TASK-306 Every live provider suite runs ~19 parallel classes against ONE database, and two different failures follow · FEATURE-014
  - [ ] TASK-328 Decide: should the event↔tenant bridge REFUSE to widen when it cannot establish a tenant? · FEATURE-014
  - [x] TASK-329 The SQL bulk stores never apply `RequireBoundedFilter`, so a filter that reduces to every row rewrites the table · FEATURE-014
  - [ ] TASK-330 Decide whether the `Action<T>` destructive overloads need an all-rows door of their own shape · FEATURE-014
  - [ ] TASK-331 An expression-valued UPDATE that binds no parameter issues no statement at all, silently · FEATURE-014
  - [x] TASK-332 The migration runner's own bookkeeping table could not be created on MySQL or SQL Server · FEATURE-014
  - [ ] TASK-446 All seven workflow instance stores have zero store-level tests, and that is where two high findings lived · FEATURE-014
  - [x] TASK-447 A Cosmos view filter's string value breaks out of its own quotes · FEATURE-014
  - [ ] TASK-448 Two § Conventions entries disagree about what a rendered expression tree leaks, and one sink acts on the wrong one · FEATURE-014
  - [x] TASK-450 The Cosmos migrator escapes filter values with SQL-standard doubling, which Cosmos does not use · FEATURE-014
  - [ ] TASK-451 `StoreDataDelegate<T>` declares a return value that 96 call sites discard · FEATURE-014
  - [ ] TASK-452 The "detach before writing to a store-owned object" rule now has three implementations · FEATURE-014
  - [ ] TASK-453 Decide whether an unchanged ViewModel save should skip the write · FEATURE-014
  - [ ] TASK-454 A decorator-hidden row reads as absent, and updating it resurrects it with its columns blanked · FEATURE-014
  - [ ] TASK-456 `AsyncMongoDBStore.CountIn` dereferences a nullable `Collection` — two CS8602 warnings · FEATURE-014
  - STORY-024 Critical findings — done (0/0 done)
  - STORY-025 High findings — done (0/0 done)
  - STORY-026 Medium findings — in-progress (0/0 done)
  - STORY-027 Low findings — done (0/0 done)
  - STORY-042 Integration-test tier — the Docker-gated remediation findings — planned (0/0 done)
  - STORY-043 Workflow backends — unify the serialization seam (ISerializer everywhere) — done (0/0 done)
  - STORY-051 Spec-harvest — high findings — in-progress (42/47 done)
    - [x] TASK-108 `Pbkdf2PasswordHasher.Verify` returns `true` for any password against an empty-segment hash · FEATURE-014
    - [x] TASK-109 A null or untranslatable filter renders `DELETE FROM "T"` — the whole table · FEATURE-014
    - [x] TASK-110 ORDER BY identifiers reach SQL text unresolved and unquoted · FEATURE-014
    - [x] TASK-111 `rule.Field` reaches the WHERE clause unresolved and unquoted · FEATURE-014
    - [x] TASK-112 `long` / `double` / `float` / `short` / `byte[]` map to no column and never persist · FEATURE-014
    - [x] TASK-113 `TenantSyncProvider` scopes only saves — reads, previews and deletes span every tenant · FEATURE-014
    - [x] TASK-114 The item-level tenant write guard trusts the caller-supplied `TenantGuid` · FEATURE-014
    - [x] TASK-115 A nested `WithTenant` does not narrow reads inside an all-tenants scope · FEATURE-014
    - [x] TASK-116 `RuleSpecification` leaves degrade to match-all — on the destructive paths · FEATURE-014
    - [x] TASK-117 `RedisCache.ClearAsync` issues `FLUSHDB` when no `KeyPrefix` is set · FEATURE-014
    - [x] TASK-118 The tenant header/claim guard covers only the hard-coded `X-Tenant-Id` · FEATURE-014
    - [x] TASK-125 `ReadOne` queries the connector directly, bypassing every store decorator · FEATURE-014
    - [x] TASK-126 `TagServiceBase` states its tenant contract in a comment and enforces nothing · FEATURE-014
    - [x] TASK-128 The view path's ORDER BY still interpolates caller text — the twin TASK-110 did not cover · FEATURE-014
    - [x] TASK-129 An aggregate view's generated DDL carries a double alias, so no persistent aggregate view can be created · FEATURE-014
    - [x] TASK-137 An empty `NOT IN` renders `1 = 1` — indistinguishable from `' OR 1=1--` in a query log · FEATURE-014
    - [x] TASK-141 MongoDB's four null-filter guards have no regression test · FEATURE-014
    - [x] TASK-207 `View.AddField` still drops a duplicate field key silently — the general case behind TASK-129's second defect · FEATURE-014
    - [x] TASK-209 A persistent view's non-aggregate columns are created unquoted and read back quoted — every such view is unqueryable on PostgreSQL · FEATURE-014
    - [x] TASK-212 A MongoDB `Delete(filter)` guards only a NULL filter — a filter that *reduces* to everything is not refused · FEATURE-014
    - [x] TASK-213 A COMPUTED operand inside `Contains` is silently discarded and replaced by a different predicate · FEATURE-014
    - [x] TASK-214 A model deriving `MongoDBModel` cannot be serialized by the driver at all · FEATURE-014
    - [x] TASK-215 Wire `RequireBoundedFilter` into the base wrappers, InMemory and ElasticSearch · FEATURE-014
    - [x] TASK-218 An `IN` filter over a C# **array** does not translate on MongoDB — `NotSupportedException` · FEATURE-014
    - [x] TASK-219 `Birko.Data.MongoDB` has two contradictory answers for what `_id` is · FEATURE-014
    - [x] TASK-220 CosmosDB has the same array-`Contains` defect as MongoDB — audit the rest of the family · FEATURE-014
    - [x] TASK-221 RavenDB cannot translate **any** set-membership filter — `Contains` is unsupported in every spelling · FEATURE-014
    - [x] TASK-222 RavenDB diverges on 6 filter shapes — and one of them is a **silent wrong answer** · FEATURE-014
    - [x] TASK-223 CosmosDB's connection mode cannot be selected — Gateway is unreachable, so the emulator is too · FEATURE-014
    - [x] TASK-224 `DateTime.Date` in a CosmosDB filter renders as a JSON sub-property and silently matches nothing · FEATURE-014
    - [x] TASK-225 MongoDB's connection string is composed with no escape hatch — no driver option can be set · FEATURE-014
    - [x] TASK-308 Triage the 7 remaining high spec-harvest findings in `filter-expression-translation` · FEATURE-014
    - [x] TASK-309 Triage the 7 remaining high spec-harvest findings in `data-sync` · FEATURE-014
    - [x] TASK-310 Triage the 3 remaining high spec-harvest findings in `caching` · FEATURE-014
    - [x] TASK-311 Triage the 2 remaining high spec-harvest findings in `tenant-isolation` · FEATURE-014
    - [x] TASK-312 Triage the 1 remaining high spec-harvest finding in `security-and-authorization` · FEATURE-014
    - [x] TASK-313 Triage the 4 remaining high spec-harvest findings in `entity-localization` · FEATURE-014
    - [x] TASK-314 Triage the 5 remaining high spec-harvest findings in `migrations` · FEATURE-014
    - [x] TASK-315 Triage the 2 remaining high spec-harvest findings in `workflow-state-machine` · FEATURE-014
    - [x] TASK-316 Triage the 2 remaining high spec-harvest findings in `repository-contract` · FEATURE-014
    - [ ] TASK-317 Triage the 1 remaining high spec-harvest finding in `background-jobs` · FEATURE-014
    - [ ] TASK-318 Triage the 1 remaining high spec-harvest finding in `event-bus-and-messaging` · FEATURE-014
    - [ ] TASK-319 Triage the 1 remaining high spec-harvest finding in `schema-index-and-ddl` · FEATURE-014
    - [ ] TASK-320 Triage the 1 remaining high spec-harvest finding in `specifications-and-paging` · FEATURE-014
    - [x] TASK-321 Triage the 1 remaining high spec-harvest finding in `store-crud-contract` · FEATURE-014
    - [x] TASK-322 Triage the 1 remaining high spec-harvest finding in `views-and-aggregation` · FEATURE-014
    - [ ] TASK-445 `ConflictResolution.Merge` is a silent no-op, and the enum advertises it as supported · FEATURE-014
  - STORY-053 Spec-harvest — medium findings — planned (0/25 done)
    - [ ] TASK-151 Triage the 36 medium spec-harvest findings in `views-and-aggregation` · FEATURE-014
    - [ ] TASK-152 Triage the 33 medium spec-harvest findings in `migrations` · FEATURE-014
    - [ ] TASK-153 Triage the 29 medium spec-harvest findings in `filter-expression-translation` · FEATURE-014
    - [ ] TASK-154 Triage the 25 medium spec-harvest findings in `schema-index-and-ddl` · FEATURE-014
    - [ ] TASK-155 Triage the 24 medium spec-harvest findings in `event-bus-and-messaging` · FEATURE-014
    - [ ] TASK-156 Triage the 22 medium spec-harvest findings in `validation-and-rules` · FEATURE-014
    - [ ] TASK-157 Triage the 21 medium spec-harvest findings in `data-sync` · FEATURE-014
    - [ ] TASK-158 Triage the 21 medium spec-harvest findings in `background-jobs` · FEATURE-014
    - [ ] TASK-159 Triage the 20 medium spec-harvest findings in `store-decorator-composition` · FEATURE-014
    - [ ] TASK-160 Triage the 20 medium spec-harvest findings in `llm-provider-and-agents` · FEATURE-014
    - [ ] TASK-161 Triage the 18 medium spec-harvest findings in `tenant-isolation` · FEATURE-014
    - [ ] TASK-162 Triage the 16 medium spec-harvest findings in `repository-contract` · FEATURE-014
    - [ ] TASK-163 Triage the 15 medium spec-harvest findings in `store-crud-contract` · FEATURE-014
    - [ ] TASK-164 Triage the 15 medium spec-harvest findings in `settings-configuration-chain` · FEATURE-014
    - [ ] TASK-165 Triage the 15 medium spec-harvest findings in `security-and-authorization` · FEATURE-014
    - [ ] TASK-166 Triage the 15 medium spec-harvest findings in `entity-tagging` · FEATURE-014
    - [ ] TASK-167 Triage the 14 medium spec-harvest findings in `serialization` · FEATURE-014
    - [ ] TASK-168 Triage the 14 medium spec-harvest findings in `entity-localization` · FEATURE-014
    - [ ] TASK-169 Triage the 14 medium spec-harvest findings in `caching` · FEATURE-014
    - [ ] TASK-170 Triage the 13 medium spec-harvest findings in `bulk-filter-operations` · FEATURE-014
    - [ ] TASK-171 Triage the 12 medium spec-harvest findings in `specifications-and-paging` · FEATURE-014
    - [ ] TASK-172 Triage the 9 medium spec-harvest findings in `workflow-state-machine` · FEATURE-014
    - [ ] TASK-323 Fix the 3 verified medium findings in `core-model-contracts` · FEATURE-014
    - [ ] TASK-324 Fix the 1 verified medium finding in `store-lazy-initialization` · FEATURE-014
    - [ ] TASK-325 Fix the 3 verified medium findings in `unit-of-work-and-transactions` · FEATURE-014
  - STORY-054 Spec-harvest — low findings — planned (0/24 done)
    - [ ] TASK-173 Triage the 31 low spec-harvest findings in `llm-provider-and-agents` · FEATURE-014
    - [ ] TASK-174 Triage the 29 low spec-harvest findings in `event-bus-and-messaging` · FEATURE-014
    - [ ] TASK-175 Triage the 24 low spec-harvest findings in `background-jobs` · FEATURE-014
    - [ ] TASK-176 Triage the 23 low spec-harvest findings in `security-and-authorization` · FEATURE-014
    - [ ] TASK-177 Triage the 23 low spec-harvest findings in `data-sync` · FEATURE-014
    - [ ] TASK-178 Triage the 22 low spec-harvest findings in `migrations` · FEATURE-014
    - [ ] TASK-179 Triage the 21 low spec-harvest findings in `workflow-state-machine` · FEATURE-014
    - [ ] TASK-180 Triage the 20 low spec-harvest findings in `views-and-aggregation` · FEATURE-014
    - [ ] TASK-181 Triage the 19 low spec-harvest findings in `validation-and-rules` · FEATURE-014
    - [ ] TASK-182 Triage the 19 low spec-harvest findings in `store-crud-contract` · FEATURE-014
    - [ ] TASK-183 Triage the 18 low spec-harvest findings in `settings-configuration-chain` · FEATURE-014
    - [ ] TASK-184 Triage the 17 low spec-harvest findings in `caching` · FEATURE-014
    - [ ] TASK-185 Triage the 15 low spec-harvest findings in `tenant-isolation` · FEATURE-014
    - [ ] TASK-186 Triage the 14 low spec-harvest findings in `entity-tagging` · FEATURE-014
    - [ ] TASK-187 Triage the 13 low spec-harvest findings in `store-decorator-composition` · FEATURE-014
    - [ ] TASK-188 Triage the 13 low spec-harvest findings in `specifications-and-paging` · FEATURE-014
    - [ ] TASK-189 Triage the 13 low spec-harvest findings in `filter-expression-translation` · FEATURE-014
    - [ ] TASK-190 Triage the 13 low spec-harvest findings in `bulk-filter-operations` · FEATURE-014
    - [ ] TASK-191 Triage the 10 low spec-harvest findings in `serialization` · FEATURE-014
    - [ ] TASK-192 Triage the 10 low spec-harvest findings in `schema-index-and-ddl` · FEATURE-014
    - [ ] TASK-193 Triage the 10 low spec-harvest findings in `repository-contract` · FEATURE-014
    - [ ] TASK-194 Triage the 10 low spec-harvest findings in `entity-localization` · FEATURE-014
    - [ ] TASK-326 Fix the 1 verified low finding in `core-model-contracts` · FEATURE-014
    - [ ] TASK-327 Fix the 3 verified low findings in `unit-of-work-and-transactions` · FEATURE-014
  - STORY-055 Spec-harvest — the three unrated areas — done (1/1 done)
    - [x] TASK-195 Rate, ID and fold the 16 recovered findings into the severity backlog · FEATURE-014
- **EPIC-015** Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web — in-progress (10/22 tasks done)
  - [x] TASK-054 Xaml restyled Slider (Tier-1 gap) + `Range` Form field type · FEATURE-015
  - [x] TASK-055 Xaml Form field-type parity with b-form (wire existing controls + FormField props) · FEATURE-015
  - [x] TASK-056 Xaml date & time picker controls + field types · FEATURE-015
  - [x] TASK-057 Xaml Form field types: MultiSelect / Tags / File · FEATURE-015
  - [x] TASK-101 Avalonia `Ribbon`: pinned vs temporary-reveal collapse, to match `b-ribbon` and Office · FEATURE-015
  - [x] TASK-102 Avalonia `Ribbon`: a narrow fallback, mirroring `b-ribbon`'s hamburger · FEATURE-015
  - [ ] TASK-103 Every Avalonia control needs a focus visual — `Buttons.axaml` has none · FEATURE-015
  - STORY-029 Tier 0 — single-source design tokens + multi-target generator — done (0/0 done)
  - STORY-030 Tier 0 — Avalonia theme system + runtime ThemeVariant swap — done (0/0 done)
  - STORY-031 Tier 0 validation — Avalonia gallery app + first restyled controls — done (0/0 done)
  - STORY-032 Birko.Xaml.Core — i18n ({l:Tr}) + base ViewModels (Avalonia-free) — done (0/0 done)
  - STORY-033 Building blocks — schema-driven Form, Drawer, SplitPanel — done (0/0 done)
  - STORY-034 Tier 1 — restyled native controls (~20) — done (0/0 done)
  - STORY-035 Tier 2 — composite controls with no native peer — done (0/0 done)
  - STORY-036 Tier 3 — Birko.Xaml.Shell: page bases + app chrome + navigation — done (0/0 done)
  - STORY-048 Avalonia 12 / .NET 10 upgrade for the Birko.Xaml stack — planned (0/5 done)
    - [ ] TASK-092 Bump Birko.Xaml to Avalonia 12.1.0 / `net10.0` + xunit v3 (Kanban DataTransfer, focus event) · FEATURE-015
    - [ ] TASK-093 Decide the LiveCharts story for Avalonia 12 (the only blocker on the bump) · FEATURE-015
    - [ ] TASK-094 Clear the 28 Avalonia 12 obsolete warnings (`Watermark`, `Bitmap.Save`) · FEATURE-015
    - [ ] TASK-095 Screenshot baseline gate for the Avalonia suite (build it *before* the Av12 bump) · FEATURE-015
    - [ ] TASK-096 Roll Avalonia 12 out to consumer repos in lockstep · FEATURE-015
  - STORY-049 Office-style ribbon overflow — progressive group scaling + group-to-popup collapse — done (4/4 done)
    - [x] TASK-097 Make the existing ribbon overflow reachable (interim fix, both skins) · FEATURE-015
    - [x] TASK-098 Ribbon model + tokens: size variant, scaling priority, group icon (XAML **and** web together) · FEATURE-015
    - [x] TASK-099 The degrade pass — measure and scale groups Large → Medium → Small in priority order · FEATURE-015
    - [x] TASK-100 Group-collapse-to-popup — the chunk button and its flyout · FEATURE-015
  - STORY-056 Mixed per-item size variants within one ribbon group — planned (0/6 done)
    - [ ] TASK-119 Decide the mixed-size model: per-item degrade order, or fixed group templates · FEATURE-015
    - [ ] TASK-120 The mixed-size model, in both skins, with its tokens · FEATURE-015
    - [ ] TASK-121 Reformulate the degrade ladder for mixed-size groups · FEATURE-015
    - [ ] TASK-122 Render mixed columns — the CSS grid and the Avalonia panel · FEATURE-015
    - [ ] TASK-123 Panel height under mixed sizes, and extending the clipping guard · FEATURE-015
    - [ ] TASK-124 The `RibbonGroupSize` doc comment describes a parity gap that no longer exists · FEATURE-015
- **EPIC-016** Birko framework backports from Reps (+ cross-provider & Xaml follow-ups) — in-progress (14/14 tasks done)
  - STORY-037 Backend / SQL framework backports (shipped) — done (0/0 done)
  - STORY-038 Frontend Birko.Web backports (shipped) — done (0/0 done)
  - STORY-039 Cross-provider SQL store-factory + DI backport — in-progress (2/2 done)
    - [x] TASK-042 Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL · FEATURE-016
    - [x] TASK-051 FIX: MSSqlStore.SetSettings drops connection fields (lossy) · FEATURE-016
  - STORY-040 Web → Xaml UI / offline / device backports — done (6/6 done)
    - [x] TASK-043 Xaml mobile app-shell (BMobileAppShell equivalent) · FEATURE-016
    - [x] TASK-044 Formatter for Birko.Xaml.Core (duration + culture-aware) · FEATURE-016
    - [x] TASK-045 Xaml wake-lock device abstraction (IWakeLock) · FEATURE-016
    - [x] TASK-046 Xaml offline read-through mirror (MirrorStore / readThrough concept) · FEATURE-016
    - [x] TASK-047 Xaml sync-status indicator (offline / syncing / synced) · FEATURE-016
    - [x] TASK-048 Xaml audio-cue device util (beep + vibrate) · FEATURE-016
  - STORY-041 BMobileAppShell showcase / placement — done (2/2 done)
    - [x] TASK-049 BMobileAppShell — better placement / demo in Birko.Web.Playground · FEATURE-016
    - [x] TASK-050 BMobileAppShell (Xaml) — showcase in Birko.Xaml.Gallery · FEATURE-016
  - STORY-052 Component gaps found by consumers adopting the `b-*` catalogue — in-progress (4/4 done)
    - [x] TASK-104 `b-chart`: axis polish for small charts (tick density, nice scale, latest-value overlay, threshold labels) · FEATURE-016
    - [x] TASK-105 `b-card`: the missing `md` padding rung, and elevation as a token · FEATURE-016
    - [x] TASK-107 `b-button`: a reachable tap target, and form participation · FEATURE-016
    - [x] TASK-135 `b-input type="decimal"`: comma-locale decimal entry, owned by the component · FEATURE-016
- **EPIC-017** Tenant isolation hardening — in-progress (0/1 tasks done)
  - STORY-044 Opt-in strict (fail-closed) tenancy mode — done (0/0 done)
  - STORY-045 Fix decorator ordering so per-tenant uniqueness probes are tenant-scoped — done (0/0 done)
  - STORY-046 Restore ambient (tenant) scope for background event dispatch — in-progress (0/1 done)
    - [ ] TASK-148 `ScopeRestorationBehavior` for the distributed-consumer dispatch path ⚠ blocked · FEATURE-017
- **EPIC-018** Birko.Web.Core — the browser-side runtime — in-progress (4/4 tasks done)
  - [x] TASK-198 `fetch` has no timeout, so a dead connection hung the app forever — and a stalled body reported success · FEATURE-018
  - [x] TASK-199 `SyncManager` had no name for a write that had already landed · FEATURE-018
  - [x] TASK-202 `ApiClient.get` corrupted any endpoint that already carried a query string · FEATURE-018
  - [x] TASK-203 "nothing recorded" and "never synced" both read as `[]` · FEATURE-018

## Loose tasks

- [x] TASK-036 Reorganize C:\Source into Birko/{Framework,Framework.Tests,Consumers} + aicode bucket (P1, ai)
- [ ] TASK-130 Scan every shipped theme for colour contrast, and gate it like the drift check (P1, ai)
- [ ] TASK-140 `resolveModuleFromHash` derives the module positionally and never consults the route table (P1, ai)
- [ ] TASK-200 Symbio: an outbox replay duplicates a create, and TASK-151 scoped the cause out of itself (P1, ai)
- [x] TASK-457 Consolidate the 349-repo framework polyrepo into one repo under a GitHub org (P1, ai)
- [ ] TASK-127 Decide what `WithAllTenants` means when a tenant is also in scope (P2, human)
- [ ] TASK-138 `ReadAsync()` with no arguments does not compile — CS0121 between the read-all and filtered overloads (P2, ai)
- [ ] TASK-139 Decide whether a `pointer: coarse` rule inside a `b-*` component is policy or a knob (P2, human)
- [ ] TASK-142 The spec map silently under-covers, and nothing detects it (P2, human)
- [ ] TASK-143 Stores that override public CRUD instead of `*Core` defeat every base-class guard (P2, human)
- [ ] TASK-145 Nothing at the `GetUnwrappedStore` call sites says they strip every decorator (P2, ai)
- [ ] TASK-147 `AttachTagAsync` validates neither a tag's existence nor its ownership (P2, human)
- [ ] TASK-149 A story that tracks work without task files is invisible to every scheduler (P2, human)
- [x] TASK-201 Reps: declare `idPinned` on the client-minted creates — and not on the one that must not have it (P2, ai)
- [ ] TASK-206 `HybridCache`'s L2 fallback filter cannot tell a misconfiguration from an outage (P2, human)
- [ ] TASK-297 `.vscode/tasks.json` and `launch.json` target a project this repo does not contain (P2, ai)
- [x] TASK-449 `PerlinNoise` and `SimplexNoise` take a seed but are not reproducible across .NET versions (P2, ai)
- [x] TASK-459 Five tests fail on Linux that pass on Windows (P2, ai)
- [x] TASK-461 Sandbox covers every reachable area, and reports it readably (P2, ai)
- ~~TASK-463 `TagServiceBase` is documented in three places and exists in none — CANCELLED, the premise was false~~ (P2, ai)
- [ ] TASK-466 Firefox shows no validation bubble for an invalid `b-*` control, and logs an error instead (P2, ai)
- [ ] TASK-468 `BIRKO_SRC` names the Framework checkout to MSBuild and the Web checkout to esbuild (P2, ai)
- [ ] TASK-059 Decide the long-term convention for nested `.projitems` imports (MSB4011) (P3, ai)
- [ ] TASK-106 Decide whether `::part` is a catalogue convention or stays a one-off (P3, human)
- ~~TASK-235 `FisData.Stock.Angular.Server` will fail `NETSDK1087` when its net10 migration lands~~ (P3, ai)
- [x] TASK-444 `Birko.Models.Inventory` had no stock-balance model, and `IBatchable` had no implementors (P3, unassigned)
- [x] TASK-455 `Udp` cannot receive multicast, and binds exclusively — LAN device discovery is not expressible (P3, ai)
- [x] TASK-458 Turn `PREZENTACIA.md` into published framework documentation (P3, ai)
- [ ] TASK-460 Move the 350 archived repos into an attic org (P3, ai)
- [x] TASK-462 Two warnings a consumer sees on every build (P3, ai)
- [x] TASK-464 ~~An unconfigured sync file store throws where its async twin returns empty~~ (P3, ai)
- [ ] TASK-465 Every `pr:` SHA recorded before the monorepo resolves to nothing (P3, ai)
- [ ] TASK-467 Borrowed validity enforces nothing, silently, where the engine lacks the input type (P3, ai)

## Completed

<details>
<summary>1 completed epic(s)</summary>

- **EPIC-009** Birko.Communication — Remaining protocols — done (2/2 tasks done)
  - STORY-019 gRPC support — done (1/1 done)
    - [x] TASK-026 gRPC client + server support · FEATURE-009
  - STORY-020 OAuth2 authorization server — done (1/1 done)
    - [x] TASK-027 Implement Birko.Security.OAuth.Server · FEATURE-009

</details>
