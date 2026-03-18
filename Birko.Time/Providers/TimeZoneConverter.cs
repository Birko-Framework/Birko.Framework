namespace Birko.Time;

/// <summary>
/// Time zone converter using System.TimeZoneInfo.
/// Supports both Windows and IANA time zone IDs on .NET 6+.
/// </summary>
public sealed class TimeZoneConverter : ITimeZoneConverter
{
    public DateTimeOffset Convert(DateTimeOffset dateTime, TimeZoneInfo targetZone)
    {
        if (targetZone == null)
        {
            throw new ArgumentNullException(nameof(targetZone));
        }

        return TimeZoneInfo.ConvertTime(dateTime, targetZone);
    }

    public DateTimeOffset ConvertToUtc(DateTimeOffset dateTime)
    {
        return dateTime.ToUniversalTime();
    }

    public DateTimeOffset ConvertFromUtc(DateTimeOffset utcDateTime, TimeZoneInfo targetZone)
    {
        if (targetZone == null)
        {
            throw new ArgumentNullException(nameof(targetZone));
        }

        return TimeZoneInfo.ConvertTime(utcDateTime.ToUniversalTime(), targetZone);
    }

    public TimeZoneInfo GetTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("Time zone ID cannot be empty.", nameof(timeZoneId));
        }

        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public IReadOnlyList<TimeZoneInfo> GetAvailableTimeZones()
    {
        return TimeZoneInfo.GetSystemTimeZones().ToList();
    }
}
