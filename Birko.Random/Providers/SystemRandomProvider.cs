using System;

namespace Birko.Random;

/// <summary>
/// Production implementation using <see cref="System.Random.Shared"/>.
/// Thread-safe via the shared instance.
/// </summary>
public sealed class SystemRandomProvider : IRandomProvider
{
    public int NextInt() => System.Random.Shared.Next();

    public int NextInt(int maxValue) => System.Random.Shared.Next(maxValue);

    public int NextInt(int minValue, int maxValue) => System.Random.Shared.Next(minValue, maxValue);

    public long NextLong() => System.Random.Shared.NextInt64();

    public double NextDouble() => System.Random.Shared.NextDouble();

    public void NextBytes(Span<byte> buffer) => System.Random.Shared.NextBytes(buffer);

    public bool NextBool() => System.Random.Shared.Next(2) == 1;
}
