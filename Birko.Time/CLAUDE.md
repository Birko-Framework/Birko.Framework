# Birko.Time

## Overview
Time utilities — testable clock abstraction, time zone conversion, and business calendar with holiday/working hours support.

## Project Location
`C:\Source\Birko.Time\` (shared project, .shproj/.projitems)

## Components

### Core Interfaces (`Core/`)
- **IDateTimeProvider** — Abstraction over system clock: `UtcNow`, `OffsetUtcNow`, `Today`
- **ITimeZoneConverter** — Time zone conversion: `Convert`, `ConvertToUtc`, `ConvertFromUtc`, `GetTimeZone`, `GetAvailableTimeZones`
- **IBusinessCalendar** — Business calendar: `IsBusinessDay`, `IsHoliday`, `IsWorkingTime`, `AddBusinessDays`, `CountBusinessDays`, `GetWorkingHours`, `GetHolidays`

### Calendar Models (`Calendars/`)
- **Holiday** — Immutable holiday definition with `Fixed()` (recurring) and `OneTime()` factories, `FallsOn(DateOnly)` check
- **HolidayCalendar** — Named collection of holidays with `IsHoliday()`, `GetHolidays()`, `With()` composition, `WithHoliday()` addition
- **DaySchedule** — Single day working hours: `Start`, `End`, `BreakDuration`, `WorkingDuration`, `IsWorkingAt()`, `Default` (9-17, 1h break)
- **WorkingHours** — Weekly schedule: `DayOfWeek` → `DaySchedule?` mapping, `IsWorkingDay()`, `WithDay()`, `Default` (Mon-Fri)
- **BusinessCalendar** — `IBusinessCalendar` implementation combining `WorkingHours` + `HolidayCalendar` + `TimeZoneInfo`

### Providers (`Providers/`)
- **SystemDateTimeProvider** — Production `IDateTimeProvider` using system clock
- **TestDateTimeProvider** — Controllable clock with `SetTime()` and `Advance()` for testing
- **TimeZoneConverter** — `ITimeZoneConverter` using `System.TimeZoneInfo` (Windows + IANA IDs)

## Namespace
All types in `Birko.Time` (flat namespace, no sub-namespaces).

## Dependencies
None.

## Tests
`C:\Source\Birko.Time.Tests\` — xUnit + FluentAssertions, 80 tests covering all components.

## Maintenance
- When adding new types, update `Birko.Time.projitems` ItemGroup
- All types are immutable or use composition (`With*` methods return new instances)
- `BusinessCalendar` is timezone-aware — `IsWorkingTime` converts to local time before checking
