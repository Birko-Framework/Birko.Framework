# Consumer Projects

Birko Framework is consumed by several projects via `.projitems` shared project imports. This document tracks which Birko components each consumer uses.

---

## Symbio

**Location:** `C:\Source\Symbio`
**Description:** IoT-capable, multi-tenant enterprise platform
**Birko projects referenced:** 50

### Symbio.Domain.Shared (16 projects)
Core domain layer shared across all Symbio modules.

| Project | Purpose |
|---------|---------|
| Birko.Data.Core | Models, ViewModels, Filters, Exceptions |
| Birko.Data.Stores | Store interfaces and abstractions |
| Birko.Data.Repositories | Repository interfaces and abstractions |
| Birko.Data.SQL | SQL base classes |
| Birko.Data.SQL.View | SQL view generation |
| Birko.Data.Patterns | Unit of Work, Soft Delete, Audit, Paging |
| Birko.Data.Tenant | Multi-tenancy support |
| Birko.Helpers | String, HTML, Object, Enumerable utilities |
| Birko.Models.Contracts | Domain interfaces (ICatalogItem, IPriceable, IHierarchical, etc.) |
| Birko.Models | Base models (AbstractPercentage, AbstractTree, ValueData) + Value Objects |
| Birko.Models.Accounting | Currency, Tax, PriceGroup, MeasureUnit |
| Birko.Models.Users | User, Role, RBAC, Agenda |
| Birko.Models.Customers | Address, Customer, InvoiceAddress |
| Birko.Rules | Data-driven rule engine |
| Birko.Serialization | JSON/XML serialization abstraction |
| Birko.Time | Time zones, business calendar, working hours |

### Symbio.DataAccess (19 projects)
Database provider implementations and migrations.

| Project | Purpose |
|---------|---------|
| Birko.Data.SQL.PostgreSQL | PostgreSQL store/repository |
| Birko.Data.SQL.MSSql | SQL Server store/repository |
| Birko.Data.SQL.SqLite | SQLite store/repository |
| Birko.Data.MongoDB | MongoDB store/repository |
| Birko.Data.TimescaleDB | TimescaleDB time-series store |
| Birko.Data.RavenDB | RavenDB document store |
| Birko.Data.ElasticSearch | Elasticsearch store/repository |
| Birko.Data.ViewModel | Base ViewModel repository |
| Birko.Data.SQL.ViewModel | SQL ViewModel repository |
| Birko.Data.Migrations | Migration framework core |
| Birko.Data.Migrations.SQL | SQL migration runner |
| + shared | Serialization, Patterns, Helpers, SQL, SQL.View |

### Symbio.Infrastructure (20 projects)
Cross-cutting infrastructure: security, messaging, caching, telemetry.

| Project | Purpose |
|---------|---------|
| Birko.Communication | Base communication interfaces |
| Birko.Communication.WebSocket | WebSocket implementation |
| Birko.Communication.SSE | Server-Sent Events |
| Birko.Security | Password hashing, AES-256-GCM, RBAC |
| Birko.Security.Jwt | JWT token provider |
| Birko.Security.AspNetCore | JWT auth, claims, tenant resolution |
| Birko.Data.EventSourcing | Event sourcing pattern |
| Birko.Caching | ICache with MemoryCache |
| Birko.Validation | Fluent validation framework |
| Birko.BackgroundJobs | Job queue and scheduler |
| Birko.MessageQueue | Message queue core interfaces |
| Birko.EventBus | In-process event bus |
| Birko.EventBus.Outbox | Transactional outbox pattern |
| Birko.Messaging | Email, SMS, Push, templates |
| Birko.Storage | File/blob storage abstraction |
| Birko.Telemetry | Metrics, tracing, correlation |
| Birko.Health | Health check framework |
| Birko.Health.Data | Database health checks |
| + shared | Data.Tenant, Serialization, Helpers |

### Symbio.Edge.IoT (6 projects)
Edge device communication for IoT sensors and hardware.

| Project | Purpose |
|---------|---------|
| Birko.Communication.Network | TCP/UDP networking |
| Birko.Communication.Hardware | Serial/GPIO communication |
| Birko.Communication.Bluetooth | Bluetooth communication |
| Birko.Communication.Modbus | Modbus RTU/TCP (industrial) |
| Birko.Communication.Camera | FFmpeg-based frame capture |
| + shared | Communication base |

### Symbio.Module.IoT (2 projects)
IoT module with MQTT messaging.

| Project | Purpose |
|---------|---------|
| Birko.MessageQueue.MQTT | MQTT broker integration |
| + shared | Communication, Network, Hardware, Bluetooth |

### Symbio.Module.Warehouse (3 projects)
Warehouse/inventory module with clean Birko model projects.

| Project | Purpose |
|---------|---------|
| Birko.Models.Inventory | StockItem, StorageLocation, InventoryDocument (clean, no SQL attrs) |
| Birko.Models.Pricing | Currency, Tax, PriceGroup, PriceList, Discount (clean, no SQL attrs) |
| Birko.Models.SQL | Fluent SQL mapping framework |

### Symbio-specific features (not in Birko scope)
- Module discovery/registration (IModule, ModuleRegistrar, dependency graph)
- Unified real-time notifier (SSE + WebSocket combined, tenant-aware)
- Time-series store abstraction (generic over TimescaleDB)
- Module-aware migration runner (topological dependency sorting)
- SQL dialect abstraction (PostgreSqlDialect, MsSqlDialect)

---

## DraCode

**Location:** `C:\Source\DraCode`
**Description:** Game/application platform using SQLite, WebSocket real-time, and event sourcing
**Birko projects referenced:** 26

