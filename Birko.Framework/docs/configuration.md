# Configuration Guide

`Birko.Configuration` (namespace `Birko.Configuration`) provides a single inheritance chain for every store, repository, and migration runner across Birko.Framework. The chain layers shared concerns — file location, secrets, network credentials, SQL timeouts, provider-specific options — so each layer only adds what is genuinely new at that level.

Every concrete store consumes a typed settings descendant via `ISettingsStore<TSettings>` / `ISettingsRepository<TSettings>` (constructor injection — there are no static "default" settings). Pass the most specific class your provider needs.

## Hierarchy at a glance

```
ISettings (GetId)
  └── Settings (Location, Name)                         — file-based stores
        └── PasswordSettings (+ Password)               — file-based + crypto
              ├── SqLiteSettings (+ CommandTimeout, Path; GetConnectionString)
              └── RemoteSettings (+ UserName, Port, UseSecure)
                    ├── CosmosDB.Stores.Settings        (+ PartitionKeyPath, RequestTimeout, AllowBulkExecution; GetCosmosClientOptions)
                    ├── RavenDB.Stores.Settings         (+ RequestTimeout; CreateDocumentStore)
                    ├── MongoDB Settings                (already in Birko.Data.MongoDB — AuthDatabase, ReplicaSet, GetConnectionString)
                    ├── RedisSettings                   (in Birko.Redis — Database, KeyPrefix, GetConnectionString)
                    └── SqlSettings (+ CommandTimeout, ConnectionTimeout; abstract GetConnectionString)
                          ├── MSSqlSettings             (+ MultipleActiveResultSets, TrustServerCertificate)
                          ├── MySqlSettings             (+ BulkInsertBatchSize)
                          ├── PostgreSqlSettings        (+ UseBinaryImport)
                          └── TimescaleDBSettings       (+ TimeColumn, ChunkTimeInterval)
```

> **Why `SqLiteSettings` skips `RemoteSettings`** — SQLite is file-based; it needs a password (optional) but no username, port, or TLS. Extending `PasswordSettings` directly keeps the contract honest.

> **Why `TimescaleDBSettings` extends `SqlSettings`** — Timescale is a PostgreSQL extension; the connection string is PostgreSQL, only the hypertable concerns are extra.

## `ISettings` and `Settings.GetId()`

Every settings class produces a deterministic ID via `GetId()` — used as the cache key by store locators and migration runners. Each layer enriches the ID with the fields it adds:

| Class | `GetId()` |
|---|---|
| `Settings` | `"{Location}:{Name}"` |
| `RemoteSettings` | `"{Location}:{Name}:{UserName}:{Port}"` |
| `SqlSettings` | `"{Location}:{Port}:{Name}:{UserName}"` (Port-prioritised order matches connection string) |
| CosmosDB `Settings` | `"{Location}:{Name}:{UserName}"` (UserName = container name) |
| RavenDB `Settings` | `"{Location}:{Name}"` |

`ILoadable<T>` is the merge primitive: every subclass overrides `LoadFrom(Settings)` to copy fields from another instance of the same shape. Use it to layer config (`appsettings.json → environment overrides → secret store`) without re-instantiating.

## SQL — the typed `SqlSettings` family

Each SQL provider has a concrete `SqlSettings` descendant whose `GetConnectionString()` knows the dialect. Generic code that holds a base `SqlSettings` reference will get a `NotSupportedException` if it tries to call `GetConnectionString()` directly — the contract requires a provider-specific subclass.

```csharp
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;

var mssql = new MSSqlSettings("tcp:my-server", "MyDb", "sa", "S3cret!")
{
    Port = 1433,
    UseSecure = true,
    TrustServerCertificate = true,         // common for Azure SQL
    MultipleActiveResultSets = true,
    CommandTimeout = 60,
};

var mysql = new MySqlSettings("db.example", "myapp", "app", "S3cret!")
{
    Port = 3306,
    BulkInsertBatchSize = 5000,            // ≤ MySQL's 65535 param cap
};

var pg = new PostgreSqlSettings("db.example", "myapp", "app", "S3cret!")
{
    Port = 5432,
    UseBinaryImport = true,                // COPY protocol for bulk inserts
    UseSecure = true,                       // appends "SSL Mode=Require;"
};

var sqlite = new SqLiteSettings(@"C:\data", "myapp.db", password: null);
// sqlite.Path → C:\data\myapp.db (computed)
```

**Default ports / TLS** (from constructor defaults — override per environment):

| Provider | Port | UseSecure |
|---|---:|---|
| MSSql | 1433 | `true` |
| MySQL | 3306 | `false` |
| PostgreSQL | 5432 | `false` |

