using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Time.Tests;

public class BusinessCalendarTests
{
    private static BusinessCalendar CreateDefaultCalendar()
    {
        var holidays = new HolidayCalendar("Test", new[]
        {
            Holiday.Fixed("New Year", 1, 1),
            Holiday.Fixed("Christmas", 12, 25),
        });

        return new BusinessCalendar(WorkingHours.Default, holidays, TimeZoneInfo.Utc);
    }

    [Fact]
    public void IsBusinessDay_Weekday_ReturnsTrue()
    {
        var calendar = CreateDefaultCalendar();

        // 2026-03-16 is Monday
        calendar.IsBusinessDay(new DateOnly(2026, 3, 16)).Should().BeTrue();
    }

    [Fact]
    public void IsBusinessDay_Weekend_ReturnsFalse()
    {
        var calendar = CreateDefaultCalendar();

        // 2026-03-14 is Saturday
        calendar.IsBusinessDay(new DateOnly(2026, 3, 14)).Should().BeFalse();
    }

    [Fact]
    public void IsBusinessDay_Holiday_ReturnsFalse()
    {
        var calendar = CreateDefaultCalendar();

        // 2026-01-01 is Thursday (New Year)
        calendar.IsBusinessDay(new DateOnly(2026, 1, 1)).Should().BeFalse();
    }

    [Fact]
    public void IsHoliday_Holiday_ReturnsTrue()
    {
        var calendar = CreateDefaultCalendar();

        calendar.IsHoliday(new DateOnly(2026, 12, 25)).Should().BeTrue();
    }

    [Fact]
    public void IsHoliday_NonHoliday_ReturnsFalse()
    {
        var calendar = CreateDefaultCalendar();

        calendar.IsHoliday(new DateOnly(2026, 6, 15)).Should().BeFalse();
    }

    [Fact]
    public void IsWorkingTime_DuringWorkHours_ReturnsTrue()
    {
        var calendar = CreateDefaultCalendar();

        // Monday 10:00 UTC
        var time = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);
        calendar.IsWorkingTime(time).Should().BeTrue();
    }

    [Fact]
    public void IsWorkingTime_OutsideWorkHours_ReturnsFalse()
    {
        var calendar = CreateDefaultCalendar();

        // Monday 20:00 UTC
        var time = new DateTimeOffset(2026, 3, 16, 20, 0, 0, TimeSpan.Zero);
        calendar.IsWorkingTime(time).Should().BeFalse();
    }

    [Fact]
    public void IsWorkingTime_Weekend_ReturnsFalse()
    {
        var calendar = CreateDefaultCalendar();

        // Saturday 10:00 UTC
        var time = new DateTimeOffset(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);
        calendar.IsWorkingTime(time).Should().BeFalse();
    }

    [Fact]
    public void IsWorkingTime_Holiday_ReturnsFalse()
    {
        var calendar = CreateDefaultCalendar();

        // New Year 10:00 UTC (Thursday)
        var time = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        calendar.IsWorkingTime(time).Should().BeFalse();
    }

    [Fact]
    public void AddBusinessDays_SkipsWeekend()
    {
        var calendar = CreateDefaultCalendar();

        // Friday 2026-03-13 + 1 business day = Monday 2026-03-16
        var result = calendar.AddBusinessDays(new DateOnly(2026, 3, 13), 1);

        result.Should().Be(new DateOnly(2026, 3, 16));
    }

    [Fact]
    public void AddBusinessDays_SkipsHoliday()
    {
        var calendar = CreateDefaultCalendar();

        // 2025-12-24 (Wednesday) + 1 = 2025-12-26 (Friday, skips Christmas Thursday 12/25)
        var result = calendar.AddBusinessDays(new DateOnly(2025, 12, 24), 1);

        result.Should().Be(new DateOnly(2025, 12, 26));
    }

    [Fact]
    public void AddBusinessDays_Zero_ReturnsSameDate()
    {
        var calendar = CreateDefaultCalendar();
        var date = new DateOnly(2026, 3, 16);

        calendar.AddBusinessDays(date, 0).Should().Be(date);
    }

    [Fact]
    public void AddBusinessDays_Negative_GoesBackward()
    {
        var calendar = CreateDefaultCalendar();

        // Monday 2026-03-16 - 1 business day = Friday 2026-03-13
        var result = calendar.AddBusinessDays(new DateOnly(2026, 3, 16), -1);

        result.Should().Be(new DateOnly(2026, 3, 13));
    }

    [Fact]
    public void AddBusinessDays_MultipleDays()
    {
        var calendar = CreateDefaultCalendar();

        // Monday 2026-03-16 + 5 = Monday 2026-03-23
        var result = calendar.AddBusinessDays(new DateOnly(2026, 3, 16), 5);

        result.Should().Be(new DateOnly(2026, 3, 23));
    }

    [Fact]
    public void CountBusinessDays_SameWeek()
    {
        var calendar = CreateDefaultCalendar();

        // Mon 2026-03-16 to Fri 2026-03-20 = 4 business days
        var count = calendar.CountBusinessDays(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 20));

        count.Should().Be(4);
    }

    [Fact]
    public void CountBusinessDays_AcrossWeekend()
    {
        var calendar = CreateDefaultCalendar();

        // Fri 2026-03-13 to Mon 2026-03-16 = 1 business day
        var count = calendar.CountBusinessDays(new DateOnly(2026, 3, 13), new DateOnly(2026, 3, 16));

        count.Should().Be(1);
    }

    [Fact]
    public void CountBusinessDays_ReversedDates_ReturnsNegative()
    {
        var calendar = CreateDefaultCalendar();

        var count = calendar.CountBusinessDays(new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 16));

        count.Should().Be(-4);
    }

    [Fact]
    public void CountBusinessDays_SameDate_ReturnsZero()
    {
        var calendar = CreateDefaultCalendar();

        calendar.CountBusinessDays(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 16)).Should().Be(0);
    }

    [Fact]
    public void GetWorkingHours_BusinessDay_ReturnsSchedule()
    {
        var calendar = CreateDefaultCalendar();

        var schedule = calendar.GetWorkingHours(new DateOnly(2026, 3, 16));

        schedule.Should().NotBeNull();
        schedule!.Start.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void GetWorkingHours_Weekend_ReturnsNull()
    {
        var calendar = CreateDefaultCalendar();

        calendar.GetWorkingHours(new DateOnly(2026, 3, 14)).Should().BeNull();
    }

    [Fact]
    public void GetWorkingHours_Holiday_ReturnsNull()
    {
        var calendar = CreateDefaultCalendar();

        calendar.GetWorkingHours(new DateOnly(2026, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void GetHolidays_ReturnsHolidaysForYear()
    {
        var calendar = CreateDefaultCalendar();

        var holidays = calendar.GetHolidays(2026);

        holidays.Should().HaveCount(2);
    }

    [Fact]
    public void DefaultConstructor_WorksWithDefaults()
    {
        var calendar = new BusinessCalendar();

        // Should work without exceptions using default working hours and no holidays
        calendar.IsBusinessDay(new DateOnly(2026, 3, 16)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2026, 3, 14)).Should().BeFalse();
    }
}
