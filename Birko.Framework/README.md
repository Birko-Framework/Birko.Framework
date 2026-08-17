# Birko Framework

A modular .NET framework providing data access, communication, AI, and model infrastructure. General-purpose — applicable to enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, real-time, and AI-driven applications. Built on .NET 10.0 with shared projects via .projitems.

## Features

- Multi-database support (SQL Server, PostgreSQL, MySQL, SQLite, MongoDB, RavenDB, Elasticsearch, InfluxDB, TimescaleDB, JSON, XML, Azure Cosmos DB)
- Typed provider settings hierarchy — `MSSqlSettings`, `MySqlSettings`, `PostgreSqlSettings`, `SqLiteSettings`, `TimescaleDBSettings`, CosmosDB/RavenDB/MongoDB/Redis Settings — each layer adds only what it needs (`Settings → PasswordSettings → RemoteSettings → SqlSettings → MSSqlSettings`, etc.). See [docs/configuration.md](docs/configuration.md).
- Sync and async store/repository abstractions with bulk operation support and lazy-init (auto-creates tables/indexes on first use)
- ViewModel layer with model-to-viewmodel mapping
- Database migrations framework (platform-agnostic — write once, run against any provider)
- Data synchronization across stores
- Multi-tenancy support (tenant-scoped stores/repositories, Permissive/Strict isolation modes, and a secure-by-default guard rejecting an `X-Tenant-Id` header that disagrees with the JWT tenant claim)
- Event sourcing pattern
- Communication layer (REST, SOAP, WebSocket, SSE, Bluetooth, Hardware, Network, Modbus, OAuth, **GraphQL** queries/mutations/subscriptions, **gRPC** client/server, Camera, IR, NFC)
- Domain model libraries (Product, Category, SEO, Customers, Users, Inventory, Pricing) with domain contracts
- Fluent validation framework
- Caching with in-memory, Redis, and hybrid (L1+L2) backends
- Security (password hashing with PBKDF2 and BCrypt, AES encryption, JWT tokens, RBAC, ASP.NET Core integration incl. tenant header/claim guard, HashiCorp Vault, Azure Key Vault, NFC authentication)
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
- Serialization abstractions (System.Text.Json, Newtonsoft.Json, MessagePack, Protobuf, YAML)
- Data structures (trees, AVL, interval tree, graphs, heaps, tries, LRU cache, Bloom filter, ring buffer, disjoint set, skip list, deque)
- Web component framework (Shadow DOM, reactive state, HTTP/SSE clients, hash router, unified i18n singleton, 55 components across inputs/layout/data/feedback/nav/command palette, three-level app shell hierarchy `BCoreAppShell → BSidebarAppShell → BAppShell`)
- Desktop / XAML UI framework (Birko.Xaml — Avalonia-first, single-source design tokens shared byte-for-byte with the web CSS, 4 runtime-swappable themes, ~20 restyled controls + Tier-2 composites, Avalonia-free MVVM core, sidebar + ribbon app shell)
- Health checks (disk, memory, SQL, NoSQL, Redis, Azure, MQTT, SMTP, WebSocket, TCP, SSE)
- Helper utilities and extensions (including RFC 4180 CSV parser, PathHelper)
- AI/LLM agent framework (multi-provider, coding/media/task agents, orchestration, resilience)

## Project Structure

### Core

