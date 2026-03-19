# Birko Framework Dependency Tree

## Overview

This document maps every Birko.\* project to its direct Birko.\* dependencies. Shared projects (.shproj/.projitems) don't chain MSBuild imports — the host app (`Birko.Framework.csproj`) imports all projitems flat. Dependencies listed here are **logical/source-code** dependencies.

Only 4 projitems-level import chains exist:
- `Birko.Configuration` imports `Birko.Contracts`
- `Birko.Data.Core` imports `Birko.Contracts`
- `Birko.Data.Stores` imports `Birko.Configuration`
- `Birko.Time` imports `Birko.Time.Abstractions`

---

## Visual Dependency Graph

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ZERO-DEPENDENCY ROOTS                        │
├─────────────────────────────────────────────────────────────────────┤
│  Birko.Contracts    Birko.Time.Abstractions    Birko.Serialization  │
│  Birko.Rules        Birko.Helpers              Birko.Localization   │
│  Birko.Communication  Birko.Health             Birko.EventBus       │
│  Birko.CQRS         Birko.Workflow             Birko.Caching        │
│  Birko.Security                                                     │
└──────────┬──────────────────────┬───────────────────────┬───────────┘
           │                      │                       │
           ▼                      ▼                       ▼
┌──────────────────┐  ┌─────────────────────┐  ┌──────────────────────┐
│ Birko.Configuration│  │ Birko.Time           │  │ Birko.Data.Core      │
│ (← Contracts)     │  │ (← Time.Abstractions)│  │ (← Contracts)        │
└────────┬─────────┘  └─────────────────────┘  └──────────┬───────────┘
         │                                                 │
         ▼                                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│                        Birko.Data.Stores                             │
│                  (← Configuration, Data.Core)                        │
└──────────────────────────────┬───────────────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                      Birko.Data.Repositories                         │
│                     (← Data.Core, Data.Stores)                       │
└──────────────────────────────┬───────────────────────────────────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         ▼                     ▼                     ▼
┌─────────────────┐  ┌─────────────────┐  ┌──────────────────────────┐
│  Birko.Data.SQL │  │  NoSQL Stores   │  │  Feature Projects        │
│  (← Core,Stores,│  │  (ES, JSON,     │  │  (Patterns, Migrations,  │
│   Repositories) │  │   Mongo, Raven, │  │   Sync, Tenant, etc.)    │
└────────┬────────┘  │   Influx)       │  └──────────────────────────┘
         │           └─────────────────┘
         ▼
┌──────────────────────────────────────────────────────────────────────┐
│  SQL Providers: MSSql, PostgreSQL, MySQL, SqLite, View, TimescaleDB  │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Foundation Layer

| Project | Dependencies |
|---------|-------------|
| **Birko.Contracts** | *(none)* |
| **Birko.Configuration** | Birko.Contracts |
| **Birko.Data.Core** | Birko.Contracts |
| **Birko.Data.Stores** | Birko.Configuration, Birko.Data.Core |
| **Birko.Data.Repositories** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Time.Abstractions** | *(none)* |
| **Birko.Time** | Birko.Time.Abstractions |

## Helpers & Structures

| Project | Dependencies |
|---------|-------------|
| **Birko.Helpers** | *(none)* |
| **Birko.Structures** | Birko.Data.Core |

## Serialization

| Project | Dependencies |
|---------|-------------|
| **Birko.Serialization** | *(none)* |
| **Birko.Serialization.Newtonsoft** | Birko.Serialization |
| **Birko.Serialization.MessagePack** | Birko.Serialization |
| **Birko.Serialization.Protobuf** | Birko.Serialization |

## Rules & Validation

| Project | Dependencies |
|---------|-------------|
| **Birko.Rules** | *(none)* |
| **Birko.Validation** | Birko.Data.Stores |

## Models

