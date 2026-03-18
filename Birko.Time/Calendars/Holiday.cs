using System;

namespace Birko.Time;

/// <summary>
/// Represents a holiday — either recurring (same date every year) or one-time.
/// </summary>
public sealed class Holiday
{
    public string Name { get; }
    public int Month { get; }
    public int Day { get; }
    public bool IsRecurring { get; }
    public int? Year { get; }

    private Holiday(string name, int month, int day, bool isRecurring, int? year)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Holiday name cannot be empty.", nameof(name));
        }

        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");
        }

        if (day < 1 || day > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), "Day must be between 1 and 31.");
        }

        Name = name;
        Month = month;
        Day = day;
        IsRecurring = isRecurring;
        Year = year;
    }

    /// <summary>
    /// Creates a recurring holiday that falls on the same date every year.
    /// </summary>
    public static Holiday Fixed(string name, int month, int day)
    {
        return new Holiday(name, month, day, isRecurring: true, year: null);
    }

    /// <summary>
    /// Creates a one-time holiday for a specific date.
    /// </summary>
    public static Holiday OneTime(string name, int year, int month, int day)
    {
        return new Holiday(name, month, day, isRecurring: false, year: year);
    }

    /// <summary>
    /// Checks whether this holiday falls on the given date.
    /// </summary>
    public bool FallsOn(DateOnly date)
    {
        if (IsRecurring)
        {
            return date.Month == Month && date.Day == Day;
        }

        return date.Year == Year && date.Month == Month && date.Day == Day;
    }

    public override string ToString()
    {
        return IsRecurring
            ? $"{Name} ({Month:D2}-{Day:D2}, recurring)"
            : $"{Name} ({Year}-{Month:D2}-{Day:D2})";
    }
}
