using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Time.Tests;

public class HolidayCalendarTests
{
    [Fact]
    public void Empty_HasNoHolidays()
    {
        HolidayCalendar.Empty.Holidays.Should().BeEmpty();
        HolidayCalendar.Empty.IsHoliday(new DateOnly(2026, 1, 1)).Should().BeFalse();
    }

    [Fact]
    public void Constructor_EmptyName_Throws()
    {
        var act = () => new HolidayCalendar("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsHoliday_RecurringHoliday_ReturnsTrue()
    {
        var calendar = new HolidayCalendar("Test", new[]
        {
            Holiday.Fixed("New Year", 1, 1)
        });

        calendar.IsHoliday(new DateOnly(2026, 1, 1)).Should().BeTrue();
        calendar.IsHoliday(new DateOnly(2027, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void IsHoliday_NonHolidayDate_ReturnsFalse()
    {
        var calendar = new HolidayCalendar("Test", new[]
        {
            Holiday.Fixed("Christmas", 12, 25)
        });

        calendar.IsHoliday(new DateOnly(2026, 6, 15)).Should().BeFalse();
    }

    [Fact]
    public void GetHolidays_ReturnsRecurringAndMatchingYear()
    {
        var calendar = new HolidayCalendar("Test", new[]
        {
            Holiday.Fixed("New Year", 1, 1),
            Holiday.OneTime("Event 2026", 2026, 5, 15),
            Holiday.OneTime("Event 2027", 2027, 6, 20),
        });

        var holidays2026 = calendar.GetHolidays(2026);
        holidays2026.Should().HaveCount(2);
        holidays2026.Should().Contain(h => h.Name == "New Year");
        holidays2026.Should().Contain(h => h.Name == "Event 2026");
    }

    [Fact]
    public void With_CombinesCalendars()
    {
        var cal1 = new HolidayCalendar("A", new[] { Holiday.Fixed("H1", 1, 1) });
        var cal2 = new HolidayCalendar("B", new[] { Holiday.Fixed("H2", 12, 25) });

        var combined = cal1.With(cal2);

        combined.Name.Should().Be("A+B");
        combined.Holidays.Should().HaveCount(2);
        combined.IsHoliday(new DateOnly(2026, 1, 1)).Should().BeTrue();
        combined.IsHoliday(new DateOnly(2026, 12, 25)).Should().BeTrue();
    }

    [Fact]
    public void WithHoliday_AddsHoliday()
    {
        var calendar = new HolidayCalendar("Test");
        var updated = calendar.WithHoliday(Holiday.Fixed("New Year", 1, 1));

        calendar.Holidays.Should().BeEmpty();
        updated.Holidays.Should().HaveCount(1);
    }

    [Fact]
    public void With_NullCalendar_Throws()
    {
        var calendar = new HolidayCalendar("Test");

        var act = () => calendar.With(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithHoliday_NullHoliday_Throws()
    {
        var calendar = new HolidayCalendar("Test");

        var act = () => calendar.WithHoliday(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
