# Birko Framework

A modular .NET framework providing data access, communication, and model infrastructure for enterprise applications. Built on .NET 10.0 with shared projects via .projitems.

## Features

- Multi-database support (SQL Server, PostgreSQL, MySQL, SQLite, MongoDB, RavenDB, Elasticsearch, InfluxDB, TimescaleDB, JSON, Azure Cosmos DB)
- Sync and async store/repository abstractions with bulk operation support
- ViewModel layer with model-to-viewmodel mapping
- Database migrations framework
- Data synchronization across stores
- Multi-tenancy support
- Event sourcing pattern
- Communication layer (REST, SOAP, WebSocket, SSE, Bluetooth, Hardware, Network, Modbus, OAuth, Camera, IR, NFC)
- Domain model libraries (Product, Category, SEO, Customers, Users, Inventory, Pricing) with domain contracts
- Fluent validation framework
- Caching with in-memory, Redis, and hybrid (L1+L2) backends
- Security (password hashing with PBKDF2 and BCrypt, AES encryption, JWT tokens, RBAC, ASP.NET Core integration, HashiCorp Vault, Azure Key Vault, NFC authentication)
- Message queue abstractions (pub/sub, point-to-point, serialization, retry, dead letter)
- Event bus (in-process, distributed via MessageQueue, transactional outbox, event sourcing integration)
- Messaging (email via SMTP, SMS and push notification interfaces, string and Razor template engines)
- File/blob storage abstraction (local filesystem, Azure Blob Storage)
- Telemetry (store metrics via System.Diagnostics.Metrics, distributed tracing via ActivitySource, correlation ID middleware)
- OpenTelemetry integration (OTLP + Console exporters, auto-wires Birko meters/activity sources)
- CQRS (Command/Query, mediator, pipeline behaviors)
- Workflow engine (state machines, guards, actions, persistence backends, Mermaid/DOT export)
- Data-driven rules engine (composable rules, groups, contexts, SQL/Specification/Validation integration)
- Generic data processors (XML, CSV, HTTP, ZIP with decorator composition)
- Background job processing with pluggable persistent queues
- Entity tagging system (tenant-scoped tags, polymorphic junction, tag service)
- Fluent view builder (cross-platform views, projections, aggregations)
- Store decorator composition (conditional runtime decorator chains)
- SQL query caching decorator
- Fluent SQL mapping framework (ModelMap, IModelMapping, ModelMapRegistry)
- Localization framework (CLDR pluralization, JSON/RESX/DB providers, entity-level localization)
- Time utilities (business calendar, holidays, working hours, time zones)
- Pluggable RNG (SystemRandom, CryptoRandom, XorShift, MersenneTwister, SplitMix), distributions, sequences, noise
- Serialization abstractions (System.Text.Json, Newtonsoft.Json, MessagePack, Protobuf)
- Data structures (trees, AVL, interval tree, graphs, heaps, tries, LRU cache, Bloom filter, ring buffer, disjoint set, skip list, deque)
- Web component framework (Shadow DOM, reactive state, HTTP/SSE clients, hash router, 31 components, app shell)
- Health checks (disk, memory, SQL, NoSQL, Redis, Azure, MQTT, SMTP, WebSocket, TCP, SSE)
- Helper utilities and extensions (including RFC 4180 CSV parser, PathHelper)
- AI/LLM agent framework (multi-provider, coding/media/task agents, orchestration, resilience)

## Project Structure

### Core

| Project | Description |
|---------|-------------|
| Birko.Framework | Main framework application |
| Birko.Data.Core | Models, ViewModels, Filters, Exceptions (foundation layer) |
| Birko.Data.Stores | Store interfaces/abstractions, Settings, OrderBy, StoreLocator |
| Birko.Data.Repositories | Repository interfaces/abstractions, RepositoryLocator, DI extensions |
| Birko.Models | Base entity and ViewModel classes |

### Data Layer

