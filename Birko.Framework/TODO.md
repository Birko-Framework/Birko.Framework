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
| Birko.Data.Patterns | Done | UoW, Paging, Specification, SoftDelete, Audit, Timestamp, Concurrency |
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

### Birko.Data.Patterns — Timestamp Audit (Pending)
**Priority:** Medium

- [ ] Consider removing `AbstractLogModel` field defaults (`= DateTime.UtcNow`) now that `TimestampStoreWrapper` is the canonical timestamp source
- [ ] Add `Birko.Data.Patterns.Tests` project with unit tests for all Timestamp wrappers

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

### Birko.Random — Random Number Generators
**Status:** Planned | **Priority:** Low

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

### Birko.Data.SQL
- [ ] Connection resiliency and retry logic
- [ ] Bulk copy for all SQL providers (currently MSSql only)
- [ ] Query caching for frequently executed queries
- [ ] Database-specific optimizations

### Birko.Data.ElasticSearch
- [ ] Index management utilities
- [ ] Re-indexing helpers
- [ ] Search result highlighting

### Birko.Data.MongoDB
- [ ] Change stream support
- [ ] Aggregation pipeline builders

### Birko.Communication
- [ ] GraphQL client
- [ ] gRPC support
- [ ] OAuth2 helpers in Authentication

### Birko.Models
- [ ] More base model types
- [ ] ViewModel to Model mapping utilities

---

## Technical Debt

- [ ] **MqttExtensions.cs** — MQTT v5 features (topic aliases, user properties). Low priority unless high-frequency IoT sensors need bandwidth optimization.

---

## Symbio Alignment

Symbio (`C:\Source\Symbio`) is the primary consumer (33 Birko projects referenced).

**Already integrated:** All core data access, patterns, tenant, migrations, security, communication, caching, validation, background jobs, message queue, event bus, storage, messaging.

**Lower priority for Symbio:**
- **Birko.Time** — `DateTimeOffset` covers most needs unless business calendar/working hours required
- **Birko.MessageQueue.Kafka/RabbitMQ** — MQTT + InMemory covers IoT workloads, only needed at higher scale

**Symbio-specific features (not in Birko scope):**
- Module discovery/registration (IModule, ModuleRegistrar, dependency graph)
- Unified real-time notifier (SSE + WebSocket combined, tenant-aware)
- Time-series store abstraction (generic over TimescaleDB)
- Module-aware migration runner (topological dependency sorting)
- SQL dialect abstraction (PostgreSqlDialect, MsSqlDialect)

---

## Reference

For implementation details, refer to:
- [CLAUDE.md](./CLAUDE.md) — Framework overview
- Individual project CLAUDE.md files
- [docs/](docs/) folder for detailed documentation

---

**Last Updated:** 2026-03-19
