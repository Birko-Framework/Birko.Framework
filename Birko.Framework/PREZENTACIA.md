# Birko Framework
## Modulárny .NET framework pre podnikové, IoT, AI a real-time aplikácie

---

## Prehľad

Birko Framework je komplexný .NET 10.0 framework postavený na princípe **jedno jadro – mnoho platforiem**. Namiesto toho, aby bol viazaný na konkrétnu databázu alebo infraštruktúru, definuje **abstraktné rozhrania (contracts)** a dodáva k nim **platformové rozšírenia** (SQL, MongoDB, ElasticSearch, RavenDB, Cosmos DB, Redis, Azure, OpenTelemetry, ...), ktoré je možné zameniť bez zmeny aplikačného kódu.

**Čo framework poskytuje:**

- **Univerzálny prístup k dátam** – 12 databázových platforiem cez jednotný `IStore` / `IAsyncStore` / `IBulkStore` interface
- **Komunikačnú vrstvu** – REST, SOAP, WebSocket, SSE, GraphQL, Modbus, OAuth 2.0, NFC/RFID, Bluetooth, Camera, IR
- **AI/LLM infraštruktúru** – multi-provider agent framework s resilience (rate limiting, circuit breaker, cost tracking)
- **Bezpečnosť** – hashovanie (PBKDF2, BCrypt), šifrovanie (AES-256-GCM), JWT, RBAC, HashiCorp Vault a Azure Key Vault integrácia, konfiguračný bridge pre ISecretProvider
- **Integračné vzory** – Unit of Work, Soft Delete, Audit, Timestamp, Multi-tenancy, Event Sourcing, CQRS, Workflow, Tagging
- **Asynchrónnu infraštruktúru** – Message Queue, Event Bus, Background Jobs, Outbox Pattern
- **Observabilitu** – Health Checks, Telemetry, OpenTelemetry, distribuované tracing

**Čísla:**

- **168 produktových projektov** (z toho približne **83 platformových rozšírení**)
- **59 testovacích projektov** (xUnit + FluentAssertions)
- **12 databázových platforiem**, **11 LLM poskytovateľov**, **55 Web Components**
- **Birko.Xaml** — desktopový (Avalonia) UI framework so **spoločným zdrojom design tokenov** s webom a **4 témami**
- **0 externých závislostí** pre core contracts

**Oblasti použitia:**

Framework je general-purpose – žiadna vrstva nie je viazaná na konkrétnu doménu:

- **Podnikové aplikácie (back-office, ERP)** – CRUD, multi-tenancy, RBAC, audit, workflow, CQRS, reporting
- **E-shopy a e-commerce** – domain modely (Customers, Products, Category, Pricing: Currency/Tax/PriceGroup/PriceList/Discount, Inventory, IDocument/IDocumentLine pre objednávky), Workflow pre stavy objednávok, Background Jobs, Messaging (Email/SMS/Push), ElasticSearch vyhľadávanie, multi-currency a multi-language
- **Prezentačné a CMS aplikácie** – Web.Components (Shadow DOM UI), Web.Shell, hash router, Razor templates (RazorLight), SEO modely, Localization (CLDR pluralizácia), JSON/XML content storage, Azure Blob pre médiá, slug generátor
- **Desktopové aplikácie** – **Birko.Xaml** (Avalonia UI so spoločnými design tokenmi a MVVM ako web), SQLite / JSON / XML stores, lokálny filesystem storage, hardvér (NFC, RFID, Bluetooth, Camera, IR, Modbus), offline background jobs (XML job queue), AI/LLM integrácia pre local-first nástroje
- **IoT a priemysel** – Modbus (RTU/TCP, funkčné kódy 01–16), NFC/RFID (ISO 14443A, NDEF), Bluetooth, IR (NEC/Samsung/RC5 @ 38 kHz), Camera, Hardware, time-series (InfluxDB, TimescaleDB)
- **AI-riadené aplikácie a devtools** – 11 LLM poskytovateľov, 10 jazykových a 4 task agenti, orchestrácia, resilience (rate limit / circuit breaker / cost tracking)
- **Real-time systémy** – WebSocket, SSE, MessageQueue, EventBus, distribuovaná synchronizácia
- **Procedurálna generácia a simulácie** – 6 RNG algoritmov, Perlin/Simplex noise, distribúcie (Uniform/Normal/Exponential/Poisson), grafy, stromy (AVL/Red-Black/Interval), heapy, Bloom filter, skip list