| Project | Description |
|---------|-------------|
| Birko.Framework | Main framework application |
| Birko.Contracts | Pure interfaces (ILoadable, ICopyable, IDefault, ITimestamped) with zero dependencies |
| Birko.Configuration | Settings hierarchy (Settings, PasswordSettings, RemoteSettings) |
| Birko.Data.Core | Models, ViewModels, Filters, Exceptions (foundation layer) |
| Birko.Data.Stores | Store interfaces/abstractions, Settings, OrderBy, StoreLocator, Aggregation (AggregateQuery, AggregateResult, IAggregatableStore) |
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
| Birko.Data.XML | XML file-based storage |
| Birko.Data.InMemory | In-memory (ConcurrentDictionary) store — testing / prototyping, no persistence |
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
| Birko.Data.Patterns | Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Sluggable, Paging) + Schema abstractions (FieldType, FieldDescriptor, ISchemaBuilder) |
| Birko.Data.Aggregates | SQL-NoSQL aggregate mapper (flatten/expand for sync) |
| Birko.Data.Tenant | Multi-tenancy support |
| Birko.Data.Composition | Runtime store decorator composition (conditional decorator chains) |
| Birko.Data.Tagging | Entity tagging system (tenant-scoped tags, polymorphic junction) |
| Birko.Data.Views | Unified fluent view builder (cross-platform views, projections, aggregations) |
| Birko.Data.EventSourcing | Event sourcing pattern |
| Birko.Data.SQL.Caching | Query caching decorator for SQL stores |

### Migrations

| Project | Description |
|---------|-------------|
| Birko.Data.Migrations | Core migration framework (IMigration, IMigrationContext, AbstractMigration, AbstractMigrationRunner) — platform-agnostic |
| Birko.Data.Migrations.SQL | SQL migration backend (SqlMigrationContext, SqlSchemaBuilder, AbstractConnector-based) |
| Birko.Data.Migrations.ElasticSearch | Elasticsearch index/mapping migrations (ElasticSearchMigrationContext) |
| Birko.Data.Migrations.MongoDB | MongoDB collection/index migrations (MongoMigrationContext) |
| Birko.Data.Migrations.RavenDB | RavenDB collection/index migrations (RavenDBMigrationContext) |
| Birko.Data.Migrations.InfluxDB | InfluxDB bucket/retention migrations (InfluxMigrationContext) |
| Birko.Data.Migrations.TimescaleDB | TimescaleDB hypertable/compression migrations (extends SQL context) |
| Birko.Data.Migrations.CosmosDB | Cosmos DB container/indexing-policy migrations (CosmosDBMigrationContext) |

### Data Sync

| Project | Description |
|---------|-------------|
| Birko.Data.Sync | Core sync framework (SyncProvider, SyncQueue, upload/download) |
| Birko.Data.Sync.Sql | SQL sync backend |
| Birko.Data.Sync.ElasticSearch | Elasticsearch sync backend |
| Birko.Data.Sync.MongoDb | MongoDB sync backend |
| Birko.Data.Sync.RavenDB | RavenDB sync backend |
| Birko.Data.Sync.CosmosDB | Cosmos DB sync backend |
| Birko.Data.Sync.Json | JSON file sync backend |
| Birko.Data.Sync.Xml | XML file sync backend |
| Birko.Data.Sync.Tenant | Tenant-scoped sync decorator |

### SQL View (DDL)

| Project | Description |
|---------|-------------|
| Birko.Data.SQL.View | SQL view generation base (attribute-based view definitions) |
| Birko.Data.SQL.MSSql.View | SQL Server view DDL dialect |
| Birko.Data.SQL.PostgreSQL.View | PostgreSQL view DDL dialect |
| Birko.Data.SQL.MySQL.View | MySQL view DDL dialect |
| Birko.Data.SQL.SqLite.View | SQLite view DDL dialect |
| Birko.Data.SQL.View.Migrations | Integration between SQL View definitions and the Migration framework |
| Birko.Data.SQL.Views | SQL platform for fluent views (translates ViewDefinition to SQL) |

### ViewModel Layer

