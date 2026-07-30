---
area: settings-configuration-chain
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Configuration/Settings.cs
  - ../Birko.Data.CosmosDB/Stores/Settings.cs
  - ../Birko.Data.ElasticSearch/Stores/Settings.cs
  - ../Birko.Data.InfluxDB/Stores/Settings.cs
  - ../Birko.Data.MongoDB/Stores/Settings.cs
  - ../Birko.Data.RavenDB/Stores/Settings.cs
  - ../Birko.Data.SQL.MSSql/Stores/MSSqlSettings.cs
  - ../Birko.Data.SQL.MySQL/Stores/MySqlSettings.cs
  - ../Birko.Data.SQL.PostgreSQL/Stores/PostgreSqlSettings.cs
  - ../Birko.Data.SQL.SqLite/Stores/SqLiteSettings.cs
  - ../Birko.Data.SQL/Stores/SqlSettings.cs
  - ../Birko.Data.TimescaleDB/Stores/Settings.cs
  - ../Birko.Redis/RedisSettings.cs
shaped-by: []
---

# Settings inheritance chain and connection-string contract

## Purpose

Every Birko store, migration runner, job queue and cache backend is configured by a *settings*
object. All of them descend from one small class in `Birko.Configuration` — `Settings` (a directory
`Location` plus a `Name`) — which is progressively extended: `PasswordSettings` adds a secret,
`RemoteSettings` adds a username, a port and a "use TLS" flag, and each backend then adds its own
dialect-specific knobs. Two things are asked of every settings object: it must be able to produce a
stable **identity string** (`GetId()`), which the store locators use as a cache key so that two
requests for the same configuration share one store instance; and it must be able to **copy itself
from another settings instance** (`LoadFrom`), which is how configuration read from one place is
folded into a pre-existing instance. Provider subclasses additionally know how to turn their fields
into whatever their driver needs — a connection string, a `CosmosClientOptions`, or a fully
initialized RavenDB `DocumentStore`.

This document records what the thirteen settings classes in the family actually do. They do not all
do the same thing: the identity format changes shape three times down the chain, connection-string
escaping is implemented on exactly one provider, and three classes never override the copy hook at
all, so their own fields are silently dropped when copied through a base-typed reference.

## Requirements

### Requirement: Settings identity is a colon-joined projection of the connection-defining fields

The system SHALL expose `ISettings.GetId()` on every settings class, returning a string built by
joining the fields that identify a connection with `:`. `Birko.Configuration.Settings.GetId()` SHALL
return `"{Location}:{Name}"`. `RemoteSettings.GetId()` SHALL return `base.GetId()` with `UserName`
and `Port` appended, i.e. `"{Location}:{Name}:{UserName}:{Port}"`.

#### Scenario: Base settings identity

- **Given** a `Birko.Configuration.Settings` with `Location = "C:\\data"` and `Name = "items.json"`
- **When** `GetId()` is called
- **Then** it returns `"C:\data:items.json"`

#### Scenario: Remote settings identity appends credentials slot and port

- **Given** a `RemoteSettings` constructed as `new RemoteSettings("srv", "db", "sa", "pw", 1433)`
- **When** `GetId()` is called
- **Then** it returns `"srv:db:sa:1433"`

#### Scenario: Identity of a default-constructed remote settings with unassigned fields

- **Given** a `RemoteSettings` created with the parameterless constructor, leaving `Location`, `Name` and `UserName` at their `null!` defaults and `Port` at `0`
- **When** `GetId()` is called
- **Then** it returns `":::0"` — the nulls interpolate as empty strings and no exception is thrown

### Requirement: The identity format is re-defined, not extended, at three points in the chain

The system SHALL allow descendants to override `GetId()` without calling `base.GetId()`, and the
concrete backends SHALL do so with mutually incompatible field orders and field sets:
`SqlSettings.GetId()` returns `"{Location}:{Port}:{Name}:{UserName}"`, `MongoDB.Stores.Settings.GetId()`
returns the same `"{Location}:{Port}:{Name}:{UserName}"`, `CosmosDB.Stores.Settings.GetId()` returns
`"{Location}:{Name}:{UserName}"` (no port), `RavenDB.Stores.Settings.GetId()` returns
`"{Location}:{Name}"` (no username, no port), `InfluxDB.Stores.Settings.GetId()` returns
`"{Location}:{Organization}:{Bucket}"`, and `RedisSettings.GetId()` returns `base.GetId()` (the
`RemoteSettings` form) with `Database` appended.

#### Scenario: SqlSettings reorders the remote identity fields

- **Given** a `SqlSettings` with `Location = "srv"`, `Name = "db"`, `UserName = "sa"`, `Port = 1433`
- **When** `GetId()` is called
- **Then** it returns `"srv:1433:db:sa"` — not the `"srv:db:sa:1433"` its `RemoteSettings` base would have produced

#### Scenario: All four SQL providers share the SqlSettings identity format

