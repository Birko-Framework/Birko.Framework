# Birko Framework
## Modulárny .NET framework pre podnikové, IoT, AI a real-time aplikácie

---

## Prehľad

Birko Framework je komplexný .NET 10.0 framework postavený na princípe **jedno jadro – mnoho platforiem**. Namiesto toho, aby bol viazaný na konkrétnu databázu alebo infraštruktúru, definuje **abstraktné rozhrania (contracts)** a dodáva k nim **platformové rozšírenia** (SQL, MongoDB, ElasticSearch, RavenDB, Cosmos DB, Redis, Azure, OpenTelemetry, ...), ktoré je možné zameniť bez zmeny aplikačného kódu.

**Čo framework poskytuje:**

- **Univerzálny prístup k dátam** – 12 databázových platforiem cez jednotný `IStore` / `IAsyncStore` / `IBulkStore` interface
- **Komunikačnú vrstvu** – REST, SOAP, WebSocket, SSE, Modbus, OAuth 2.0, NFC/RFID, Bluetooth, Camera, IR
- **AI/LLM infraštruktúru** – multi-provider agent framework s resilience (rate limiting, circuit breaker, cost tracking)
- **Bezpečnosť** – hashovanie (PBKDF2, BCrypt), šifrovanie (AES-256-GCM), JWT, RBAC, HashiCorp Vault a Azure Key Vault integrácia
- **Integračné vzory** – Unit of Work, Soft Delete, Audit, Timestamp, Multi-tenancy, Event Sourcing, CQRS, Workflow, Tagging
- **Asynchrónnu infraštruktúru** – Message Queue, Event Bus, Background Jobs, Outbox Pattern
- **Observabilitu** – Health Checks, Telemetry, OpenTelemetry, distribuované tracing

**Čísla:**

- **161 produktových projektov** (z toho približne **81 platformových rozšírení**)
- **55 testovacích projektov** (xUnit + FluentAssertions)
- **12 databázových platforiem**, **11 LLM poskytovateľov**, **43 Web Components**
- **0 externých závislostí** pre core contracts

**Oblasti použitia:**

Framework je general-purpose – žiadna vrstva nie je viazaná na konkrétnu doménu:

- **Podnikové aplikácie (back-office, ERP)** – CRUD, multi-tenancy, RBAC, audit, workflow, CQRS, reporting
- **E-shopy a e-commerce** – domain modely (Customers, Products, Category, Pricing: Currency/Tax/PriceGroup/PriceList/Discount, Inventory, IDocument/IDocumentLine pre objednávky), Workflow pre stavy objednávok, Background Jobs, Messaging (Email/SMS/Push), ElasticSearch vyhľadávanie, multi-currency a multi-language
- **Prezentačné a CMS aplikácie** – Web.Components (Shadow DOM UI), Web.Shell, hash router, Razor templates (RazorLight), SEO modely, Localization (CLDR pluralizácia), JSON/XML content storage, Azure Blob pre médiá, slug generátor
- **Desktopové aplikácie** – SQLite / JSON / XML stores, lokálny filesystem storage, hardvér (NFC, RFID, Bluetooth, Camera, IR, Modbus), offline background jobs (XML job queue), AI/LLM integrácia pre local-first nástroje
- **IoT a priemysel** – Modbus (RTU/TCP, funkčné kódy 01–16), NFC/RFID (ISO 14443A, NDEF), Bluetooth, IR (NEC/Samsung/RC5 @ 38 kHz), Camera, Hardware, time-series (InfluxDB, TimescaleDB)
- **AI-riadené aplikácie a devtools** – 11 LLM poskytovateľov, 10 jazykových a 4 task agenti, orchestrácia, resilience (rate limit / circuit breaker / cost tracking)
- **Real-time systémy** – WebSocket, SSE, MessageQueue, EventBus, distribuovaná synchronizácia
- **Procedurálna generácia a simulácie** – 6 RNG algoritmov, Perlin/Simplex noise, distribúcie (Uniform/Normal/Exponential/Poisson), grafy, stromy (AVL/Red-Black/Interval), heapy, Bloom filter, skip list

---

## Kľúčové vlastnosti

### 🗄️ Vrstva prístupu k dátam (13 storage projektov)
- **SQL**: SQL Server, PostgreSQL, MySQL, SQLite (5 projektov vrátane základu)
- **NoSQL**: MongoDB, RavenDB, Elasticsearch, Cosmos DB (4 projekty)
- **Time-series**: InfluxDB, TimescaleDB (2 projekty)
- **Súborové**: JSON (System.Text.Json), XML (System.Xml.Serialization)
- **Repository & Store pattern** s lazy-init a bulk operáciami (Create/Read/Update/Delete + filter-based Update/Delete s `PropertyUpdate<T>`)
- **Agregácia na úrovni store** — `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` pre server-side GROUP BY, SUM, AVG, MIN, MAX, COUNT s time bucketing a stránkovaním

