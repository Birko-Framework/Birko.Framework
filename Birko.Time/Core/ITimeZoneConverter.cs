namespace Birko.Time;

/// <summary>
/// Converts between time zones.
/// </summary>
public interface ITimeZoneConverter
{
    DateTimeOffset Convert(DateTimeOffset dateTime, TimeZoneInfo targetZone);
    DateTimeOffset ConvertToUtc(DateTimeOffset dateTime);
    DateTimeOffset ConvertFromUtc(DateTimeOffset utcDateTime, TimeZoneInfo targetZone);
    TimeZoneInfo GetTimeZone(string timeZoneId);
    IReadOnlyList<TimeZoneInfo> GetAvailableTimeZones();
}