- **Given** an `MSSqlSettings`, a `MySqlSettings`, a `PostgreSqlSettings` and a `TimescaleDBSettings`, each with `Location = "srv"`, `Name = "db"`, `UserName = "u"` and `Port = 1`
- **When** `GetId()` is called on each
- **Then** all four return `"srv:1:db:u"`, because none of them overrides `GetId()`

#### Scenario: RavenDB identity ignores credentials

- **Given** two `RavenDB.Stores.Settings` instances, both `new Settings("http://srv:8080", "db", ...)`, differing only in `username`/`password`
- **When** `GetId()` is called on each
- **Then** both return `"http://srv:8080:db"` — the two configurations are indistinguishable by identity

#### Scenario: CosmosDB identity carries the container in the username slot

- **Given** a `CosmosDB.Stores.Settings` constructed as `new Settings("https://acct.documents.azure.com", "db", "key", "orders")`, which assigns `containerName` to the inherited `UserName`
- **When** `GetId()` is called
- **Then** it returns `"https://acct.documents.azure.com:db:orders"`

#### Scenario: Redis identity appends the database index to the remote form

- **Given** a `RedisSettings` constructed as `new RedisSettings("srv", 6380, "pw", 3, true)`, which passes `string.Empty` for both `Name` and `UserName`
- **When** `GetId()` is called
- **Then** it returns `"srv:::6380:3"`

#### Scenario: InfluxDB identity is organization-scoped

- **Given** an `InfluxDB.Stores.Settings` constructed as `new Settings("http://localhost:8086", "metrics", "tok", "acme")`
- **When** `GetId()` is called
- **Then** it returns `"http://localhost:8086:acme:metrics"` — `Bucket` reads through to the base `Name`

#### Scenario: SQLite and ElasticSearch inherit the two-field base identity

- **Given** a `SqLiteSettings("C:\\data", "app.db", "secret")` and an `ElasticSearch.Stores.Settings` with `Location = "C:\\data"`, `Name = "app.db"`
- **When** `GetId()` is called on each
- **Then** both return `"C:\data:app.db"`, because neither overrides `GetId()`

### Requirement: No secret is ever part of a settings identity

The system SHALL exclude `PasswordSettings.Password`, `InfluxDB` `Token`, and `RedisSettings.KeyPrefix`
/ `RawConnectionString` from every `GetId()` implementation in the chain.

#### Scenario: Two settings differing only by password share an identity

- **Given** two `SqLiteSettings` instances, both `("C:\\data", "app.db")`, one with `Password = "a"` and one with `Password = "b"`
- **When** `GetId()` is called on each
- **Then** both return `"C:\data:app.db"` — the password does not participate in identity, so a store cache keyed on `GetId()` cannot distinguish them

#### Scenario: The InfluxDB token is not part of the identity

- **Given** two `InfluxDB.Stores.Settings`, identical except `Token`
- **When** `GetId()` is called on each
- **Then** both return the same `"{Location}:{Organization}:{Bucket}"` string

### Requirement: A settings object copies from another via a virtual LoadFrom(Settings) hook

The system SHALL declare `Settings.LoadFrom(Settings data)` as `virtual`, SHALL make it a no-op when
`data` is `null`, and SHALL otherwise assign `Location` and `Name` from the source. Each override in
the chain SHALL first delegate to its base so the base fields are copied unconditionally, and SHALL
then copy its own fields only when the source is type-compatible.

#### Scenario: Base fields are copied

- **Given** a target `Settings` and a source `Settings { Location = "L", Name = "N" }`
- **When** `target.LoadFrom(source)` is called
- **Then** `target.Location == "L"` and `target.Name == "N"`

#### Scenario: A null source leaves the target untouched

- **Given** a target `Settings { Location = "L", Name = "N" }`
- **When** `LoadFrom((Settings)null)` is called
- **Then** no exception is thrown and both properties keep their values

#### Scenario: PasswordSettings copies base fields even when the source has no password

- **Given** a target `PasswordSettings { Password = "keep" }` and a source that is a plain `Settings { Location = "L", Name = "N" }`
- **When** `target.LoadFrom(source)` is dispatched through the virtual `LoadFrom(Settings)` override
- **Then** `Location` and `Name` are copied and `Password` remains `"keep"` — the base fields are not lost to the type guard

#### Scenario: RemoteSettings copies base fields even when the source is not remote

- **Given** a target `RemoteSettings { UserName = "u", Port = 1, UseSecure = true }` and a source `PasswordSettings { Location = "L", Name = "N", Password = "p" }`
- **When** `target.LoadFrom(source)` is called through `LoadFrom(Settings)`
- **Then** `Location`, `Name` and `Password` are copied while `UserName`, `Port` and `UseSecure` are unchanged

#### Scenario: A provider source narrower than the target copies only as far as it can

- **Given** a target `MSSqlSettings { MultipleActiveResultSets = true }` and a source `SqlSettings { Location = "L", Name = "N", CommandTimeout = 99 }`
- **When** `target.LoadFrom((Birko.Configuration.Settings)source)` is called
- **Then** the `SqlSettings`-level fields including `CommandTimeout = 99` are copied and `MultipleActiveResultSets` stays `true`

