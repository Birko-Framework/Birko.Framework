# Birko.Security.Grants.SQL

SQL tables, stores and indexes for [Birko.Security.Grants](../Birko.Security.Grants/README.md), on any Birko SQL
provider.

## Features

- Fluent mappings for `ResourceGrant` → `BirkoResourceGrants` and `Invitation` → `BirkoInvitations`
- `SqlResourceGrantStore<DB>` / `SqlInvitationStore<DB>`: one pair for SQLite, PostgreSQL, SQL Server or MySQL
- `GrantSqlIndexes.EnsureAsync`: the indexes grant checks read by, created idempotently
- The invitation token hash is UNIQUE in the database

## Usage

```csharp
using Birko.Data.SQL.SQLite.IndexManagement;   // or the PostgreSQL / MSSql / MySQL index manager
using Birko.Models.SQL.Mapping;
using Birko.Security.Grants.SQL;
using Birko.Security.Grants.SQL.Stores;

// Once at startup: the mappings write process-wide table registrations.
GrantSqlMappings.Register(new ModelMapRegistry()).ApplyToDatabase();

var settings = new SqLiteSettings(dataDir, "app.db");
var grantStore = new SqlResourceGrantStore<SqLiteConnector>();
var invitationStore = new SqlInvitationStore<SqLiteConnector>();
grantStore.SetSettings(settings);
invitationStore.SetSettings(settings);

// Tables plus lookup indexes. Pass the provider's own index manager.
await GrantSqlIndexes.EnsureAsync(grantStore, invitationStore, c => new SqLiteIndexManager(c));

builder.Services.AddSingleton<IResourceGrantStore>(grantStore);
builder.Services.AddSingleton<IInvitationStore>(invitationStore);
builder.Services.AddBirkoGrants(...);   // Birko.Security.Grants.AspNetCore
```

For another provider, swap `SqLiteConnector` / `SqLiteSettings` / `SqLiteIndexManager` for that provider's
connector, settings and index manager.

## Running tests

```
dotnet test tests/Birko.Security.Grants.SQL.Tests --nologo
```

## License

See the repository root `LICENSE`.
