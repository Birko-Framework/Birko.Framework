using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Birko.Random;

/// <summary>
/// Cryptographically secure random number provider using <see cref="RandomNumberGenerator"/>.
/// Thread-safe. Suitable for tokens, keys, and security-sensitive randomness.
/// </summary>
public sealed class CryptoRandomProvider : IRandomProvider
{
    public int NextInt()
    {
        return RandomNumberGenerator.GetInt32(int.MaxValue);
    }

    public int NextInt(int maxValue)
    {
        if (maxValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive.");
        }

        return RandomNumberGenerator.GetInt32(maxValue);
    }

    public int NextInt(int minValue, int maxValue)
    {
        if (minValue >= maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than maxValue.");
        }

        return RandomNumberGenerator.GetInt32(minValue, maxValue);
    }

    public long NextLong()
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer) & long.MaxValue;
    }

    public double NextDouble()
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(buffer) >> 11;
        return value * (1.0 / (1UL << 53));
    }

    public void NextBytes(Span<byte> buffer)
    {
        RandomNumberGenerator.Fill(buffer);
    }

    public bool NextBool()
    {
        Span<byte> buffer = stackalloc byte[1];
        RandomNumberGenerator.Fill(buffer);
        return (buffer[0] & 1) == 1;
    }
}
