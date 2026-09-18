using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Time.Tests;

public class WorkingHoursTests
{
    [Fact]
    public void Default_MondayToFridayAreWorking()
    {
        var wh = WorkingHours.Default;

        wh.IsWorkingDay(DayOfWeek.Monday).Should().BeTrue();
        wh.IsWorkingDay(DayOfWeek.Tuesday).Should().BeTrue();
        wh.IsWorkingDay(DayOfWeek.Wednesday).Should().BeTrue();
        wh.IsWorkingDay(DayOfWeek.Thursday).Should().BeTrue();
        wh.IsWorkingDay(DayOfWeek.Friday).Should().BeTrue();
    }

    [Fact]
    public void Default_WeekendIsNotWorking()
    {
        var wh = WorkingHours.Default;

        wh.IsWorkingDay(DayOfWeek.Saturday).Should().BeFalse();
        wh.IsWorkingDay(DayOfWeek.Sunday).Should().BeFalse();
    }

    [Fact]
    public void GetSchedule_WorkingDay_ReturnsSchedule()
    {
        var wh = WorkingHours.Default;

        var schedule = wh.GetSchedule(DayOfWeek.Monday);

        schedule.Should().NotBeNull();
        schedule!.Start.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void GetSchedule_NonWorkingDay_ReturnsNull()
    {
        var wh = WorkingHours.Default;

        wh.GetSchedule(DayOfWeek.Saturday).Should().BeNull();
    }

    [Fact]
    public void EmptyConstructor_AllDaysOff()
    {
        var wh = new WorkingHours();

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            wh.IsWorkingDay(day).Should().BeFalse();
        }
    }

    [Fact]
    public void WithDay_ReturnsNewInstance()
    {
        var original = new WorkingHours();
        var modified = original.WithDay(DayOfWeek.Monday, DaySchedule.Default);

        original.IsWorkingDay(DayOfWeek.Monday).Should().BeFalse();
        modified.IsWorkingDay(DayOfWeek.Monday).Should().BeTrue();
    }

    [Fact]
    public void WithDay_NullSchedule_MakesDayOff()
    {
        var wh = WorkingHours.Default.WithDay(DayOfWeek.Monday, null);

        wh.IsWorkingDay(DayOfWeek.Monday).Should().BeFalse();
        wh.IsWorkingDay(DayOfWeek.Tuesday).Should().BeTrue();
    }

    [Fact]
    public void WithDay_CustomSchedule()
    {
        var custom = new DaySchedule(new TimeOnly(6, 0), new TimeOnly(14, 0));
        var wh = new WorkingHours().WithDay(DayOfWeek.Saturday, custom);

        wh.IsWorkingDay(DayOfWeek.Saturday).Should().BeTrue();
        wh.GetSchedule(DayOfWeek.Saturday)!.Start.Should().Be(new TimeOnly(6, 0));
    }
}
