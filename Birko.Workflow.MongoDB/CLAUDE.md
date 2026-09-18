# Birko.Workflow.MongoDB

## Overview
MongoDB workflow instance persistence using AsyncMongoDBStore. Collection: `WorkflowInstances`.

## Project Location
`C:\Source\Birko.Workflow.MongoDB\` (shared project via `.projitems`)

## Components
- **Models/MongoWorkflowInstanceModel.cs** — AbstractModel + BSON attributes
- **MongoDBWorkflowInstanceStore.cs** — `IWorkflowInstanceStore<TData>` over `AsyncMongoDBStore`
- **MongoDBWorkflowInstanceSchema.cs** — Static EnsureCreatedAsync/DropAsync

## Dependencies
Birko.Workflow, Birko.Data.MongoDB