| Project | Dependencies |
|---------|-------------|
| **Birko.Models** | Birko.Data.Core |
| **Birko.Models.Accounting** | Birko.Data.Core, Birko.Models |
| **Birko.Models.Category** | Birko.Models, Birko.Structures |
| **Birko.Models.Customers** | Birko.Data.Core, Birko.Models.Accounting |
| **Birko.Models.Product** | Birko.Models |
| **Birko.Models.SEO** | Birko.Models |
| **Birko.Models.Users** | Birko.Data.Core, Birko.Data.SQL |
| **Birko.Models.Warehouse** | Birko.Data.Core, Birko.Models, Birko.Models.Accounting, Birko.Models.Category, Birko.Models.Users |

## SQL Data Layer

| Project | Dependencies |
|---------|-------------|
| **Birko.Data.SQL** | Birko.Data.Core, Birko.Data.Repositories, Birko.Data.Stores |
| **Birko.Data.SQL.MSSql** | Birko.Data.Core, Birko.Data.SQL, Birko.Data.Stores |
| **Birko.Data.SQL.MySQL** | Birko.Data.Core, Birko.Data.SQL, Birko.Data.Stores |
| **Birko.Data.SQL.PostgreSQL** | Birko.Data.Core, Birko.Data.SQL, Birko.Data.Stores |
| **Birko.Data.SQL.SqLite** | Birko.Data.Core, Birko.Data.SQL, Birko.Data.Stores |
| **Birko.Data.SQL.View** | Birko.Data.Core, Birko.Data.SQL, Birko.Data.Stores |

## NoSQL Data Layer

| Project | Dependencies |
|---------|-------------|
| **Birko.Data.ElasticSearch** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Data.JSON** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Data.MongoDB** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Data.RavenDB** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Data.InfluxDB** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Data.TimescaleDB** | Birko.Data.Core, Birko.Data.SQL, Birko.Data.SQL.PostgreSQL, Birko.Data.Stores |

## ViewModel Layer

| Project | Dependencies |
|---------|-------------|
| **Birko.Data.ViewModel** | Birko.Data.Core, Birko.Data.Repositories, Birko.Data.Stores, Birko.Serialization |
| **Birko.Data.SQL.ViewModel** | Birko.Data.Core, Birko.Data.Repositories, Birko.Data.SQL, Birko.Data.Stores, Birko.Data.ViewModel |
| **Birko.Data.ElasticSearch.ViewModel** | Birko.Data.Core, Birko.Data.ElasticSearch, Birko.Data.Repositories, Birko.Data.Stores, Birko.Data.ViewModel |
| **Birko.Data.JSON.ViewModel** | Birko.Data.Core, Birko.Data.JSON, Birko.Data.Repositories, Birko.Data.Stores, Birko.Data.ViewModel |
| **Birko.Data.MongoDB.ViewModel** | Birko.Data.Core, Birko.Data.MongoDB, Birko.Data.Repositories, Birko.Data.Stores, Birko.Data.ViewModel |
| **Birko.Data.RavenDB.ViewModel** | Birko.Data.Core, Birko.Data.RavenDB, Birko.Data.Repositories, Birko.Data.Stores, Birko.Data.ViewModel |
| **Birko.Data.InfluxDB.ViewModel** | Birko.Data.Core, Birko.Data.InfluxDB, Birko.Data.Repositories, Birko.Data.Stores, Birko.Data.ViewModel |
| **Birko.Data.TimescaleDB.ViewModel** | Birko.Data.Core, Birko.Data.Repositories, Birko.Data.SQL, Birko.Data.Stores, Birko.Data.TimescaleDB, Birko.Data.ViewModel |

## Data Patterns & Features

