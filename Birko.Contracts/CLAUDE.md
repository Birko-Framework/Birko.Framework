# Birko.Contracts

## Overview
Zero-dependency shared project containing the most fundamental interfaces used across the Birko Framework. Extracted from Birko.Data.Core to enable lightweight consumers to depend on core contracts without pulling in models, ViewModels, filters, or exceptions.

## Project Location
`C:\Source\Birko.Contracts\`

## Namespace
`Birko.Data.Models` — preserves backward compatibility with Birko.Data.Core.

## Components

### Models/ILoadable.cs
- `ILoadable<T>` — Load-from pattern interface

### Models/ICopyable.cs
- `ICopyable<T>` — Copy-to pattern interface

### Models/IDefault.cs
- `IDefault` — Single `bool Default` property

### Models/ITimestamped.cs
- `ITimestamped` — `CreatedAt`, `UpdatedAt`, `PrevUpdatedAt` timestamp tracking

### Models/IGuidEntity.cs
- `IGuidEntity` — Entity with `Guid?` identifier, implemented by both AbstractModel and ModelViewModel

### Models/ILogEntity.cs
- `ILogEntity` — Extends `IGuidEntity` + `ITimestamped`, implemented by both AbstractLogModel and LogViewModel

### Retry/RetryPolicy.cs
- `RetryPolicy` — Configurable retry with exponential backoff (`MaxRetries`, `BaseDelay`, `MaxDelay`, `GetDelay()`). Namespace `Birko`.

## Dependencies
None. This is a zero-dependency project.

## Consumers
- **Birko.Data.Core** — imports this project (backward compatible)
- **Birko.Configuration** — imports this project for `ILoadable<T>`
- **Any project** needing only these interfaces can import directly

## Maintenance
- This project must remain zero-dependency
- Model interfaces use namespace `Birko.Data.Models` for backward compatibility
- RetryPolicy uses namespace `Birko` as a general-purpose utility