### DraCode.KoboldLair (19 projects)
Main application with SQLite data layer and event sourcing.

| Project | Purpose |
|---------|---------|
| Birko.Data.Core | Models, ViewModels, Filters |
| Birko.Data.Stores | Store interfaces |
| Birko.Data.Repositories | Repository interfaces |
| Birko.Data.SQL | SQL base classes |
| Birko.Data.SQL.View | SQL view generation |
| Birko.Data.SQL.SqLite | SQLite provider |
| Birko.Data.SQL.ViewModel | SQL ViewModel repository |
| Birko.Data.ViewModel | Base ViewModel repository |
| Birko.Data.Patterns | Unit of Work, Soft Delete, Audit |
| Birko.Data.EventSourcing | Event sourcing pattern |
| Birko.Caching | In-memory caching |
| Birko.EventBus | In-process event bus |
| Birko.Helpers | Utility helpers |
| Birko.Models.Contracts | Domain interfaces |
| Birko.Models | Base models |
| Birko.Rules | Rule engine |
| Birko.Serialization | Serialization abstraction |
| Birko.Time | Time utilities |
| Birko.MessageQueue | Message queue core |
| Birko.Validation | Fluent validation |

### DraCode.KoboldLair.Server (7 projects)
Server-side with WebSocket/SSE real-time and security.

| Project | Purpose |
|---------|---------|
| Birko.Communication | Base communication |
| Birko.Communication.WebSocket | WebSocket real-time |
| Birko.Communication.SSE | Server-Sent Events |
| Birko.Security | Password hashing, encryption |
| Birko.Security.Jwt | JWT authentication |
| Birko.BackgroundJobs | Background job processing |
| Birko.MessageQueue.InMemory | In-process message queue |

### DraCode.WebSocket (6 projects)
Standalone WebSocket client library.

| Project | Purpose |
|---------|---------|
| Birko.Communication | Base communication |
| Birko.Communication.WebSocket | WebSocket implementation |
| Birko.Security | Encryption utilities |
| Birko.EventBus | Event dispatching |
| Birko.Time | Time utilities |
| Birko.Validation | Input validation |

> **Note:** DraCode.WebSocket excludes Birko.Validation store wrappers and Birko.EventBus RuleFilterBehavior since it doesn't import Birko.Data or Birko.Rules.

---

## Affiliate

**Location:** `C:\Source\Affiliate`
**Description:** Product/category aggregation platform using Elasticsearch and InfluxDB
**Birko projects referenced:** 22

### Affiliate (18 projects)
Main application with Elasticsearch data layer and caching.

| Project | Purpose |
|---------|---------|
| Birko.Data.Core | Models, ViewModels, Filters |
| Birko.Data.Stores | Store interfaces |
| Birko.Data.Repositories | Repository interfaces |
| Birko.Data.Patterns | Unit of Work, decorators |
| Birko.Data.ElasticSearch | Elasticsearch store/repository |
| Birko.Data.InfluxDB | InfluxDB time-series store |
| Birko.Data.ViewModel | Base ViewModel repository |
| Birko.Data.ElasticSearch.ViewModel | ES ViewModel repository |
| Birko.Data.InfluxDB.ViewModel | InfluxDB ViewModel repository |
| Birko.Caching | In-memory caching |
| Birko.Helpers | Utility helpers |
| Birko.Models.Contracts | Domain interfaces |
| Birko.Models | Base models |
| Birko.Models.Category | Category models |
| Birko.Models.Product | Product models |
| Birko.Models.SEO | SEO models |
| Birko.Rules | Rule engine |
| Birko.Serialization | Serialization abstraction |
| Birko.Time | Time utilities |

### Affiliate.Import (21 projects)
Data import pipeline with JSON and CSV processing.

| Project | Purpose |
|---------|---------|
| *(all from Affiliate above, minus Birko.Caching, plus:)* | |
| Birko.Structures | Tree data structures |
| Birko.Data.JSON | JSON file-based storage |
| Birko.Data.Processors | CSV/XML/HTTP/ZIP stream processors |

---

## FisData.Stock

**Location:** `C:\Source\FisData.Stock`
**Description:** Stock/inventory management (currently inactive — models extracted to Birko.Models.*)
**Birko projects referenced:** 0 (empty solution)

FisData.Stock.Core models were refactored in March 2026 to extend Birko.Models.* equivalents. The solution is currently empty pending reconfiguration.

**Models extracted to Birko Framework:**
- Birko.Models.Accounting (Currency, Tax, PriceGroup, MeasureUnit)
- Birko.Models.Customers (Address, Customer, InvoiceAddress)
- Birko.Models.Users (User, Agenda, UserAgenda)
- Birko.Models.Warehouse (Item, ItemVariant, Repository, WareHouseDocument)

**FisData-specific models** (not in Birko): UserAuthentication, CustomerInvoiceAddress, Property, ItemRepositoryInventory/Movement variants.

---

## Summary

| Consumer | Birko Projects | Primary Data Store | Key Features Used |
|----------|---------------|-------------------|-------------------|
| Symbio | 54 | PostgreSQL, MSSql, MongoDB, TimescaleDB, RavenDB, ES | Full stack: IoT, multi-tenant, event sourcing, health, telemetry |
| DraCode | 27 | SQLite | WebSocket real-time, event sourcing, in-memory messaging |
| Affiliate | 23 | Elasticsearch, InfluxDB | Product aggregation, data import/processing |
| FisData.Stock | 0 | *(inactive)* | Models extracted to Birko.Models.* |
