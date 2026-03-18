# Birko.Time

Time utilities for the Birko Framework — testable clock abstraction, time zone conversion, and business calendar.

## Architecture

```
Birko.Time/
├── Core/
│   ├── IDateTimeProvider.cs          Clock abstraction
│   ├── ITimeZoneConverter.cs         Time zone conversion
│   └── IBusinessCalendar.cs          Business calendar interface
├── Calendars/
│   ├── Holiday.cs                    Holiday definition (Fixed/OneTime)
│   ├── HolidayCalendar.cs           Named, composable holiday collection
│   ├── DaySchedule.cs               Single day working hours
│   ├── WorkingHours.cs              Weekly schedule
│   └── BusinessCalendar.cs          IBusinessCalendar implementation
└── Providers/
    ├── SystemDateTimeProvider.cs     Production clock
    ├── TestDateTimeProvider.cs       Controllable test clock
    └── TimeZoneConverter.cs          System.TimeZoneInfo wrapper
```

## Core Interfaces

### IDateTimeProvider

Abstraction over the system clock. Use this instead of `DateTime.Now` or `DateTimeOffset.UtcNow` directly.

```csharp
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTimeOffset OffsetUtcNow { get; }
    DateOnly Today { get; }
}
```

**Implementations:**
- `SystemDateTimeProvider` — delegates to system clock
- `TestDateTimeProvider` — controllable clock with `SetTime()` and `Advance()`

### ITimeZoneConverter

```csharp
public interface ITimeZoneConverter
{
    DateTimeOffset Convert(DateTimeOffset dateTime, TimeZoneInfo targetZone);
    DateTimeOffset ConvertToUtc(DateTimeOffset dateTime);
    DateTimeOffset ConvertFromUtc(DateTimeOffset utcDateTime, TimeZoneInfo targetZone);
    TimeZoneInfo GetTimeZone(string timeZoneId);
    IReadOnlyList<TimeZoneInfo> GetAvailableTimeZones();
}
```

Supports both Windows (`Central European Standard Time`) and IANA (`Europe/Prague`) time zone IDs on .NET 6+.

### IBusinessCalendar

```csharp
public interface IBusinessCalendar
{
    bool IsBusinessDay(DateOnly date);
    bool IsHoliday(DateOnly date);
    bool IsWorkingTime(DateTimeOffset dateTime);
    DateOnly AddBusinessDays(DateOnly date, int days);
    int CountBusinessDays(DateOnly from, DateOnly to);
    DaySchedule? GetWorkingHours(DateOnly date);
    IReadOnlyList<Holiday> GetHolidays(int year);
}
```

## Calendar Models

### Holiday

Immutable. Two factory methods:

```csharp
// Recurring — falls on Dec 25 every year
var christmas = Holiday.Fixed("Christmas", 12, 25);

// One-time — only in 2026
var event2026 = Holiday.OneTime("Company Day", 2026, 6, 15);

// Check
christmas.FallsOn(new DateOnly(2030, 12, 25)); // true
event2026.FallsOn(new DateOnly(2027, 6, 15));  // false
```

### HolidayCalendar

Named, composable collection:

```csharp
var national = new HolidayCalendar("National", new[]
{
    Holiday.Fixed("New Year", 1, 1),
    Holiday.Fixed("Independence Day", 7, 4),
});

var company = new HolidayCalendar("Company", new[]
{
    Holiday.OneTime("Team Building", 2026, 9, 15),
});

// Compose
var combined = national.With(company);

// Or add one holiday
var updated = national.WithHoliday(Holiday.Fixed("Christmas", 12, 25));
```

### DaySchedule

Working hours for a single day:

```csharp
// Default: 09:00 - 17:00, 1h break
var standard = DaySchedule.Default;
standard.WorkingDuration; // 7 hours

// Custom
var early = new DaySchedule(new TimeOnly(6, 0), new TimeOnly(14, 0), TimeSpan.FromMinutes(30));
early.IsWorkingAt(new TimeOnly(10, 0)); // true
early.IsWorkingAt(new TimeOnly(15, 0)); // false
```

### WorkingHours

Weekly schedule:

```csharp
// Default: Mon-Fri with DaySchedule.Default
var standard = WorkingHours.Default;

// Custom
var custom = new WorkingHours()
    .WithDay(DayOfWeek.Monday, new DaySchedule(new TimeOnly(8, 0), new TimeOnly(16, 0)))
    .WithDay(DayOfWeek.Tuesday, DaySchedule.Default)
    .WithDay(DayOfWeek.Saturday, new DaySchedule(new TimeOnly(9, 0), new TimeOnly(13, 0)));
```

## BusinessCalendar Usage

```csharp
var calendar = new BusinessCalendar(
    WorkingHours.Default,
    holidays,
    TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"));

// Is today a business day?
calendar.IsBusinessDay(DateOnly.FromDateTime(DateTime.Today));

// When is 5 business days from now?
calendar.AddBusinessDays(new DateOnly(2026, 3, 13), 5);

// How many business days in March?
calendar.CountBusinessDays(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

// Is it currently working time? (converts to calendar's timezone first)
calendar.IsWorkingTime(DateTimeOffset.UtcNow);
```

## Testing

```csharp
// Freeze time for deterministic tests
var clock = new TestDateTimeProvider(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

var service = new MyService(clock);
service.DoWork();

// Advance time
clock.Advance(TimeSpan.FromHours(8));

// Verify time-dependent behavior
clock.UtcNow; // 2026-01-01 17:00 UTC
```

## Dependencies

None. Uses only `System.TimeZoneInfo` from the BCL.
