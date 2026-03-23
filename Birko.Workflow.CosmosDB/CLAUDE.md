# Birko.Workflow.CosmosDB

## Overview
Cosmos DB workflow instance persistence using AsyncCosmosDBStore.

## Project Location
`C:\Source\Birko.Workflow.CosmosDB\`

## Components
- **Models/CosmosWorkflowInstanceModel.cs** — AbstractModel, JSON-serialized data/history
- **CosmosDBWorkflowInstanceStore.cs** — `IWorkflowInstanceStore<TData>` over `AsyncCosmosDBStore`
- **CosmosDBWorkflowInstanceSchema.cs** — Static EnsureCreatedAsync/DropAsync

## Dependencies
- Birko.Workflow (IWorkflowInstanceStore, WorkflowInstance, WorkflowStatus)
- Birko.Data.CosmosDB (AsyncCosmosDBStore)
- Microsoft.Azure.Cosmos