| Project | Description |
|---------|-------------|
| Birko.Data.ViewModel | Base ViewModel repository abstractions (`AbstractViewModelRepository`, `AbstractAsyncViewModelRepository`, `MapToModel`) |
| Birko.Data.SQL.ViewModel | SQL ViewModel repositories |
| Birko.Data.ElasticSearch.ViewModel | Elasticsearch ViewModel repositories |
| Birko.Data.MongoDB.ViewModel | MongoDB ViewModel repositories |
| Birko.Data.RavenDB.ViewModel | RavenDB ViewModel repositories |
| Birko.Data.CosmosDB.ViewModel | Cosmos DB ViewModel repositories |
| Birko.Data.InfluxDB.ViewModel | InfluxDB ViewModel repositories |
| Birko.Data.TimescaleDB.ViewModel | TimescaleDB ViewModel repositories |
| Birko.Data.JSON.ViewModel | JSON file ViewModel repositories |
| Birko.Data.XML.ViewModel | XML file ViewModel repositories |

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
| Birko.Models.SQL | Fluent SQL mapping framework (ModelMap, FieldBuilder, IModelMapping, ModelMapRegistry — uses FieldDescriptor from Data.Patterns). Framework only — canonical mappings live in the `.SQL` siblings below. |
| Birko.Models.Users.SQL | Canonical IModelMapping<T> for User, UserLogin, UserProfile, UserRole, UserTenant, Role, RolePermission, Tenant |
| Birko.Models.Customers.SQL | Canonical IModelMapping<T> for Address, InvoiceAddress, ContactPerson, Customer |
| Birko.Models.Inventory.SQL | Canonical IModelMapping<T> for StockItem, StorageLocation, InventoryDocumentLine |
| Birko.Models.Pricing.SQL | Canonical IModelMapping<T> for Currency, Tax, PriceGroup |
| Birko.Models.Product.SQL | Canonical IModelMapping<T> for MeasureUnit, UnitConversion, ProductPartnerCode |

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
| Birko.Communication.OAuth.Providers | Pre-configured OAuth providers (e.g. GitHubOAuthProvider for Device Code flow) |
| Birko.Communication.GraphQL | GraphQL client (queries, mutations, subscriptions over HttpClient + ClientWebSocket, zero external deps) |
| Birko.Communication.gRPC | gRPC client (channel pool, typed client factory, auth interceptor, settings over Grpc.Net.Client) |
| Birko.Communication.gRPC.Server | gRPC server (AddBirkoGrpc DI wiring, server auth interceptor over Grpc.AspNetCore) |
| Birko.Communication.AspNetCore | ASP.NET Core minimal-API helpers — owner-scoped CRUD skeleton (MapOwnedCrud + ownership guards) |

### AI / LLM

| Project | Description |
|---------|-------------|
| Birko.AI.Contracts | ILlmProvider, Message, ContentBlock, TokenUsage, LlmResponse, LlmStreamingResponse, Tool base, AgentOptions, LlmProviderFactory |
| Birko.AI | LlmProviderBase (retry, SSE, OpenAI helpers), Agent base class (run loop, streaming, tools), AgentFactory (registration-based), 9 default tools |
| Birko.AI.Providers | 11 providers: Claude, OpenAI, AzureOpenAI, Gemini, Ollama, LlamaCpp, Vllm, Sglang, GitHubCopilot, ZAi + ProviderRegistration |
| Birko.AI.Agents | CodingAgent, 10 language agents, 4 task agents, media agents, OrchestratorAgent + AgentRegistration |
| Birko.AI.Resilience | ProviderRateLimiter, ProviderCircuitBreaker, CostTrackingService, TrackedLlmProvider |
| Birko.AI.Orchestration | ITaskDispatcher, ImplementationPlan, StepDependencyAnalyzer, EscalationAlert |

### Web

| Project | Description |
|---------|-------------|
| Birko.Web.Core | Minimal Web Component framework — Shadow DOM base class, reactive state (Signal/Store), fetch-based HTTP client, SSE client, hash router, **unified i18n singleton** (`i18n`, `t()`, `useI18n()`, `onI18nChange()`, `BaseComponent.label()`). No dependencies. |
| Birko.Web.Components | Component library built on Birko.Web.Core — 55 Shadow DOM web components: 21 inputs (`b-tag-input`, `b-segmented`, `b-markdown-editor` with H1–H6/table/task-list/highlight/sup/sub, `b-datetime-picker`, `b-time`, `b-date-range-picker` with 2-month layout + hover-preview + opt-in presets + `confirm` mode, ...), 9 layout (`b-chat`, `b-split-panel`, ...), 14 data (`b-kanban` with recursive nesting + 3-zone DnD, `b-editable-table`, `b-code-block`, `b-json-viewer`, `b-xml-viewer`, `b-object-tree`, `b-definition-list`, `b-pre`, ...), 6 feedback (`b-progress`, `b-stale-banner`, ...), 4 navigation, 1 command palette. Canonical `bwc.*` i18n keys. |
| Birko.Web.Shell | Application shell framework built on Birko.Web.Core — three-level hierarchy (`BCoreAppShell → BSidebarAppShell → BAppShell`), auth, modules, command palette, notifications, tenants, page bases (`BaseListPage`/`BaseSplitPage`/`BaseDetailPage`/`BaseFormModal`/`BaseDashboardWidget`). Canonical `bws.*` i18n keys with automatic `{entity}` interpolation. |

