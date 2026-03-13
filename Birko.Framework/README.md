# Birko Framework

A modular .NET framework providing data access, communication, and model infrastructure for enterprise applications. Built on .NET 10.0 with shared projects via .projitems.

## Features

- Multi-database support (SQL Server, PostgreSQL, MySQL, SQLite, MongoDB, RavenDB, Elasticsearch, InfluxDB, TimescaleDB, JSON)
- Sync and async store/repository abstractions with bulk operation support
- ViewModel layer with model-to-viewmodel mapping
- Database migrations framework
- Data synchronization across stores
- Multi-tenancy support
- Event sourcing pattern
- Communication layer (REST, SOAP, WebSocket, SSE, Bluetooth, Hardware, Network)
- Domain model libraries (Product, Category, SEO, Accounting, Customers, Users, Warehouse)
- Fluent validation framework
- Caching with in-memory and Redis backends
- Security (password hashing, AES encryption, JWT tokens, RBAC)
- Message queue abstractions (pub/sub, point-to-point, serialization, retry, dead letter)
- Background job processing with pluggable persistent queues
- Data structures (trees, AVL trees, BST)
- Helper utilities and extensions

## Project Structure

### Core

| Project | Description |
|---------|-------------|
| Birko.Framework | Main framework application |
| Birko.Data | Core data interfaces (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore) |
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

### Data Features

| Project | Description |
|---------|-------------|
| Birko.Data.Patterns | Cross-cutting patterns (Unit of Work, Soft Delete, Audit, Paging) |
| Birko.Data.Migrations | Database migration framework (SQL, ES, MongoDB, RavenDB, InfluxDB, TimescaleDB) |
| Birko.Data.Sync | Data synchronization (SQL, ES, MongoDB, RavenDB, JSON, Tenant) |
| Birko.Data.Tenant | Multi-tenancy support |
| Birko.Data.EventSourcing | Event sourcing pattern |
| Birko.Data.SQL.View | SQL view generation |

### ViewModel Layer

| Project | Description |
|---------|-------------|
| Birko.Data.ViewModel | Base ViewModel repository abstractions |
| Birko.Data.SQL.ViewModel | SQL ViewModel repositories |
| Platform-specific ViewModel projects | ES, InfluxDB, JSON, MongoDB, RavenDB, TimescaleDB |

### Models

| Project | Description |
|---------|-------------|
| Birko.Models.Product | Product, variants, images, pricing |
| Birko.Models.Category | Categories with hierarchical tree support |
| Birko.Models.SEO | SEO metadata, URL aliases, sitemaps |
| Birko.Models.Accounting | Currency, Tax, PriceGroup, MeasureUnit |
| Birko.Models.Customers | Address, Customer, InvoiceAddress |
| Birko.Models.Users | User, Agenda, UserAgenda |
| Birko.Models.Warehouse | Items, variants, repositories, documents |

### Communication

| Project | Description |
|---------|-------------|
| Birko.Communication | Base communication interfaces |
| Birko.Communication.REST | REST API client |
| Birko.Communication.SOAP | SOAP client |
| Birko.Communication.WebSocket | WebSocket implementation |
| Birko.Communication.SSE | Server-Sent Events |
| Birko.Communication.Network | Network communication |
| Birko.Communication.Hardware | Hardware communication |
| Birko.Communication.Bluetooth | Bluetooth communication |

### Cross-Cutting

| Project | Description |
|---------|-------------|
| Birko.Validation | Fluent validation framework |
| Birko.Caching | In-memory caching with ICache interface |
| Birko.Caching.Redis | Redis-backed cache |
| Birko.Security | Password hashing, AES encryption, RBAC interfaces |
| Birko.Security.Jwt | JWT token provider |
| Birko.BackgroundJobs | Background job processing framework |
| Birko.BackgroundJobs.SQL | SQL-based persistent job queue |
| Birko.BackgroundJobs.ElasticSearch | Elasticsearch-based persistent job queue |
| Birko.BackgroundJobs.MongoDB | MongoDB-based persistent job queue |
| Birko.BackgroundJobs.RavenDB | RavenDB-based persistent job queue |
| Birko.BackgroundJobs.JSON | JSON file-based job queue (dev/testing) |
| Birko.BackgroundJobs.Redis | Redis-based persistent job queue |
| Birko.MessageQueue | Core message queue interfaces (pub/sub, point-to-point) |
| Birko.MessageQueue.InMemory | In-memory channel-based queue (testing/development) |
| Birko.MessageQueue.MQTT | MQTT implementation via MQTTnet (IoT, sensors, telemetry) |
| Birko.Structures | Tree data structures (AVL, BST) |
| Birko.Helpers | Utility and extension methods |

### Tests

| Project | Description |
|---------|-------------|
| Birko.Data.Tests | Core store abstraction tests |
| Birko.Data.SQL.Tests | SQL connector, strategy, and expression tests |
| Birko.Data.ElasticSearch.Tests | Elasticsearch expression tests |
| Birko.Helpers.Tests | Helper utility tests |
| Birko.Structures.Tests | Tree data structure tests |
| Birko.BackgroundJobs.Tests | Background job processing tests |

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
- [Security Guide](docs/security.md)
- [Background Jobs Guide](docs/background-jobs.md)
- [Message Queue Guide](docs/message-queue.md)
- [Event Sourcing Guide](docs/event-sourcing.md)
- [Data Synchronization Guide](docs/sync.md)
- [Multi-Tenancy Guide](docs/tenant.md)

## License

Part of the Birko Framework.
