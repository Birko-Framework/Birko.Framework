using System;
using System.Collections.Generic;

namespace Birko.Time;

/// <summary>
/// Business calendar for working day and working hour calculations.
/// </summary>
public interface IBusinessCalendar
{
    bool IsBusinessDay(DateOnly date);
    bool IsHoliday(DateOnly date);
    bool IsWorkingTime(DateTimeOffset dateTime);
    DateOnly AddBusinessDays(DateOnly date, int days);
    int CountBusinessDays(DateOnly from, DateOnly to);
    DaySchedule? GetWorkingHours(DateOnly date);
    IReadOnlyList<Holiday> GetHolidays(int year);
}