### Desktop / XAML UI

| Project | Description |
|---------|-------------|
| Birko.DesignTokens | Build-time token generator (`generate`/`verify`/`extract`) — `tokens.json` → **byte-identical** web CSS + Avalonia AXAML, so the web and desktop design systems can't drift |
| Birko.Xaml.Core | Avalonia-free platform core — theming abstractions, i18n, base MVVM ViewModels (CrudViewModelBase, ListPage/DetailPage/SplitPage/ShellViewModel), FormField/Navigation/Command/Kanban/Ribbon/Chart models, `ICrudDataSource<T>` port. Dep: CommunityToolkit.Mvvm |
| Birko.Xaml.Avalonia | Avalonia skin — theme system (4 runtime-swappable variants: light/dark/neon/finstat), ~20 restyled Tier-1 controls, building blocks (Form/Drawer/SplitPanel/Modal/FormModal), 7 Tier-2 composites (tree-menu, command-palette, object/JSON + XML viewers, kanban, markdown-editor, chart-on-LiveCharts2) |
| Birko.Xaml.Shell | Application shell — sidebar + ribbon chrome, ViewLocator, generic list/detail/split page views, command palette (Ctrl+K), user/tenant areas, cross-fade page transitions |

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
| Birko.Workflow.XML | XML file persistence backend |

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
| Birko.Security.Vault.Configuration | Microsoft.Extensions.Configuration integration for any ISecretProvider (Vault, Azure Key Vault, etc.) |
| Birko.Security.AzureKeyVault | Azure Key Vault secret provider (OAuth2, REST API) |
| Birko.Security.NFC | NFC-based authentication (tag-to-user mapping, JWT integration) |
| Birko.Security.OAuth.Server | OAuth2 authorization server (token/authorize/device/registration endpoints; client-credentials, authcode+PKCE, refresh, device-code; persists via Birko.Data.Stores) |
| Birko.BackgroundJobs | Background job processing framework |
| Birko.BackgroundJobs.SQL | SQL-based persistent job queue |
| Birko.BackgroundJobs.ElasticSearch | Elasticsearch-based persistent job queue |
| Birko.BackgroundJobs.MongoDB | MongoDB-based persistent job queue |
| Birko.BackgroundJobs.RavenDB | RavenDB-based persistent job queue |
| Birko.BackgroundJobs.JSON | JSON file-based job queue (dev/testing) |
| Birko.BackgroundJobs.XML | XML file-based job queue (dev/testing, human-readable audit) |
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
| Birko.EventBus.Tenant | Tenant-scope bridge to Birko.Data.Tenant (enricher + scope accessor for background dispatch) |
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
| Birko.Serialization.Yaml | YAML serializer (YamlDotNet) |
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
| Birko.Communication.GraphQL.Tests | GraphQL client tests (queries, mutations, subscriptions, request builder) |
| Birko.Communication.gRPC.Tests | gRPC client tests (settings, channel pool, auth interceptor, client factory, exception) |
| Birko.Communication.gRPC.Server.Tests | gRPC server tests (settings, AddBirkoGrpc DI, server auth interceptor) |
| Birko.MessageQueue.Tests | Message queue tests (core, InMemory, MQTT) |
| Birko.EventBus.Tests | Event bus tests (core, distributed, outbox, event sourcing) |
| Birko.Security.AspNetCore.Tests | ASP.NET Core security integration tests (JWT, permissions, tenants) |
| Birko.Storage.Tests | File storage tests (core types, LocalFileStorage, extensions) |
| Birko.Messaging.Tests | Messaging tests (core types, email, templates) |
| Birko.Telemetry.Tests | Telemetry tests (conventions, store wrappers, metrics, middleware) |
| Birko.Security.BCrypt.Tests | BCrypt password hashing tests |
| Birko.Security.Vault.Tests | Vault secret provider tests |
| Birko.Security.AzureKeyVault.Tests | Azure Key Vault secret provider tests |
| Birko.Security.OAuth.Server.Tests | OAuth2 authorization server tests (43 tests; all four grant types + PKCE/consent/registration paths) |
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
| Birko.Caching.Hybrid.Tests | Hybrid two-tier cache tests (L1/L2 sync, eviction, TTL) |
| Birko.CQRS.Tests | CQRS tests (commands, queries, pipeline behaviors, mediator) |
| Birko.Data.Aggregates.Tests | Aggregate mapper tests (flatten/expand, sync integration) |
| Birko.Data.Localization.Tests | Entity-level localization tests (ILocalizable, store decorators) |
| Birko.Data.XML.Tests | XML file store tests |
| Birko.Health.Tests | Health check tests (runner, disk space, memory) |
| Birko.Health.Azure.Tests | Azure health check tests (Blob Storage, Key Vault) |
| Birko.Localization.Tests | Localization tests (CLDR pluralization, JSON/RESX providers) |
| Birko.Localization.Data.Tests | Database-backed localization tests (namespace scoping, TTL cache) |
| Birko.MessageQueue.Redis.Tests | Redis message queue tests |
| Birko.Messaging.Razor.Tests | Razor template engine tests (.cshtml rendering) |
| Birko.Serialization.Tests | Serialization tests (System.Text.Json, System.Xml) |
| Birko.Storage.AzureBlob.Tests | Azure Blob Storage tests (REST API, SAS, presigned URLs) |
| Birko.Time.Tests | Time utility tests (calendar, holidays, working hours, zones) |
| Birko.Workflow.Tests | Workflow engine tests (state machine, guards, actions, Mermaid/DOT) |
| Birko.DesignTokens.Tests | Token generator tests (CSS byte-parity round-trip, extractor, AXAML per-variant resolution, cross-theme key parity) |
| Birko.Xaml.Core.Tests | XAML core tests (i18n, CRUD ViewModels over a fake port, permission gating, Avalonia-free enforcement) |
| Birko.Xaml.Avalonia.Tests | Avalonia skin tests (headless + Skia — theme system, all controls, per-theme parity screenshots) |

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