### Requirement: LoadFrom(ISettings) bridges into the virtual chain and silently ignores foreign implementations

The system SHALL implement `Settings.LoadFrom(ISettings data)` as a non-virtual method that calls the
**virtual** `LoadFrom(Settings)` when `data is Settings`, and SHALL do nothing at all otherwise.

#### Scenario: An ISettings-typed source reaches the most-derived override

- **Given** a `MySqlSettings` target and an `ISettings` reference holding a `MySqlSettings { BulkInsertBatchSize = 5000 }`
- **When** `target.LoadFrom(iSettingsReference)` is called
- **Then** virtual dispatch reaches `MySqlSettings.LoadFrom(Settings)` and `BulkInsertBatchSize` becomes `5000`

#### Scenario: An ISettings implementation outside the Settings hierarchy is discarded

- **Given** a custom class implementing `ISettings` but not deriving from `Birko.Configuration.Settings`
- **When** `settings.LoadFrom(thatInstance)` is called
- **Then** nothing is copied, no exception is thrown, and the caller receives no signal that the load was skipped

### Requirement: RedisSettings discards every field when loading from a non-Redis source

The system SHALL, in `RedisSettings.LoadFrom(Settings data)`, copy only when `data is RedisSettings`
and SHALL perform **no base delegation** otherwise — unlike every other override in the family,
there is no `else base.LoadFrom(data)` branch.

#### Scenario: Loading a RedisSettings from a plain RemoteSettings copies nothing

- **Given** a target `RedisSettings { Location = "old", Port = 6379 }` and a source `RemoteSettings { Location = "new", Name = "n", UserName = "u", Port = 7000 }`
- **When** `target.LoadFrom((Birko.Configuration.Settings)source)` is called
- **Then** `Location` is still `"old"` and `Port` is still `6379` — `Location`, `Name`, `Password`, `UserName`, `Port` and `UseSecure` are all silently dropped

#### Scenario: Loading a RedisSettings from a RedisSettings copies the whole chain

- **Given** a target `RedisSettings` and a source `RedisSettings { Location = "srv", Port = 7000, Database = 3, KeyPrefix = "app:", RawConnectionString = "raw" }`
- **When** `target.LoadFrom(source)` is called
- **Then** all inherited fields plus `Database`, `KeyPrefix` and `RawConnectionString` are copied

### Requirement: Settings classes that do not override LoadFrom(Settings) lose their own fields through a base-typed call

The system SHALL dispatch `LoadFrom(Settings)` virtually, so a class that declares only a
same-type `LoadFrom(T)` overload — `TimescaleDBSettings`, `InfluxDB.Stores.Settings` and
`ElasticSearch.Stores.Settings` — SHALL have its own fields left unchanged whenever the copy is made
through a `Birko.Configuration.Settings`-typed reference.

#### Scenario: TimescaleDB hypertable fields are dropped through the base hook

- **Given** a target `TimescaleDBSettings { TimeColumn = "ts", ChunkTimeInterval = "1 day" }` and a source `TimescaleDBSettings { TimeColumn = "created", ChunkTimeInterval = "30 days", Location = "srv" }`
- **When** `((Birko.Configuration.Settings)target).LoadFrom(source)` is called, which dispatches to `SqlSettings.LoadFrom(Settings)`
- **Then** `Location` and the `SqlSettings` timeouts are copied but `TimeColumn` is still `"ts"` and `ChunkTimeInterval` is still `"1 day"`

#### Scenario: The TimescaleDB fields are copied only via the exact-type overload

- **Given** the same target and source, both statically typed `TimescaleDBSettings`
- **When** `target.LoadFrom(source)` binds to `LoadFrom(TimescaleDBSettings)`
- **Then** `TimeColumn` becomes `"created"` and `ChunkTimeInterval` becomes `"30 days"`

#### Scenario: InfluxDB token and organization are dropped through the base hook

- **Given** a target `InfluxDB.Stores.Settings { Token = "old", Organization = "old-org" }` and a source `InfluxDB.Stores.Settings { Token = "new", Organization = "new-org", Location = "http://srv" }`
- **When** `((Birko.Configuration.Settings)target).LoadFrom(source)` is called, reaching the un-overridden `Settings.LoadFrom(Settings)`
- **Then** only `Location` and `Name` are copied; `Token` and `Organization` keep their old values

#### Scenario: ElasticSearch index settings are never copied by any LoadFrom

- **Given** a target `ElasticSearch.Stores.Settings` and a source `ElasticSearch.Stores.Settings` with a populated `IndexSettings` collection
- **When** `target.LoadFrom(source)` is called — the only available overload is the inherited `Settings.LoadFrom(Settings)`
- **Then** `Location` and `Name` are copied and `target.IndexSettings` is unchanged (still its `null!` default if never assigned)

### Requirement: The non-provider SqlSettings base refuses to produce a connection string

