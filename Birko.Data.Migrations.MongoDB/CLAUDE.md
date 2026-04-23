# Birko.Data.Migrations.MongoDB

## Overview
MongoDB migration backend using MongoDBClient from Birko.Data.MongoDB. Implements platform-agnostic IMigrationContext.

## Project Location
`C:\Source\Birko.Data.Migrations.MongoDB\`

## Components

### Runner
- `MongoMigrationRunner` — Takes `MongoDBClient` (from `store.Client`). Optional session/transaction support for replica sets.

### Context
- `MongoMigrationContext` — Wraps IMongoDatabase. Schema and Data properties. Raw() exposes IMongoDatabase.
- `MongoSchemaBuilder` — CreateCollection/DropCollection via IMongoDatabase. AddField/DropField are no-op (schema-less). RenameField uses $rename. Index creation via Builders<BsonDocument>.IndexKeys.
- `MongoDataMigrator` — BsonDocument filter parsing, $set updates, $merge for CopyData.

### Store
- `MongoMigrationStore` — Stores migration state in a MongoDB collection with unique index on version.

### Settings
- `MongoMigrationSettings` — MigrationsCollection, UseSession

## Usage

```csharp
var runner = new MongoMigrationRunner(store.Client);
runner.Register(new CreateUsersCollection());
runner.Migrate();
```

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Patterns
- Birko.Data.MongoDB (MongoDBClient)
- MongoDB.Driver

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
