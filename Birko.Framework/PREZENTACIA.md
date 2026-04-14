# Birko Framework
## Modulárny .NET framework pre podnikové aplikácie

---

## Prehľad

Birko Framework je komplexný .NET 10.0 framework poskytujúci:

- **Univerzálny prístup k dátam** – viac ako 10 databázových platform
- **Komunikačnú vrstvu** – REST, WebSocket, Modbus, OAuth, NFC a ďalšie
- **AI/LLM infraštruktúru** – multi-provider agent framework
- **Bezpečnosť** – šifrovanie, JWT, RBAC, Vault integrácia
- **Integrácie** – message queue, event bus, background jobs

---

## Kľúčové vlastnosti

### 🗄️ Vrstva prístupu k dátam
- **SQL**: SQL Server, PostgreSQL, MySQL, SQLite
- **NoSQL**: MongoDB, RavenDB, Elasticsearch, Cosmos DB
- **Time-series**: InfluxDB, TimescaleDB
- **Súbory**: JSON (System.Text.Json), XML (System.Xml.Serialization)
- **Repository & Store pattern** s lazy-init a bulk operáciami

### 🔄 Vzory a funkcionality
- **Migrácie** – automatické vytváranie tabuliek a indexov
- **Synchronizácia** – viacplatformová sync medzi úložiskami
- **Multi-tenancy** – izolácia dát pre viac klientov
- **Event Sourcing** – event-driven architektúra
- **CQRS** – oddelenie čítania a zápisu
- **Workflow Engine** – stavové stroje s guardami a akciami

### 🌐 Komunikácia
- **REST/SOAP** – API klienti a server
- **WebSocket/SSE** – real-time komunikácia
- **Modbus** – priemyselná komunikácia (RTU/TCP)
- **OAuth 2.0** – Client Credentials, Auth Code, PKCE, Device Code
- **NFC/RFID** – čítanie tagov (ISO 14443A, NDEF)
- **Camera/IR** – multimédia a infračervená komunikácia

### 🤖 AI / LLM (nové!)
- **11 poskytovateľov**: Claude, OpenAI, Azure OpenAI, Gemini, Ollama, atď.
- **Agenti**: CodingAgent, 10 jazykových agentov, 4 task agenti
- **Resilience**: Rate limiting, circuit breaker, cost tracking
- **Orchestration**: task dispatcher, parallel execution, escalation