The system SHALL implement `SqlSettings.GetConnectionString()` as a `virtual` method whose base body
throws `NotSupportedException`, with a message naming the runtime type via `GetType().Name` and
listing the provider subclasses that can answer.

#### Scenario: Calling GetConnectionString on a bare SqlSettings throws

- **Given** an instance of `SqlSettings` itself (for example a `SqlMigrationSettings` that only carries connection fields)
- **When** `GetConnectionString()` is called
- **Then** a `NotSupportedException` is thrown whose message begins with the runtime type name and states it "is not a provider-specific SqlSettings subclass"

#### Scenario: A provider subclass answers instead of throwing

- **Given** a `PostgreSqlSettings`
- **When** `GetConnectionString()` is called
- **Then** the override runs and a connection string is returned; no exception is thrown

### Requirement: Connection-string production is not a single polymorphic contract across backends

The system SHALL declare connection-string production separately per backend rather than on a shared
interface: `SqlSettings` declares `virtual GetConnectionString()` (overridden by `MSSqlSettings`,
`MySqlSettings`, `PostgreSqlSettings`, `TimescaleDBSettings`); `SqLiteSettings`,
`MongoDB.Stores.Settings` and `InfluxDB.Stores.Settings` each declare their own unrelated
`virtual GetConnectionString()`; `RedisSettings` declares a non-virtual `GetConnectionString()`; and
`CosmosDB.Stores.Settings`, `RavenDB.Stores.Settings` and `ElasticSearch.Stores.Settings` declare no
connection-string method at all.

#### Scenario: SQLite is outside the SqlSettings polymorphism

- **Given** `SqLiteSettings` derives from `PasswordSettings`, not from `SqlSettings`
- **When** code holding a `SqlSettings` reference calls `GetConnectionString()`
- **Then** a `SqLiteSettings` cannot be supplied at all — it is not a `SqlSettings`, and its own `GetConnectionString()` is a distinct method that does not override the `SqlSettings` one

#### Scenario: Cosmos and Raven expose driver objects instead of strings

- **Given** a `CosmosDB.Stores.Settings` and a `RavenDB.Stores.Settings`
- **When** a caller needs to reach the backend
- **Then** it must call `GetCosmosClientOptions()` and `CreateDocumentStore()` respectively; neither type has a `GetConnectionString()`

#### Scenario: ElasticSearch settings carry no connection information at all

- **Given** an `ElasticSearch.Stores.Settings`
- **When** its members are enumerated
- **Then** it exposes only the inherited `Location` / `Name` plus an `IndexSettings` collection — no connection-string method, no `GetId` override, no `LoadFrom` override

### Requirement: MSSqlSettings composes a SQL Server connection string by raw interpolation

The system SHALL build the MSSql connection string as
`Server=tcp:{Location},{Port};Initial Catalog={Name};Persist Security Info=False;User ID={UserName};Password={Password};MultipleActiveResultSets={True|False};Encrypt={UseSecure};TrustServerCertificate={True|False};Connection Timeout={ConnectionTimeout};`,
mapping `UseSecure` onto `Encrypt` and using `ConnectionTimeout` but **not** `CommandTimeout`.

#### Scenario: Default MSSql connection string

- **Given** `new MSSqlSettings("srv", "db", "sa", "pw")`, which defaults `port = 1433` and `useSecure = true`
- **When** `GetConnectionString()` is called
- **Then** it returns `Server=tcp:srv,1433;Initial Catalog=db;Persist Security Info=False;User ID=sa;Password=pw;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=15;`

#### Scenario: CommandTimeout is not represented in the MSSql connection string

- **Given** an `MSSqlSettings` with `CommandTimeout = 300`
- **When** `GetConnectionString()` is called
- **Then** the returned string contains `Connection Timeout=15` and no command-timeout keyword — the value must be applied by the connector, not the connection string

#### Scenario: A password containing a separator is emitted unescaped

- **Given** an `MSSqlSettings` with `Password = "pa;ss"`
- **When** `GetConnectionString()` is called
- **Then** the raw `Password=pa;ss;` fragment is emitted, terminating the value early and leaving `ss` to be parsed as a further keyword

### Requirement: MySqlSettings composes a MySQL connection string by raw interpolation and appends SslMode only when secure

The system SHALL build the MySQL connection string as
`Server={Location};Port={Port};User ID={UserName};Password={Password};Database={Name};Connection Timeout={ConnectionTimeout};`
and SHALL append `SslMode=Required;` when `UseSecure` is true.

#### Scenario: Default MySQL connection string

- **Given** `new MySqlSettings("srv", "db", "root", "pw")`, which defaults `port = 3306` and `useSecure = false`
- **When** `GetConnectionString()` is called
- **Then** it returns `Server=srv;Port=3306;User ID=root;Password=pw;Database=db;Connection Timeout=15;` with no SSL fragment

#### Scenario: Secure MySQL connection string

- **Given** the same settings with `UseSecure = true`
- **When** `GetConnectionString()` is called
- **Then** the returned string ends with `SslMode=Required;`

