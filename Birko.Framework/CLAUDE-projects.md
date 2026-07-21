# Birko Framework — Project Catalog

## Core Projects
- **Birko.Framework** - Main framework application (.NET 10.0, shared projects via .projitems)
- **Birko.Contracts** - Pure interfaces (ILoadable, ICopyable, IDefault, ITimestamped) with zero dependencies
- **Birko.Data.Core** - Models, ViewModels, Filters, Exceptions (foundation layer, imports Birko.Contracts)
- **Birko.Configuration** - Settings hierarchy (Settings, PasswordSettings, RemoteSettings) in namespace `Birko.Configuration`, imports Birko.Contracts
- **Birko.Data.Stores** - Store interfaces/abstractions, OrderBy, StoreLocator, Aggregation (AggregateFunction, AggregateQuery, AggregateResult, IAggregatableStore/IAsyncAggregatableStore, AggregateHelper, TimeIntervalParser, OrderByHelper) (imports Birko.Configuration transitively)
- **Birko.Data.Repositories** - Repository interfaces/abstractions, RepositoryLocator, DI extensions
- **Birko.Models** - Base models and extensions

## Data Layer
- **Birko.Data.SQL** - SQL base classes (DataBaseStore, DataBaseBulkStore, AsyncDataBaseStore, AsyncDataBaseBulkStore)
- **Birko.Data.SQL.MSSql** - Microsoft SQL Server implementation
- **Birko.Data.SQL.PostgreSQL** - PostgreSQL implementation
- **Birko.Data.SQL.MySQL** - MySQL implementation
- **Birko.Data.SQL.SqLite** - SQLite implementation
- **Birko.Data.JSON** - JSON file-based storage
- **Birko.Data.ElasticSearch** - Elasticsearch repository/store
- **Birko.Data.MongoDB** - MongoDB repository/store
- **Birko.Data.RavenDB** - RavenDB repository/store
- **Birko.Data.InfluxDB** - InfluxDB time-series database
- **Birko.Data.TimescaleDB** - TimescaleDB implementation
- **Birko.Data.CosmosDB** - Azure Cosmos DB (NoSQL API) repository/store
- **Birko.Data.MongoDB.Views** - MongoDB platform for fluent views (MongoViewTranslator, aggregation pipelines, db.createView; uses StoreAggregationHelper for group stages)
- **Birko.Data.ElasticSearch.Views** - ElasticSearch platform for fluent views (NEST aggregations, terms/composite + metric sub-aggs; uses StoreAggregationHelper for metric creation/extraction)
- **Birko.Data.RavenDB.Views** - RavenDB platform for fluent views (RavenViewTranslator, Map/Reduce static indexes)
- **Birko.Data.CosmosDB.Views** - Cosmos DB platform for fluent views (LINQ queries, Cosmos SQL GROUP BY; uses CosmosAggregationHelper and OrderByHelper)

## ViewModel Layer
- **Birko.Data.ViewModel** - Base ViewModel repository abstractions
- **Birko.Data.SQL.ViewModel** - SQL ViewModel repositories
- **Birko.Data.ElasticSearch.ViewModel** / **InfluxDB.ViewModel** / **JSON.ViewModel** / **XML.ViewModel** / **MongoDB.ViewModel** / **RavenDB.ViewModel** / **TimescaleDB.ViewModel** / **CosmosDB.ViewModel**

