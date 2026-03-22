# Birko Framework TODO

Tracks planned features, enhancements, and remaining work for the Birko Framework.

---

## Architecture Principle

**When to create separate projects vs. co-locate in providers:**

- **Separate projects** (`Birko.Feature.Platform/`) — 3+ files, own models/settings, self-contained subsystem (e.g., Migrations, Sync, Caching, Workflow)
- **Co-locate in provider** (`Birko.Data.SQL/Feature/`) — 1-2 files, thin adapter tightly coupled to provider (e.g., UnitOfWork, Paging)

---

## Priority Legend

- **High** - Immediate value, low complexity
- **Medium** - Good value, moderate complexity
- **Low** - Nice to have, or high complexity

---

## Completed Projects

All phases below are fully implemented. See each project's CLAUDE.md for details.

| Project | Status | Notes |
|---------|--------|-------|
| Birko.Data.Patterns | Done | UoW, Paging, Specification, SoftDelete, Audit, Timestamp, Concurrency, IndexManagement |
| Birko.Caching + Redis + Hybrid | Done | ICache, MemoryCache, RedisCache, HybridCache |
| Birko.Validation | Done | Fluent validators, store wrappers |
| Birko.BackgroundJobs + SQL + Redis | Done | Job queue, processor, dispatcher, scheduler |
| Birko.MessageQueue + MQTT + InMemory + Redis | Done | Core interfaces, MQTT, InMemory, Redis Streams |
| Birko.Storage + AzureBlob | Done | IFileStorage, LocalFileStorage, Azure Blob REST API |
| Birko.Messaging + Razor | Done | SMTP email, string templates, Razor templates |
| Birko.EventBus + MessageQueue + Outbox + EventSourcing | Done | In-process bus, distributed, outbox, event sourcing bridge |
| Birko.Telemetry + OpenTelemetry | Done | Store metrics, tracing, correlation ID middleware, OTLP |
| Birko.Security + BCrypt + Vault + AzureKeyVault + Jwt + AspNetCore | Done | Password hashing, encryption, JWT, RBAC, secret providers |
| Birko.Workflow + SQL/ES/MongoDB/RavenDB/JSON | Done | State machine, fluent builder, visualization, persistence |
| Birko.Time + Abstractions | Done | Clock, time zones, business calendar, holidays |
| Birko.Health + Data + Redis + Azure | Done | Health checks for all infrastructure |
| Birko.Serialization + Newtonsoft + MessagePack + Protobuf | Done | Unified ISerializer, 4 implementations |
| Birko.Localization + Data | Done | Translation framework, CLDR pluralization, DB provider |
| Birko.Data.Localization | Done | Entity-level localization, store wrappers |
| Birko.CQRS | Done | Commands, queries, pipeline behaviors, mediator |
| Birko.Data.Processors | Done | XML, CSV, HTTP, ZIP processors with decorator composition |
| Birko.Data.Aggregates | Done | SQL-NoSQL aggregate mapper, flatten/expand, sync pipeline |
| Birko.Data.Migrations + all providers | Done | Migration framework, all DB providers |
| Birko.Data.Sync + all providers | Done | Data sync framework, all providers |
| Birko.Rules | Done | Data-driven rule engine |

---

## Remaining Work

### Birko.Data.Patterns — Timestamp Audit (Done)
**Priority:** Medium

- [x] Removed `AbstractLogModel` field defaults (`= DateTime.UtcNow`) — `TimestampStoreWrapper` is the canonical timestamp source
- [x] Added unit tests for Timestamp, SoftDelete, and Audit wrappers in `Birko.Data.Tests/Decorators/`

---

### Birko.Caching.NCache
**Status:** Planned | **Priority:** Low

NCache distributed cache implementation.

**Dependencies:** Birko.Caching, Alachisoft.NCache.Client

---

### Birko.Storage — Cloud Providers
**Priority:** Medium–Low

