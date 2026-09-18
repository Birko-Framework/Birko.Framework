using System;

namespace Birko.Random;

/// <summary>
/// Abstraction over random number generation for testability and pluggability.
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Returns a non-negative random 32-bit integer.
    /// </summary>
    int NextInt();

    /// <summary>
    /// Returns a non-negative random 32-bit integer less than <paramref name="maxValue"/>.
    /// </summary>
    int NextInt(int maxValue);

    /// <summary>
    /// Returns a random 32-bit integer in the range [<paramref name="minValue"/>, <paramref name="maxValue"/>).
    /// </summary>
    int NextInt(int minValue, int maxValue);

    /// <summary>
    /// Returns a random 64-bit integer.
    /// </summary>
    long NextLong();

    /// <summary>
    /// Returns a random double in the range [0.0, 1.0).
    /// </summary>
    double NextDouble();

    /// <summary>
    /// Fills the specified buffer with random bytes.
    /// </summary>
    void NextBytes(Span<byte> buffer);

    /// <summary>
    /// Returns a random boolean value.
    /// </summary>
    bool NextBool();
}
