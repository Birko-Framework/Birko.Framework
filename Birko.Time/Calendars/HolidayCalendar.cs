using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Time;

/// <summary>
/// A named collection of holidays with composition support.
/// </summary>
public sealed class HolidayCalendar
{
    private readonly List<Holiday> _holidays;

    public string Name { get; }
    public IReadOnlyList<Holiday> Holidays => _holidays;

    public HolidayCalendar(string name, IEnumerable<Holiday>? holidays = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Calendar name cannot be empty.", nameof(name));
        }

        Name = name;
        _holidays = holidays?.ToList() ?? new List<Holiday>();
    }

    public bool IsHoliday(DateOnly date)
    {
        return _holidays.Any(h => h.FallsOn(date));
    }

    /// <summary>
    /// Returns the holidays relevant to <paramref name="year"/>: every recurring holiday plus the one-time
    /// holidays whose <see cref="Holiday.Year"/> matches.
    /// <para>
    /// CR-L386: recurring holidays are returned <b>year-agnostic</b> — their <see cref="Holiday.Year"/> stays
    /// <c>null</c>; they are not projected onto <paramref name="year"/>. Resolve a concrete date by combining
    /// <paramref name="year"/> with each holiday's <see cref="Holiday.Month"/>/<see cref="Holiday.Day"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<Holiday> GetHolidays(int year)
    {
        return _holidays
            .Where(h => h.IsRecurring || h.Year == year)
            .ToList();
    }

    /// <summary>
    /// Returns a new calendar that combines this calendar's holidays with another's.
    /// </summary>
    public HolidayCalendar With(HolidayCalendar other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        var combined = new List<Holiday>(_holidays);
        combined.AddRange(other._holidays);
        return new HolidayCalendar($"{Name}+{other.Name}", combined);
    }

    /// <summary>
    /// Returns a new calendar with an additional holiday.
    /// </summary>
    public HolidayCalendar WithHoliday(Holiday holiday)
    {
        if (holiday == null)
        {
            throw new ArgumentNullException(nameof(holiday));
        }

        var combined = new List<Holiday>(_holidays) { holiday };
        return new HolidayCalendar(Name, combined);
    }

    /// <summary>
    /// An empty calendar with no holidays.
    /// </summary>
    public static HolidayCalendar Empty { get; } = new("Empty");
}
