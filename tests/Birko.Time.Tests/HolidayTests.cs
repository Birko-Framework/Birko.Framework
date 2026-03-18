using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Time.Tests;

public class HolidayTests
{
    [Fact]
    public void Fixed_CreatesRecurringHoliday()
    {
        var holiday = Holiday.Fixed("Christmas", 12, 25);

        holiday.Name.Should().Be("Christmas");
        holiday.Month.Should().Be(12);
        holiday.Day.Should().Be(25);
        holiday.IsRecurring.Should().BeTrue();
        holiday.Year.Should().BeNull();
    }

    [Fact]
    public void OneTime_CreatesNonRecurringHoliday()
    {
        var holiday = Holiday.OneTime("Company Event", 2026, 7, 4);

        holiday.Name.Should().Be("Company Event");
        holiday.Month.Should().Be(7);
        holiday.Day.Should().Be(4);
        holiday.IsRecurring.Should().BeFalse();
        holiday.Year.Should().Be(2026);
    }

    [Fact]
    public void Fixed_FallsOn_MatchesAnyYear()
    {
        var holiday = Holiday.Fixed("New Year", 1, 1);

        holiday.FallsOn(new DateOnly(2025, 1, 1)).Should().BeTrue();
        holiday.FallsOn(new DateOnly(2026, 1, 1)).Should().BeTrue();
        holiday.FallsOn(new DateOnly(2030, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void Fixed_FallsOn_DoesNotMatchDifferentDate()
    {
        var holiday = Holiday.Fixed("Christmas", 12, 25);

        holiday.FallsOn(new DateOnly(2026, 12, 24)).Should().BeFalse();
        holiday.FallsOn(new DateOnly(2026, 1, 25)).Should().BeFalse();
    }

    [Fact]
    public void OneTime_FallsOn_MatchesOnlySpecificDate()
    {
        var holiday = Holiday.OneTime("Event", 2026, 5, 15);

        holiday.FallsOn(new DateOnly(2026, 5, 15)).Should().BeTrue();
        holiday.FallsOn(new DateOnly(2027, 5, 15)).Should().BeFalse();
    }

    [Fact]
    public void Fixed_EmptyName_Throws()
    {
        var act = () => Holiday.Fixed("", 1, 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Fixed_InvalidMonth_Throws()
    {
        var act = () => Holiday.Fixed("Bad", 13, 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Fixed_InvalidDay_Throws()
    {
        var act = () => Holiday.Fixed("Bad", 1, 32);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToString_Recurring_ShowsMonthDay()
    {
        var holiday = Holiday.Fixed("Christmas", 12, 25);

        holiday.ToString().Should().Contain("12-25").And.Contain("recurring");
    }

    [Fact]
    public void ToString_OneTime_ShowsFullDate()
    {
        var holiday = Holiday.OneTime("Event", 2026, 3, 15);

        holiday.ToString().Should().Contain("2026").And.Contain("03-15");
    }
}
