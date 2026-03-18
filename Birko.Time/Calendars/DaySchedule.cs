namespace Birko.Time;

/// <summary>
/// Defines working hours for a single day.
/// </summary>
public sealed class DaySchedule
{
    public TimeOnly Start { get; }
    public TimeOnly End { get; }
    public TimeSpan BreakDuration { get; }

    public DaySchedule(TimeOnly start, TimeOnly end, TimeSpan? breakDuration = null)
    {
        if (end <= start)
        {
            throw new ArgumentException("End time must be after start time.", nameof(end));
        }

        if (breakDuration.HasValue && breakDuration.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(breakDuration), "Break duration cannot be negative.");
        }

        Start = start;
        End = end;
        BreakDuration = breakDuration ?? TimeSpan.Zero;

        if (BreakDuration >= WorkingSpan)
        {
            throw new ArgumentException("Break duration must be less than the working span.", nameof(breakDuration));
        }
    }

    /// <summary>
    /// Total span from start to end (before subtracting breaks).
    /// </summary>
    public TimeSpan WorkingSpan => End - Start;

    /// <summary>
    /// Net working duration (span minus breaks).
    /// </summary>
    public TimeSpan WorkingDuration => WorkingSpan - BreakDuration;

    /// <summary>
    /// Checks if the given time falls within this schedule's working hours.
    /// </summary>
    public bool IsWorkingAt(TimeOnly time)
    {
        return time >= Start && time < End;
    }

    /// <summary>
    /// Default schedule: 09:00 - 17:00 with 1 hour break.
    /// </summary>
    public static DaySchedule Default { get; } = new(
        new TimeOnly(9, 0),
        new TimeOnly(17, 0),
        TimeSpan.FromHours(1));

    public override string ToString()
    {
        return $"{Start:HH:mm}-{End:HH:mm} (break: {BreakDuration.TotalMinutes}min)";
    }
}
