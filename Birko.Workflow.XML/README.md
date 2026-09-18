# Birko.Workflow.XML

XML file-based workflow persistence for Birko Framework.

## Overview

Provides XML file-based storage for workflow instances, enabling persistent workflow execution with XML serialization.

## Features

- **XML Persistence**: Workflow instances stored as XML files
- **Async/Await**: Fully async API for non-blocking operations
- **State Tracking**: Track workflow state, status, and history
- **Query Support**: Find workflows by state, status, or name
- **Schema Management**: Easy setup and teardown of workflow storage

## Usage

```csharp
using Birko.Workflow.XML;

// Initialize storage
var settings = new XmlSettings
{
    Location = "./data/workflows"
};
await XmlWorkflowInstanceSchema.EnsureCreatedAsync(settings);

// Create store
var store = new XmlWorkflowInstanceStore<MyData>(settings);

// Save workflow instance
var instance = new WorkflowInstance<MyData>(initialData);
await store.SaveAsync("OrderProcessing", instance);

// Load workflow instance
var loaded = await store.LoadAsync(instance.InstanceId);

// Query workflows
var running = await store.FindByStatusAsync(WorkflowStatus.Running);
var orders = await store.FindByWorkflowNameAsync("OrderProcessing");
```

## Dependencies

- Birko.Workflow
- Birko.Data.XML
- Birko.Serialization