| Project | Dependencies |
|---------|-------------|
| **Birko.Data.Patterns** | Birko.Data.Core, Birko.Data.Repositories, Birko.Data.Stores, Birko.Time |
| **Birko.Data.Migrations** | Birko.Data.Core |
| **Birko.Data.Migrations.SQL** | Birko.Data.Core, Birko.Data.Migrations, Birko.Data.SQL, Birko.Data.Stores |
| **Birko.Data.Migrations.ElasticSearch** | Birko.Data.ElasticSearch, Birko.Data.Migrations |
| **Birko.Data.Migrations.MongoDB** | Birko.Data.Migrations, Birko.Data.MongoDB |
| **Birko.Data.Migrations.RavenDB** | Birko.Data.Migrations, Birko.Data.RavenDB |
| **Birko.Data.Migrations.InfluxDB** | Birko.Data.InfluxDB, Birko.Data.Migrations |
| **Birko.Data.Migrations.TimescaleDB** | Birko.Data.Migrations, Birko.Data.Migrations.SQL, Birko.Data.TimescaleDB |
| **Birko.Data.Sync** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Data.Sync.Sql** | Birko.Data.Core, Birko.Data.SQL, Birko.Data.Sync |
| **Birko.Data.Sync.ElasticSearch** | Birko.Data.Core, Birko.Data.Sync |
| **Birko.Data.Sync.Json** | Birko.Data.Core, Birko.Data.Sync |
| **Birko.Data.Sync.MongoDb** | Birko.Data.Core, Birko.Data.Sync |
| **Birko.Data.Sync.RavenDB** | Birko.Data.RavenDB, Birko.Data.Sync |
| **Birko.Data.Sync.Tenant** | Birko.Data.Sync, Birko.Data.Tenant |
| **Birko.Data.Aggregates** | Birko.Data.Core, Birko.Data.Stores, Birko.Helpers |
| **Birko.Data.Tenant** | Birko.Data.Core, Birko.Data.Stores, Birko.Serialization |
| **Birko.Data.EventSourcing** | Birko.Data.Core, Birko.Data.Stores, Birko.Serialization |
| **Birko.Data.Processors** | Birko.Helpers |
| **Birko.Data.Localization** | Birko.Data.Core, Birko.Data.Stores |

## Localization

| Project | Dependencies |
|---------|-------------|
| **Birko.Localization** | *(none)* |
| **Birko.Localization.Data** | Birko.Data.Core, Birko.Data.Stores, Birko.Localization |

## Caching & Redis

| Project | Dependencies |
|---------|-------------|
| **Birko.Redis** | Birko.Data.Stores |
| **Birko.Caching** | *(none)* |
| **Birko.Caching.Redis** | Birko.Caching, Birko.Redis |
| **Birko.Caching.Hybrid** | Birko.Caching |

## Security

| Project | Dependencies |
|---------|-------------|
| **Birko.Security** | *(none)* |
| **Birko.Security.BCrypt** | Birko.Security |
| **Birko.Security.Jwt** | Birko.Security |
| **Birko.Security.AspNetCore** | Birko.Data.Tenant, Birko.Security, Birko.Security.Jwt |
| **Birko.Security.Vault** | Birko.Security, Birko.Serialization |
| **Birko.Security.AzureKeyVault** | Birko.Security, Birko.Serialization |

## Storage

| Project | Dependencies |
|---------|-------------|
| **Birko.Storage** | Birko.Data.Stores, Birko.Helpers |
| **Birko.Storage.AzureBlob** | Birko.Data.Stores, Birko.Storage |

## Telemetry

| Project | Dependencies |
|---------|-------------|
| **Birko.Telemetry** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Telemetry.OpenTelemetry** | Birko.Telemetry |

## Health

| Project | Dependencies |
|---------|-------------|
| **Birko.Health** | *(none)* |
| **Birko.Health.Data** | Birko.Health |
| **Birko.Health.Redis** | Birko.Health |
| **Birko.Health.Azure** | Birko.Health, Birko.Security.AzureKeyVault, Birko.Storage.AzureBlob |

## Messaging

| Project | Dependencies |
|---------|-------------|
| **Birko.Messaging** | Birko.Data.Core, Birko.Data.Stores |
| **Birko.Messaging.Razor** | Birko.Messaging |

## Communication

