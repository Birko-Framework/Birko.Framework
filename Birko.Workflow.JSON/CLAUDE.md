# Birko.Workflow.JSON

## Overview
JSON file-based workflow instance persistence using AsyncJsonStore. For development and testing.

## Project Location
`C:\Source\Birko.Workflow.JSON\` (shared project via `.projitems`)

## Components
- **Models/JsonWorkflowInstanceModel.cs** — AbstractModel + JsonPropertyName attributes
- **JsonWorkflowInstanceStore.cs** — `IWorkflowInstanceStore<TData>` over `AsyncJsonStore`
- **JsonWorkflowInstanceSchema.cs** — Static EnsureCreatedAsync/DropAsync

## Dependencies
- Birko.Workflow, Birko.Data.JSON
- Birko.Serialization — ISerializer for workflow state/history serialization (optional, defaults to SystemJsonSerializer)