## Data Features
- **Birko.Data.Patterns** - Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Timestamp, Default Constraint, Sluggable, Paging)
- **Birko.Data.Migrations** + **.SQL** / **.ElasticSearch** / **.MongoDB** / **.RavenDB** / **.InfluxDB** / **.TimescaleDB** / **.CosmosDB**
- **Birko.Data.Sync** + **.Sql** / **.ElasticSearch** / **.MongoDb** / **.RavenDB** / **.Json** / **.Xml** / **.CosmosDB** / **.Tenant**
- **Birko.Data.Aggregates** - SQL-NoSQL aggregate mapper (flatten/expand for sync)
- **Birko.Data.Tenant** - Multi-tenancy support
- **Birko.Data.Composition** - Runtime store decorator composition (StoreWrapperBuilder — conditional Tenant/Default/SoftDelete/Audit/Timestamp chains)
- **Birko.Data.Tagging** - Entity tagging system (Tag, EntityTag, ITaggable, ITagService, TagServiceBase — tenant-scoped, polymorphic junction)
- **Birko.Data.Views** - Unified fluent view builder (ViewDefinitionBuilder, IViewMapping, ViewMapRegistry, IViewStore, IViewManager — cross-platform views/projections/aggregations; AggregateFunction imported from Birko.Data.Stores)
- **Birko.Data.EventSourcing** - Event sourcing pattern
- **Birko.Data.SQL.View** + **.MSSql.View** / **.PostgreSQL.View** / **.MySQL.View** / **.SqLite.View** - SQL view DDL
- **Birko.Data.SQL.View.Migrations** - Integration between SQL View definitions and the Migration framework (ViewSqlGenerator, ViewMigrationExtensions)
- **Birko.Data.SQL.Caching** - Query caching decorator for SQL stores (CachedAsyncDataBaseBulkStore, SqlCacheKeyBuilder, SqlCacheOptions)
- **Birko.Data.SQL.Views** - SQL platform implementation for fluent views (SqlViewTranslator, SqlViewStore, SqlViewManager — translates ViewDefinition to Tables.View; uses AbstractConnectorBase.GetSqlFunctionName and FunctionField.CreateFunctionField for aggregates)
- **Birko.Data.Processors** - Stream processors (XML, CSV, HTTP, ZIP) with decorator composition
- **Birko.Structures** - Data structures (trees, AVL, interval tree, graphs, heaps, tries, LRU cache, Bloom filter, ring buffer, disjoint set, skip list, deque)
- **Birko.Random** - Pluggable RNG (SystemRandom, CryptoRandom, XorShift, MersenneTwister, SplitMix, TestRandom), distributions, sequences (GuidV4/V7, NanoId, Snowflake, tokens), noise (Perlin, Simplex)
- **Birko.Helpers** - Data helper utilities (slug generation moved to Birko.Data.Patterns SlugGenerator)

## Communication
- **Birko.Communication** - Base interfaces
- **Birko.Communication.Network** / **.Hardware** / **.Bluetooth** / **.WebSocket** / **.REST** / **.REST.Server** / **.SOAP** / **.SSE**
- **Birko.Communication.Modbus** - Modbus RTU/TCP (serial/network, function codes 01-06/15-16)
- **Birko.Communication.OAuth** - OAuth2 client (Client Credentials, Auth Code, PKCE, Device Code, Refresh Token)
- **Birko.Communication.GraphQL** - GraphQL client (queries, mutations, subscriptions over HttpClient + ClientWebSocket, zero external deps — GraphQLClient, GraphQLSettings, GraphQLRequestBuilder, GraphQLSubscription, GraphQLException)
- **Birko.Communication.Camera** - Camera frame capture (FFmpeg-based JPEG snapshots)
- **Birko.Communication.IR** - Consumer IR (38 kHz, NEC/Samsung/RC5, pluggable transports)
- **Birko.Communication.NFC** - NFC/RFID tag reading (ISO 14443A, NDEF, pluggable transports)
- **Birko.Communication.AspNetCore** - ASP.NET Core minimal-API helpers (owner-scoped CRUD skeleton: MapOwnedCrud, OwnedCrudResults, OwnedCrudMapping)

## Messaging
- **Birko.Messaging** - Core interfaces (IMessageSender, IEmailSender, ISmsSender, IPushSender), SMTP, string templates
- **Birko.Messaging.Razor** - Razor template engine (RazorLight-based, .cshtml templates)