### 🔄 Vzory a funkcionality (8 feature + 8 migrations + 9 sync + 10 viewmodel + 5 views)
- **Migrácie** – automatické vytváranie tabuliek, indexov a schémových zmien (base + 7 platforiem)
- **Synchronizácia** – cross-platformová replikácia medzi úložiskami (base + 8 backendov)
- **ViewModel repository** – projekcie a mapovania modelov (base + 9 platforiem)
- **Fluent Views** – SQL/NoSQL agregácie a projekcie (base + 5 platforiem)
- **Multi-tenancy** – izolácia dát pre viac klientov
- **Event Sourcing** – event-driven architektúra
- **CQRS** – oddelenie čítania a zápisu s Mediator patternom
- **Workflow Engine** – stavové stroje s podmienkami (guards) a akciami
- **Tagging** – polymorfné priradenie tagov entitám
- **Unit of Work** – transakčné spracovanie

### 🌐 Komunikácia (15 projektov)
- **REST/SOAP** – API klienti a server (Birko.Communication.REST, .REST.Server, .SOAP, .SSE)
- **WebSocket/SSE** – obojsmerná a server-sent real-time komunikácia
- **Modbus** – priemyselná komunikácia (RTU/TCP, funkčné kódy 01–06 / 15–16)
- **OAuth 2.0** – Client Credentials, Auth Code, PKCE, Device Code, Refresh Token
- **NFC/RFID** – čítanie tagov (ISO 14443A, NDEF)
- **Camera/IR** – multimédiá a infračervená komunikácia (NEC/Samsung/RC5, 38 kHz)
- **Sieťové a hardvérové** – Network, Hardware, Bluetooth

