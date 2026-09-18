# Birko.Time.Tests

## Overview
Test suite for the Birko.Time library. Validates business calendars, holiday handling, working hours scheduling, day schedules, time zone conversion, and date/time provider abstractions using xUnit and FluentAssertions.

## Project Location
`C:\Source\Birko.Time.Tests\` — Test project (.csproj)

## Components
- **BusinessCalendarTests.cs** — Tests BusinessCalendar weekday/holiday/weekend detection, working time checks, AddBusinessDays (positive, zero, negative), CountBusinessDays, GetWorkingHours, and GetHolidays.
- **HolidayCalendarTests.cs** — Tests HolidayCalendar construction, Empty singleton, recurring/one-time holiday matching, GetHolidays year filtering, With/WithHoliday combining, and null validation.
- **HolidayTests.cs** — Tests Holiday.Fixed and Holiday.OneTime factories, FallsOn date matching, validation (empty name, invalid month/day), and ToString formatting.
- **DayScheduleTests.cs** — Tests DaySchedule construction, Default (9-17 with 1h break), WorkingSpan, WorkingDuration (with/without break), IsWorkingAt boundary checks, and constructor validation (end before start, negative break, break exceeding span).
- **WorkingHoursTests.cs** — Tests WorkingHours Default (Mon-Fri working, Sat-Sun off), empty constructor, GetSchedule, WithDay immutability, WithDay with null schedule, and custom schedules.
- **TimeZoneConverterTests.cs** — Tests TimeZoneConverter Convert/ConvertToUtc/ConvertFromUtc, round-trip preservation, GetTimeZone by ID, GetAvailableTimeZones, and null zone validation.
- **DateTimeProviderTests.cs** — Tests SystemDateTimeProvider (UtcNow, OffsetUtcNow, Today accuracy) and TestDateTimeProvider (default time, constructor with initial time, SetTime, Advance forward/backward/accumulated, Today reflection).

## Dependencies
- Birko.Time (shared project import)
- xUnit (2.9.3)
- FluentAssertions (7.0.0)
- Microsoft.NET.Test.Sdk (18.0.1)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