### Aggregator project pattern (recommended)

When using Birko.Framework projects in your own solution, create **one or more aggregator library projects** that bundle the `Birko.*` shared projects you need — and reference those aggregators from the rest of your code instead of importing `.projitems` directly into every consumer csproj. Each `.projitems` is then compiled exactly once into a single assembly, eliminating `CS0433`/`CS0436` type-clash errors that arise when multiple projects import overlapping sets of shared projects independently.

**How many aggregators?** Pick what fits your shape:

- **Single aggregator** — `{YourSolution}.Birko` containing everything. Simplest, smallest project count. Works well when most of your code needs most of Birko, or when you don't care about pulling in unused dependencies. Symbio uses this shape (`Symbio.Birko` with ~90 imports).
- **Multiple aggregators by layer / purpose** — split when different parts of your solution need disjoint Birko subsets, especially when a subset pulls in heavy dependencies (e.g. ML / camera / hardware libs) that you don't want leaking into every project. Examples:
  - `{YourSolution}.Birko.Core` — Data, Models, Helpers, Security, Time
  - `{YourSolution}.Birko.Edge` — Communication.Hardware, Communication.Bluetooth, Communication.Modbus, Communication.Camera
  - `{YourSolution}.Birko.Ai` — AI.Contracts, AI, AI.Providers, AI.Agents
  - `{YourSolution}.Birko.Web` — Web.Core, Web.Components, Web.Shell (for solutions that ship a UI alongside non-UI services)

