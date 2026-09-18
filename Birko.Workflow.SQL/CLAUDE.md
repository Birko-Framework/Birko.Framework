# Birko.Workflow.SQL

## Overview
SQL-based workflow instance persistence using AsyncDataBaseBulkStore. Works with any SQL connector.

## Project Location
`C:\Source\Birko.Workflow.SQL\` (shared project via `.projitems`)

## Components
- **Models/WorkflowInstanceModel.cs** — AbstractModel + SQL attributes, `__WorkflowInstances` table
- **SqlWorkflowInstanceStore.cs** — `IWorkflowInstanceStore<TData>` over `AsyncDataBaseBulkStore<DB, WorkflowInstanceModel>`
- **SqlWorkflowInstanceSchema.cs** — Static EnsureCreatedAsync/DropAsync

## Dependencies
Birko.Workflow, Birko.Data.SQL, Birko.Data.Stores
