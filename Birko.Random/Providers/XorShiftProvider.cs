using System;

namespace Birko.Random;

/// <summary>
/// XorShift128 random number generator. Fast, lightweight PRNG suitable for
/// simulations and non-cryptographic use cases.
/// Not thread-safe — use one instance per thread.
/// </summary>
public sealed class XorShiftProvider : IRandomProvider
{
    private ulong _s0;
    private ulong _s1;

    public XorShiftProvider() : this(unchecked((ulong)Environment.TickCount64))
    {
    }

    public XorShiftProvider(ulong seed)
    {
        if (seed == 0)
        {
            seed = 1;
        }

        // SplitMix64 to initialize state from a single seed
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
    }

    private static ulong SplitMix(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    private ulong NextUInt64()
    {
        ulong s0 = _s0;
        ulong s1 = _s1;
        ulong result = s0 + s1;

        s1 ^= s0;
        _s0 = ((s0 << 24) | (s0 >> 40)) ^ s1 ^ (s1 << 16);
        _s1 = (s1 << 37) | (s1 >> 27);

        return result;
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
