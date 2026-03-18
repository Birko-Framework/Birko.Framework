# Birko.Time

Time utilities for the Birko Framework — testable clock abstraction, time zone conversion, and business calendar with holiday and working hours support.

## Features

- **IDateTimeProvider** — Injectable clock abstraction for testability
  - `SystemDateTimeProvider` — Production implementation (system clock)
  - `TestDateTimeProvider` — Controllable clock for unit tests (freeze, advance)
- **ITimeZoneConverter** — Time zone conversion using `System.TimeZoneInfo`
  - Supports both Windows and IANA time zone IDs (.NET 6+)
- **IBusinessCalendar** — Business day and working hour calculations
  - Working day checks (weekends + holidays)
  - Add/count business days
  - Working time checks (time-zone-aware)
- **Holiday** — Recurring (annual) and one-time holiday definitions
- **HolidayCalendar** — Named, composable holiday collections
- **DaySchedule** — Per-day working hours with break duration
- **WorkingHours** — Weekly schedule (day-of-week to schedule mapping)

## Usage

### Clock Abstraction

```csharp
// Production code — inject IDateTimeProvider
public class OrderService
{
    private readonly IDateTimeProvider _clock;

    public OrderService(IDateTimeProvider clock) => _clock = clock;

    public Order CreateOrder()
    {
        return new Order { CreatedAt = _clock.OffsetUtcNow };
    }
}

// Registration
services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

// In tests
var clock = new TestDateTimeProvider(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));
clock.Advance(TimeSpan.FromHours(2));
// clock.UtcNow is now 2026-03-15 12:00 UTC
```

### Time Zone Conversion

```csharp
var converter = new TimeZoneConverter();

var utcTime = DateTimeOffset.UtcNow;
var pragueZone = converter.GetTimeZone("Europe/Prague");
var pragueTime = converter.ConvertFromUtc(utcTime, pragueZone);
```

### Business Calendar

```csharp
// Define holidays
var holidays = new HolidayCalendar("Slovakia", new[]
{
    Holiday.Fixed("New Year", 1, 1),
    Holiday.Fixed("Christmas", 12, 25),
    Holiday.Fixed("Christmas 2", 12, 26),
    Holiday.OneTime("Company Day", 2026, 6, 15),
});

// Define working hours (default: Mon-Fri 9-17, 1h break)
var workingHours = WorkingHours.Default;

// Or custom
var custom = new WorkingHours()
    .WithDay(DayOfWeek.Monday, new DaySchedule(new TimeOnly(8, 0), new TimeOnly(16, 0), TimeSpan.FromMinutes(30)))
    .WithDay(DayOfWeek.Tuesday, new DaySchedule(new TimeOnly(8, 0), new TimeOnly(16, 0), TimeSpan.FromMinutes(30)));

// Create calendar
var calendar = new BusinessCalendar(workingHours, holidays, TimeZoneInfo.Utc);

// Business day operations
bool isWorkDay = calendar.IsBusinessDay(new DateOnly(2026, 3, 16)); // true (Monday)
DateOnly deadline = calendar.AddBusinessDays(new DateOnly(2026, 3, 13), 5); // skips weekend
int workDays = calendar.CountBusinessDays(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

// Working time check (time-zone-aware)
bool isWorking = calendar.IsWorkingTime(DateTimeOffset.UtcNow);
```

### Holiday Calendar Composition

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

var combined = national.With(company);
```

## Dependencies

None.

## License

MIT License — see [License.md](License.md) for details.
