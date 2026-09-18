# Birko.Workflow.RavenDB

RavenDB-based workflow instance persistence for the Birko Workflow engine.

## Features

- Convention-based mapping (no special attributes)
- Save (upsert), Load, Delete, FindByState/Status/WorkflowName
- Schema management utilities (EnsureCreated/Drop)

## Usage

```csharp
using Birko.Workflow.RavenDB;

var store = new RavenDBWorkflowInstanceStore<OrderData>(settings);
await store.SaveAsync("OrderProcessing", instance);
var loaded = await store.LoadAsync(instanceId);
```

## License

MIT License - see [License.md](License.md)
