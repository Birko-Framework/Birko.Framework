# Birko.Workflow.MongoDB

MongoDB-based workflow instance persistence for the Birko Workflow engine.

## Features

- Persists workflow instances to `WorkflowInstances` collection
- BSON attributes for document mapping
- Save (upsert), Load, Delete, FindByState/Status/WorkflowName
- Schema management utilities (EnsureCreated/Drop)

## Usage

```csharp
using Birko.Workflow.MongoDB;

var store = new MongoDBWorkflowInstanceStore<OrderData>(settings);
await store.SaveAsync("OrderProcessing", instance);
var loaded = await store.LoadAsync(instanceId);
```

## License

MIT License - see [License.md](License.md)
