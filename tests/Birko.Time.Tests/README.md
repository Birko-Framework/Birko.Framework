# Birko.Time.Tests

Unit tests for the Birko.Time library covering business calendars, date/time providers, working hours, holidays, and time zone conversion.

## Project Location
`C:\Source\Birko.Time.Tests\` — test project (.csproj, net10.0)

## Components

- **BusinessCalendarTests.cs** — Tests for `BusinessCalendar`: business-day detection, holiday skipping, weekend handling, `AddBusinessDays` (forward, backward, zero), `CountBusinessDays`, and working-hours retrieval.
- **DateTimeProviderTests.cs** — Tests for `SystemDateTimeProvider` (UTC accuracy, offset, today) and `TestDateTimeProvider` (initial time, `SetTime`, `Advance` forward/backward, accumulation).
- **DayScheduleTests.cs** — Tests for `DaySchedule`: default 9-17 with break, working span/duration calculations, `IsWorkingAt` boundary checks, and constructor validation (end before start, negative break, break exceeding span).
- **HolidayCalendarTests.cs** — Tests for `HolidayCalendar`: empty calendar, recurring vs one-time holidays, `GetHolidays` year filtering, `With` combination, and `WithHoliday` addition.
- **HolidayTests.cs** — Tests for `Holiday.Fixed` and `Holiday.OneTime` factory methods, `FallsOn` matching, constructor validation (empty name, invalid month/day), and `ToString` output.
- **TimeZoneConverterTests.cs** — Tests for `TimeZoneConverter`: UTC conversion, round-trip preservation, `GetTimeZone` lookup, available zones listing, and null-argument guards.
- **WorkingHoursTests.cs** — Tests for `WorkingHours`: default Mon-Fri schedule, empty constructor, `WithDay` immutability, null schedule removal, and custom day schedules.

## Dependencies

- Birko.Time (shared project import)
- xUnit (2.9.3)
- FluentAssertions (7.0.0)
- Microsoft.NET.Test.Sdk (18.0.1)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