| Project | Description |
|---------|-------------|
| Birko.Data.SQL | SQL base classes (DataBaseStore, AsyncDataBaseStore, bulk variants) |
| Birko.Data.SQL.MSSql | SQL Server implementation |
| Birko.Data.SQL.PostgreSQL | PostgreSQL implementation |
| Birko.Data.SQL.MySQL | MySQL implementation |
| Birko.Data.SQL.SqLite | SQLite implementation |
| Birko.Data.JSON | JSON file-based storage |
| Birko.Data.ElasticSearch | Elasticsearch repository/store |
| Birko.Data.MongoDB | MongoDB repository/store |
| Birko.Data.RavenDB | RavenDB repository/store |
| Birko.Data.InfluxDB | InfluxDB time-series database |
| Birko.Data.TimescaleDB | TimescaleDB implementation |
| Birko.Data.CosmosDB | Azure Cosmos DB (NoSQL API) repository/store |
| Birko.Data.MongoDB.Views | MongoDB platform for fluent views (aggregation pipelines) |
| Birko.Data.ElasticSearch.Views | ElasticSearch platform for fluent views (NEST aggregations) |
| Birko.Data.RavenDB.Views | RavenDB platform for fluent views (Map/Reduce static indexes) |
| Birko.Data.CosmosDB.Views | Cosmos DB platform for fluent views (LINQ + Cosmos SQL) |

### Data Features

| Project | Description |
|---------|-------------|
| Birko.Data.Patterns | Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Sluggable, Paging) |
| Birko.Data.Migrations | Database migration framework (SQL, ES, MongoDB, RavenDB, InfluxDB, TimescaleDB, CosmosDB) |
| Birko.Data.Sync | Data synchronization (SQL, ES, MongoDB, RavenDB, JSON, CosmosDB, Tenant) |
| Birko.Data.Aggregates | SQL-NoSQL aggregate mapper (flatten/expand for sync) |
| Birko.Data.Tenant | Multi-tenancy support |
| Birko.Data.Composition | Runtime store decorator composition (conditional decorator chains) |
| Birko.Data.Tagging | Entity tagging system (tenant-scoped tags, polymorphic junction) |
| Birko.Data.Views | Unified fluent view builder (cross-platform views, projections, aggregations) |
| Birko.Data.EventSourcing | Event sourcing pattern |
| Birko.Data.SQL.View | SQL view generation (attribute-based) |
| Birko.Data.SQL.View.Migrations | Integration between SQL View definitions and the Migration framework |
| Birko.Data.SQL.Caching | Query caching decorator for SQL stores |
| Birko.Data.SQL.Views | SQL platform for fluent views (translates ViewDefinition to SQL) |

### ViewModel Layer

| Project | Description |
|---------|-------------|
| Birko.Data.ViewModel | Base ViewModel repository abstractions |
| Birko.Data.SQL.ViewModel | SQL ViewModel repositories |
| Platform-specific ViewModel projects | ES, InfluxDB, JSON, MongoDB, RavenDB, TimescaleDB, CosmosDB |

### Models

| Project | Description |
|---------|-------------|
| Birko.Models.Contracts | Domain interfaces (ICatalogItem, IPriceable, IHierarchical, etc.) |
| Birko.Models | Base models + Value Objects (Money, MoneyWithTax, Percentage, PostalAddress, Quantity) |
| Birko.Models.Product | Product, variants, images, pricing |
| Birko.Models.Category | Categories with hierarchical tree support |
| Birko.Models.SEO | SEO metadata, URL aliases, sitemaps |
| Birko.Models.Customers | Address, Customer, InvoiceAddress, ContactPerson |
| Birko.Models.Users | User, UserLogin, UserProfile, Role, Tenant, UserTenant |
| Birko.Models.Inventory | StockItem, StockItemVariant, StorageLocation, InventoryDocument |
| Birko.Models.Pricing | Currency, Tax, PriceGroup, PriceList, Discount |
| Birko.Models.SQL | Fluent SQL mapping framework (ModelMap, IModelMapping, ModelMapRegistry) |

### Communication