---

## Kľúčové vlastnosti

### 🗄️ Vrstva prístupu k dátam (14 storage projektov)
- **SQL**: SQL Server, PostgreSQL, MySQL, SQLite (5 projektov vrátane základu)
- **NoSQL**: MongoDB, RavenDB, Elasticsearch, Cosmos DB (4 projekty)
- **Time-series**: InfluxDB, TimescaleDB (2 projekty)
- **Súborové**: JSON (System.Text.Json), XML (System.Xml.Serialization)
- **In-memory**: `ConcurrentDictionary` store (testovanie, prototypy, demá — bez perzistencie; zároveň najjednoduchšia referenčná implementácia)
- **Typovaná hierarchia nastavení** — `MSSqlSettings`, `MySqlSettings`, `PostgreSqlSettings`, `SqLiteSettings`, `TimescaleDBSettings`, CosmosDB/RavenDB `Settings` — každá úroveň pridáva iba to, čo je v nej skutočne nové (`Settings → PasswordSettings → RemoteSettings → SqlSettings → MSSqlSettings`, ...). Dialect-špecifický `GetConnectionString()` je `virtual` na úrovni providera. Pozri [docs/configuration.md](docs/configuration.md).
- **Platformovo-agnostické migrácie** — `IMigration` + `IMigrationContext` (`Schema`, `Data`, `Raw`, `ProviderName`); jednu migráciu napíšete raz a spustíte voči ktorémukoľvek providerovi (SQL, MongoDB, ElasticSearch, RavenDB, CosmosDB, InfluxDB, TimescaleDB). NoSQL providery ticho preskakujú nepoužiteľné operácie.
- **Repository & Store pattern** s lazy-init a bulk operáciami (Create/Read/Update/Delete + filter-based Update/Delete s `PropertyUpdate<T>`)
- **Agregácia na úrovni store** — `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` pre server-side GROUP BY, SUM, AVG, MIN, MAX, COUNT s time bucketing a stránkovaním
- **Jednotný preklad LINQ filtrov naprieč backendmi** — spoločný `ExpressionNormalizer` (funkcletizácia + rozklad ternárneho operátora a `??`) plus preklad porovnaní, `IN`, reťazcových vzorov a stĺpcovej aritmetiky do SQL WHERE resp. ES query. Sémantika sa drží in-memory referencie (oracle testy): prázdna kolekcia v `Contains` nematchuje nič (`1 = 0` / `MatchNone`, negácia všetko), enum hodnoty sa viažu ako integer a filter, ktorý sa preložiť **nedá**, vyhodí výnimku namiesto tichého rozšírenia na „všetky riadky"

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

### 🌐 Komunikácia (16 projektov)
- **REST/SOAP** – API klienti a server (Birko.Communication.REST, .REST.Server, .SOAP, .SSE)
- **WebSocket/SSE** – obojsmerná a server-sent real-time komunikácia
- **GraphQL** – queries, mutations, subscriptions cez HttpClient + ClientWebSocket (graphql-ws), fluent `GraphQLRequestBuilder`, IObservable subscriptions, žiadne externé závislosti
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