Splitting keeps each downstream project's binary footprint tight: an Edge collector doesn't need to pull in AI providers; a backend API doesn't need camera frame-capture libs.

```
# Single-aggregator shape
YourSolution/
  YourSolution.Birko/          # One .csproj importing all needed Birko.* .projitems
  YourSolution.Core/           # References YourSolution.Birko
  YourSolution.Web/            # References YourSolution.Birko

# Multi-aggregator shape
YourSolution/
  YourSolution.Birko.Core/     # Imports Birko.Data.*, Birko.Models.*, Birko.Helpers, Birko.Security
  YourSolution.Birko.Edge/     # Imports Birko.Communication.*
  YourSolution.Birko.Ai/       # Imports Birko.AI.*
  YourSolution.Api/            # References YourSolution.Birko.Core + .Ai
  YourSolution.Edge.Service/   # References YourSolution.Birko.Core + .Edge
```

A minimal aggregator `csproj` looks like:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Birko</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <!-- NuGet packages required by the Birko.* projitems imported below -->
    <PackageReference Include="Npgsql"        Version="9.*" />
    <PackageReference Include="MongoDB.Driver" Version="3.*" />
    <!-- … -->
  </ItemGroup>

  <!-- Birko.* shared projects — pick what this aggregator needs -->
  <Import Project="$(BirkoSrc)\Birko.Helpers\Birko.Helpers.projitems"         Label="Shared" />
  <Import Project="$(BirkoSrc)\Birko.Data.Core\Birko.Data.Core.projitems"     Label="Shared" />
  <Import Project="$(BirkoSrc)\Birko.Data.Stores\Birko.Data.Stores.projitems" Label="Shared" />
  <!-- … one Import per Birko.* projitems you need -->
</Project>
```

> **Rule of thumb for choosing aggregator boundaries:** when in doubt, start with **one** aggregator. Split it only when you hit a concrete pain point — bloated binaries, leaky transitive references, or projects that build slowly because they pull in `.projitems` they don't use. Splitting too early is overhead with no payoff.

> **`Birko.Models.*.SQL` are opt-in per persisted domain.** Import `Birko.Models.SQL` (the fluent mapping framework) once, then add a domain sibling — `Birko.Models.Users.SQL`, `.Customers.SQL`, `.Inventory.SQL`, `.Pricing.SQL`, `.Product.SQL` — only if you actually persist that domain's models via SQL. The repo's own `Birko.Framework.csproj` imports all five as a build-validation kitchen sink; **don't mirror that in your aggregator** — NoSQL-only consumers or apps that touch a subset of domains should skip the unused siblings to keep their footprint tight. See [docs/models.md — Layer 4](docs/models.md#layer-4-sql-mapping-birkomodelssql--domain-siblings) for the full table.

**Live examples** of these patterns:
- `Symbio.Birko.csproj` — single aggregator, ~90 Birko shared projects consolidated into one DLL (large enterprise platform)
- `WebFinstatApiTester.csproj` — no aggregator at all; the app `csproj` directly imports a lean subset (~10 projitems) because the project is small and overlapping-import risk is nil
- `Birko.Sandbox` (`Birko\Consumers\Birko.Sandbox`) — the runnable integration **smoke harness** and the "first test place" for framework changes: `dotnet run` does a tiny round-trip per layer and exits non-zero on failure. It uses a **single aggregator importing all ~165 `.projitems`** — not a lean slice — because bundling the whole framework into one assembly is the pattern this README recommends by default, and the harness is what proves it still works.
  > **It lives in `Consumers\`, not in `Framework.Tests\`, on purpose.** It is a test *surface* but not a test *project*, and the difference is the import mechanism: `Framework.Tests` projects import `.projitems` by hard relative path (`..\..\Framework\…`), whereas Sandbox resolves `$(BirkoSrc)` through its own `Directory.Build.props` — CLI parameter, then `BIRKO_SRC`, then a relative default. That is exactly what a real consumer does, so Sandbox is the only thing in the family that exercises the **consumption mechanism itself**. A test project cannot cover it, because a test project does not use it.

### Locating Birko.Framework sources — `$(BirkoSrc)` / `BIRKO_SRC`

The `Import Project` paths in your aggregator `csproj` need to resolve to wherever you have the `Birko.*` source folders checked out. **Don't hard-code** absolute paths like `C:\Source\Birko\Framework\Birko.Helpers\…` — instead use the `$(BirkoSrc)` MSBuild property, resolved from a `Directory.Build.props` at your repo root:

```xml
<!-- {YourSolution}/Directory.Build.props -->
<Project>
  <PropertyGroup>
    <!-- Resolution order:
           1. /p:BirkoSrc=...      MSBuild CLI parameter (highest priority)
           2. BIRKO_SRC env var    Shell environment
           3. Default              Path to the Birko\Framework checkout, relative
                                   to this Directory.Build.props
         The recommended layout nests the framework under a Birko\Framework bucket
         and consumers under a sibling Birko\Consumers bucket, e.g.
           C:\Source\Birko\Framework\Birko.Helpers
           C:\Source\Birko\Consumers\YourSolution
         so a consumer one bucket over reaches the framework via ..\..\Framework.
         (If you instead keep Birko.* as flat siblings of your repo, the default
         is just "..".) -->
    <BirkoSrc Condition="'$(BirkoSrc)' == '' and '$(BIRKO_SRC)' != ''">$(BIRKO_SRC)</BirkoSrc>
    <BirkoSrc Condition="'$(BirkoSrc)' == ''">$(MSBuildThisFileDirectory)..\..\Framework</BirkoSrc>
  </PropertyGroup>