## Models
- **Birko.Models.Contracts** - Domain contract interfaces (ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument, IContactable, IAddressable)
- **Birko.Models** - Base models (AbstractPercentage, AbstractTree, ValueData, SourceValue) + Value Objects (Money, MoneyWithTax, Percentage, PostalAddress, Quantity)
- **Birko.Models.Product** (ISluggable from Name) / **.Category** (ISluggable from Title) / **.SEO**
- **Birko.Models.Customers** - Address, Customer, InvoiceAddress
- **Birko.Models.Users** - User, UserLogin, UserProfile, RBAC (Role, RolePermission, UserRole), Tenant, UserTenant
- **Birko.Models.Inventory** - StockItem, StockItemVariant, StorageLocation, StockMovement, InventoryDocument (clean, no SQL attrs)
- **Birko.Models.Pricing** - Currency, Tax, PriceGroup, PriceList, PriceListEntry, Discount (clean, no SQL attrs)
- **Birko.Models.SQL** - Fluent SQL mapping framework (ModelMap, IModelMapping, ModelMapRegistry, FieldBuilder) — framework only, no canonical mappings
- **Birko.Models.Users.SQL** - Canonical SQL mappings for `Birko.Models.Users`: UserMapping, UserLoginMapping, UserProfileMapping, UserRoleMapping, UserTenantMapping, RoleMapping, RolePermissionMapping, TenantMapping
- **Birko.Models.Customers.SQL** - Canonical SQL mappings for `Birko.Models.Customers`: AddressMapping (Address + InvoiceAddress + ContactPerson), CustomerMapping
- **Birko.Models.Inventory.SQL** - Canonical SQL mappings for `Birko.Models.Inventory`: StockItemMapping, StorageLocationMapping, InventoryDocumentLineMapping
- **Birko.Models.Pricing.SQL** - Canonical SQL mappings for `Birko.Models.Pricing`: CurrencyMapping (Currency + Tax + PriceGroup)
- **Birko.Models.Product.SQL** - Canonical SQL mappings for `Birko.Models.Product`: MeasureUnitMapping (MeasureUnit + UnitConversion), ProductPartnerCodeMapping

## Validation & Rules
- **Birko.Validation** - Fluent validation (IValidator<T>, AbstractValidator<T>, built-in rules, store wrappers)
- **Birko.Rules** - Data-driven rule engine (IRule, RuleGroup, RuleSet, RuleEvaluator)

## CQRS & Workflow
- **Birko.CQRS** - Command/Query (ICommand, IQuery, IRequestHandler, IPipelineBehavior, IMediator)
- **Birko.Workflow** - State machine engine (WorkflowBuilder, WorkflowEngine, guards, actions, Mermaid/DOT)
- **Birko.Workflow.SQL** / **.ElasticSearch** / **.MongoDB** / **.RavenDB** / **.JSON** / **.XML** / **.CosmosDB** - Persistence backends

## Serialization
- **Birko.Serialization** - Abstraction (ISerializer, SystemJsonSerializer, SystemXmlSerializer)
- **Birko.Serialization.Newtonsoft** / **.MessagePack** / **.Protobuf** / **.Yaml**

## Caching & Redis
- **Birko.Caching** - ICache, MemoryCache, CacheSerializer
- **Birko.Caching.Redis** - Redis backend
- **Birko.Caching.Hybrid** - L1 memory + L2 distributed two-tier cache
- **Birko.Redis** - Shared Redis infrastructure (RedisSettings, RedisConnectionManager)

