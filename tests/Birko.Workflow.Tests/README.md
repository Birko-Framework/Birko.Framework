# Birko.Workflow.Tests

Unit tests for the Birko.Workflow state machine engine.

## Test Coverage

- **WorkflowBuilderTests** — Builder validation (valid definitions, missing/undefined states, final initial state, descriptions, multiple triggers)
- **WorkflowEngineTests** — Engine transitions (success, guard failure, not found, final state completion, completed/faulted exceptions, action execution order, state change callback, multiple guards)
- **WorkflowInstanceTests** — Instance creation and restoration (Create, Restore factories)
- **VisualizationTests** — Mermaid and Graphviz DOT diagram output
- **DiExtensionTests** — DI registration (AddWorkflowEngine, diagram generator)

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0
- Microsoft.NET.Test.Sdk 18.0.1

## Running Tests

```bash
dotnet test Birko.Workflow.Tests/Birko.Workflow.Tests.csproj
```

## License

MIT License - see [License.md](License.md)
