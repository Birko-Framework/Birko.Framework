# Birko.Security.Grants.SQL

## Overview
SQL schema for `Birko.Security.Grants` (TASK-594, STORY-062). It contains the fluent `IModelMapping`s for
`ResourceGrant` and `Invitation`, a store pair generic over the SQL connector, and the lookup indexes. Schemaless
backends (InMemory, JSON, XML, Mongo, Raven, Cosmos, ES) need none of this. SQL does, because `Birko.Data.SQL` builds
columns only from SQL attributes or a fluent mapping, and the core models carry neither by design. This follows
the `Birko.Models.{Domain}.SQL` pattern.

## Project Location
`Birko.Security.Grants.SQL/`

## Structure
```
Birko.Security.Grants.SQL/
├── GrantSqlMappings.cs          — Register(ModelMapRegistry): both mappings; then ApplyToDatabase()
├── GrantSqlIndexes.cs           — EnsureAsync(grants, invitations, indexManager): tables + the 3 lookup indexes, idempotent
├── Mappings/
│   ├── ResourceGrantMapping.cs  — table BirkoResourceGrants; Guid primary; UserGuid/ResourceType/Role required
│   └── InvitationMapping.cs     — table BirkoInvitations; Guid primary; TokenHash required + UNIQUE
└── Stores/SqlGrantStores.cs     — SqlResourceGrantStore<DB> / SqlInvitationStore<DB> : AsyncDataBaseBulkStore<DB, T>
```

## Dependencies
- `Birko.Security.Grants`, `Birko.Models.SQL` (fluent mapping), `Birko.Data.SQL` (stores, `IIndexManager` via the
  provider), `Birko.Data.Patterns` (`IndexDefinition`)

## Rules the code keeps
- **Every property is a column**, measured on SQLite: the fluent mapping gives a plain model its columns.
  `IsRequired` becomes NOT NULL, and `IsUnique` becomes a real UNIQUE constraint (the token hash).
- **The mappings declare the indexes with `HasIndex`** (TASK-693), so schema-ensure creates them and, on MySQL and
  SQL Server, bounds `ResourceType` and `Email` to `VARCHAR(255)` / `NVARCHAR(255)` (rule 37). Created only through the
  index manager, as TASK-594 did, the columns stayed `LONGTEXT` / `NVARCHAR(MAX)`, which neither provider can index,
  and `GrantSqlIndexes.EnsureAsync` threw on both (measured). `ResourceType` sits in two indexes, which needed
  TASK-695. `EnsureAsync` stays as the startup check and creates any index schema-ensure reported as failed. A table
  created on MySQL or SQL Server before this keeps its unbounded columns: alter `ResourceType` and `Email` to 255
  characters, then call `EnsureAsync`. The indexes:
  - `IX_BirkoResourceGrants_User` on (UserGuid, ResourceType): `CanAsync` and listing.
  - `IX_BirkoResourceGrants_Resource` on (ResourceType, ResourceId): grantees and the last-owner count.
  - `IX_BirkoInvitations_Email` on (Email): sign-in.
- **The index manager is a required argument.** The base `SqlIndexManager` is MySQL's `information_schema`
  dialect, and on SQLite it throws `no such table: information_schema.statistics`, measured. Pass the provider's
  own manager.
- **No uniqueness on the grant key** (user, type, resource, tenant). Two of its columns are nullable, and a unique
  index over nullable columns behaves differently per provider (rule 58). The services already serialize writes
  and act by key, so a duplicate row cannot keep access.
- The mapping registry writes **process-wide** state (`DataBase.RegisterTableName`, field flags): register once at
  startup.

## Maintenance
See `Birko.Framework/CLAUDE-maintenance.md`. Tests: `tests/Birko.Security.Grants.SQL.Tests/`, on SQLite.
TASK-593 measured the grant behaviour on these stores: lookup parity with InMemory, the claim across instances,
and the `PropertyUpdate` path. All match on SQLite. The raw-bind mechanism for converted fields is TASK-596.