</Project>
```

Then any consumer csproj imports become portable:

```xml
<Import Project="$(BirkoSrc)\Birko.Helpers\Birko.Helpers.projitems" Label="Shared" />
```

**Why both `/p:BirkoSrc` and `BIRKO_SRC`?**

| Channel | Use case |
|---|---|
| **Default** (relative to repo) | Local dev with the bucket layout — framework at `C:\Source\Birko\Framework\Birko.X`, your repo at `C:\Source\Birko\Consumers\YourSolution`. Zero configuration. |
| **`BIRKO_SRC` env var** | CI runners, Docker builds, custom workstation layouts (e.g. `D:\src` or `/home/foo/code`). Set once per shell session. |
| **`/p:BirkoSrc=…` CLI** | One-off override for a single build, e.g. comparing two checkouts side by side. |

#### Frontend (TypeScript) consumers

`Birko.Web.Core`, `Birko.Web.Components`, and `Birko.Web.Shell` ship as TypeScript sources, consumed by esbuild via an alias map. They live in their **own `Birko\Web` bucket**, separate from the .NET `Birko\Framework` bucket the MSBuild side resolves — so `BIRKO_SRC` (frontend) points at `Birko\Web`, while MSBuild's `$(BirkoSrc)` points at `Birko\Framework`. **Don't bake a machine-specific absolute path into the committed fallback** — prefer the env var, then walk up to find the `Birko\Web` checkout (depth-independent, safe to commit):

```js
// build.js — esbuild config
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const __dirname = dirname(fileURLToPath(import.meta.url));

function resolveBirkoSrc() {
  if (process.env.BIRKO_SRC) return process.env.BIRKO_SRC.replace(/[\\/]+$/, '').replaceAll('\\', '/');
  for (let d = __dirname; d !== dirname(d); d = dirname(d)) {
    const c = resolve(d, 'Birko/Web');
    if (existsSync(resolve(c, 'Birko.Web.Core'))) return c.replaceAll('\\', '/');
  }
  throw new Error('Set BIRKO_SRC to your Birko\\Web path.');
}
const BIRKO_SRC = resolveBirkoSrc();

