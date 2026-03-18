using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Time.Tests;

public class DayScheduleTests
{
    [Fact]
    public void Default_Is9To17With1HourBreak()
    {
        var schedule = DaySchedule.Default;

        schedule.Start.Should().Be(new TimeOnly(9, 0));
        schedule.End.Should().Be(new TimeOnly(17, 0));
        schedule.BreakDuration.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void WorkingSpan_ReturnsStartToEnd()
    {
        var schedule = new DaySchedule(new TimeOnly(8, 0), new TimeOnly(16, 0));

        schedule.WorkingSpan.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void WorkingDuration_SubtractsBreak()
    {
        var schedule = new DaySchedule(
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            TimeSpan.FromHours(1));

        schedule.WorkingDuration.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public void WorkingDuration_NoBreak_EqualsSpan()
    {
        var schedule = new DaySchedule(new TimeOnly(9, 0), new TimeOnly(17, 0));

        schedule.WorkingDuration.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void IsWorkingAt_WithinHours_ReturnsTrue()
    {
        var schedule = new DaySchedule(new TimeOnly(9, 0), new TimeOnly(17, 0));

        schedule.IsWorkingAt(new TimeOnly(12, 0)).Should().BeTrue();
        schedule.IsWorkingAt(new TimeOnly(9, 0)).Should().BeTrue();
        schedule.IsWorkingAt(new TimeOnly(16, 59)).Should().BeTrue();
    }

    [Fact]
    public void IsWorkingAt_OutsideHours_ReturnsFalse()
    {
        var schedule = new DaySchedule(new TimeOnly(9, 0), new TimeOnly(17, 0));

        schedule.IsWorkingAt(new TimeOnly(8, 59)).Should().BeFalse();
        schedule.IsWorkingAt(new TimeOnly(17, 0)).Should().BeFalse();
        schedule.IsWorkingAt(new TimeOnly(23, 0)).Should().BeFalse();
    }

    [Fact]
    public void Constructor_EndBeforeStart_Throws()
    {
        var act = () => new DaySchedule(new TimeOnly(17, 0), new TimeOnly(9, 0));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NegativeBreak_Throws()
    {
        var act = () => new DaySchedule(
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            TimeSpan.FromHours(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_BreakExceedsSpan_Throws()
    {
        var act = () => new DaySchedule(
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            TimeSpan.FromHours(8));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ContainsTimeRange()
    {
        var schedule = new DaySchedule(new TimeOnly(9, 0), new TimeOnly(17, 0), TimeSpan.FromMinutes(30));

        schedule.ToString().Should().Contain("09:00").And.Contain("17:00").And.Contain("30");
    }
}
