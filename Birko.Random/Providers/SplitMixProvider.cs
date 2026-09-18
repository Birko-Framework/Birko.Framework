using System;

namespace Birko.Random;

/// <summary>
/// SplitMix64 random number generator. Very fast PRNG with 64-bit state.
/// Excellent statistical quality for its simplicity. Commonly used for seeding other PRNGs.
/// Not thread-safe — use one instance per thread.
/// </summary>
public sealed class SplitMixProvider : IRandomProvider
{
    private ulong _state;

    public SplitMixProvider() : this(unchecked((ulong)Environment.TickCount64))
    {
    }

    public SplitMixProvider(ulong seed)
    {
        _state = seed;
    }

    private ulong NextUInt64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    public int NextInt() => (int)(NextUInt64() >> 33);

    public int NextInt(int maxValue)
    {
        if (maxValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive.");
        }

        return (int)(NextDouble() * maxValue);
    }

    public int NextInt(int minValue, int maxValue)
    {
        if (minValue >= maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than maxValue.");
        }

        return minValue + (int)(NextDouble() * (maxValue - minValue));
    }

    public long NextLong() => (long)(NextUInt64() >> 1);

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public void NextBytes(Span<byte> buffer)
    {
        int i = 0;
        while (i + 8 <= buffer.Length)
        {
            ulong value = NextUInt64();
            BitConverter.TryWriteBytes(buffer.Slice(i, 8), value);
            i += 8;
        }

        if (i < buffer.Length)
        {
            ulong value = NextUInt64();
            Span<byte> remaining = stackalloc byte[8];
            BitConverter.TryWriteBytes(remaining, value);
            remaining.Slice(0, buffer.Length - i).CopyTo(buffer.Slice(i));
        }
    }

    public bool NextBool() => (NextUInt64() & 1) == 1;
}
