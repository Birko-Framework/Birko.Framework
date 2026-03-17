# Birko.Workflow.RavenDB

## Overview
RavenDB workflow instance persistence using AsyncRavenDBStore. Convention-based mapping.

## Project Location
`C:\Source\Birko.Workflow.RavenDB\` (shared project via `.projitems`)

## Components
- **Models/RavenWorkflowInstanceModel.cs** — AbstractModel (no attributes, convention-based)
- **RavenDBWorkflowInstanceStore.cs** — `IWorkflowInstanceStore<TData>` over `AsyncRavenDBStore`
- **RavenDBWorkflowInstanceSchema.cs** — Static EnsureCreatedAsync/DropAsync

## Dependencies
Birko.Workflow, Birko.Data.RavenDB