## Security
- **Birko.Security** - PBKDF2 hashing, AES-256-GCM, token/secret provider interfaces, static token auth, RBAC interfaces
- **Birko.Security.BCrypt** - BCrypt hashing (pure C# Blowfish)
- **Birko.Security.Vault** - HashiCorp Vault (ISecretProvider, KV v1/v2)
- **Birko.Security.Vault.Configuration** - Provider-agnostic Microsoft.Extensions.Configuration integration for ISecretProvider (SecretConfigurationProvider, AddSecretConfiguration; includes Vault-specific LocalVault extensions)
- **Birko.Security.AzureKeyVault** - Azure Key Vault (ISecretProvider, OAuth2, REST API)
- **Birko.Security.Jwt** - JWT ITokenProvider
- **Birko.Security.AspNetCore** - ASP.NET Core integration (JWT Bearer, ICurrentUser, permissions, tenant middleware)
- **Birko.Security.NFC** - NFC-based authentication (tag-to-user mapping, enrollment, revocation)
- **Birko.Security.OAuth.Server** - OAuth2 authorization server (token/authorize/device_authorization/dynamic-client-registration endpoints, all four grant types, PKCE, refresh-token rotation, persistence via Birko.Data.Stores)

## Background Jobs & Message Queue
- **Birko.BackgroundJobs** - Job interfaces, in-memory queue, processor, dispatcher, scheduler
- **Birko.BackgroundJobs.SQL** / **.ElasticSearch** / **.MongoDB** / **.RavenDB** / **.JSON** / **.XML** / **.Redis** / **.CosmosDB**
- **Birko.MessageQueue** - Core interfaces (IMessageQueue, IMessageProducer, IMessageConsumer), Pub/Sub, P2P
- **Birko.MessageQueue.InMemory** / **.MQTT** / **.Redis**

## Event Bus
- **Birko.EventBus** - Core (IEvent, IEventBus, IEventHandler), in-process bus, pipeline, deduplication
- **Birko.EventBus.MessageQueue** - Distributed event bus over Birko.MessageQueue
- **Birko.EventBus.Outbox** - Transactional outbox pattern
- **Birko.EventBus.EventSourcing** - EventStore-to-EventBus bridge
- **Birko.EventBus.Tenant** - Tenant-scope bridge to Birko.Data.Tenant (TenantEventEnricher + TenantEventScopeAccessor for background dispatch under Strict isolation)

## Health
- **Birko.Health** - IHealthCheck, HealthCheckRunner, DiskSpace/Memory checks
- **Birko.Health.Data** - SQL, Elasticsearch, MongoDB, RavenDB, InfluxDB, TimescaleDB, CosmosDB, Vault, MQTT, SMTP, WebSocket, TCP, SSE checks
- **Birko.Health.Redis** - Redis PING + latency
- **Birko.Health.Azure** - Blob Storage, Key Vault checks

## Storage & Telemetry
- **Birko.Storage** - IFileStorage, LocalFileStorage, presigned URLs
- **Birko.Storage.AzureBlob** - Azure Blob Storage (REST API, OAuth2, SAS)
- **Birko.Telemetry** - Store metrics, distributed tracing, correlation ID middleware
- **Birko.Telemetry.OpenTelemetry** - OTLP + Console exporters, ASP.NET Core instrumentation

## Time & Localization
- **Birko.Time.Abstractions** - IDateTimeProvider (zero deps)
- **Birko.Time** - Time zones, business calendar, holidays, working hours
- **Birko.Localization** - Translation framework, CLDR pluralization, JSON/RESX/InMemory providers
- **Birko.Localization.Data** - Database-backed translations, namespace scoping, TTL cache
- **Birko.Data.Localization** - Entity-level localization (ILocalizable, store decorator wrappers)

## AI / LLM
- **Birko.AI.Contracts** - LLM provider interface (ILlmProvider), models (Message, ContentBlock, TokenUsage, LlmResponse, LlmStreamingResponse), Tool base class, AgentOptions, LlmProviderFactory (registration-based, `Birko.AI.Factories` namespace)
- **Birko.AI** - LlmProviderBase (retry with Birko.RetryPolicy, SSE parsing, OpenAI-style message/tool builders), Agent base class (run loop, streaming, tool execution), AgentFactory (registration-based, `Birko.AI.Factories` namespace), 9 default tools (ListFiles, ReadFile, WriteFile, EditFile, AppendToFile, SearchCode, RunCommand, DisplayText, AskUser)
- **Birko.AI.Providers** - 11 LLM provider implementations: ClaudeProvider, OpenAiProvider, AzureOpenAiProvider, GeminiProvider, OllamaProvider, OpenAiCompatibleProviderBase, LlamaCppProvider, VllmProvider, SglangProvider, GitHubCopilotProvider (uses IOAuthClient), ZAiProvider + ProviderRegistration (registers all providers with LlmProviderFactory)
- **Birko.AI.Agents** - CodingAgent base, 10 language-specific agents (CSharp, Python, JS/TS, Cpp, React, Angular, CSS, HTML, PHP, Assembler), 4 task agents (Debug, Refactor, Test, Documentation), DiagrammingAgent, MediaAgent + 3 media agents (Image, Svg, Bitmap), OrchestratorAgent base + AgentRegistration (registers all agents with AgentFactory, convenience Create)
- **Birko.AI.Resilience** - ProviderRateLimiter (sliding window per-minute/per-day), ProviderCircuitBreaker (Closed/Open/HalfOpen with ICircuitBreakerStore), CostTrackingService (pricing, budget enforcement with IUsageRepository), TrackedLlmProvider (decorator combining rate limiting + cost tracking)
- **Birko.AI.Orchestration** - ITaskDispatcher + DirectTaskDispatcher, AgentTaskRecord (status, dependencies, retry tracking), ImplementationPlan/ImplementationStep (file-level dependencies), StepDependencyAnalyzer (parallel groups, topological sort), EscalationAlert (escalation types, reflection entries)
- **Birko.Communication.OAuth.Providers** - GitHubOAuthProvider (pre-configured OAuthSettings factory for GitHub Device Code flow, uses Birko.Communication.OAuth)

## Web (TypeScript)
- **Birko.Web.Core** - Minimal Web Component framework (Shadow DOM base class, reactive state, HTTP/SSE clients, hash router, unified i18n: `i18n` singleton, `t()`, `useI18n()`, `onI18nChange()`, `BaseComponent.label()` helper)
- **Birko.Web.Components** - Component library (55 Shadow DOM web components: 21 inputs incl. b-tag-input, b-segmented, b-markdown-editor with H1–H6/table/task-list/highlight/sup/sub, b-datetime-picker, b-time, b-date-range-picker with 2-month layout/hover-preview/opt-in presets/`confirm` mode; 9 layout incl. b-chat, b-split-panel; 14 data incl. b-kanban with recursive nesting + 3-zone DnD, b-editable-table, b-pre, b-code-block, b-definition-list, b-object-tree, b-json-viewer, b-xml-viewer with sticky-header modes; 6 feedback incl. b-progress, b-stale-banner; 4 navigation; 1 command palette. Canonical `bwc.*` i18n key namespace, shared sheets — `dataViewerCardSheet`/`dataViewerHeaderSheet`/`toolbarBtnSheet`)
- **Birko.Web.Shell** - Application shell framework (three-level hierarchy: `BCoreAppShell` → `BSidebarAppShell` → `BAppShell`; authentication, module loading, command palette, notifications, tenant switching, page bases — `BaseListPage`/`BaseSplitPage`/`BaseDetailPage`/`BaseFormModal`/`BaseDashboardWidget`; canonical `bws.*` i18n keys with `{entity}` interpolation)

## Desktop / XAML UI (EPIC-015)
First real, buildable `.csproj` assemblies in the `Birko\Framework` bucket (every other sibling is `.shproj`/`.projitems`). Referenced via `ProjectReference`, **not** the `Birko.Framework.csproj` aggregator. Avalonia-targeting projects are `net8.0` (Avalonia 11.2.3). The runnable gallery (`Birko.Xaml.Gallery`) lives in `Birko\Consumers`, not here.
- **Birko.DesignTokens** (net10.0) - Build-time token generator. `tokens.json` → **byte-identical** web CSS + Avalonia AXAML (`generate`/`verify`/`extract` CLI). Single source of truth for all design tokens; language-neutral schema. A tool, not a runtime library
- **Birko.Xaml.Core** (net8.0) - Avalonia-free platform core (enforced by test): `Theming` (ThemeInfo, BirkoThemes, IThemeManager), `Localization` (II18n/I18n + singleton), base MVVM ViewModels (BasePageViewModel, CrudViewModelBase, ListPageViewModel, DetailPageViewModel, SplitPageViewModel, ShellViewModel), `Forms.FormField`, `Navigation`, `Commands.CommandItem`, Kanban/Ribbon/Chart models, `Data.ICrudDataSource<T>` port. Dep: CommunityToolkit.Mvvm
- **Birko.Xaml.Avalonia** (net8.0) - Avalonia skin: theme system (ThemeDictionaries, 4 runtime-swappable variants light/dark/neon/finstat), ~20 restyled Tier-1 controls, building blocks (Form/Drawer/SplitPanel/Modal/FormModal), 7 Tier-2 composites (tree-menu, command-palette, object-tree/json-viewer, xml-viewer, kanban, markdown-editor, BChart on LiveCharts2), `{l:Tr}` markup extension. Deps: Avalonia, Avalonia.Themes.Fluent, Avalonia.Controls.DataGrid, LiveChartsCore.SkiaSharpView.Avalonia
- **Birko.Xaml.Shell** (net8.0) - Application shell: ViewLocator, `ShellView` (sidebar chrome), `RibbonShellView` (ribbon/BAppShell chrome), generic List/Detail/Split page views, Ctrl+K command palette, user/tenant areas, cross-fade page transitions

## Tests
All test projects use xUnit + FluentAssertions. Each `*.Tests` project has its own CLAUDE.md.
- Birko.Data.Tests, Birko.Data.SQL.Tests, Birko.Data.ElasticSearch.Tests
- Birko.Helpers.Tests, Birko.Structures.Tests
- Birko.BackgroundJobs.Tests, Birko.MessageQueue.Tests, Birko.MessageQueue.Redis.Tests
- Birko.EventBus.Tests, Birko.CQRS.Tests, Birko.Workflow.Tests
- Birko.Security.AspNetCore.Tests, Birko.Security.BCrypt.Tests, Birko.Security.Vault.Tests, Birko.Security.AzureKeyVault.Tests, Birko.Security.NFC.Tests, Birko.Security.OAuth.Server.Tests (Vault.Tests also covers Vault.Configuration)
- Birko.Storage.Tests, Birko.Storage.AzureBlob.Tests
- Birko.Telemetry.Tests, Birko.Telemetry.OpenTelemetry.Tests
- Birko.Rules.Tests, Birko.Data.Processors.Tests, Birko.Data.Aggregates.Tests
- Birko.Health.Tests, Birko.Health.Azure.Tests
- Birko.Messaging.Tests, Birko.Messaging.Razor.Tests
- Birko.Serialization.Tests, Birko.Time.Tests, Birko.Caching.Hybrid.Tests
- Birko.Localization.Tests, Birko.Localization.Data.Tests, Birko.Data.Localization.Tests
- Birko.Communication.Modbus.Tests, Birko.Communication.OAuth.Tests, Birko.Communication.GraphQL.Tests, Birko.Communication.IR.Tests, Birko.Communication.NFC.Tests
- Birko.Data.MongoDB.Tests
- Birko.Random.Tests
- Birko.Data.RavenDB.Tests, Birko.Data.CosmosDB.Tests
- Birko.Data.TimescaleDB.Tests, Birko.Data.InfluxDB.Tests, Birko.Data.JSON.Tests
- Birko.Data.Views.Tests
- Birko.Validation.Tests, Birko.Data.Sync.Tests
- Birko.BackgroundJobs.SQL.Tests, Birko.Workflow.SQL.Tests
- Birko.Communication.Camera.Tests, Birko.Communication.REST.Tests, Birko.Communication.WebSocket.Tests
- Birko.Data.Migrations.SQL.Tests, Birko.Data.XML.Tests
- Birko.Caching.Tests
- Birko.DesignTokens.Tests, Birko.Xaml.Core.Tests, Birko.Xaml.Avalonia.Tests (EPIC-015; Avalonia tests are headless + Skia)

## Per-Project CLAUDE.md
Each project has its own CLAUDE.md at `../Birko.{ProjectName}/CLAUDE.md` with specific details about components, dependencies, and conventions.