| Project | Description |
|---------|-------------|
| Birko.Communication | Base communication interfaces |
| Birko.Communication.REST | REST API client |
| Birko.Communication.REST.Server | REST API server (HttpListener, routing, middleware, authentication) |
| Birko.Communication.SOAP | SOAP client |
| Birko.Communication.WebSocket | WebSocket implementation |
| Birko.Communication.SSE | Server-Sent Events |
| Birko.Communication.Network | Network communication |
| Birko.Communication.Hardware | Hardware communication |
| Birko.Communication.Bluetooth | Bluetooth communication |
| Birko.Communication.Modbus | Modbus RTU/TCP communication (serial/network, function codes 01-06/15-16) |
| Birko.Communication.OAuth | OAuth2 client (Client Credentials, Auth Code, PKCE, Device Code) |
| Birko.Communication.Camera | Camera frame capture (FFmpeg-based JPEG snapshots) |
| Birko.Communication.IR | Consumer IR (NEC, Samsung, RC5 protocols, pluggable transports) |
| Birko.Communication.NFC | NFC/RFID tag reading (ISO 14443A, NDEF, Serial/HTTP/HID transports) |

### AI / LLM

| Project | Description |
|---------|-------------|
| Birko.AI.Contracts | ILlmProvider, Message, ContentBlock, TokenUsage, LlmResponse, LlmStreamingResponse, Tool base, AgentOptions, LlmProviderFactory |
| Birko.AI | LlmProviderBase (retry, SSE, OpenAI helpers), Agent base class (run loop, streaming, tools), AgentFactory (registration-based), 9 default tools |
| Birko.AI.Providers | 11 providers: Claude, OpenAI, AzureOpenAI, Gemini, Ollama, LlamaCpp, Vllm, Sglang, GitHubCopilot, ZAi + ProviderRegistration |
| Birko.AI.Agents | CodingAgent, 10 language agents, 4 task agents, media agents, OrchestratorAgent + AgentRegistration |
| Birko.AI.Resilience | ProviderRateLimiter, ProviderCircuitBreaker, CostTrackingService, TrackedLlmProvider |
| Birko.AI.Orchestration | ITaskDispatcher, ImplementationPlan, StepDependencyAnalyzer, EscalationAlert |
| Birko.Communication.OAuth.Providers | GitHubOAuthProvider (pre-configured device flow) |

### Web

| Project | Description |
|---------|-------------|
| Birko.Web.Core | Minimal Web Component framework — Shadow DOM base class, reactive state (Signal/Store), fetch-based HTTP client, SSE client, and hash router. No dependencies. |
| Birko.Web.Components | Component library built on Birko.Web.Core — 38 Shadow DOM web components covering inputs, layout, data, feedback, and navigation. |
| Birko.Web.Shell | Application shell framework built on Birko.Web.Core — auth, modules, command palette, notifications, tenants. |

### Workflow

| Project | Description |
|---------|-------------|
| Birko.Workflow | State machine engine (WorkflowBuilder, WorkflowEngine, guards, actions, Mermaid/DOT) |
| Birko.Workflow.SQL | SQL persistence backend |
| Birko.Workflow.ElasticSearch | Elasticsearch persistence backend |
| Birko.Workflow.MongoDB | MongoDB persistence backend |
| Birko.Workflow.RavenDB | RavenDB persistence backend |
| Birko.Workflow.JSON | JSON file persistence backend |
| Birko.Workflow.CosmosDB | Cosmos DB persistence backend |

### Health

| Project | Description |
|---------|-------------|
| Birko.Health | IHealthCheck, HealthCheckRunner, DiskSpace/Memory checks |
| Birko.Health.Data | SQL, Elasticsearch, MongoDB, RavenDB, InfluxDB, TimescaleDB, CosmosDB, Vault, MQTT, SMTP, WebSocket, TCP, SSE checks |
| Birko.Health.Redis | Redis PING + latency |
| Birko.Health.Azure | Blob Storage, Key Vault checks |

### Cross-Cutting

