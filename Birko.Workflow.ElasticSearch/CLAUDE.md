# Birko.Workflow.ElasticSearch

## Overview
Elasticsearch workflow instance persistence using AsyncElasticSearchStore. Index: `workflow-instances`.

## Project Location
`Birko.Workflow.ElasticSearch/` (shared project via `.projitems`)

## Components
- **Models/ElasticWorkflowInstanceModel.cs** — AbstractModel + NEST attributes
- **ElasticSearchWorkflowInstanceStore.cs** — `IWorkflowInstanceStore<TData>` over `AsyncElasticSearchStore`
- **ElasticSearchWorkflowInstanceSchema.cs** — Static EnsureCreatedAsync/DropAsync

## Dependencies
Birko.Workflow, Birko.Data.ElasticSearch
