# Birko.Workflow.Tests

## Overview
Unit tests for Birko.Workflow — state machine engine for business process automation.

## Project Location
`tests/Birko.Workflow.Tests/` (.csproj test project)

## Test Classes
- **WorkflowBuilderTests.cs** — Builder validation: valid definitions, missing initial state, undefined states, final initial state, state descriptions, multiple triggers
- **WorkflowEngineTests.cs** — Engine transitions: success, guard failure, not found, final state completion, completed/faulted exceptions, action execution order, state change callback, multiple guards
- **WorkflowInstanceTests.cs** — Instance creation and restoration (Create, Restore factories)
- **VisualizationTests.cs** — Mermaid and DOT diagram output validation
- **DiExtensionTests.cs** — DI registration (AddWorkflowEngine, diagram generator)

## Dependencies
- Birko.Workflow (via .projitems)
- xunit 2.9.3, FluentAssertions 7.0.0, Microsoft.NET.Test.Sdk 18.0.1

## Running Tests
```bash
dotnet test Birko.Workflow.Tests/Birko.Workflow.Tests.csproj
```