| Project | Provider | Priority | Dependencies |
|---------|----------|----------|-------------|
| Birko.Storage.Aws | AWS S3 | Medium | Birko.Storage, AWSSDK.S3 |
| Birko.Storage.Google | Google Cloud Storage | Low | Birko.Storage, Google.Cloud.Storage.V1 |
| Birko.Storage.Minio | MinIO (S3-compatible) | Low | Birko.Storage, Minio |

---

### Birko.Messaging — Providers
**Priority:** Medium–Low

| Project | Provider | Priority | Dependencies |
|---------|----------|----------|-------------|
| Birko.Messaging.SendGrid | SendGrid email | Medium | Birko.Messaging, SendGrid |
| Birko.Messaging.Twilio | Twilio SMS | Medium | Birko.Messaging, Twilio |
| Birko.Messaging.Mailgun | Mailgun email | Low | Birko.Messaging, Mailgun |
| Birko.Messaging.Firebase | FCM push notifications | Low | Birko.Messaging, FirebaseAdmin |
| Birko.Messaging.Apple | APNs push notifications | Low | Birko.Messaging, PushSharp |

---

### Birko.MessageQueue — Remaining Providers
**Priority:** Medium–Low

| Project | Provider | Priority | Dependencies |
|---------|----------|----------|-------------|
| Birko.MessageQueue.RabbitMQ | AMQP (exchanges, queues, publisher confirms) | Medium | Birko.MessageQueue, RabbitMQ.Client |
| Birko.MessageQueue.Kafka | Topics, partitions, consumer groups, offsets | Medium | Birko.MessageQueue, Confluent.Kafka |
| Birko.MessageQueue.Azure | Azure Service Bus (queues, topics, sessions) | Low | Birko.MessageQueue, Azure.Messaging.ServiceBus |
| Birko.MessageQueue.Aws | AWS SQS (standard + FIFO) | Low | Birko.MessageQueue, AWSSDK.SQS |
| Birko.MessageQueue.MassTransit | MassTransit adapter | Low | Birko.MessageQueue, MassTransit |

---

### Birko.Telemetry — Additional Exporters
**Priority:** Low

| Project | Provider | Dependencies |
|---------|----------|-------------|
| Birko.Telemetry.Prometheus | Prometheus metrics | Birko.Telemetry, Prometheus.Client |
| Birko.Telemetry.Seq | Seq log exporter | Birko.Telemetry, Seq.Client |
| Birko.Telemetry.Grafana | LGTM stack (Loki, Grafana, Tempo, Mimir), dashboard provisioning | Birko.Telemetry, Grafana HTTP API |

---

### Birko.Health — Planned Health Checks
**Priority:** Low

Add when corresponding providers are implemented:

| Health Check | Service | Probe | Target Project |
|-------------|---------|-------|---------------|
| ~~WebSocketHealthCheck~~ | ~~WebSocket server~~ | ~~TCP + WS handshake~~ | ~~Birko.Health.Data~~ |
| ~~SseHealthCheck~~ | ~~SSE endpoint~~ | ~~HTTP GET + event stream~~ | ~~Birko.Health.Data~~ |
| ~~TcpHealthCheck~~ | ~~Generic TCP~~ | ~~TCP connect + latency~~ | ~~Birko.Health.Data~~ |
| RabbitMqHealthCheck | RabbitMQ | HTTP management API | Birko.Health.Data |
| KafkaHealthCheck | Apache Kafka | Metadata request | Birko.Health.Data |
| AzureServiceBusHealthCheck | Azure Service Bus | REST API probe | Birko.Health.Azure |
| AwsSqsHealthCheck | AWS SQS | GetQueueAttributes | Birko.Health.Aws (new) |

---

### Birko.Structures — Additional Data Structures
**Status:** Planned | **Priority:** Low

Extend existing tree structures (AVL, BST, Tree) with general-purpose data structures.

**Currently implemented:** Trees (Tree, AVLTree, BinaryNode, BinarySearchNode, Node) + Extensions

**Planned additions:**

