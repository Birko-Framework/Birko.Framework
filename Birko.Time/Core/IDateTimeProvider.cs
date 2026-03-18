using System;

namespace Birko.Time;

/// <summary>
/// Abstraction over the system clock for testability.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTimeOffset OffsetUtcNow { get; }
    DateOnly Today { get; }
}
