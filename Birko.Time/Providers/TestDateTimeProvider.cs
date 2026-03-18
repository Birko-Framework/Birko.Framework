namespace Birko.Time;

/// <summary>
/// Controllable clock for unit tests. Allows freezing and advancing time.
/// </summary>
public sealed class TestDateTimeProvider : IDateTimeProvider
{
    private DateTimeOffset _current;

    public TestDateTimeProvider(DateTimeOffset? initialTime = null)
    {
        _current = initialTime ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTime UtcNow => _current.UtcDateTime;
    public DateTimeOffset OffsetUtcNow => _current;
    public DateOnly Today => DateOnly.FromDateTime(_current.UtcDateTime);

    /// <summary>
    /// Sets the clock to a specific time.
    /// </summary>
    public void SetTime(DateTimeOffset time)
    {
        _current = time;
    }

    /// <summary>
    /// Advances the clock by the given duration.
    /// </summary>
    public void Advance(TimeSpan duration)
    {
        _current = _current.Add(duration);
    }
}
