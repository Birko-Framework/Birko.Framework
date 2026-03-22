using System;

namespace Birko.Random;

/// <summary>
/// Mersenne Twister (MT19937) random number generator. High-quality PRNG with
/// a period of 2^19937-1. Suitable for simulations and statistical sampling.
/// Not thread-safe — use one instance per thread.
/// </summary>
public sealed class MersenneTwisterProvider : IRandomProvider
{
    private const int N = 624;
    private const int M = 397;
    private const uint MatrixA = 0x9908B0DF;
    private const uint UpperMask = 0x80000000;
    private const uint LowerMask = 0x7FFFFFFF;

    private readonly uint[] _mt = new uint[N];
    private int _index = N + 1;

    public MersenneTwisterProvider() : this(unchecked((uint)Environment.TickCount))
    {
    }

    public MersenneTwisterProvider(uint seed)
    {
        _mt[0] = seed;
        for (int i = 1; i < N; i++)
        {
            _mt[i] = 1812433253 * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i;
        }

        _index = N;
    }

    private void GenerateNumbers()
    {
        for (int i = 0; i < N; i++)
        {
            uint y = (_mt[i] & UpperMask) | (_mt[(i + 1) % N] & LowerMask);
            _mt[i] = _mt[(i + M) % N] ^ (y >> 1);
            if ((y & 1) != 0)
            {
                _mt[i] ^= MatrixA;
            }
        }

        _index = 0;
    }

    private uint NextUInt32()
    {
        if (_index >= N)
        {
            GenerateNumbers();
        }

        uint y = _mt[_index++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9D2C5680;
        y ^= (y << 15) & 0xEFC60000;
        y ^= y >> 18;
        return y;
    }

    public int NextInt() => (int)(NextUInt32() >> 1);

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

    public long NextLong()
    {
        ulong high = NextUInt32();
        ulong low = NextUInt32();
        return (long)((high << 32 | low) >> 1);
    }

    public double NextDouble() => NextUInt32() * (1.0 / 4294967296.0);

    public void NextBytes(Span<byte> buffer)
    {
        int i = 0;
        while (i + 4 <= buffer.Length)
        {
            uint value = NextUInt32();
            BitConverter.TryWriteBytes(buffer.Slice(i, 4), value);
            i += 4;
        }

        if (i < buffer.Length)
        {
            uint value = NextUInt32();
            Span<byte> remaining = stackalloc byte[4];
            BitConverter.TryWriteBytes(remaining, value);
            remaining.Slice(0, buffer.Length - i).CopyTo(buffer.Slice(i));
        }
    }

    public bool NextBool() => (NextUInt32() & 1) == 1;
}