#### Scenario: BulkInsertBatchSize defaults to 1000 and is not part of the connection string

- **Given** a default-constructed `MySqlSettings`
- **When** `BulkInsertBatchSize` is read and `GetConnectionString()` is called
- **Then** `BulkInsertBatchSize == 1000` and the connection string contains no batch-size keyword

### Requirement: PostgreSqlSettings composes its connection string through NpgsqlConnectionStringBuilder

The system SHALL build the PostgreSQL connection string with a
`Npgsql.NpgsqlConnectionStringBuilder`, assigning `Host = Location`, `Port`, `Username = UserName`,
`Password`, `Database = Name`, `Timeout = ConnectionTimeout` and `CommandTimeout = CommandTimeout`,
setting `SslMode = Npgsql.SslMode.Require` when `UseSecure` is true, and returning
`builder.ConnectionString` — so values containing `;` or `=` are quoted by the builder and keys whose
value equals the Npgsql default are omitted from the output.

#### Scenario: A password containing a separator is quoted rather than breaking the string

- **Given** a `PostgreSqlSettings` with `Password = "pa;ss"`
- **When** `GetConnectionString()` is called
- **Then** the builder emits the password as a quoted value, so parsing the result yields `pa;ss` as a single password and no injected keyword

#### Scenario: Default-valued keys are absent from the output

- **Given** `new PostgreSqlSettings("localhost", "db", "u", "p")`, which defaults `port = 5432`, `ConnectionTimeout = 15` and `CommandTimeout = 30` — all equal to the Npgsql defaults
- **When** `GetConnectionString()` is called
- **Then** the returned string carries `Host`, `Username`, `Password` and `Database` but no `Port`, `Timeout` or `Command Timeout` keyword

#### Scenario: UseSecure maps onto SslMode.Require

- **Given** a `PostgreSqlSettings` with `UseSecure = true`
- **When** `GetConnectionString()` is called
- **Then** the returned string includes `SSL Mode=Require`

#### Scenario: UseBinaryImport defaults to true and does not reach the connection string

- **Given** a default-constructed `PostgreSqlSettings`
- **When** `UseBinaryImport` is read
- **Then** it is `true`, and the connection string contains no corresponding keyword — it is a bulk-path switch, not a driver option

### Requirement: TimescaleDBSettings composes a PostgreSQL connection string by raw interpolation

The system SHALL build the TimescaleDB connection string as
`Host={Location};Port={Port};Username={UserName};Password={Password};Database={Name};Timeout={ConnectionTimeout};Command Timeout={CommandTimeout};`,
appending `SSL Mode=Require;` when `UseSecure` is true — without using
`NpgsqlConnectionStringBuilder`, despite targeting the same wire protocol as `PostgreSqlSettings`.

#### Scenario: Default TimescaleDB connection string

- **Given** `new TimescaleDBSettings("srv", "db", "u", "p", 5432)`
- **When** `GetConnectionString()` is called
- **Then** it returns `Host=srv;Port=5432;Username=u;Password=p;Database=db;Timeout=15;Command Timeout=30;`

#### Scenario: TimescaleDB does not escape separator characters

- **Given** a `TimescaleDBSettings` with `Password = "pa;ss"`
- **When** `GetConnectionString()` is called
- **Then** the raw `Password=pa;ss;` fragment is emitted, in contrast to `PostgreSqlSettings` which quotes it

#### Scenario: Hypertable defaults

- **Given** a default-constructed `TimescaleDBSettings`
- **When** `TimeColumn` and `ChunkTimeInterval` are read
- **Then** they are `"timestamp"` and `"7 days"` respectively, and neither appears in the connection string

### Requirement: SqLiteSettings derives its file path from Location and Name and yields null when either is missing

The system SHALL expose `SqLiteSettings.Path` as a computed property returning
`System.IO.Path.Combine(Location, Name)` when both `Location` and `Name` are non-empty, and `null`
otherwise. `GetConnectionString()` SHALL return `Data Source={Path}`, append `;Password={Password}`
only when `Password` is non-empty, and always append `;Default Timeout={CommandTimeout}`.

#### Scenario: Path and connection string for a fully configured SQLite settings

- **Given** `new SqLiteSettings("C:\\data", "app.db")`
- **When** `Path` and `GetConnectionString()` are read
- **Then** `Path == "C:\data\app.db"` and the connection string is `Data Source=C:\data\app.db;Default Timeout=30`

#### Scenario: A password is embedded when present

- **Given** `new SqLiteSettings("C:\\data", "app.db", "secret")`
- **When** `GetConnectionString()` is called
- **Then** it returns `Data Source=C:\data\app.db;Password=secret;Default Timeout=30`

#### Scenario: A missing Location produces a connection string with an empty data source

- **Given** a `SqLiteSettings` with `Location = ""` and `Name = "app.db"`
- **When** `Path` and `GetConnectionString()` are read
- **Then** `Path` is `null` and the connection string is `Data Source=;Default Timeout=30` — no exception is thrown and the misconfiguration only surfaces when the driver opens the connection