| Project | Dependencies |
|---------|-------------|
| **Birko.Communication** | *(none)* |
| **Birko.Communication.Network** | Birko.Communication |
| **Birko.Communication.Hardware** | Birko.Communication |
| **Birko.Communication.Bluetooth** | Birko.Communication |
| **Birko.Communication.WebSocket** | Birko.Communication |
| **Birko.Communication.REST** | Birko.Communication |
| **Birko.Communication.SOAP** | Birko.Communication |
| **Birko.Communication.SSE** | Birko.Communication, Birko.Serialization |
| **Birko.Communication.Modbus** | Birko.Communication |
| **Birko.Communication.Camera** | Birko.Communication |
| **Birko.Communication.OAuth** | Birko.Configuration |
| **Birko.Communication.IR** | Birko.Communication, Birko.Communication.Hardware |

## Message Queue

| Project | Dependencies |
|---------|-------------|
| **Birko.MessageQueue** | Birko.Contracts, Birko.Serialization |
| **Birko.MessageQueue.InMemory** | Birko.MessageQueue |
| **Birko.MessageQueue.MQTT** | Birko.Data.Stores, Birko.MessageQueue |
| **Birko.MessageQueue.Redis** | Birko.MessageQueue, Birko.Redis |

## Event Bus

| Project | Dependencies |
|---------|-------------|
| **Birko.EventBus** | *(none)* |
| **Birko.EventBus.MessageQueue** | Birko.EventBus, Birko.MessageQueue |
| **Birko.EventBus.Outbox** | Birko.EventBus, Birko.MessageQueue |
| **Birko.EventBus.EventSourcing** | Birko.Data.EventSourcing, Birko.EventBus |

## Background Jobs

| Project | Dependencies |
|---------|-------------|
| **Birko.BackgroundJobs** | Birko.Contracts |
| **Birko.BackgroundJobs.SQL** | Birko.BackgroundJobs, Birko.Data.Core, Birko.Data.SQL, Birko.Data.Stores |
| **Birko.BackgroundJobs.ElasticSearch** | Birko.BackgroundJobs, Birko.Data.Core, Birko.Data.ElasticSearch, Birko.Data.Stores |
| **Birko.BackgroundJobs.MongoDB** | Birko.BackgroundJobs, Birko.Data.Core, Birko.Data.MongoDB, Birko.Data.Stores |
| **Birko.BackgroundJobs.RavenDB** | Birko.BackgroundJobs, Birko.Data.Core, Birko.Data.RavenDB, Birko.Data.Stores |
| **Birko.BackgroundJobs.JSON** | Birko.BackgroundJobs, Birko.Data.Core, Birko.Data.JSON, Birko.Data.Stores, Birko.Serialization |
| **Birko.BackgroundJobs.Redis** | Birko.BackgroundJobs, Birko.Redis, Birko.Serialization |

## CQRS & Workflow

| Project | Dependencies |
|---------|-------------|
| **Birko.CQRS** | *(none)* |
| **Birko.Workflow** | *(none)* |
| **Birko.Workflow.SQL** | Birko.Data.SQL, Birko.Data.Stores, Birko.Workflow |
| **Birko.Workflow.ElasticSearch** | Birko.Data.ElasticSearch, Birko.Workflow |
| **Birko.Workflow.MongoDB** | Birko.Data.MongoDB, Birko.Workflow |
| **Birko.Workflow.RavenDB** | Birko.Data.RavenDB, Birko.Workflow |
| **Birko.Workflow.JSON** | Birko.Data.JSON, Birko.Serialization, Birko.Workflow |

---

## Statistics

- **Total projects:** 110+ (including tests)
- **Zero-dependency roots:** 13 projects
- **Most depended-upon:** Birko.Data.Stores (~40 dependents), Birko.Data.Core (~35), Birko.Data.SQL (~15)
- **Deepest chain:** Contracts → Configuration → Data.Stores → Data.Repositories → Data.SQL → SQL.ViewModel (6 levels)
