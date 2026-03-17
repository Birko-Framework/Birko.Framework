# Birko.Workflow.SQL

SQL-based workflow instance persistence for the Birko Workflow engine. Works with any SQL connector (PostgreSQL, MSSql, MySQL, SQLite).

## Features

- Persists workflow instances to `__WorkflowInstances` table
- Generic over SQL connector type (`DB : AbstractConnector`)
- Save (upsert), Load, Delete, FindByState/Status/WorkflowName
- Schema management utilities (EnsureCreated/Drop)

## Usage

```csharp
using Birko.Workflow.SQL;
using Birko.Data.SQL.PostgreSQL.Connectors;

// Create store
var store = new SqlWorkflowInstanceStore<PostgreSqlConnector, OrderData>(settings);

// Save instance
await store.SaveAsync("OrderProcessing", instance);

// Load instance
var loaded = await store.LoadAsync(instanceId);

// Query
var active = await store.FindByStatusAsync(WorkflowStatus.Active);
var pending = await store.FindByStateAsync("Pending");
```

## License

MIT License - see [License.md](License.md)