#### Scenario: SQLite carries no connection timeout

- **Given** a `SqLiteSettings`, which extends `PasswordSettings` rather than `SqlSettings`
- **When** its members are enumerated
- **Then** it has `CommandTimeout` (default 30) but no `ConnectionTimeout`, no `Port`, no `UserName` and no `UseSecure`

### Requirement: MongoDB composes a mongodb:// URI, including credentials only when both parts are present

The system SHALL build the MongoDB connection string starting from `mongodb://`, SHALL prepend
`{UserName}:{Password}@` only when **both** `UserName` and `Password` are non-empty, SHALL append
`{Location}:{Port}`, SHALL append `/{Name}` when `Name` is non-empty, and SHALL append a query string
composed of `authSource={AuthDatabase}` (when non-empty), `replicaSet={ReplicaSet}` (when non-empty),
`tls=true` (when `UseSecure`), and unconditionally `retryWrites=true` and `retryReads=true`.

#### Scenario: Fully configured MongoDB connection string

- **Given** `new MongoDB.Stores.Settings("h", "db", "u", "p")`, which fixes `port = 27017` and leaves `AuthDatabase` at `"admin"`
- **When** `GetConnectionString()` is called
- **Then** it returns `mongodb://u:p@h:27017/db?authSource=admin&retryWrites=true&retryReads=true`

#### Scenario: A username without a password is silently dropped

- **Given** a `MongoDB.Stores.Settings` with `UserName = "u"` and `Password = ""`
- **When** `GetConnectionString()` is called
- **Then** the credential segment is omitted entirely and the URI connects anonymously

#### Scenario: Default-constructed MongoDB settings still yield a syntactically shaped URI

- **Given** `new MongoDB.Stores.Settings()`, which sets `Port = 27017` and leaves `Location`, `Name` and `UserName` at their `null!` defaults
- **When** `GetConnectionString()` is called
- **Then** it returns `mongodb://:27017?authSource=admin&retryWrites=true&retryReads=true`

#### Scenario: Credentials are not URL-encoded

- **Given** a `MongoDB.Stores.Settings` with `Password = "p@ss"`
- **When** `GetConnectionString()` is called
- **Then** the emitted URI is `mongodb://u:p@ss@h:27017/...`, containing two `@` characters

#### Scenario: Replica set and TLS are added when configured

- **Given** a `MongoDB.Stores.Settings` with `ReplicaSet = "rs0"` and `UseSecure = true`
- **When** `GetConnectionString()` is called
- **Then** the query string contains `replicaSet=rs0` and `tls=true` alongside `authSource` and the two retry flags

### Requirement: RedisSettings prefers an explicit raw connection string and otherwise falls back to localhost:6379

The system SHALL return `RawConnectionString` verbatim from `GetConnectionString()` when it is
non-null and non-empty, and SHALL otherwise compose `{Location}:{Port}` — substituting `"localhost"`
for a null `Location` and `6379` for a `Port` that is not greater than zero — followed by
`,password=` (when `Password` is non-empty), `,user=` (when `UserName` is non-empty),
`,ssl=True,sslHost={Location}` (when `UseSecure`), `,defaultDatabase=` (when `Database != 0`), and
`,name=` (when `Name` is non-empty).

#### Scenario: A set raw connection string wins

- **Given** a `RedisSettings` with `RawConnectionString = "a:1,b:2,abortConnect=false"` and `Location = "ignored"`
- **When** `GetConnectionString()` is called
- **Then** it returns `"a:1,b:2,abortConnect=false"` unchanged

#### Scenario: An empty raw connection string falls through to property-based building

- **Given** a `RedisSettings` with `RawConnectionString = ""` and `Location = "srv"`, `Port = 6380`
- **When** `GetConnectionString()` is called
- **Then** it returns `"srv:6380"` rather than the empty string

#### Scenario: Zero and negative ports fall back to 6379

- **Given** a `RedisSettings` with `Location = "srv"` and `Port = 0`
- **When** `GetConnectionString()` is called
- **Then** it returns `"srv:6379"`

#### Scenario: Database index 0 is omitted

- **Given** a `RedisSettings` with `Database = 0`
- **When** `GetConnectionString()` is called
- **Then** no `defaultDatabase=` fragment is present, because the check is `Database != 0`

#### Scenario: Full option composition

- **Given** a `RedisSettings` with `Location = "srv"`, `Port = 6380`, `Password = "pw"`, `UserName = "acl"`, `UseSecure = true`, `Database = 3`, `Name = "client1"`
- **When** `GetConnectionString()` is called
- **Then** it returns `srv:6380,password=pw,user=acl,ssl=True,sslHost=srv,defaultDatabase=3,name=client1`

#### Scenario: Default-constructed Redis settings target localhost

- **Given** `new RedisSettings()`
- **When** `Location` and `Port` are read
- **Then** they are `"localhost"` and `6379`