### 🔒 Bezpečnosť
- **Hashovanie**: PBKDF2, BCrypt (pure C#)
- **Šifrovanie**: AES-256-GCM
- **Autentizácia**: JWT, NFC-based auth
- **Secret Management**: HashiCorp Vault, Azure Key Vault
- **ASP.NET Core** integrácia – RBAC, permissions, tenants

### 📦 Infraštruktúra
| Komponent | Implementácie |
|-----------|---------------|
| **Caching** | In-memory, Redis, Hybrid (L1+L2) |
| **Message Queue** | InMemory, MQTT, Redis |
| **Event Bus** | In-process, Distributed, Outbox |
| **Background Jobs** | SQL, ES, MongoDB, RavenDB, JSON, Redis, CosmosDB |
| **Health Checks** | SQL, NoSQL, Redis, Azure, MQTT, SMTP, WebSocket |
| **Telemetry** | System.Diagnostics.Metrics, OpenTelemetry |

### 🌍 Ďalšie funkcie
- **Validácia** – fluent validation framework
- **Localization** – CLDR pluralizácia, JSON/RESX/DB poskytovatelia
- **Messaging** – Email (SMTP), SMS, Push, Razor templates
- **Storage** – lokálny súborový systém, Azure Blob Storage
- **Rules Engine** – dátou riadené pravidlá
- **Data Processors** – XML, CSV, HTTP, ZIP
- **Tagging** – entity tagging s polymorphic junction
- **Views** – fluent view builder pre cross-platform projections
- **Data Structures** – stromy, grafy, heap, tries, LRU cache, Bloom filter
- **Random & Sequences** – RNG algoritmy, distribúcie, GuidV4/V7, NanoId, Snowflake
- **Serialization** – System JSON/XML, Newtonsoft, MessagePack, Protobuf
- **CQRS** – Command/Query Separation s Mediator pattern
- **Workflow** – stavové stroje s guardami a akciami
- **Helpers** – PathHelper, StringHelper, ConvertHelper

---

## Architektúra

### Celková štruktúra frameworku

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BIRKO FRAMEWORK                                    │
│                        Modular .NET 10.0 Architecture                        │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          CORE CONTRACTS (zero deps)                          │
├─────────────────────────────────────────────────────────────────────────────┤
│  Birko.Contracts      │  ILoadable, ICopyable, IGuidEntity, RetryPolicy    │
│  Birko.Models.Contracts│  ICatalogItem, IPriceable, IVariantable...        │
│  Birko.Time.Abstractions│  IDateTimeProvider, SystemDateTimeProvider       │
│  Birko.AI.Contracts   │  ILlmProvider, Message, Tool, AgentOptions         │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                    ┌─────────────────┴─────────────────┐
                    │           (závislosti)            │
    ┌───────────┬───┴───┬───────┬───────┬───────┬───────┼───────┬───────┬───────┐
    ▼           ▼       ▼       ▼       ▼       ▼       ▼       ▼       ▼       ▼
┌─────────┐ ┌─────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐
│  DATA   │ │ AI/ │ │ COMM │ │INFRA │ │MODEL │ │SECU- ││ WEB  │ │TIME  │ │ CONF │
│  LAYER  │ │ LLM │ │      │ │      │ │      │ │RITY  │ │ (TS) │ │      │ │ IG   │
├─────────┤ ├─────┤ ├──────┤ ├──────┤ ├──────┤ ├──────┤ ├──────┤ ├──────┤ ├──────┤
│Stores10+│ │Prov │ │REST  │ │Cache ││Custo-││Hash- ││Core  │ │Calen│ │Set-  │
│Repos   │ │Agnt │ │WS    │ │MsgQ  ││mers  ││ing   ││Comp. │ │dar   │ │tings │
│UnitOfW │ │Tool │ │Modbus│ │Event ││Users ││Encry ││Shell │ │WorkH │ │      │
│Migrat. │ │Resi │ │OAuth │ │BgJob ││Inven ││JWT   ││      │ │TimeZ │ │      │
│Sync    │ │Orch │ │NFC   │ │Health││Prici ││Vault ││      │ │Local │ │      │
│EventSrc│ │     │ │Cam/IR│ │Tele  ││Value ││RBAC  ││      │ │      │ │      │
│Views   │ │     │ │      │ │Valid ││SQLMap││ASP.N ││      │ │      │ │      │
│Tenant  │ │     │ │      │ │Rules ││Prod ││      ││      │ │      │ │      │
│Tagging │ │     │ │      │ │CQRS  ││Cat  ││      ││      │ │      │ │      │
│Pattern │ │     │ │      │ │Workfl││SEO  ││      ││      │ │      │ │      │
│Process.│ │     │ │      │ │Struct││      ││      ││      │ │      │ │      │
│Aggreg. │ │     │ │      │ │Random││      ││      ││      │ │      │ │      │
│Composition│    │ │      │ │Serial││      ││      ││      │ │      │ │      │
└─────────┘ └─────┘ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘

│           │       │       │       │       │       │       │       │       │
│           │       │       └───────┴───────┴───────┴───────┴───────┴───────┤
│           │       │               ĎALŠIE OBLASTI                          │
│           │       │       ┌──────────┬──────────┬──────────┬──────────┐   │
│           ▼       ▼       ▼          ▼          ▼          ▼          ▼   │
│       ┌─────────┐┌─────────┐┌─────────┐┌─────────┐┌─────────┐┌─────────┐│
│       │Messaging││Storage  ││Serializ ││Help ers ││Tests    ││Examp les││
│       ├─────────┤├─────────┤├─────────┤├─────────┤├─────────┤├─────────┤│
│       │SMTP     ││Local    ││Newton   ││Slug     ││40+ test ││Demo app ││
│       │SMS      ││AzureBlob││MessageP ││Path     ││projects ││         ││
│       │Push     ││         ││Protobuf ││         ││         ││         ││
│       │Razor    ││         ││         ││         ││         ││         ││
│       └─────────┘└─────────┘└─────────┘└─────────┘└─────────┘└─────────┘┘

     Nezávislé na sebe – všetky závisia len od Contracts, nie medzi sebou
```

### Store Hierarchy (Template Method Pattern)
```
AbstractStore → AbstractBulkStore (sync)
AbstractAsyncStore → AbstractAsyncBulkStore (async)
```

**Lazy-init**: CRUD metódy automaticky volajú `Init()`/`InitAsync()` pred prvým použitím.

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

#### 1. Core Contracts (nulové závislosti)
```
Birko.Contracts/
├── ILoadable, ICopyable, IDefault
├── ITimestamped, IGuidEntity, ILogEntity
├── RetryPolicy (s backoff a jitter)
└── Zero external dependencies
```

#### 2. Data Layer (10+ platform)
```
Birko.Data.Core/              → AbstractModel, ViewModels, Filters
Birko.Data.Stores/            → IStore, IAsyncStore, IBulkStore
Birko.Data.Repositories/      → IRepository, IAsyncRepository
Birko.Data.SqlServer/         → SQL Server implementácia
Birko.Data.PostgreSQL/        → PostgreSQL implementácia
Birko.Data.MySql/             → MySQL implementácia
Birko.Data.MongoDB/           → MongoDB implementácia
Birko.Data.RavenDB/           → RavenDB implementácia
Birko.Data.ElasticSearch/     → ElasticSearch implementácia
Birko.Data.CosmosDB/          → Azure Cosmos DB implementácia
Birko.Data.InfluxDB/          → InfluxDB implementácia
Birko.Data.TimescaleDB/       → TimescaleDB implementácia
Birko.Data.Json/              → JSON súborový store (System.Text.Json)
Birko.Data.XML/               → XML súborový store (System.Xml.Serialization)
Birko.Data.Migrations/        → Migrácie framework
Birko.Data.Sync/              → Sync framework
Birko.Data.Patterns/          → StoreWrapperBuilder (decoratory)
Birko.Data.Views/             → ViewDefinitionBuilder
```

#### 3. Models (Value Objects + Domain)
```
Birko.Models.Contracts/       → ICatalogItem, IPriceable, IVariantable...
Birko.Models/                 → Money, Percentage, PostalAddress, Quantity
Birko.Models.Customers/       → Address, Customer, InvoiceAddress
Birko.Models.Users/           → User, Role, Permission
Birko.Models.Inventory/       → StockItem, StorageLocation
Birko.Models.Pricing/         → Currency, Tax, PriceList, Discount
Birko.Models.SQL/             → ModelMap<T>, ModelMapRegistry
```

#### 4. AI / LLM (nové!)
```
Birko.AI.Contracts/          → ILlmProvider, Message, Tool, AgentOptions
Birko.AI/                    → LlmProviderBase, Agent, AgentFactory
Birko.AI.Providers/          → 11 LLM providerov
Birko.AI.Agents/             → CodingAgent, language agents, task agents
Birko.AI.Resilience/         → RateLimiter, CircuitBreaker, CostTracking
Birko.AI.Orchestration/      → TaskDispatcher, parallel execution
```

#### 5. Communication
```
Birko.Communication.Core/    → REST/SOAP klienti
Birko.Communication.WebSocket/→ WebSocket, SSE
Birko.Communication.Modbus/   → RTU/TCP priemyselná komunikácia
Birko.Communication.OAuth/    → OAuth 2.0 + PKCE + Device Flow
Birko.Communication.NFC/      → NFC/RFID čítanie
Birko.Communication.Camera/   → Camera, IR multimédia
```

#### 6. Infraštruktúra
```
Birko.Caching/               → In-memory, Redis, Hybrid
Birko.MessageQueue/          → InMemory, MQTT, Redis
Birko.EventBus/              → In-process, Distributed, Outbox
Birko.BackgroundJobs/        → SQL, ES, MongoDB, RavenDB, JSON, Redis
Birko.Health/                → Health Checks pre všetky platformy
Birko.Telemetry/             → OpenTelemetry integrácia
Birko.Validation/            → Fluent validation
Birko.Localization/          → CLDR pluralizácia
Birko.Messaging/             → Email, SMS, Push, Razor templates
Birko.Storage/               → Lokálny FS, Azure Blob
Birko.Rules/                 → Rules engine
Birko.Workflow/              → Workflow engine
Birko.Structures/            → Trees, AVL, Interval Tree, Graphs, Heaps, Tries, LRU, Bloom Filter
Birko.Random/                → RNG (System, Crypto, XorShift, MersenneTwister), Distribúcie, GuidV4/V7, NanoId, Snowflake
Birko.Serialization/         → ISerializer, SystemJson, SystemXml
Birko.Serialization.Newtonsoft/ → Newtonsoft JSON
Birko.Serialization.MessagePack/ → MessagePack
Birko.Serialization.Protobuf/   → Protobuf
Birko.Helpers/               → PathHelper, StringHelper, ConvertHelper
Birko.CQRS/                  → Command/Query, IRequestHandler, IPipelineBehavior, IMediator
```

#### 7. Bezpečnosť
```
Birko.Security/              → PBKDF2, BCrypt (pure C#)
Birko.Security.Cryptography/ → AES-256-GCM šifrovanie
Birko.Security.JWT/          → JWT tokeny
Birko.Secrets/               → HashiCorp Vault, Azure Key Vault
Birko.Security.AspNetCore/   → RBAC, permissions, tenants
```

#### 8. Web (TypeScript)
```
Birko.Web.Core/              → Shadow DOM, Signal/Store, Router
Birko.Web.Components/        → 38 komponentov
Birko.Web.Shell/             → Application shell framework
```

### Detailné popisy špeciálných projektov

#### 🏗️ Birko.Structures – Dátové štruktúry
Komplexná knižnica dátových štruktúr pre .NET:
- **Stromy**: Binary Tree, AVL Tree (self-balancing), Red-Black Tree
- **Grafy**: Directed/Undirected Graph, BFS/DFS prehľadávanie, najkratšia cesta
- **Heaps**: Binary Heap, Min/Max Heap operácie
- **Tries**: Prefix trie pre string vyhľadávanie
- **Caching**: LRU Cache (Least Recently Used)
- **Filters**: Bloom Filter (priestorovo efektívny membership test)
- **Ďalšie**: Interval Tree, Ring Buffer, Disjoint Set, Skip List, Deque

#### 🎲 Birko.Random – Generátory náhodných čísel
Pluggable RNG framework s viacero algoritmami:
- **Generátory**: SystemRandom, CryptoRandom, XorShift, MersenneTwister, SplitMix, TestRandom (deterministický pre testy)
- **Distribúcie**: Uniform, Normal (Gaussian), Exponential, Poisson
- **Sekvencie**: GuidV4/V7, NanoId (URL-friendly), Snowflake (distributed IDs), tokeny
- **Noise**: Perlin noise, Simplex noise pre procedurálnu generáciu

#### 📦 Birko.Serialization – Serializácia
Unifikovaný interface pre viaceré serializačné formáty:
- **Abstrakcia**: `ISerializer` – zameniteľné implementácie
- **System**: SystemJsonSerializer, SystemXmlSerializer (built-in)
- **Newtonsoft**: Newtonsoft JSON serializer
- **MessagePack**: Binary serializácia (rýchla a kompaktná)
- **Protobuf**: Protocol Buffers serializácia

**Poznámka**: `Birko.Data.JSON` a `Birko.Data.XML` používajú priamo
`System.Text.Json` a `System.Xml.Serialization`, nie túto abstrakciu.

#### 📋 Birko.CQRS – Command/Query Separation
Implementácia CQRS patternu s Mediator:
- **Patterny**: Command (zápis), Query (čítanie), Request/Response
- **Rozhrania**: `ICommand`, `IQuery`, `IRequestHandler<TRequest, TResponse>`
- **Pipeline**: `IPipelineBehavior<T>` – logging, validation, transactions
- **Mediator**: `IMediator` – centralný dispatcher pre commands/queries
- **Benefits**: oddelenie zápisu/čítania, lepšia škálovateľnosť, čistejší kód

#### ⚙️ Birko.Workflow – Workflow Engine
State machine engine pre business procesy:
- **Builder**: Fluent API pre definovanie workflow
- **Stavy**: States, transitions, guards (podmienky), actions (akcie)
- **Engine**: Spustenie workflow, validácia transition
- **Visualizácia**: Mermaid, DOT export pre diagramy
- **Persistence**: SQL, ElasticSearch, MongoDB, RavenDB, JSON, CosmosDB

#### 📏 Birko.Rules – Rules Engine
Dátou riadený rule engine:
- **Pravidlá**: `IRule`, `IRule<T>` – vyhodnotiteľné pravidlá
- **Grouping**: `RuleGroup`, `RuleSet` – organizácia pravidiel
- **Evaluator**: `RuleEvaluator` – vyhodnocovanie s podmienkami (AND/OR)
- **Use Cases**: validácia, business logika, dynamické rozhodovanie

#### 🛠️ Birko.Helpers – Pomocné funkcie
Utility funkcie pre bežné úlohy:
- **PathHelper**: IsPathSafe, IsUnderDirectory, GetCanonicalPath
- **StringHelper**: formátovanie, porovnávanie, transformácie
- **ConvertHelper**: bezpečná konverzia typov

---

## Modely

### Domain Contracts
- `ICatalogItem`, `IPriceable`, `IVariantable`, `ICategorizeable`
- `IBatchable`, `ILocatable`, `IHierarchical`
- `IDocument`, `IContactable`, `IAddressable`

### Value Objects
- `Money`, `MoneyWithTax`, `Percentage`, `PostalAddress`, `Quantity`

### Domain Models
- **Customers**: Address, Customer, InvoiceAddress
- **Users**: User, UserLogin, UserProfile, RBAC (Role, Permission)
- **Inventory**: StockItem, StorageLocation, InventoryDocument
- **Pricing**: Currency, Tax, PriceList, Discount

---

## Web Components (TypeScript)

### Birko.Web.Core
- Shadow DOM komponenty
- Reaktívny state (Signal/Store)
- HTTP/SSE klienti
- Hash router

### Birko.Web.Components
38 komponentov: inputs, layout, data, feedback, navigation

### Birko.Web.Shell
Application shell framework – auth, modules, command palette, notifications

---

## Použitie v konzumerských riešeniach

Odporúčaný vzor:

```
YourSolution/
  YourSolution.Birko/          # Agregátor všetkých Birko.* projektov
  YourSolution.Core/           # Referencuje YourSolution.Birko
  YourSolution.Web/            # Referencuje YourSolution.Birko
```

Vytvorte jeden **agregačný projekt** `{YourSolution}.Birko` (napr. `FisData.Birko`) a importujte všetky potrebné `Birko.*` shared projekty. Vaše ostatné projekty potom referencujú iba tento jeden agregačný projekt.

---

## Štatistiky

- **100+ projektov** v rámci frameworku
- **40+ testovacích projektov** (xUnit + FluentAssertions)
- **11 databázových platform**
- **11 LLM poskytovateľov**
- **38 Web Components**
- **0 dependencies** pre core contracts

---

## Dokumentácia

- [README.md](README.md) – kompletná dokumentácia
- [CLAUDE-projects.md](CLAUDE-projects.md) – katalóg projektov
- [docs/](docs/) – detailné guide pre každú oblasť
- [TODO.md](TODO.md) – roadmap

---

## Quick Start

```bash
# Klonovanie a build
git clone <repository>
cd Birko.Framework
dotnet build Birko.Framework.slnx

# Spustenie testov
dotnet test
```

---

## Licencia

Súčasť frameworku Birko Framework.

---

*Prezentácia vytvorená pre .NET 10.0*