**Common SQL timeouts** (`SqlSettings`): `CommandTimeout = 30s`, `ConnectionTimeout = 15s`. PostgreSQL's connection string passes both; MSSql/MySQL pass `ConnectionTimeout` and rely on the ADO.NET command default for the command timeout (still readable via `settings.CommandTimeout` for downstream interceptors).

### TimescaleDB

Extends `SqlSettings` with hypertable defaults — the connection string is identical to PostgreSQL:

```csharp
using Birko.Data.SQL.TimescaleDB.Stores;

var ts = new TimescaleDBSettings("db.example", "metrics", "app", "S3cret!", port: 5432)
{
    TimeColumn = "ts",
    ChunkTimeInterval = "1 day",
};
```

## Document / NoSQL providers

### CosmosDB

`Birko.Data.CosmosDB.Stores.Settings` extends `RemoteSettings`. The remote fields are repurposed: `Location` → endpoint URL, `Name` → database, `Password` → account key, `UserName` → container name (optional — defaults to the entity type name).

```csharp
using Birko.Data.CosmosDB.Stores;

var cosmos = new Settings(
    location: "https://my-cosmos.documents.azure.com:443/",
    name:     "AppDb",
    password: accountKey,
    containerName: "Devices")
{
    PartitionKeyPath  = "/tenantId",
    RequestTimeout    = TimeSpan.FromSeconds(60),
    AllowBulkExecution = true,                       // SDK bulk mode
};

CosmosClientOptions opts = cosmos.GetCosmosClientOptions();
```

### RavenDB

`Birko.Data.RavenDB.Stores.Settings` adds `RequestTimeout` and a `CreateDocumentStore()` factory that wires the timeout into `DocumentConventions`:

```csharp
using Birko.Data.RavenDB.Stores;

var raven = new Settings(
    location: "https://raven.example:8080",
    name:     "AppDb",
    username: null,                                  // certificate auth not modelled here
    password: null)
{
    RequestTimeout = TimeSpan.FromSeconds(45),
};

IDocumentStore store = raven.CreateDocumentStore(); // already Initialize()d
```

### MongoDB

`Birko.Data.MongoDB.Stores.Settings` (pre-existing) extends `RemoteSettings` with `AuthDatabase`, `ReplicaSet`, and an explicit `GetConnectionString()` that produces `mongodb://...` URIs.

### Redis

`Birko.Redis.RedisSettings` extends `RemoteSettings` with `Database` (int) and `KeyPrefix` (string), and produces a StackExchange.Redis-compatible connection string. Used by `Birko.Caching.Redis`, `Birko.BackgroundJobs.Redis`, `Birko.MessageQueue.Redis`, and `Birko.Health.Redis`.

## Migrations

Each migration runner takes the store's native connector (not a `Settings` directly) — but the connector itself is built from typed settings, so the same provider hierarchy applies. Example:

```csharp
var pg = new PostgreSqlSettings("db.example", "myapp", "app", pwd) { Port = 5432 };
using var store = new PostgreSqlStore<Device>(pg);
var runner = new SqlMigrationRunner(store.Connector);
await runner.MigrateUpAsync(myMigrations);
```

See [docs/migrations.md](migrations.md) for the platform-agnostic migration framework that builds on top of these settings.

## BackgroundJobs / Workflow / View backends

The platform-specific BackgroundJobs and Workflow backends (`Birko.BackgroundJobs.SQL`, `Birko.Workflow.CosmosDB`, etc.) accept the same typed settings — no more passing `RemoteSettings` and reaching for static `PartitionKeyPath` properties. Example:

```csharp
var jobs = new SqlJobStore(new PostgreSqlSettings("db.example", "myapp", "app", pwd));
var wf   = new CosmosWorkflowStore(new Settings("https://...", "AppDb", accountKey));
```

## Loading & overriding (`ILoadable<T>`)

Use `LoadFrom` to layer configuration sources. Each subclass narrows the safest copy path:

```csharp
var base   = new PostgreSqlSettings("db.example", "myapp", "app", "");
var secret = new PostgreSqlSettings { Password = vault.Get("pg/myapp") };

base.LoadFrom(secret);   // copies only what's set on `secret`; preserves everything else
```

This is the recommended pattern when blending `appsettings.json`, environment variables, and a secret store (`Birko.Security.Vault`, `Birko.Security.AzureKeyVault`). The fluent flow always ends with a fully populated provider-specific instance — generic code can keep handling `SqlSettings`, while concrete stores still call the correct `GetConnectionString()` override.

## See also

- [Store Implementation Guide](store-implementation.md) — how stores consume `ISettingsStore<TSettings>`
- [Migrations](migrations.md) — runners and contexts that share the same settings
- [Security Guide](security.md) — `Birko.Security.Vault` / `AzureKeyVault` for secret-backed configuration
- [Dependencies Guide](dependencies.md) — full project dependency graph