### Requirement: InfluxDB settings model a token/organization/bucket connection with the URL as Location

The system SHALL implement `InfluxDB.Stores.Settings` as a direct descendant of
`Birko.Configuration.Settings` (not `RemoteSettings`), SHALL alias `Bucket` onto the inherited `Name`
property in both directions, and SHALL return the raw `Location` from `GetConnectionString()`.

#### Scenario: The connection string is just the server URL

- **Given** `new InfluxDB.Stores.Settings("http://localhost:8086", "metrics", "tok", "acme")`
- **When** `GetConnectionString()` is called
- **Then** it returns `"http://localhost:8086"` with no credential material

#### Scenario: Bucket and Name are the same storage slot

- **Given** an `InfluxDB.Stores.Settings`
- **When** `Bucket = "other"` is assigned
- **Then** `Name == "other"`, and conversely assigning `Name` changes `Bucket`

#### Scenario: Null token and organization are coerced to empty

- **Given** `new InfluxDB.Stores.Settings("http://srv", "bucket")` with both optional arguments omitted
- **When** `Token` and `Organization` are read
- **Then** both are `string.Empty`, not `null`

#### Scenario: There are no credential slots to misuse

- **Given** the `InfluxDB.Stores.Settings` type
- **When** its inheritance is inspected
- **Then** it has no `UserName`, `Password`, `Port` or `UseSecure`, because it does not descend from `PasswordSettings`/`RemoteSettings`

### Requirement: InfluxDB settings classify transient failures, defaulting to no retries

The system SHALL default `InfluxDB.Stores.Settings.RetryPolicy` to `RetryPolicy.None`, and SHALL
implement `IsTransientException(Exception ex)` to return true for `TimeoutException`, for
`HttpRequestException`, for a `TaskCanceledException` whose `InnerException` is a `TimeoutException`,
and for any exception whose `Message` contains the substring `"429"`, `"503"` or `"unavailable"`;
otherwise it SHALL return false.

#### Scenario: A timeout is transient

- **Given** a `new TimeoutException()`
- **When** `IsTransientException` is called
- **Then** it returns `true`

#### Scenario: A cancellation without a timeout inner exception is not transient

- **Given** a `TaskCanceledException` with a null `InnerException`
- **When** `IsTransientException` is called
- **Then** it returns `false`

#### Scenario: Classification by message substring matches unrelated exceptions

- **Given** an `InvalidOperationException("record 503 could not be parsed")`
- **When** `IsTransientException` is called
- **Then** it returns `true`, because the check is a plain `Message.Contains("503")` with no status-code parsing

#### Scenario: Retries are off unless configured

- **Given** a default-constructed `InfluxDB.Stores.Settings`
- **When** `RetryPolicy` is read
- **Then** it is `RetryPolicy.None` — the classifier has no effect until a caller assigns a policy

### Requirement: CosmosDB settings produce CosmosClientOptions carrying a Guid-id serializer

The system SHALL implement `CosmosDB.Stores.Settings.GetCosmosClientOptions()` to return a
`CosmosClientOptions` with `RequestTimeout` and `AllowBulkExecution` taken from the settings and
`Serializer` set to a new `Serialization.CosmosGuidIdSerializer`, and SHALL NOT include
`PartitionKeyPath` in the client options.

#### Scenario: Default client options

- **Given** a default-constructed `CosmosDB.Stores.Settings`
- **When** `GetCosmosClientOptions()` is called
- **Then** the result has `RequestTimeout == TimeSpan.FromSeconds(30)`, `AllowBulkExecution == true`, and a `CosmosGuidIdSerializer` instance as its `Serializer`

#### Scenario: A fresh serializer per call

- **Given** a `CosmosDB.Stores.Settings`
- **When** `GetCosmosClientOptions()` is called twice
- **Then** each result carries its own newly constructed `CosmosGuidIdSerializer`

#### Scenario: Cosmos maps its constructor arguments onto the RemoteSettings slots

- **Given** `new CosmosDB.Stores.Settings("https://acct.documents.azure.com", "db", "accountKey", "orders")`
- **When** the inherited properties are read
- **Then** `Location` is the endpoint, `Name` is the database, `Password` is the account key, `UserName` is the container name, `Port` is `0` and `UseSecure` is `true`

#### Scenario: PartitionKeyPath defaults to /id

- **Given** a default-constructed `CosmosDB.Stores.Settings`
- **When** `PartitionKeyPath` is read
- **Then** it is `"/id"`

### Requirement: RavenDB settings build and initialize a DocumentStore, ignoring the credential slots

The system SHALL implement `RavenDB.Stores.Settings.CreateDocumentStore()` to construct a
`DocumentStore` with `Urls = new[] { Location }`, `Database = Name` and
`Conventions = new DocumentConventions { RequestTimeout = RequestTimeout }`, to call `Initialize()`
on it, and to return the initialized instance — with the inherited `UserName`, `Password` and
`UseSecure` unused.

#### Scenario: The returned store is already initialized