### 🤖 AI / LLM (6 projektov + 1 OAuth provider – nové!)
- **11 poskytovateľov**: Claude, OpenAI, Azure OpenAI, Gemini, Ollama, LlamaCpp, vLLM, SGLang, GitHub Copilot, Z.AI + OpenAI-compatible base
- **Agenti**: CodingAgent, 10 jazykových agentov (C#, Python, JS/TS, C++, React, Angular, CSS, HTML, PHP, Assembler), 4 task agenti (Debug, Refactor, Test, Documentation), 3 media agenti, OrchestratorAgent
- **Resilience**: Rate limiting (sliding window), Circuit Breaker (3-stavový), Cost Tracking (budget enforcement)
- **Orchestration**: Task dispatcher, implementation plán, paralelné vykonávanie, závislostná analýza, eskalácie

### 🔒 Bezpečnosť (7 projektov)
- **Hashovanie**: PBKDF2 (core), BCrypt (pure C# Blowfish)
- **Šifrovanie**: AES-256-GCM
- **Autentifikácia**: JWT, NFC-based auth (tag-to-user mapovanie)
- **Secret Management**: HashiCorp Vault (KV v1/v2), Azure Key Vault (OAuth2 + REST API)
- **ASP.NET Core** integrácia – JWT Bearer, `ICurrentUser`, permissions, tenant middleware, RBAC

### 📦 Infraštruktúra
| Komponent | Počet projektov | Implementácie |
|-----------|----------------:|---------------|
| **Caching** | 4 | Birko.Caching, .Redis, .Hybrid (L1+L2), Birko.Redis (shared) |
| **Message Queue** | 4 | Base + InMemory, MQTT, Redis |
| **Event Bus** | 4 | In-process, MessageQueue (distribuovaný), Outbox, EventSourcing bridge |
| **Background Jobs** | 9 | Base + SQL, ES, MongoDB, RavenDB, JSON, XML, Redis, CosmosDB |
| **Health Checks** | 4 | Core + Data (SQL, NoSQL, MQTT, SMTP, WebSocket, ...), Redis, Azure |
| **Telemetry** | 2 | System.Diagnostics.Metrics, OpenTelemetry (OTLP + Console) |
| **Serialization** | 4 | System JSON/XML + Newtonsoft, MessagePack, Protobuf |
| **Storage** | 2 | Lokálny FS, Azure Blob Storage (REST + OAuth2 + SAS) |

### 🌍 Ďalšie funkcie
- **Validation** – fluent validation framework (`IValidator<T>`, `AbstractValidator<T>`)
- **Rules Engine** – dátami riadené pravidlá (`IRule`, `RuleGroup`, `RuleSet`, `RuleEvaluator`)
- **Localization** – 3 projekty: Birko.Localization (CLDR pluralizácia), .Localization.Data (DB), Birko.Data.Localization (entity-level)
- **Messaging** – Email (SMTP), SMS, Push, Razor templates (RazorLight)
- **Data Processors** – XML, CSV, HTTP, ZIP stream procesory s dekorátorovou kompozíciou
- **Tagging** – polymorfný junction s tenant scoping
- **Data Structures** – stromy (Binary, AVL, Red-Black, Interval), grafy, heapy, tries, LRU cache, Bloom filter, ring buffer, skip list, deque, disjoint set
- **Random & Sequences** – 6 RNG algoritmov (System, Crypto, XorShift, MersenneTwister, SplitMix, Test), distribúcie (Uniform, Normal, Exponential, Poisson), GuidV4/V7, NanoId, Snowflake, Perlin/Simplex noise
- **Time** – kalendáre, pracovné hodiny, časové zóny, sviatky
- **Helpers** – PathHelper, StringHelper, ConvertHelper, SlugGenerator

---

## Architektúra

### Legenda

- 🟦 **Core** – rozhrania a základné abstrakcie (zero-dependency alebo zero-external-dependency)
- 🟩 **Feature** – konkrétna funkcionalita nezávislá na platforme
- 🟨 **Platform extension** – implementácia featury nad konkrétnou platformou (SQL, MongoDB, Redis, Azure, ...)

### Celková štruktúra frameworku

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BIRKO FRAMEWORK                                    │
│                        Modular .NET 10.0 Architecture                        │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                  🟦 CORE CONTRACTS (zero external deps)                      │
├─────────────────────────────────────────────────────────────────────────────┤
│  Birko.Contracts         │  ILoadable, ICopyable, IGuidEntity, RetryPolicy  │
│  Birko.Models.Contracts  │  ICatalogItem, IPriceable, IVariantable, ...     │
│  Birko.Time.Abstractions │  IDateTimeProvider, SystemDateTimeProvider        │
│  Birko.AI.Contracts      │  ILlmProvider, Message, Tool, AgentOptions        │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                       🟩 DOMÉNOVÉ VRSTVY                                    │
├─────────────┬─────────────┬────────────┬───────────┬───────────┬────────────┤
│ DATA LAYER  │   AI/LLM    │  COMM      │ INFRA     │ SECURITY  │ MODELS     │
│             │             │            │           │           │            │
│ Stores/Repo │ LlmProvider │ REST/SOAP  │ Caching   │ PBKDF2    │ Customers  │
│ UnitOfWork  │ Agent       │ WebSocket  │ MsgQueue  │ AES-GCM   │ Users/RBAC │
│ Migrations  │ Tools       │ Modbus     │ EventBus  │ JWT       │ Inventory  │
│ Sync        │ Resilience  │ OAuth 2.0  │ BgJobs    │ Vault     │ Pricing    │
│ EventSource │ Orch.       │ NFC/RFID   │ Health    │ RBAC      │ ValueObj.  │
│ Views       │             │ Cam/IR/SSE │ Telemetry │ ASP.NET   │ SQLMap     │
│ Tagging     │             │ Network    │ Validation│           │ Product    │
│ Patterns    │             │ Bluetooth  │ Rules     │           │ Category   │
│ Composition │             │            │ CQRS      │           │ SEO        │
│ Processors  │             │            │ Workflow  │           │            │
│ Aggregates  │             │            │ Structures│           │            │
│ Tenant      │             │            │ Random    │           │            │
│             │             │            │ Serial.   │           │            │
└─────────────┴─────────────┴────────────┴───────────┴───────────┴────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│          🟨 PLATFORMOVÉ ROZŠÍRENIA (vymeniteľné bez zmeny kódu)              │
├─────────────────────────────────────────────────────────────────────────────┤
│ SQL: MSSql, PostgreSQL, MySQL, SQLite  │  NoSQL: Mongo, Raven, ES, Cosmos   │
│ Time-series: Influx, Timescale         │  Files: JSON, XML                   │
│ Caching: Redis, Hybrid                 │  Queue: InMemory, MQTT, Redis       │
│ AI Providers: Claude, OpenAI, Gemini,  │  Storage: AzureBlob                 │
│   Ollama, AzureOpenAI, GitHubCopilot,  │  Telemetry: OpenTelemetry           │
│   ZAi, vLLM, SGLang, LlamaCpp          │  Secrets: Vault, AzureKeyVault      │
│ Sync/Mig/ViewModel/Views/Workflow/     │  Serialization: Newtonsoft,         │
│   BgJobs na každej hlavnej platforme   │    MessagePack, Protobuf            │
└─────────────────────────────────────────────────────────────────────────────┘

         Všetky feature moduly závisia len na Contracts, nie medzi sebou.
```

### Store Hierarchy (Template Method Pattern)
```
AbstractStore → AbstractBulkStore (sync)
AbstractAsyncStore → AbstractAsyncBulkStore (async)
```

**Lazy-init**: Verejné CRUD metódy automaticky volajú `Init()` / `InitAsync()` pred prvým použitím (double-checked locking, thread-safe). Konkrétne store triedy prepisujú **`protected *Core`** metódy, nie verejné CRUD, vďaka čomu je inicializácia vždy zaručená.

### SQL Stores
```
DataBaseStore<DB,T> → DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> → AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy
```
AbstractRepository → AbstractBulkRepository (sync)
AbstractAsyncRepository → AbstractAsyncBulkRepository (async)
```

### Detailný prehľad vrstiev

#### 1. 🟦 Core Contracts (4 projekty, zero external deps)
```
Birko.Contracts          → ILoadable, ICopyable, IDefault, ITimestamped,
                           IGuidEntity, ILogEntity, RetryPolicy (backoff + jitter)
Birko.Models.Contracts   → ICatalogItem, IPriceable, IVariantable,
                           ICategorizeable, IBatchable, ILocatable,
                           IHierarchical, IDocument, IContactable, IAddressable
Birko.Time.Abstractions  → IDateTimeProvider, SystemDateTimeProvider,
                           TestDateTimeProvider
Birko.AI.Contracts       → ILlmProvider, Message, ContentBlock, TokenUsage,
                           Tool, AgentOptions, LlmProviderFactory
```

#### 2. 🟩 Core Framework (5 projektov)
```
Birko.Framework          → Main framework aggregator
Birko.Configuration      → Settings, PasswordSettings, RemoteSettings
Birko.Data.Core          → AbstractModel, ViewModels, Filters, Exceptions
Birko.Data.Stores        → IStore, IAsyncStore, IBulkStore, OrderBy, StoreLocator,
                           AggregateFunction, AggregateQuery, AggregateResult,
                           IAggregatableStore, OrderByHelper, TimeIntervalParser
Birko.Data.Repositories  → IRepository, RepositoryLocator, DI extensions
```

#### 3. Data Layer – Storage Providers (13 projektov – 🟩 1 core + 🟨 12 platformových rozšírení)
```
🟩 Birko.Data.SQL                → Base: DataBaseStore, AsyncDataBaseStore
🟨 Birko.Data.SQL.MSSql          → Microsoft SQL Server
🟨 Birko.Data.SQL.PostgreSQL     → PostgreSQL
🟨 Birko.Data.SQL.MySQL          → MySQL
🟨 Birko.Data.SQL.SqLite         → SQLite
🟨 Birko.Data.MongoDB            → MongoDB
🟨 Birko.Data.RavenDB            → RavenDB
🟨 Birko.Data.ElasticSearch      → Elasticsearch (referenčná async/bulk implementácia)
🟨 Birko.Data.CosmosDB           → Azure Cosmos DB (NoSQL API)
🟨 Birko.Data.InfluxDB           → InfluxDB (time-series)
🟨 Birko.Data.TimescaleDB        → TimescaleDB (time-series nad PostgreSQL)
🟨 Birko.Data.JSON               → Súborový JSON (referenčná file-based implementácia)
🟨 Birko.Data.XML                → Súborový XML
```

#### 4. Data Features + platformové rozšírenia (celkovo 43 projektov)
```
🟩 Birko.Data.Patterns          → UoW, SoftDelete, Audit, Timestamp, Sluggable
🟩 Birko.Data.Composition       → StoreWrapperBuilder (runtime dekorátorové reťazce)
🟩 Birko.Data.Tenant            → Multi-tenancy podpora
🟩 Birko.Data.Tagging           → Tag, EntityTag, ITaggable, polymorphic junction
🟩 Birko.Data.EventSourcing     → Event Sourcing pattern
🟩 Birko.Data.Aggregates        → SQL-NoSQL aggregate mapper (flatten/expand)
🟩 Birko.Data.Views             → Fluent ViewDefinitionBuilder, ViewMapRegistry
                                   (AggregateFunction z Birko.Data.Stores)
🟩 Birko.Data.Processors        → Stream procesory (XML, CSV, HTTP, ZIP)

🟩 Birko.Data.Migrations        → Migračný framework (base)
🟨 Birko.Data.Migrations.{SQL, ElasticSearch, MongoDB, RavenDB,
                           InfluxDB, TimescaleDB, CosmosDB}   (7 rozšírení)

🟩 Birko.Data.Sync              → Sync framework (base)
🟨 Birko.Data.Sync.{Sql, ElasticSearch, MongoDb, RavenDB, Json,
                    Xml, CosmosDB, Tenant}                    (8 rozšírení)

🟩 Birko.Data.ViewModel         → ViewModel repository (base)
🟨 Birko.Data.{SQL, ElasticSearch, MongoDB, RavenDB, CosmosDB,
               InfluxDB, TimescaleDB, JSON, XML}.ViewModel    (9 rozšírení)

🟨 Birko.Data.{SQL, MongoDB, ElasticSearch, RavenDB, CosmosDB}.Views
                                                              (5 rozšírení)

🟩 Birko.Data.SQL.View          → SQL view DDL (base)
🟨 Birko.Data.SQL.{MSSql, PostgreSQL, MySQL, SqLite}.View     (4 rozšírenia)
🟨 Birko.Data.SQL.View.Migrations → integrácia s migráciami
🟨 Birko.Data.SQL.Caching        → query caching decorator
🟨 Birko.Data.Localization       → entity-level lokalizácia
```

#### 5. 🤖 AI / LLM (6 projektov + 1 OAuth provider)
```
🟦 Birko.AI.Contracts           → interfaces
🟩 Birko.AI                     → LlmProviderBase (retry, SSE), Agent base,
                                   AgentFactory, 9 default tools
🟨 Birko.AI.Providers           → 11 LLM providerov (Claude, OpenAI,
                                   AzureOpenAI, Gemini, Ollama, LlamaCpp,
                                   vLLM, SGLang, GitHubCopilot, ZAi,
                                   OpenAiCompatibleBase)
🟨 Birko.AI.Agents              → CodingAgent + 10 language + 4 task +
                                   4 media + Orchestrator agenti
🟩 Birko.AI.Resilience          → RateLimiter, CircuitBreaker, CostTracking
🟩 Birko.AI.Orchestration       → TaskDispatcher, ImplementationPlan,
                                   StepDependencyAnalyzer
🟨 Birko.Communication.OAuth.Providers → GitHub Device Code flow pre Copilot
```

#### 6. 🌐 Communication (15 projektov – 🟩 1 core + 🟨 14 rozšírení)
```
🟩 Birko.Communication             → Base interfaces
🟨 Birko.Communication.Network     → TCP/UDP
🟨 Birko.Communication.Hardware    → Serial port, USB
🟨 Birko.Communication.Bluetooth   → BT klient
🟨 Birko.Communication.WebSocket   → WebSocket klient/server
🟨 Birko.Communication.REST        → REST klient
🟨 Birko.Communication.REST.Server → REST server
🟨 Birko.Communication.SOAP        → SOAP klient
🟨 Birko.Communication.SSE         → Server-Sent Events
🟨 Birko.Communication.Modbus      → RTU/TCP (funkčné kódy 01-06, 15-16)
🟨 Birko.Communication.OAuth       → OAuth 2.0 flows (Client Credentials,
                                      Auth Code, PKCE, Device Code, Refresh)
🟨 Birko.Communication.OAuth.Providers → GitHubOAuthProvider
🟨 Birko.Communication.Camera      → FFmpeg-based JPEG snapshots
🟨 Birko.Communication.IR          → Consumer IR (NEC/Samsung/RC5, 38 kHz)
🟨 Birko.Communication.NFC         → ISO 14443A, NDEF tag reading
```

#### 7. Infraštruktúra (celkovo 28 projektov)
```
🟩 Birko.Caching                → ICache, MemoryCache, CacheSerializer
🟨 Birko.Caching.Redis          → Redis backend
🟨 Birko.Caching.Hybrid         → L1 memory + L2 distributed
🟨 Birko.Redis                  → Shared Redis infra (RedisSettings, Manager)

🟩 Birko.MessageQueue           → Core (IMessageQueue, Pub/Sub, P2P)
🟨 Birko.MessageQueue.InMemory  → In-process
🟨 Birko.MessageQueue.MQTT      → MQTT broker
🟨 Birko.MessageQueue.Redis     → Redis Pub/Sub + streams

🟩 Birko.EventBus               → In-process, pipeline, deduplikácia
🟨 Birko.EventBus.MessageQueue  → Distribuovaný event bus
🟨 Birko.EventBus.Outbox        → Transakčný outbox pattern
🟨 Birko.EventBus.EventSourcing → EventStore-to-EventBus bridge

🟩 Birko.BackgroundJobs         → Core (queue, processor, scheduler)
🟨 Birko.BackgroundJobs.{SQL, ElasticSearch, MongoDB, RavenDB,
                          JSON, XML, Redis, CosmosDB}  (8 rozšírení)

🟩 Birko.Health                 → IHealthCheck, HealthCheckRunner
🟨 Birko.Health.Data            → SQL, NoSQL, MQTT, SMTP, WebSocket, TCP, SSE
🟨 Birko.Health.Redis           → Redis PING + latency
🟨 Birko.Health.Azure           → Blob Storage, Key Vault

🟩 Birko.Telemetry              → Store metrics, tracing, correlation ID
🟨 Birko.Telemetry.OpenTelemetry → OTLP + Console, ASP.NET Core

🟩 Birko.Serialization          → ISerializer, SystemJson, SystemXml
🟨 Birko.Serialization.Newtonsoft / .MessagePack / .Protobuf

🟩 Birko.Storage                → IFileStorage, LocalFileStorage, presigned URLs
🟨 Birko.Storage.AzureBlob      → REST + OAuth2 + SAS
```

#### 8. 🔒 Security (7 projektov)
```
🟩 Birko.Security                → PBKDF2, AES-256-GCM, RBAC interfaces
🟨 Birko.Security.BCrypt         → BCrypt (pure C# Blowfish)
🟨 Birko.Security.Jwt            → JWT ITokenProvider
🟨 Birko.Security.Vault          → HashiCorp Vault (KV v1/v2)
🟨 Birko.Security.AzureKeyVault  → Azure Key Vault (OAuth2 + REST)
🟨 Birko.Security.AspNetCore     → JWT Bearer, ICurrentUser, tenant middleware
🟨 Birko.Security.NFC            → NFC tag-to-user auth (enrollment, revocation)
```

#### 9. Models (10 projektov)
```
🟦 Birko.Models.Contracts       → Domain contract interfaces
🟩 Birko.Models                 → Base (AbstractPercentage, AbstractTree,
                                  ValueData) + Value Objects (Money,
                                  MoneyWithTax, Percentage, PostalAddress,
                                  Quantity)
🟩 Birko.Models.Customers       → Address, Customer, InvoiceAddress
🟩 Birko.Models.Users           → User, UserLogin, UserProfile, RBAC
                                   (Role, Permission, UserRole), Tenant
🟩 Birko.Models.Inventory       → StockItem, StorageLocation,
                                   StockMovement, InventoryDocument
🟩 Birko.Models.Pricing         → Currency, Tax, PriceGroup, PriceList,
                                   PriceListEntry, Discount
🟩 Birko.Models.Product         → Produkt (ISluggable z Name)
🟩 Birko.Models.Category        → Kategórie (ISluggable z Title)
🟩 Birko.Models.SEO             → SEO metadata
🟨 Birko.Models.SQL             → ModelMap<T>, IModelMapping, ModelMapRegistry
```

#### 10. CQRS, Workflow, Validation, Rules
```
🟩 Birko.CQRS                   → ICommand, IQuery, IRequestHandler,
                                   IPipelineBehavior, IMediator

🟩 Birko.Workflow               → WorkflowBuilder, WorkflowEngine,
                                   guards, actions, Mermaid/DOT export
🟨 Birko.Workflow.{SQL, ElasticSearch, MongoDB, RavenDB,
                   JSON, XML, CosmosDB}             (7 rozšírení)

🟩 Birko.Validation              → IValidator<T>, AbstractValidator<T>,
                                    store decorator wrapper
🟩 Birko.Rules                   → IRule, RuleGroup, RuleSet, RuleEvaluator
```

#### 11. Time & Localization (5 projektov)
```
🟦 Birko.Time.Abstractions       → IDateTimeProvider (zero deps)
🟩 Birko.Time                    → Kalendáre, sviatky, pracovné hodiny,
                                    časové zóny
🟩 Birko.Localization             → CLDR pluralizácia, JSON/RESX/InMemory
🟨 Birko.Localization.Data        → DB-backed preklady, TTL cache
🟨 Birko.Data.Localization        → Entity-level ILocalizable + wrapper
```

#### 12. Messaging (2 projekty)
```
🟩 Birko.Messaging                → SMTP, SMS, Push, string templates
🟨 Birko.Messaging.Razor          → Razor template engine (RazorLight)
```

#### 13. Utility / Helpers (3 projekty)
```
🟩 Birko.Structures               → Binary/AVL/Red-Black/Interval trees,
                                     grafy, heapy, tries, LRU cache,
                                     Bloom filter, ring buffer, skip list,
                                     disjoint set, deque
🟩 Birko.Random                   → 6 RNG algoritmov, distribúcie,
                                     GuidV4/V7, NanoId, Snowflake, noise
🟩 Birko.Helpers                  → PathHelper, StringHelper, ConvertHelper
```

#### 14. 🌐 Web (TypeScript, 3 projekty)
```
🟩 Birko.Web.Core                 → Shadow DOM base, Signal/Store,
                                     HTTP/SSE klient, hash router
🟩 Birko.Web.Components           → 38 Shadow DOM komponentov
                                     (inputs, layout, data, feedback, navigation)
🟩 Birko.Web.Shell                → Application shell — trojvrstvová hierarchia:
                                     • BCoreAppShell (theme, online/offline,
                                       user dropdown, brand, breadcrumbs)
                                     • BSidebarAppShell (opt-in left + right
                                       sidebars cez <b-sidebar>)
                                     • BAppShell (ribbon, notifikácie,
                                       tenant switcher, status bar,
                                       command palette)
```

### Detailné popisy špeciálnych projektov

#### 🏗️ Birko.Structures – Dátové štruktúry
Komplexná knižnica dátových štruktúr pre .NET:
- **Stromy**: Binary Tree, AVL Tree (self-balancing), Red-Black Tree, Interval Tree
- **Grafy**: Directed/Undirected Graph, BFS/DFS prehľadávanie, najkratšia cesta
- **Heapy**: Binary Heap, Min/Max Heap operácie
- **Tries**: Prefix trie pre vyhľadávanie reťazcov
- **Caching**: LRU Cache (Least Recently Used)
- **Filtre**: Bloom Filter (priestorovo efektívny membership test)
- **Ďalšie**: Ring Buffer, Disjoint Set, Skip List, Deque

#### 🎲 Birko.Random – Generátory náhodných čísel
Pluggable RNG framework s viacerými algoritmami:
- **Generátory**: SystemRandom, CryptoRandom, XorShift, MersenneTwister, SplitMix, TestRandom (deterministický pre testy)
- **Distribúcie**: Uniform, Normal (Gaussian), Exponential, Poisson
- **Sekvencie**: GuidV4/V7, NanoId (URL-friendly), Snowflake (distribuované ID), tokeny
- **Noise**: Perlin noise, Simplex noise pre procedurálnu generáciu

#### 📦 Birko.Serialization – Serializácia
Unifikovaný interface pre viaceré serializačné formáty:
- **Abstrakcia**: `ISerializer` – zameniteľné implementácie
- **System**: SystemJsonSerializer, SystemXmlSerializer (vstavané)
- **Newtonsoft**: Newtonsoft JSON serializer
- **MessagePack**: Binárna serializácia (rýchla a kompaktná)
- **Protobuf**: Protocol Buffers serializácia

**Poznámka**: `Birko.Data.JSON` a `Birko.Data.XML` stores akceptujú `ISerializer` v konštruktore a default používajú `SystemJsonSerializer` / `SystemXmlSerializer` z tejto abstrakcie — takže je možné injectnúť aj iný serializer (napr. Newtonsoft) bez zmeny store kódu.

#### 📋 Birko.CQRS – Command/Query Separation
Implementácia CQRS vzoru s Mediátorom:
- **Vzory**: Command (zápis), Query (čítanie), Request/Response
- **Rozhrania**: `ICommand`, `IQuery`, `IRequestHandler<TRequest, TResponse>`
- **Pipeline**: `IPipelineBehavior<T>` – logging, validácia, transakcie
- **Mediator**: `IMediator` – centrálny dispatcher pre commands/queries
- **Výhody**: oddelenie zápisu/čítania, lepšia škálovateľnosť, čistejší kód

#### ⚙️ Birko.Workflow – Workflow Engine
State machine engine pre biznis procesy:
- **Builder**: Fluent API pre definovanie workflow
- **Stavy**: States, transitions, guards (podmienky), actions (akcie)
- **Engine**: Spustenie workflow, validácia prechodov
- **Vizualizácia**: Mermaid, DOT export pre diagramy
- **Persistencia**: SQL, ElasticSearch, MongoDB, RavenDB, JSON, XML, CosmosDB (7 platformových rozšírení)

#### 📏 Birko.Rules – Rules Engine
Dátami riadený rule engine:
- **Pravidlá**: `IRule`, `IRule<T>` – vyhodnotiteľné pravidlá
- **Zoskupovanie**: `RuleGroup`, `RuleSet` – organizácia pravidiel
- **Evaluátor**: `RuleEvaluator` – vyhodnocovanie s podmienkami (AND/OR)
- **Použitie**: validácia, biznis logika, dynamické rozhodovanie

#### 🛠️ Birko.Helpers – Pomocné funkcie
Utility funkcie pre bežné úlohy:
- **PathHelper**: IsPathSafe, IsUnderDirectory, GetCanonicalPath
- **StringHelper**: formátovanie, porovnávanie, transformácie
- **ConvertHelper**: bezpečná konverzia typov

---

## Modely

### Domain Contracts (Birko.Models.Contracts)
- `ICatalogItem`, `IPriceable`, `IVariantable`, `ICategorizeable`
- `IBatchable`, `ILocatable`, `IHierarchical`
- `IDocument`, `IDocumentLine`, `IContactable`, `IAddressable`, `ISluggable`

### Value Objects (Birko.Models)
- `Money`, `MoneyWithTax`, `Percentage`, `PostalAddress`, `Quantity`

### Doménové modely
- **Customers**: Address, Customer, InvoiceAddress
- **Users**: User, UserLogin, UserProfile, RBAC (Role, RolePermission, UserRole), Tenant, UserTenant
- **Inventory**: StockItem, StockItemVariant, StorageLocation, StockMovement, InventoryDocument, InventoryDocumentLine
- **Pricing**: Currency, Tax, PriceGroup, PriceList, PriceListEntry, Discount
- **Product / Category / SEO**: modely pre katalógové produkty a kategórie

---

## Web Components (TypeScript)

### Birko.Web.Core
- Shadow DOM komponenty (vstavané štýly bez leakovania)
- Reaktívny state (Signal/Store pattern)
- HTTP/SSE klienti
- Hash router

### Birko.Web.Components
**43 komponentov**: inputs (17), layout (9), data (7), feedback (5), navigation (4), command palette (1)

### Birko.Web.Shell
Application shell framework s **trojvrstvovou hierarchiou** abstraktných tried:

- **`BCoreAppShell`** — abstraktné jadro: theme/layout persistence, online/offline tracking, user dropdown, brand link, breadcrumb listener, base CSS, default minimálny layout. Použiteľné priamo pre login stránky, error stránky, kiosky.
- **`BSidebarAppShell extends BCoreAppShell`** — opt-in **ľavý a/alebo pravý sidebar** cez `<b-sidebar>`. Oba môžu byť aktívne súčasne (Outlook-style: folder list vľavo + reading pane vpravo). Žiadne nové abstraktné metódy — sidebar plne opt-in cez gettery.
- **`BAppShell extends BSidebarAppShell`** — full Office-style ribbon shell: navigation tabs, notification bell, tenant switcher, status bar, command palette. Dedí sidebar capability — ribbon shell môže mať aj ľavé/pravé panely.

Plus factory funkcie: autentifikácia (createAuthStore), dynamické moduly (createModuleStore), tenant switching, command palette providers, route guards, page base classes (BaseListPage, BaseSplitPage, BaseDetailPage, BaseFormModal, BaseDashboardWidget).

---

## Testovacie pokrytie

**55 testovacích projektov** (xUnit + FluentAssertions, každý má vlastný `CLAUDE.md`). Každý testovací projekt pokrýva zodpovedajúci produktový projekt rovnakým názvom (napr. `Birko.Data.Tests` testuje `Birko.Data.*` base abstrakcie).

### Rozdelenie testovacích projektov podľa oblasti

| Oblasť | # | Pokrývajú |
|---|---:|---|
| **Data core & patterns** | 6 | Base stores/repos, dekorátory (SoftDelete, Audit, Timestamp), UnitOfWork, ViewModels, Sync (inicial/download/upload), Aggregates (flatten/expand), Processors (XML, CSV, HTTP, ZIP) |
| **Data storage platforms** | 9 | SQL base + konkrétne platformy: MongoDB, RavenDB, ElasticSearch, CosmosDB, InfluxDB, TimescaleDB, JSON, XML |
| **Data features** | 3 | Fluent Views, SQL migrácie, SQL.Tests |
| **Validation & Rules** | 2 | 122 testov pre Validation (Required, Email, Length, Range, Regex, Custom, fluent builder, store wrapper); Rules engine (evaluator, podmienky AND/OR) |
| **CQRS & Workflow** | 3 | CQRS mediator + pipeline; Workflow engine (guards, actions, transitions); Workflow.SQL persistence |
| **Background Jobs & MQ** | 4 | BackgroundJobs core + SQL backend; MessageQueue (InMemory) + Redis backend |
| **Event Bus** | 1 | In-process bus, pipeline, deduplikácia, outbox |
| **Caching** | 2 | Caching core + Hybrid (L1+L2) |
| **Security** | 5 | ASP.NET Core (JWT, tenant middleware), BCrypt, Vault, AzureKeyVault, NFC auth |
| **Communication** | 6 | Modbus (RTU/TCP), OAuth (všetky flows), IR, NFC, Camera, REST, WebSocket |
| **Messaging** | 2 | SMTP + string templates; Razor template engine |
| **Storage** | 2 | LocalFileStorage; Azure Blob |
| **Health** | 2 | Core health runner; Azure (Blob, Key Vault) |
| **Telemetry** | 2 | Store metrics + tracing; OpenTelemetry (OTLP) |
| **Localization** | 3 | Core (CLDR pluralizácia), Data (DB), entity-level |
| **Utility** | 4 | Structures (AVL, graphs, heaps, tries, LRU, Bloom, ...), Random (RNG, distribúcie, sekvencie), Serialization (všetky formáty), Time (kalendáre, sviatky, timezones), Helpers |

### Výber známych pokrytí (počet testov)

- **Birko.Validation.Tests** – **122 testov**: pravidlá, fluent `AbstractValidator`, `ValidationResult`, store wrapper integrácia (sync, async, bulk)
- **Birko.Data.Tests** – **181 testov**: base stores/repos, async soft-delete/audit/timestamp dekorátory, `DefaultStoreWrapper`, `SluggableStoreWrapper`, `SlugGenerator`, `SoftDeleteFilter`, `UnitOfWork` výnimky, `PagedResult`
- **Birko.Data.Sync.Tests** – **21 testov**: SyncProvider (initial/download/upload), SyncQueue (serializácia, concurrency), model defaults

---

## Použitie v konzumerských riešeniach

Odporúčaný vzor – vytvoriť **jeden agregačný projekt** na strane konzumera a referenčne v ňom spojiť všetky potrebné Birko.* shared projekty:

```
YourSolution/
  YourSolution.Birko/          # Agregátor všetkých Birko.* projektov
  YourSolution.Core/           # Referencuje len YourSolution.Birko
  YourSolution.Web/            # Referencuje len YourSolution.Birko
```

Vytvorte projekt `{YourSolution}.Birko` (napríklad `FisData.Birko`) a importujte doň všetky potrebné `Birko.*` shared projekty. Vaše ostatné projekty potom referencujú iba tento jeden agregačný projekt. Predchádza to kompilačným a tranzitívnym konfliktom, keď viaceré projekty importujú prekrývajúce sa sady shared projektov nezávisle od seba.

---

## Štatistiky

| Metrika | Hodnota |
|---|---:|
| **Produktové projekty celkom** | **161** |
| **z toho platformové rozšírenia** (🟨) | **~81** |
| **z toho core / feature projekty** (🟦 + 🟩) | **~80** |
| **Testovacie projekty** | **55** |
| **Projekty celkom** | **216** |
| Databázové platformy | 12 |
| LLM poskytovatelia | 11 |
| Web Components | 43 |
| Jazykové AI agenti | 10 |
| Task AI agenti | 4 |
| Communication protokoly | 14 |
| Workflow persistence backendy | 7 |
| BackgroundJobs backendy | 8 |
| Sync backendy | 8 |
| Migration backendy | 7 |
| **Zero external dependencies** (core contracts) | ✓ |

---

## Dokumentácia

- [README.md](README.md) – kompletná užívateľská dokumentácia
- [docs/](docs/) – detailné návody pre každú oblasť (architektúra, stores, repozitáre, migrácie, vzory, caching, validácia, background jobs, message queue, event bus, event sourcing, storage, messaging, telemetry, security, rules, workflow, CQRS, health, procesory, serializácia, sync, čas, lokalizácia, tenant, komunikácia, závislosti, konzumeri, AI/LLM)
- Každý projekt má vlastný `README.md` s rýchlym prehľadom API a príkladmi použitia

---

## Quick Start

```bash
# Klonovanie a build
git clone <repository>
cd Birko.Framework
dotnet build Birko.Framework.slnx

# Spustenie všetkých testov
dotnet test
```

---

## Licencia

Súčasť Birko Framework – pozri [License.md](License.md).

---

*Prezentácia pre .NET 10.0 · Birko Framework · aktualizované 2026-04-16*
