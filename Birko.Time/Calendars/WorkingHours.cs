namespace Birko.Time;

/// <summary>
/// Weekly working hours schedule mapping each day of the week to its schedule.
/// A null schedule means the day is not a working day.
/// </summary>
public sealed class WorkingHours
{
    private readonly Dictionary<DayOfWeek, DaySchedule?> _schedule;

    public WorkingHours(Dictionary<DayOfWeek, DaySchedule?>? schedule = null)
    {
        _schedule = new Dictionary<DayOfWeek, DaySchedule?>();

        if (schedule != null)
        {
            foreach (var kvp in schedule)
            {
                _schedule[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Gets the schedule for the given day, or null if it's not a working day.
    /// </summary>
    public DaySchedule? GetSchedule(DayOfWeek day)
    {
        return _schedule.TryGetValue(day, out var schedule) ? schedule : null;
    }

    /// <summary>
    /// Returns true if the given day has a working schedule.
    /// </summary>
    public bool IsWorkingDay(DayOfWeek day)
    {
        return _schedule.TryGetValue(day, out var schedule) && schedule != null;
    }

    /// <summary>
    /// Returns a new WorkingHours with the specified day's schedule set.
    /// </summary>
    public WorkingHours WithDay(DayOfWeek day, DaySchedule? schedule)
    {
        var newSchedule = new Dictionary<DayOfWeek, DaySchedule?>(_schedule)
        {
            [day] = schedule
        };
        return new WorkingHours(newSchedule);
    }

    /// <summary>
    /// Default: Monday-Friday, 09:00-17:00 with 1h break. Saturday/Sunday off.
    /// </summary>
    public static WorkingHours Default { get; } = new(new Dictionary<DayOfWeek, DaySchedule?>
    {
        [DayOfWeek.Monday] = DaySchedule.Default,
        [DayOfWeek.Tuesday] = DaySchedule.Default,
        [DayOfWeek.Wednesday] = DaySchedule.Default,
        [DayOfWeek.Thursday] = DaySchedule.Default,
        [DayOfWeek.Friday] = DaySchedule.Default,
    });
}