| Category | Structures | Use Cases |
|----------|-----------|-----------|
| Graphs | Graph, DirectedGraph, WeightedGraph | Workflow routing, dependency resolution, migration ordering |
| Heaps | BinaryHeap, MinHeap, MaxHeap | Job scheduling, event ordering |
| Tries | Trie, CompressedTrie | Autocomplete, localization key lookup |
| Caches | LruCache | Lightweight eviction without full Birko.Caching |
| Filters | BloomFilter | Deduplication in event bus, cache prefetch |
| Buffers | RingBuffer | Telemetry sampling, sliding window metrics |
| Trees | IntervalTree | Business calendar overlap, time-range queries |
| Sets | DisjointSet | Tenant grouping, data sync partitioning |
| Lists | SkipList, Deque | Concurrent ordered collections, work-stealing |

**Dependencies:** None

---

### Birko.Random — Random Number Generators (Done)
**Status:** Done | **Priority:** Low

Pluggable random number generation with testable abstractions.

| Category | Components | Use Cases |
|----------|-----------|-----------|
| Providers | SystemRandom, CryptoRandom, XorShift, MersenneTwister, SplitMix, TestRandom | Testable randomness, secure tokens |
| Distributions | Uniform, Normal, Exponential, Poisson, Bernoulli | Load testing, simulation, retry jitter |
| Sequences | GuidGenerator (v4/v7), NanoId, Snowflake, TokenGenerator | Distributed IDs, URL-safe IDs, API keys |
| Noise | PerlinNoise, SimplexNoise | Procedural content, test data |

**Dependencies:** None

---

## Existing Project Enhancements

### Birko.Data.SQL (Done)
- [x] Index management (SqlIndexManager + PostgreSql, MSSql, SqLite, MySql dialect subclasses)
- [x] Connection resiliency and retry logic (RetryPolicy on AbstractConnectorBase, provider-specific IsTransientException)
- [x] Bulk copy for all SQL providers (MSSql: SqlBulkCopy, PostgreSQL: COPY binary protocol, MySQL: multi-value INSERT batching, SQLite: transaction-batched prepared statements)
- [x] Query caching for frequently executed queries (CachedAsyncDataBaseBulkStore decorator with ICache integration, SHA256 key builder, table-prefix invalidation on writes)

### Birko.Data.ElasticSearch
- [x] Index management utilities
- [x] Re-indexing helpers
- [x] IIndexManager adapter (ElasticSearchIndexManagerAdapter)
- [x] Connection resiliency (MaxRetries + RequestTimeout on ConnectionSettings)
- [x] Search result highlighting (HighlightOptions, SearchResult<T>, HighlightedSearchResults<T>, SearchWithHighlights sync/async with NEST Highlight API)

### Birko.Data.MongoDB
- [x] Index management utilities (MongoDBIndexManager — create, drop, list, exists, info, compound/text/geospatial indexes)
- [x] TTL index support for auto-expiring documents
- [x] Connection resiliency (retryWrites=true, retryReads=true in connection string)
- [x] Change stream support (ChangeStreamEvent<T>, ChangeStreamOptions, WatchAsync/Watch on stores, resume token support)
- [x] Aggregation pipeline builders (AggregationPipelineBuilder<T> with fluent Match/Group/Sort/Project/Limit/Skip/Unwind/Lookup/Count/AddFields, ToListAsync/FirstOrDefaultAsync execution)

### Birko.Data.RavenDB
- [x] Connection resiliency (RequestTimeout on DocumentConventions)

### Birko.Data.InfluxDB
- [x] Connection resiliency (RetryPolicy on Settings, transient error detection, sync/async retry wrappers on all CRUD operations)

### Birko.Communication
- [ ] GraphQL client
- [ ] gRPC support
- [x] OAuth2 client (Birko.Communication.OAuth) — Client Credentials, Auth Code, PKCE, Device Code, Refresh Token, DelegatingHandler
- [ ] OAuth2 server (Birko.Security.OAuth.Server) — Token endpoint, authorization endpoint, client registration, consent management (needs Birko.Data.Stores for token/client persistence)

### Birko.Models — Restructuring
**Status:** Planned | **Priority:** High

