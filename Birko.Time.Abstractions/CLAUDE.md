# Birko.Time.Abstractions

## Overview
Zero-dependency shared project containing the clock abstraction and providers extracted from Birko.Time. Enables lightweight consumers to depend on `IDateTimeProvider` without pulling in calendars, holidays, and working hours.

## Project Location
`Birko.Time.Abstractions/`

## Namespace
`Birko.Time` — preserves backward compatibility with Birko.Time.

## Components

### Core/IDateTimeProvider.cs
- `IDateTimeProvider` — Clock abstraction with `UtcNow`, `OffsetUtcNow`, `Today`

### Providers/SystemDateTimeProvider.cs
- `SystemDateTimeProvider` — Production implementation using system clock

### Providers/TestDateTimeProvider.cs
- `TestDateTimeProvider` — Test-controllable clock with `SetTime()` and `Advance()`

## Dependencies
None. This is a zero-dependency project.

## Consumers
- **Birko.Time** — imports this project (backward compatible)
- **Birko.MessageQueue**, **Birko.BackgroundJobs**, **Birko.EventBus**, **Birko.Data.Patterns** — can import directly instead of Birko.Time

## Maintenance
- These types must remain zero-dependency
- Namespace must stay as `Birko.Time` for backward compatibility
- Only clock-related abstractions and basic providers belong here
