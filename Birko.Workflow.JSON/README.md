# Birko.Workflow.JSON

JSON file-based workflow instance persistence for the Birko Workflow engine. Ideal for development, testing, and single-process deployments.

## Features

- Persists workflow instances as JSON files
- No external database required
- Save (upsert), Load, Delete, FindByState/Status/WorkflowName
- Schema management utilities (EnsureCreated/Drop)

## Usage

```csharp
using Birko.Workflow.JSON;

var settings = new Birko.Data.Stores.Settings { Location = "./data", Name = "workflows" };
var store = new JsonWorkflowInstanceStore<OrderData>(settings);
await store.SaveAsync("OrderProcessing", instance);
var loaded = await store.LoadAsync(instanceId);
```

## License

MIT License - see [License.md](License.md)