Current problems:
- SQL attributes (`[Table]`, `[UniqueField]`, `[PrecisionField]`) baked into domain models
- `Product` has no variants (variants are in Warehouse — wrong domain boundary)
- `Repository` name clashes with data access pattern (means warehouse location)
- `WareHouseDocumentItem` has 15+ embedded price fields (pricing should be composable)
- `Product` and `Item` duplicate properties (Code, BarCode, Name) with no shared contract
- Inconsistent base classes (`Product` → `AbstractLogModel`, `Item` → `AbstractDatabaseLogModel`)
- 1:1 Model↔ViewModel mirroring creates massive duplication

**Phase A — Contracts & Value Objects (non-breaking)**

New project `Birko.Models.Contracts/`:
- [ ] `ICatalogItem` — Name, Code, BarCode, Description
- [ ] `IPriceable` — Price, PriceVAT, VAT, Currency
- [ ] `IVariantable` — Variants collection
- [ ] `ICategorizeable` — CategoryId
- [ ] `IBatchable` — BatchNumber, ExpiryDate
- [ ] `ILocatable` — LocationId (warehouse/building)
- [ ] `IHierarchical` — ParentId, Path (replaces AbstractTree)
- [ ] `IDocument` / `IDocumentLine` — DocumentNumber, Status, Lines / Quantity, UnitPrice
- [ ] `IContactable` / `IAddressable` — Phone, Email / Street, City, ZIP, Country

New value objects in `Birko.Models/ValueObjects/`:
- [ ] `Money` — Amount + CurrencyCode (replaces scattered decimals)
- [ ] `MoneyWithTax` — Price + PriceVAT + VAT (replaces ValueData)
- [ ] `Percentage` — Value decimal (replaces AbstractPercentage)
- [ ] `PostalAddress` — Street, City, Zip, Country, State (immutable record)
- [ ] `Quantity` — Amount + Unit

Have old models implement new contracts for gradual compatibility.

**Phase B — Clean domain model projects (parallel to old)**

| New Project | Replaces | Key Changes |
|-------------|----------|-------------|
| `Birko.Models.Catalog` | Birko.Models.Product + Category | Product with variants, attributes, categories. No SQL attributes. |
| `Birko.Models.Inventory` | Birko.Models.Warehouse | StockItem, StockMovement, StorageLocation (renamed from Repository), ReceiptDocument, IssueDocument, TransferDocument. Pricing extracted. |
| `Birko.Models.Pricing` | Pricing fields from Warehouse + Accounting | PriceGroup, PriceList, PriceListEntry, Tax, Currency, Discount. Uses Money value object. |

Refactor existing:
| Project | Changes |
|---------|---------|
| `Birko.Models.Users` | Remove SQL attributes, rename Agenda→Tenant |
| `Birko.Models.Customers` | Remove SQL attributes, use PostalAddress value object |
| `Birko.Models.Accounting` | Keep Tax, Currency, MeasureUnit. Move PriceGroup to Pricing. |

**Phase C — SQL separation**

New project `Birko.Models.SQL/`:
- [ ] SQL mapping via partial classes or fluent `ModelMap<T>.ToTable("X").HasUnique(p => p.SKU)` configuration
- [ ] All `[Table]`, `[UniqueField]`, `[PrecisionField]`, `[ScaleField]` attributes move here
- [ ] Models extend clean `AuditableModel` instead of `AbstractDatabaseLogModel`

**Migration path:**
1. Phase A — additive, zero breakage. Old models implement new contracts.
2. Phase B — new projects live alongside old. New consumers (Symbio) adopt new. Old consumers keep old.
3. Phase C — extract SQL. Old models become thin wrappers / deprecated.

**Consumer impact:**
- Symbio: Can adopt Contracts + Value Objects immediately (Phase A). Phase B models align with Symbio's existing entity design.
- DraCode: No breakage — old models stay until manual migration.
- Affiliate: No breakage — old models stay.

---

## Decisions Pending

### Birko.Data.RavenDB — Map/Reduce Index Management
**Status:** ✅ Done (Option A implemented) | **Priority:** Medium

