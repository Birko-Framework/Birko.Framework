namespace Birko.Time;

/// <summary>
/// Business calendar combining working hours and holidays.
/// Optionally time-zone-aware for distributed systems.
/// </summary>
public sealed class BusinessCalendar : IBusinessCalendar
{
    private readonly WorkingHours _workingHours;
    private readonly HolidayCalendar _holidayCalendar;
    private readonly TimeZoneInfo _timeZone;

    public BusinessCalendar(
        WorkingHours? workingHours = null,
        HolidayCalendar? holidayCalendar = null,
        TimeZoneInfo? timeZone = null)
    {
        _workingHours = workingHours ?? WorkingHours.Default;
        _holidayCalendar = holidayCalendar ?? HolidayCalendar.Empty;
        _timeZone = timeZone ?? TimeZoneInfo.Utc;
    }

    public bool IsBusinessDay(DateOnly date)
    {
        return _workingHours.IsWorkingDay(date.DayOfWeek) && !_holidayCalendar.IsHoliday(date);
    }

    public bool IsHoliday(DateOnly date)
    {
        return _holidayCalendar.IsHoliday(date);
    }

    public bool IsWorkingTime(DateTimeOffset dateTime)
    {
        var local = TimeZoneInfo.ConvertTime(dateTime, _timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);

        if (!IsBusinessDay(date))
        {
            return false;
        }

        var schedule = _workingHours.GetSchedule(date.DayOfWeek);
        if (schedule == null)
        {
            return false;
        }

        var time = TimeOnly.FromDateTime(local.DateTime);
        return schedule.IsWorkingAt(time);
    }

    public DateOnly AddBusinessDays(DateOnly date, int days)
    {
        if (days == 0)
        {
            return date;
        }

        var direction = days > 0 ? 1 : -1;
        var remaining = Math.Abs(days);
        var current = date;

        while (remaining > 0)
        {
            current = current.AddDays(direction);
            if (IsBusinessDay(current))
            {
                remaining--;
            }
        }

        return current;
    }

    public int CountBusinessDays(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            return -CountBusinessDays(to, from);
        }

        var count = 0;
        var current = from;

        while (current < to)
        {
            current = current.AddDays(1);
            if (IsBusinessDay(current))
            {
                count++;
            }
        }

        return count;
    }

    public DaySchedule? GetWorkingHours(DateOnly date)
    {
        if (!IsBusinessDay(date))
        {
            return null;
        }

        return _workingHours.GetSchedule(date.DayOfWeek);
    }

    public IReadOnlyList<Holiday> GetHolidays(int year)
    {
        return _holidayCalendar.GetHolidays(year);
    }
}