const aliases = {
  'birko-web-core':       `${BIRKO_SRC}/Birko.Web.Core/src/index.ts`,
  'birko-web-components': `${BIRKO_SRC}/Birko.Web.Components/src/index.ts`,
  'birko-web-shell':      `${BIRKO_SRC}/Birko.Web.Shell/src/index.ts`,
  // …
};
```

Mirroring the MSBuild property gives a single override (`BIRKO_SRC`) that controls **both** the backend `dotnet build` and the frontend bundle build — important for Docker images where everything lives under `/src/`.

#### Docker example

```dockerfile
# Build context = parent of your repo (so Birko.* + YourSolution are siblings)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy Birko.* shared projects you need
COPY Birko.Helpers/    Birko.Helpers/
COPY Birko.Data.Core/  Birko.Data.Core/
# … etc.

# Copy your solution
COPY YourSolution/ YourSolution/

# Tell both MSBuild and esbuild where to look
ENV BIRKO_SRC=/src

WORKDIR /src/YourSolution
RUN dotnet publish src/Host/YourSolution.Api.csproj -c Release -o /app/publish
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
- [Configuration Guide](docs/configuration.md) (Settings hierarchy, provider-specific settings for SQL/Cosmos/Raven/SQLite/Timescale, `ILoadable<T>` layering)
- [Store Implementation Guide](docs/store-implementation.md)
- [Repository Implementation Guide](docs/repository-implementation.md)
- [Store Composition Guide](docs/composition.md) (Runtime decorator composition — `StoreWrapperBuilder`)
- [Migration Guide](docs/migrations.md)
- [Data Patterns Guide](docs/patterns.md) (Unit of Work, Soft Delete, Audit, Paging, Specifications, Concurrency)
- [Domain Models Guide](docs/models.md) (Contracts, Value Objects, Customers, Users, Inventory, Pricing, Product, Category, SEO, fluent SQL mapping)
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
- [Aggregates Guide](docs/aggregates.md) (SQL ↔ NoSQL flatten/expand for cross-store sync)
- [Multi-Tenancy Guide](docs/tenant.md)
- [Telemetry Guide](docs/telemetry.md) (Store metrics, distributed tracing, correlation ID)
- [CQRS Guide](docs/cqrs.md) (Command/Query, Mediator, Pipeline behaviors)
- [Workflow Guide](docs/workflow.md) (State machines, guards, actions, persistence)
- [Rules Engine Guide](docs/rules.md) (Data-driven rules, groups, contexts, SQL/Spec/Validation integration)
- [Data Processors Guide](docs/processors.md) (XML, CSV, HTTP, ZIP, decorator composition)
- [Tagging Guide](docs/tagging.md) (Entity tagging, polymorphic junction, tenant-scoped tags)
- [Views Guide](docs/views.md) (Fluent view builder, cross-platform projections, aggregations)
- [Serialization Guide](docs/serialization.md) (System.Text.Json, Newtonsoft, MessagePack, Protobuf, YAML)
- [Localization Guide](docs/localization.md) (Translations, CLDR pluralization, entity-level)
- [Time Guide](docs/time.md) (Business calendar, holidays, working hours, time zones)
- [Random Guide](docs/random.md) (RNG providers, distributions, sequences, noise)
- [Data Structures Guide](docs/structures.md) (Trees, graphs, heaps, tries, LRU, Bloom filter, skip list)
- [Health Guide](docs/health.md) (Health checks, runners, platform probes)
- [AI / LLM Guide](docs/ai.md) (Multi-provider LLM, agents, orchestration, resilience)
- [Web Components Guide](docs/web.md) (`Birko.Web.Core`/`.Components`/`.Shell` — Shadow DOM, Signal/Store, ribbon shell)
- [XAML / Desktop UI Guide](docs/xaml.md) (`Birko.DesignTokens` + `Birko.Xaml.Core`/`.Avalonia`/`.Shell` — Avalonia skin, single-source design tokens, theme system, MVVM shell)
- [Dependencies Guide](docs/dependencies.md)
- [Consumers Guide](docs/consumers.md)
- [Open backlog](tasks/README.md) — hierarchical task tracker (Epics → Stories → Tasks) managed by the `/tasks` Claude Code skill

## License

Part of the Birko Framework.