| Project | Description |
|---------|-------------|
| Birko.Validation | Fluent validation framework |
| Birko.Caching | In-memory caching with ICache interface |
| Birko.Caching.Redis | Redis-backed cache |
| Birko.Caching.Hybrid | L1 memory + L2 distributed two-tier cache |
| Birko.Redis | Shared Redis infrastructure (RedisSettings, RedisConnectionManager) |
| Birko.Security | Password hashing, AES encryption, RBAC interfaces |
| Birko.Security.Jwt | JWT token provider |
| Birko.Security.AspNetCore | ASP.NET Core integration (JWT Bearer auth, ICurrentUser, tenant middleware, permission filters) |
| Birko.Security.BCrypt | BCrypt password hashing (pure C#, configurable work factor) |
| Birko.Security.Vault | HashiCorp Vault secret provider (KV v1/v2, HTTP API) |
| Birko.Security.AzureKeyVault | Azure Key Vault secret provider (OAuth2, REST API) |
| Birko.Security.NFC | NFC-based authentication (tag-to-user mapping, JWT integration) |
| Birko.BackgroundJobs | Background job processing framework |
| Birko.BackgroundJobs.SQL | SQL-based persistent job queue |
| Birko.BackgroundJobs.ElasticSearch | Elasticsearch-based persistent job queue |
| Birko.BackgroundJobs.MongoDB | MongoDB-based persistent job queue |
| Birko.BackgroundJobs.RavenDB | RavenDB-based persistent job queue |
| Birko.BackgroundJobs.JSON | JSON file-based job queue (dev/testing) |
| Birko.BackgroundJobs.Redis | Redis-based persistent job queue |
| Birko.BackgroundJobs.CosmosDB | Cosmos DB-based persistent job queue |
| Birko.MessageQueue | Core message queue interfaces (pub/sub, point-to-point) |
| Birko.MessageQueue.InMemory | In-memory channel-based queue (testing/development) |
| Birko.MessageQueue.MQTT | MQTT implementation via MQTTnet (IoT, sensors, telemetry) |
| Birko.MessageQueue.Redis | Redis-backed message queue |
| Birko.EventBus | Core event bus (in-process, pipelines, deduplication, DI) |
| Birko.EventBus.MessageQueue | Distributed event bus over MessageQueue providers |
| Birko.EventBus.Outbox | Transactional outbox pattern (at-least-once delivery) |
| Birko.EventBus.EventSourcing | EventStore-to-EventBus bridge and replay |
| Birko.Messaging | Email, SMS, push notification interfaces and SMTP sender |
| Birko.Messaging.Razor | Razor template engine (RazorLight-based, .cshtml templates) |
| Birko.Storage | File/blob storage abstraction (local filesystem) |
| Birko.Storage.AzureBlob | Azure Blob Storage (REST API, OAuth2, SAS) |
| Birko.Telemetry | Store instrumentation (metrics, tracing), correlation ID middleware |
| Birko.Telemetry.OpenTelemetry | OpenTelemetry SDK integration (OTLP, Console exporters) |
| Birko.CQRS | Command/Query (ICommand, IQuery, IRequestHandler, IPipelineBehavior, IMediator) |
| Birko.Rules | Data-driven rule engine (rules, groups, contexts, evaluator) |
| Birko.Data.Processors | Generic stream processors (XML, CSV, HTTP, ZIP, decorator composition) |
| Birko.Serialization | Serialization abstraction (ISerializer, SystemJsonSerializer, SystemXmlSerializer) |
| Birko.Serialization.Newtonsoft | Newtonsoft.Json serializer |
| Birko.Serialization.MessagePack | MessagePack serializer |
| Birko.Serialization.Protobuf | Protocol Buffers serializer |
| Birko.Time.Abstractions | IDateTimeProvider (zero deps) |
| Birko.Time | Time zones, business calendar, holidays, working hours |
| Birko.Localization | Translation framework, CLDR pluralization, JSON/RESX/InMemory providers |
| Birko.Localization.Data | Database-backed translations, namespace scoping, TTL cache |
| Birko.Data.Localization | Entity-level localization (ILocalizable, store decorator wrappers) |
| Birko.Random | Pluggable RNG, distributions, sequences (GuidV4/V7, NanoId, Snowflake), noise (Perlin, Simplex) |
| Birko.Structures | Data structures (trees, AVL, interval tree, graphs, heaps, tries, LRU cache, Bloom filter, ring buffer, deque) |
| Birko.Helpers | Utility and extension methods, CsvParser |

### Tests

| Project | Description |
|---------|-------------|
| Birko.Data.Tests | Core store/patterns tests (decorators, paging, specification, concurrency, sluggable, default) |
| Birko.Data.SQL.Tests | SQL connector, strategy, and expression tests |
| Birko.Data.ElasticSearch.Tests | Elasticsearch expression tests |
| Birko.Helpers.Tests | Helper utility tests |
| Birko.Structures.Tests | Tree data structure tests |
| Birko.BackgroundJobs.Tests | Background job processing tests |
| Birko.MessageQueue.Tests | Message queue tests (core, InMemory, MQTT) |
| Birko.EventBus.Tests | Event bus tests (core, distributed, outbox, event sourcing) |
| Birko.Security.AspNetCore.Tests | ASP.NET Core security integration tests (JWT, permissions, tenants) |
| Birko.Storage.Tests | File storage tests (core types, LocalFileStorage, extensions) |
| Birko.Messaging.Tests | Messaging tests (core types, email, templates) |
| Birko.Telemetry.Tests | Telemetry tests (conventions, store wrappers, metrics, middleware) |
| Birko.Security.BCrypt.Tests | BCrypt password hashing tests |
| Birko.Security.Vault.Tests | Vault secret provider tests |
| Birko.Security.AzureKeyVault.Tests | Azure Key Vault secret provider tests |
| Birko.Rules.Tests | Rule engine tests (core types, contexts, evaluator) |
| Birko.Data.Processors.Tests | Data processor tests (CSV parser, XML/CSV/ZIP processors, HTTP transport) |
| Birko.Telemetry.OpenTelemetry.Tests | OpenTelemetry integration tests (options, DI, providers) |
| Birko.Communication.Modbus.Tests | Modbus communication tests (RTU/TCP framing, CRC, error handling) |
| Birko.Communication.OAuth.Tests | OAuth2 client tests (settings, token, PKCE, flows) |
| Birko.Communication.IR.Tests | IR communication tests (NEC/Samsung/RC5 encode/decode, IrTiming) |
| Birko.Communication.NFC.Tests | NFC communication tests (tag data, NDEF parsing, ISO 14443A, HID transport) |
| Birko.Security.NFC.Tests | NFC authentication tests (enroll/authenticate/revoke, normalization, store) |
| Birko.Data.MongoDB.Tests | MongoDB store/repository tests |
| Birko.Data.RavenDB.Tests | RavenDB store/repository tests |
| Birko.Data.CosmosDB.Tests | Cosmos DB store/repository tests |
| Birko.Data.TimescaleDB.Tests | TimescaleDB store/repository tests |
| Birko.Data.InfluxDB.Tests | InfluxDB store/repository tests |
| Birko.Data.JSON.Tests | JSON file store tests |
| Birko.Data.Views.Tests | Fluent view builder tests |
| Birko.Random.Tests | RNG providers, distributions, sequences tests |
| Birko.Validation.Tests | Fluent validation rules, validator composition, store wrapper integration tests |
| Birko.Data.Sync.Tests | Data sync framework tests (SyncProvider, SyncQueue, models) |
| Birko.BackgroundJobs.SQL.Tests | SQL job queue model mapping tests (JobDescriptorModel) |
| Birko.Workflow.SQL.Tests | SQL workflow instance model mapping tests (WorkflowInstanceModel) |
| Birko.Communication.Camera.Tests | Camera communication tests (CapturedFrame, settings, source state) |
| Birko.Data.Migrations.SQL.Tests | SQL migration tests (MigrationResult, SqlMigration, settings) |
| Birko.Caching.Tests | Core caching tests (CacheResult, CacheEntryOptions, MemoryCache) |
| Birko.Communication.REST.Tests | REST client tests (BuildUri, HttpMethod, event args, defaults) |
| Birko.Communication.WebSocket.Tests | WebSocket settings and configuration tests |

## Architecture

### Store Hierarchy

```
AbstractStore -> AbstractBulkStore (sync)
AbstractAsyncStore -> AbstractAsyncBulkStore (async)
```

### SQL Store Hierarchy

```
DataBaseStore<DB,T> -> DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy

```
AbstractRepository -> AbstractBulkRepository (sync)
AbstractAsyncRepository -> AbstractAsyncBulkRepository (async)
```

## Usage in Consumer Solutions

When using Birko.Framework projects in your own solution, it is recommended to create a single aggregator library project named `{YourSolution}.Birko` (e.g. `FisData.Birko`) and include all needed `Birko.*` shared project references in that one project. Your other projects then reference only `{YourSolution}.Birko` instead of individual Birko shared projects.

This pattern helps avoid compilation and transitive reference issues that arise when multiple projects import overlapping sets of shared projects independently.

```
YourSolution/
  YourSolution.Birko/          # Single .csproj importing all Birko.* .projitems
    YourSolution.Birko.csproj
  YourSolution.Core/            # References YourSolution.Birko
  YourSolution.Web/             # References YourSolution.Birko
```

## Getting Started

```bash
# Clone and build
dotnet build Birko.Framework.slnx
```

## Running Tests

```bash
dotnet test
```

## Documentation

- [Architecture](docs/architecture.md)
- [Store Implementation Guide](docs/store-implementation.md)
- [Repository Implementation Guide](docs/repository-implementation.md)
- [Migration Guide](docs/migrations.md)
- [Data Patterns Guide](docs/patterns.md) (Unit of Work, Soft Delete, Audit, Paging, Specifications, Concurrency)
- [Validation Guide](docs/validation.md)
- [Caching Guide](docs/caching.md)
- [Security Guide](docs/security.md) (Password hashing, AES encryption, JWT, ASP.NET Core integration)
- [Background Jobs Guide](docs/background-jobs.md)
- [Message Queue Guide](docs/message-queue.md)
- [Event Bus Guide](docs/event-bus.md) (In-process, Distributed, Outbox, Event Sourcing integration)
- [Event Sourcing Guide](docs/event-sourcing.md)
- [Storage Guide](docs/storage.md) (Local filesystem, cloud providers)
- [Messaging Guide](docs/messaging.md) (Email, SMS, Push, Templates)
- [Communication Guide](docs/communication.md) (REST, SOAP, WebSocket, SSE, Modbus, OAuth, IR, NFC)
- [Data Synchronization Guide](docs/sync.md)
- [Multi-Tenancy Guide](docs/tenant.md)
- [Telemetry Guide](docs/telemetry.md) (Store metrics, distributed tracing, correlation ID)
- [CQRS Guide](docs/cqrs.md) (Command/Query, Mediator, Pipeline behaviors)
- [Workflow Guide](docs/workflow.md) (State machines, guards, actions, persistence)
- [Rules Engine Guide](docs/rules.md) (Data-driven rules, groups, contexts, SQL/Spec/Validation integration)
- [Data Processors Guide](docs/processors.md) (XML, CSV, HTTP, ZIP, decorator composition)
- [Tagging Guide](docs/tagging.md) (Entity tagging, polymorphic junction, tenant-scoped tags)
- [Views Guide](docs/views.md) (Fluent view builder, cross-platform projections, aggregations)
- [Serialization Guide](docs/serialization.md) (System.Text.Json, Newtonsoft, MessagePack, Protobuf)
- [Localization Guide](docs/localization.md) (Translations, CLDR pluralization, entity-level)
- [Time Guide](docs/time.md) (Business calendar, holidays, working hours, time zones)
- [Random Guide](docs/random.md) (RNG providers, distributions, sequences, noise)
- [Health Guide](docs/health.md) (Health checks, runners, platform probes)
- [Dependencies Guide](docs/dependencies.md)
- [Consumers Guide](docs/consumers.md)
- [TODO / Roadmap](TODO.md)

## License

Part of the Birko Framework.
