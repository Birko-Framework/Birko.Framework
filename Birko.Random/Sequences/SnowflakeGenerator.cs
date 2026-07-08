using System;
using System.Threading;

namespace Birko.Random;

/// <summary>
/// Generates unique, time-ordered 64-bit IDs inspired by Twitter Snowflake.
/// Layout: 1 bit unused | 41 bits timestamp | 10 bits machine ID | 12 bits sequence.
/// Supports ~4096 IDs per millisecond per machine.
/// Thread-safe.
/// </summary>
public sealed class SnowflakeGenerator
{
    private const int TimestampBits = 41;
    private const int MachineIdBits = 10;
    private const int SequenceBits = 12;

    private const long MaxMachineId = (1L << MachineIdBits) - 1;
    private const long MaxSequence = (1L << SequenceBits) - 1;

    private const int MachineIdShift = SequenceBits;
    private const int TimestampShift = SequenceBits + MachineIdBits;

    private readonly long _machineId;
    private readonly long _epoch;

    private long _lastTimestamp = -1;
    private long _sequence;
    private readonly object _lock = new();

    /// <param name="machineId">Machine/worker ID (0 to 1023).</param>
    /// <param name="epoch">Custom epoch as Unix milliseconds. Defaults to 2024-01-01T00:00:00Z.</param>
    public SnowflakeGenerator(int machineId, long? epoch = null)
    {
        if (machineId < 0 || machineId > MaxMachineId)
        {
            throw new ArgumentOutOfRangeException(nameof(machineId),
                $"Machine ID must be between 0 and {MaxMachineId}.");
        }

        _machineId = machineId;
        _epoch = epoch ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Generates the next unique Snowflake ID.
    /// </summary>
    public long Next()
    {
        lock (_lock)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _epoch;

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    // Sequence exhausted — wait for next millisecond
                    timestamp = WaitNextMillisecond(_lastTimestamp);
                }
            }
            else
            {
                _sequence = 0;
            }

            if (timestamp < _lastTimestamp)
            {
                throw new InvalidOperationException(
                    $"Clock moved backwards. Refusing to generate ID for {_lastTimestamp - timestamp}ms.");
            }

            _lastTimestamp = timestamp;

            return (timestamp << TimestampShift)
                   | (_machineId << MachineIdShift)
                   | _sequence;
        }
    }

    /// <summary>
    /// Extracts the timestamp from a Snowflake ID.
    /// </summary>
    public DateTimeOffset ExtractTimestamp(long id)
    {
        long timestamp = (id >> TimestampShift) + _epoch;
        return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
    }

    /// <summary>
    /// Extracts the machine ID from a Snowflake ID.
    /// </summary>
    public static int ExtractMachineId(long id)
    {
        return (int)((id >> MachineIdShift) & MaxMachineId);
    }

    /// <summary>
    /// Extracts the sequence number from a Snowflake ID.
    /// </summary>
    public static int ExtractSequence(long id)
    {
        return (int)(id & MaxSequence);
    }

    // Returns an EPOCH-RELATIVE timestamp, consistent with Next() (which works in
    // `UtcNow - _epoch`). Returning raw absolute Unix ms here made the `<= lastTimestamp`
    // comparison always false on the first iteration (absolute ≫ relative), so the loop
    // returned ~1.7e12 which overflowed the 41-bit timestamp field and corrupted the ID.
    private long WaitNextMillisecond(long lastTimestamp)
    {
        long timestamp;
        do
        {
            Thread.SpinWait(10);
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _epoch;
        } while (timestamp <= lastTimestamp);

        return timestamp;
    }
}
