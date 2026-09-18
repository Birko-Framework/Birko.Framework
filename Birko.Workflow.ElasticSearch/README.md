# Birko.Workflow.ElasticSearch

Elasticsearch-based workflow instance persistence for the Birko Workflow engine.

## Features

- Persists workflow instances to `workflow-instances` index
- NEST attributes for field mapping (Keyword, Text, Number, Date)
- Save (upsert), Load, Delete, FindByState/Status/WorkflowName
- Schema management utilities (EnsureCreated/Drop)

## Usage

```csharp
using Birko.Workflow.ElasticSearch;

var store = new ElasticSearchWorkflowInstanceStore<OrderData>(settings);
await store.SaveAsync("OrderProcessing", instance);
var loaded = await store.LoadAsync(instanceId);
```

## License

MIT License - see [License.md](License.md)