RavenDBIndexManager implements IIndexManager with full lifecycle: create (from IndexDefinition or AbstractIndexCreationTask), drop, list, exists, info, reset, enable/disable, priority, stale detection. Map/reduce supported via Properties dict (`Map`, `Reduce` keys).

**Remaining enhancements:**
- [x] Bulk deploy indexes from assembly (DeployFromAssemblyAsync — scans assembly for AbstractIndexCreationTask types)
- [x] Query helpers for Map/Reduce results with expression-based filters (QueryMapReduceAsync, QueryMapReduceFirstAsync, CountMapReduceAsync)
- [ ] Attribute-driven index definitions (Option B — deferred, low priority)

### Birko.Data.SQL.View — Persistent View Support (Done)
**Status:** Done | **Priority:** Medium

- [x] `CREATE VIEW` / `CREATE OR REPLACE VIEW` generation from existing attribute definitions
- [x] `DROP VIEW` support
- [x] Database-specific syntax (MSSql, PostgreSQL, MySQL, SQLite differences) — separate projects: Birko.Data.SQL.{MSSql,PostgreSQL,MySQL,SqLite}.View
- [x] View existence check before create
- [x] Integration with migration framework (ViewSqlGenerator + ViewMigrationExtensions in Birko.Data.SQL.View.Migrations shared project)
- [x] Option to query against persistent view vs on-the-fly SELECT (ViewQueryMode enum: OnTheFly/Persistent/Auto with cached existence checks and automatic fallback)
- [x] Materialized view support (PostgreSQL: sync+async Create/Refresh/Drop/Exists materialized views; MSSql: indexed views with SCHEMABINDING + clustered index; MaterializedViewType enum)

---

## Test Coverage Gaps

### Phase 1 — High Priority (Core Data Layer)
- [ ] Birko.Validation.Tests — fluent validation rules, validator composition
- [ ] Birko.Data.Patterns.Tests — Unit of Work, Soft Delete, Audit, Timestamp, Paging patterns
- [ ] Birko.Data.Sync.Tests — core sync framework + at least Birko.Data.Sync.Sql.Tests

### Phase 2 — Medium Priority (Platform Implementations)
- [ ] Birko.BackgroundJobs.SQL.Tests + Birko.BackgroundJobs.Redis.Tests
- [ ] Birko.Workflow.SQL.Tests
- [ ] Birko.EventBus.Outbox.Tests
- [ ] Birko.Communication.Camera.Tests

### Phase 3 — Infrastructure
- [ ] Birko.Data.Migrations.SQL.Tests
- [ ] Birko.Caching.Redis.Tests
- [ ] Birko.Communication.REST.Tests + Birko.Communication.WebSocket.Tests

### Phase 4 — Lower Priority
- [ ] Birko.Models.* — lightweight validation tests for model projects
- [ ] Birko.Data.*.ViewModel — CRUD pattern tests for ViewModel repositories
- [ ] Birko.Configuration, Birko.Contracts — mostly simple DTOs, low risk

---

## Technical Debt

- [ ] **MqttExtensions.cs** — MQTT v5 features (topic aliases, user properties). Low priority unless high-frequency IoT sensors need bandwidth optimization.

---

## Consumer Projects

See [docs/consumers.md](docs/consumers.md) for detailed per-project breakdown.

| Consumer | Birko Projects | Primary Data Store |
|----------|---------------|-------------------|
| Symbio | 50 | PostgreSQL, MSSql, MongoDB, TimescaleDB, RavenDB, ES |
| DraCode | 26 | SQLite |
| Affiliate | 22 | Elasticsearch, InfluxDB |
| FisData.Stock | 0 | *(inactive — models extracted to Birko.Models.*)* |

---

## Reference

For implementation details, refer to:
- [CLAUDE.md](./CLAUDE.md) — Framework overview
- Individual project CLAUDE.md files
- [docs/](docs/) folder for detailed documentation
- [docs/consumers.md](docs/consumers.md) — Consumer project reference

---

**Last Updated:** 2026-03-20