- **Given** `new RavenDB.Stores.Settings("http://localhost:8080", "db")`
- **When** `CreateDocumentStore()` is called
- **Then** an `IDocumentStore` is returned on which `Initialize()` has already been invoked, with a single URL and `Database == "db"`

#### Scenario: Credentials supplied to the constructor never reach the driver

- **Given** `new RavenDB.Stores.Settings("https://secure.raven", "db", "user", "secret")`
- **When** `CreateDocumentStore()` is called
- **Then** the `DocumentStore` is created with no `Certificate` and no credential configuration — a secured RavenDB cluster cannot be reached through this method

#### Scenario: Request timeout default

- **Given** a default-constructed `RavenDB.Stores.Settings`
- **When** `RequestTimeout` is read
- **Then** it is `TimeSpan.FromSeconds(30)`, and that value is placed on `DocumentConventions.RequestTimeout` by `CreateDocumentStore()`

#### Scenario: Raven fixes port and secure flag

- **Given** `new RavenDB.Stores.Settings("http://srv", "db")`
- **When** `Port` and `UseSecure` are read
- **Then** they are `0` and `false`, hard-coded by the constructor rather than exposed as parameters

### Requirement: Each provider chooses its own default port and transport-security default

The system SHALL default the connection port and `UseSecure` per provider constructor:
`MSSqlSettings` 1433 with `useSecure = true`; `MySqlSettings` 3306 with `useSecure = false`;
`PostgreSqlSettings` 5432 with `useSecure = false`; `MongoDB.Stores.Settings` 27017 (both
constructors, including the parameterless one); `RedisSettings` 6379 with `useSsl = false`;
`CosmosDB.Stores.Settings` port 0 with secure `true`; `RavenDB.Stores.Settings` port 0 with secure
`false`; `TimescaleDBSettings` requires the port explicitly; `SqLiteSettings` and
`InfluxDB.Stores.Settings` have no port at all.

#### Scenario: MSSql is secure by default and MySQL is not

- **Given** `new MSSqlSettings("s", "d")` and `new MySqlSettings("s", "d")`
- **When** `UseSecure` is read on each
- **Then** the MSSql instance is `true` (yielding `Encrypt=True`) and the MySQL instance is `false` (yielding no `SslMode` fragment)

#### Scenario: The parameterless MongoDB constructor still sets the port

- **Given** `new MongoDB.Stores.Settings()`
- **When** `Port` is read
- **Then** it is `27017`, assigned by the constructor body rather than inherited as zero

#### Scenario: TimescaleDB has no port default

- **Given** the `TimescaleDBSettings` parameterized constructor
- **When** its signature is inspected
- **Then** `port` is a required argument with no default value, unlike every other SQL provider

### Requirement: Password-bearing settings never hold a null password from their constructors

The system SHALL default `PasswordSettings.Password` to `string.Empty`, and SHALL coerce a null
`password` argument to `string.Empty` in `PasswordSettings`, `SqlSettings`, `SqLiteSettings`,
`MongoDB.Stores.Settings`, `RavenDB.Stores.Settings`, `CosmosDB.Stores.Settings` and
`RedisSettings` — while `RemoteSettings`' own parameterized constructor takes a non-nullable
`password` and assigns it through without a check.

#### Scenario: A null password argument becomes empty

- **Given** `new PasswordSettings("L", "N", null)`
- **When** `Password` is read
- **Then** it is `string.Empty`, so `string.IsNullOrEmpty(Password)` guards in connection-string builders behave identically to the "no password" case

#### Scenario: A null username argument also becomes empty in the provider constructors

- **Given** `new SqlSettings("L", "N")` with `username` and `password` omitted
- **When** `UserName` and `Password` are read
- **Then** both are `string.Empty`, because the constructor forwards `username ?? string.Empty` and `password ?? string.Empty` to `RemoteSettings`

#### Scenario: Redis passes empty strings rather than nulls for the unused slots

- **Given** `new RedisSettings("srv")`
- **When** `Name`, `UserName` and `Password` are read
- **Then** all three are `string.Empty`, which `GetId()` and `GetConnectionString()` both treat as "unset"

### Requirement: ElasticSearch settings carry per-type index configuration with an unset result window

The system SHALL expose `ElasticSearch.Stores.Settings.IndexSettings` as an
`IEnumerable<IndexSettings>` defaulting to `null!`, and SHALL define `IndexSettings` with a
`TypeName` (the full CLR type name used to map a type to an index), a `Name` (the custom index name)
and a nullable `MaxResultWindow`.

#### Scenario: The index settings collection is not initialized

- **Given** a default-constructed `ElasticSearch.Stores.Settings`
- **When** `IndexSettings` is read
- **Then** it is `null` despite the non-nullable annotation, so consumers must null-check before enumerating

#### Scenario: MaxResultWindow is unset rather than 10000

- **Given** a default-constructed `IndexSettings`
- **When** `MaxResultWindow` is read
- **Then** it is `null` — the documented 10,000 default is the ElasticSearch server default, not a value this object supplies