### 🔒 Bezpečnosť (9 projektov)
- **Hashovanie**: PBKDF2 (core), BCrypt (pure C# Blowfish)
- **Šifrovanie**: AES-256-GCM
- **Autentifikácia**: JWT, NFC-based auth (tag-to-user mapovanie)
- **OAuth2 authorization server**: vlastný issuer (token, authorize, device_authorization, dynamic client registration) — všetky štyri grant typy vrátane PKCE a RFC 8628 device flow; perzistencia cez `Birko.Data.Stores` (provider-agnostic)
- **Secret Management**: HashiCorp Vault (KV v1/v2), Azure Key Vault (OAuth2 + REST API), konfiguračný bridge (Microsoft.Extensions.Configuration)
- **Configuration bridge**: `AddSecretConfiguration(any ISecretProvider)` — Vault, Azure Key Vault, čiľubovoľný provider do `IConfiguration`
- **ASP.NET Core** integrácia – JWT Bearer, `ICurrentUser`, permissions, tenant middleware, RBAC
- **Multi-tenant izolácia (secure by default)**: `UseBirkoTenantHeaderGuard()` odmietne hlavičku `X-Tenant-Id`, ktorá nesúhlasí s tenant claimom v JWT (403 `Tenant.HeaderClaimMismatch`) — bez tejto korelácie by volajúci s vlastnými oprávneniami mohol čítať **aj zapisovať** do cudzieho tenanta

### 📦 Infraštruktúra
| Komponent | Počet projektov | Implementácie |
|-----------|----------------:|---------------|
| **Caching** | 4 | Birko.Caching, .Redis, .Hybrid (L1+L2), Birko.Redis (shared) |
| **Message Queue** | 4 | Base + InMemory, MQTT, Redis |
| **Event Bus** | 4 | In-process, MessageQueue (distribuovaný), Outbox, EventSourcing bridge |
| **Background Jobs** | 9 | Base + SQL, ES, MongoDB, RavenDB, JSON, XML, Redis, CosmosDB |
| **Health Checks** | 4 | Core + Data (SQL, NoSQL, MQTT, SMTP, WebSocket, ...), Redis, Azure |
| **Telemetry** | 2 | System.Diagnostics.Metrics, OpenTelemetry (OTLP + Console) |
| **Serialization** | 5 | System JSON/XML + Newtonsoft, MessagePack, Protobuf, YAML |
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

### 🖥️ Desktop / XAML UI (Birko.Xaml — 4 projekty)
- **Spoločný zdroj design tokenov** – `Birko.DesignTokens` generuje z jedného `tokens.json` **byte-identickú** webovú CSS **aj** Avalonia AXAML, takže webový a desktopový dizajn nemôžu nikdy divergovať (`generate` / `verify` / `extract` CLI)
- **Theme systém** – 4 témy (light / dark / neon / finstat) prepínateľné za behu bez reštartu (Avalonia `ThemeDictionaries` + `RequestedThemeVariant`), zhodné s Birko.Web
- **~20 Tier-1 controlov** – token-driven `ControlTheme`s (Button, TextBox, ComboBox, CheckBox, RadioButton, ToggleSwitch, Card, TabControl, Badge, Tag, ProgressBar, DataGrid, ...) + building blocks (Form, Drawer, SplitPanel, Modal, FormModal)
- **7 Tier-2 kompozitov** – tree-menu, command-palette, object-tree/json-viewer, xml-viewer, kanban, markdown-editor, chart (na LiveCharts2)
- **Avalonia-free MVVM core** – `Birko.Xaml.Core` (i18n, base ViewModels, CRUD port) neobsahuje žiadnu Avalonia závislosť (vynútené testom) → znovupoužiteľné budúcim WPF skinom
- **App shell** – sidebar **aj** ribbon chrome nad jedným `ShellViewModel`, navigácia, generické list/detail/split page views, command palette (Ctrl+K), user/tenant oblasti, cross-fade prechody

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
│ Time-series: Influx, Timescale         │  Files: JSON, XML, InMemory         │
│ Caching: Redis, Hybrid                 │  Queue: InMemory, MQTT, Redis       │
│ AI Providers: Claude, OpenAI, Gemini,  │  Storage: AzureBlob                 │
│   Ollama, AzureOpenAI, GitHubCopilot,  │  Telemetry: OpenTelemetry           │
│   ZAi, vLLM, SGLang, LlamaCpp          │  Secrets: Vault, AzureKeyVault      │
│ Sync/Mig/ViewModel/Views/Workflow/     │  Serialization: Newtonsoft,         │
│   BgJobs na každej hlavnej platforme   │    MessagePack, Protobuf, YAML      │
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
                           (typované descendanty: SqlSettings → MSSql/MySql/
                           PostgreSql/Timescale Settings; SqLiteSettings;
                           CosmosDB/RavenDB/MongoDB/Redis Settings)
Birko.Data.Core          → AbstractModel, ViewModels, Filters, Exceptions
Birko.Data.Stores        → IStore, IAsyncStore, IBulkStore, OrderBy, StoreLocator,
                           AggregateFunction, AggregateQuery, AggregateResult,
                           IAggregatableStore, OrderByHelper, TimeIntervalParser
Birko.Data.Repositories  → IRepository, RepositoryLocator, DI extensions
```

#### 3. Data Layer – Storage Providers (14 projektov – 🟩 1 core + 🟨 13 platformových rozšírení)
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
🟨 Birko.Data.InMemory           → In-memory ConcurrentDictionary (testovanie / prototypy)
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

#### 6. 🌐 Communication (16 projektov – 🟩 1 core + 🟨 15 rozšírení)
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
🟨 Birko.Communication.GraphQL     → GraphQL queries/mutations/subscriptions
                                      (HttpClient + ClientWebSocket graphql-ws,
                                      žiadne externé závislosti)
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
🟨 Birko.Serialization.Newtonsoft / .MessagePack / .Protobuf / .Yaml

🟩 Birko.Storage                → IFileStorage, LocalFileStorage, presigned URLs
🟨 Birko.Storage.AzureBlob      → REST + OAuth2 + SAS
```

#### 8. 🔒 Security (9 projektov)
```
🟩 Birko.Security                → PBKDF2, AES-256-GCM, RBAC interfaces
🟨 Birko.Security.BCrypt         → BCrypt (pure C# Blowfish)
🟨 Birko.Security.Jwt            → JWT ITokenProvider
🟨 Birko.Security.Vault          → HashiCorp Vault (KV v1/v2)
🟨 Birko.Security.Vault.Configuration → IConfiguration bridge pre ISecretProvider
                                      (provider-agnostic + Vault hierarchical paths)
🟨 Birko.Security.AzureKeyVault  → Azure Key Vault (OAuth2 + REST)
🟨 Birko.Security.AspNetCore     → JWT Bearer, ICurrentUser, tenant middleware
🟨 Birko.Security.NFC            → NFC tag-to-user auth (enrollment, revocation)
🟨 Birko.Security.OAuth.Server   → OAuth2 authorization server (token, authorize,
                                    device_authorization, dynamic client registration;
                                    všetky štyri grant typy + PKCE; persistuje cez Birko.Data.Stores)
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
                                     HTTP/SSE klient, hash router,
                                     unified i18n singleton (i18n, t(),
                                     useI18n(), onI18nChange(),
                                     BaseComponent.label())
🟩 Birko.Web.Components           → 55 Shadow DOM komponentov
                                     (21 inputs vrátane b-tag-input,
                                     b-segmented, b-markdown-editor s
                                     H1–H6/table/task-list/highlight/sup/sub,
                                     b-datetime-picker, b-time,
                                     b-date-range-picker s 2-mesačným
                                     layoutom, hover-preview, opt-in
                                     presetmi a `confirm` režimom;
                                     9 layout vrátane b-chat;
                                     14 data vrátane b-kanban s rekurzívnym
                                     vnorením a 3-zone DnD,
                                     b-editable-table, b-code-block,
                                     b-json-viewer, b-xml-viewer,
                                     b-object-tree, b-definition-list, b-pre;
                                     6 feedback vrátane b-progress,
                                     b-stale-banner;
                                     4 navigation; 1 command palette).
                                     Kanonické bwc.* i18n kľúče.
🟩 Birko.Web.Shell                → Application shell — trojvrstvová hierarchia:
                                     • BCoreAppShell (theme, online/offline,
                                       user dropdown, brand, breadcrumbs)
                                     • BSidebarAppShell (opt-in left + right
                                       sidebars cez <b-sidebar>)
                                     • BAppShell (ribbon, notifikácie,
                                       tenant switcher, status bar,
                                       command palette)
                                     Kanonické bws.* i18n kľúče s
                                     automatickou {entity} interpoláciou.
```

#### 15. 🖥️ Desktop / XAML UI — Birko.Xaml (4 projekty, EPIC-015)
```
🟩 Birko.DesignTokens   → Build-time generátor tokenov (net10.0):
                          tokens.json → byte-identická web CSS + Avalonia
                          AXAML (generate / verify / extract). Jediný zdroj
                          pravdy pre všetky design tokeny; jazykovo-neutrálna
                          schéma. Nástroj, nie runtime knižnica
🟩 Birko.Xaml.Core      → Avalonia-free platform core (net8.0, vynútené testom):
                          theming abstrakcie, i18n (I18n singleton), base MVVM
                          ViewModels (CrudViewModelBase, ListPage/DetailPage/
                          SplitPage/ShellViewModel), FormField/Navigation/
                          Command/Kanban/Ribbon/Chart modely, ICrudDataSource<T>
                          port. Dep: CommunityToolkit.Mvvm
🟨 Birko.Xaml.Avalonia  → Avalonia skin (net8.0, Avalonia 11.2.3): theme systém
                          (4 varianty light/dark/neon/finstat prepínateľné za
                          behu), ~20 Tier-1 controlov, building blocks (Form/
                          Drawer/SplitPanel/Modal/FormModal), 7 Tier-2 kompozitov
                          (tree-menu, command-palette, object/JSON + XML viewery,
                          kanban, markdown-editor, chart na LiveCharts2),
                          {l:Tr} markup extension
🟨 Birko.Xaml.Shell     → Application shell (net8.0): ViewLocator, ShellView
                          (sidebar chrome), RibbonShellView (ribbon/BAppShell
                          chrome), generické List/Detail/Split page views,
                          Ctrl+K command palette, user/tenant oblasti,
                          cross-fade prechody
```

**Poznámka**: `Birko.Xaml.*` a `Birko.DesignTokens` sú prvé skutočné, buildovateľné `.csproj` **assemblies** v `Birko\Framework` buckete (všetci ostatní súrodenci sú `.shproj`/`.projitems`). Referencujú sa cez `ProjectReference`, **nie** cez `Birko.Framework.csproj` agregátor. Avalonia projekty sú `net8.0` (Avalonia 11.2.3 cieli net8.0). Spustiteľná galéria (`Birko.Xaml.Gallery`) žije v `Birko\Consumers`, nie tu. WPF skin je odložený — zdieľal by rovnaké tokeny a Core ViewModely a forkoval by len šablóny controlov.

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
- **YAML**: YamlDotNet (čitateľný text pre konfiguráciu, CI manifesty)

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
- **Unified i18n** — jeden globálny singleton (`i18n`, `t(key, params?, fallback?)`, `useI18n(instance)`, `onI18nChange(fn)`) plus `BaseComponent.label(attrName, i18nKey, fallback, params?)`. Komponenty sa auto-prepojujú cez `onI18nChange` a automaticky sa prerenderujú pri zmene locale. Per-instance override cez `label-*` atribúty stále vyhráva nad globálnym lookupom.

### Birko.Web.Components
**55 komponentov**: inputs (21), layout (9), data (14), feedback (6), navigation (4), command palette (1)

Kanonický namespace kľúčov: **`bwc.*`** (`bwc.common.close`, `bwc.palette.placeholder`, `bwc.pagination.prev`, ...). Anglický bundle je v `birko-web-components/locales/en.json`.

Nové display/inspection komponenty: `b-pre`, `b-code-block` (syntax-highlighted s copy button), `b-definition-list` (stacked/inline/horizontal/grid), `b-object-tree` (rekurzívny property tree s lazy expansion; voliteľný `show-header` režim s toolbarom), `b-json-viewer` (wrapper nad object-tree s JSON parse + Expand/Collapse/Copy), `b-xml-viewer` (DOM tree cez DOMParser — elementy, atribúty, CDATA, komentáre). Všetky štyri viewery podporujú `max-height` (vnútorný scroll s headerom nad ním) a `sticky-header="page"` (header sa prilepí k viewportu pri scrollovaní stránky) a zdieľajú nové CSS sheets `dataViewerCardSheet` / `dataViewerHeaderSheet` / `toolbarBtnSheet`.

**b-kanban** podporuje rekurzívne vnorenie kariet: `KanbanCard.children` pre sub-úlohy, 3-zónový drag-and-drop (before/inside/after), expand/collapse prepínač na každej rodicovskej karte, `maxNestingDepth` config, depth-aware `renderCard(card, depth)` callback, keyboard navigácia cez vnorené úrovne (ArrowRight expanduje, ArrowLeft kolabuje/fokusuje rodica).

**b-markdown-editor** dostal rozšírenú toolbar: heading dropdown H1–H6, GFM tabuľky, task lists (`- [ ] task`), highlight (`==text==` → `<mark>`), superscript (`^text^` → `<sup>`), subscript (`~text~` → `<sub>`); split/source/preview módy, Word HTML paste cleanup, pluggable renderer cez `setRenderer()`.

Nové input komponenty: `b-tag-input` (freeform multi-value vstup s Enter-to-create a paste-split na oddeľovačoch `,`/`\n`/`\t`) a `b-segmented` (single-select segmented control pre 3–5 krátkych volieb).

### Birko.Web.Shell
Application shell framework s **trojvrstvovou hierarchiou** abstraktných tried:

- **`BCoreAppShell`** — abstraktné jadro: theme/layout persistence, online/offline tracking, user dropdown, brand link, breadcrumb listener, base CSS, default minimálny layout. Použiteľné priamo pre login stránky, error stránky, kiosky.
- **`BSidebarAppShell extends BCoreAppShell`** — opt-in **ľavý a/alebo pravý sidebar** cez `<b-sidebar>`. Oba môžu byť aktívne súčasne (Outlook-style: folder list vľavo + reading pane vpravo). Žiadne nové abstraktné metódy — sidebar plne opt-in cez gettery.
- **`BAppShell extends BSidebarAppShell`** — full Office-style ribbon shell: navigation tabs, notification bell, tenant switcher, status bar, command palette. Dedí sidebar capability — ribbon shell môže mať aj ľavé/pravé panely.

Plus factory funkcie: autentifikácia (createAuthStore), dynamické moduly (createModuleStore), tenant switching, command palette providers, route guards, page base classes (BaseListPage, BaseSplitPage, BaseDetailPage, BaseFormModal, BaseDashboardWidget).

Kanonický namespace kľúčov: **`bws.*`**. Shellov `t()` automaticky interpoluje `{entity}` s `entityLabel` aktuálnej CRUD stránky — jeden bundle entry typu `"bws.common.new": "Nový {entity}"` produkuje entitne-špecifické reťazce naprieč všetkými CRUD stránkami. Spätne-kompatibilné shims (`BForm.setTranslate`, `BDatePicker.setLocale`, atď.) sú zachované.

---

## Testovacie pokrytie

**59 testovacích projektov** (xUnit + FluentAssertions, každý má vlastný `CLAUDE.md`). Každý testovací projekt pokrýva zodpovedajúci produktový projekt rovnakým názvom (napr. `Birko.Data.Tests` testuje `Birko.Data.*` base abstrakcie). Avalonia skin (`Birko.Xaml.Avalonia.Tests`) beží headless + Skia a zachytáva parity screenshoty per téma.

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
| **Security** | 6 | ASP.NET Core (JWT, tenant middleware), BCrypt, Vault + Vault.Configuration, AzureKeyVault, NFC auth, OAuth2 authorization server |
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

### Agregačný projekt (odporúčaný vzor)

Odporúčaný vzor – vytvoriť **jeden alebo viac agregačných projektov** podľa potreby a includnúť do nich len tie `Birko.*` shared projekty, ktoré daná časť riešenia naozaj používa. Ostatné projekty potom referencujú agregátor(y) namiesto toho, aby každý nezávisle importoval `.projitems`. Každý `.projitems` sa tým skompiluje práve raz do jednej spoločnej assembly, čím predídete `CS0433`/`CS0436` type-clash chybám pri prekrývajúcich sa importoch.

**Koľko agregátorov?** Podľa tvaru riešenia:

- **Jeden agregátor** – `{YourSolution}.Birko` so všetkým. Najjednoduchšie, najmenej projektov. Hodí sa keď väčšina kódu používa väčšinu Birka, alebo keď ti nevadí "extra" tranzitívne závislosti. Symbio používa tento tvar (`Symbio.Birko`, ~90 importov).
- **Viac agregátorov podľa vrstvy / účelu** – rozdeľ ak rôzne časti riešenia potrebujú disjunktné podmnožiny Birka, najmä keď niektorá podmnožina ťahá ťažké závislosti (ML, kamera, hardware), ktoré nechceš mať vo všetkých projektoch. Príklady:
  - `{YourSolution}.Birko.Core` — Data, Models, Helpers, Security, Time
  - `{YourSolution}.Birko.Edge` — Communication.Hardware, Communication.Bluetooth, Communication.Modbus, Communication.Camera
  - `{YourSolution}.Birko.Ai` — AI.Contracts, AI, AI.Providers, AI.Agents
  - `{YourSolution}.Birko.Web` — Web.Core, Web.Components, Web.Shell (pre riešenia s UI vedľa neUI služieb)

Rozdelením držíš binárnu stopu downstream projektov tesnú: Edge collector neťahá AI providerov, backend API neťahá camera frame-capture knižnice.

```
# Jeden agregátor
YourSolution/
  YourSolution.Birko/          # Jediný .csproj importujúci všetky potrebné Birko.* .projitems
  YourSolution.Core/           # Referencuje YourSolution.Birko
  YourSolution.Web/            # Referencuje YourSolution.Birko

# Viac agregátorov
YourSolution/
  YourSolution.Birko.Core/     # Importuje Birko.Data.*, Birko.Models.*, Birko.Helpers, Birko.Security
  YourSolution.Birko.Edge/     # Importuje Birko.Communication.*
  YourSolution.Birko.Ai/       # Importuje Birko.AI.*
  YourSolution.Api/            # Referencuje YourSolution.Birko.Core + .Ai
  YourSolution.Edge.Service/   # Referencuje YourSolution.Birko.Core + .Edge
```

> **Pravidlo paláca:** keď neviete, začnite s **jedným** agregátorom. Rozdeľte ho až keď narazíte na konkrétnu bolesť — nafúknuté binárky, presakujúce tranzitívne referencie, alebo projekty čo pomaly buildia lebo ťahajú `.projitems` ktoré nepoužívajú. Predčasné rozdelenie je réžia bez výhody.

Reálne príklady: **Symbio.Birko** (jeden agregátor, ~90 shared projektov, veľká enterprise platforma) a **WebFinstatApiTester** (bez agregátora — app csproj rovno importuje lean podmnožinu ~10 projitems lebo projekt je malý a riziko prekrývajúcich sa importov je nulové).

### Cesta k Birko.Framework zdrojom – `$(BirkoSrc)` / `BIRKO_SRC`

Cesty v `Import Project` v `csproj` agregátora **nehardcoduj** ako `C:\Source\Birko.Helpers\…`. Namiesto toho použi MSBuild premennú `$(BirkoSrc)` definovanú v `Directory.Build.props` v koreni tvojho repa:

```xml
<!-- {YourSolution}/Directory.Build.props -->
<Project>
  <PropertyGroup>
    <BirkoSrc Condition="'$(BirkoSrc)' == '' and '$(BIRKO_SRC)' != ''">$(BIRKO_SRC)</BirkoSrc>
    <BirkoSrc Condition="'$(BirkoSrc)' == ''">$(MSBuildThisFileDirectory)..</BirkoSrc>
  </PropertyGroup>
</Project>
```

Resolution chain (od najvyššej priority):

| Zdroj | Použitie |
|---|---|
| **`/p:BirkoSrc=…`** CLI | Jednorazový override pri konkrétnom builde (porovnanie dvoch checkoutov) |
| **`BIRKO_SRC`** env var | CI runner, Docker build, alternatívne dev layouty (`D:\src`, `/home/foo/code`) |
| **Default** (parent adresára) | Lokálny dev so súrodencovými Birko.* repozitármi (`C:\Source\Birko.X` vedľa `C:\Source\YourSolution`) — bez konfigurácie |

Aggregator csproj potom všade používa portable cestu:

```xml
<Import Project="$(BirkoSrc)\Birko.Helpers\Birko.Helpers.projitems" Label="Shared" />
```

#### Frontend (TypeScript) — to isté `BIRKO_SRC`

`Birko.Web.Core` / `Birko.Web.Components` / `Birko.Web.Shell` sa konzumujú ako TypeScript zdroje cez esbuild alias map. Konvencia je rovnaká premenná, aby jeden override (`BIRKO_SRC`) kontroloval **aj** backend `dotnet build` **aj** frontend bundle — kľúčové pre Docker, kde všetko žije pod `/src/`:

```js
// build.js
const BIRKO_SRC = (process.env.BIRKO_SRC ?? 'C:/Source').replace(/\/+$/, '');
const aliases = {
  'birko-web-core':       `${BIRKO_SRC}/Birko.Web.Core/src/index.ts`,
  'birko-web-components': `${BIRKO_SRC}/Birko.Web.Components/src/index.ts`,
  'birko-web-shell':      `${BIRKO_SRC}/Birko.Web.Shell/src/index.ts`,
};
```

#### Docker príklad

```dockerfile
# Build context = parent tvojho repa (Birko.* + YourSolution sú súrodenci)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Birko.Helpers/    Birko.Helpers/
COPY Birko.Data.Core/  Birko.Data.Core/
# …
COPY YourSolution/     YourSolution/

ENV BIRKO_SRC=/src

WORKDIR /src/YourSolution
RUN dotnet publish src/Host/YourSolution.Api.csproj -c Release -o /app/publish
```

---

## Štatistiky

| Metrika | Hodnota |
|---|---:|
| **Produktové projekty celkom** | **168** |
| **z toho platformové rozšírenia** (🟨) | **~83** |
| **z toho core / feature projekty** (🟦 + 🟩) | **~85** |
| **Testovacie projekty** | **59** |
| **Projekty celkom** | **227** |
| Databázové platformy | 12 |
| LLM poskytovatelia | 11 |
| Web Components | 55 |
| Desktop / XAML UI projekty (Birko.Xaml) | 4 |
| XAML témy (light/dark/neon/finstat) | 4 |
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
- [docs/configuration.md](docs/configuration.md) – nastavenia (Settings hierarchy, provider-specific settings pre SQL/Cosmos/Raven/SQLite/Timescale, `ILoadable<T>` skladanie konfigurácie)
- [docs/migrations.md](docs/migrations.md) – platformovo-agnostické migrácie (`IMigration`, `IMigrationContext`, schémové abstrakcie)
- [docs/web.md](docs/web.md) – Web Components, Shell, **Internationalization** (`useI18n`, `onI18nChange`, `bwc.*`/`bws.*` namespaces)
- [docs/xaml.md](docs/xaml.md) – **Desktop / XAML UI** (Birko.Xaml): single-source design tokeny, Avalonia theme systém, controly, MVVM shell
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

*Prezentácia pre .NET 10.0 · Birko Framework · aktualizované 2026-07-06*
