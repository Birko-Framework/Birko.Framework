using System;
using System.Collections;

namespace Birko.Structures.Filters;

/// <summary>
/// Probabilistic data structure for membership testing.
/// No false negatives; configurable false positive rate.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class BloomFilter<T> where T : notnull
{
    private readonly BitArray _bits;
    private readonly int _hashCount;
    private readonly int _bitSize;

    /// <summary>
    /// Gets the number of items added.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the number of bits in the filter.
    /// </summary>
    public int BitSize => _bitSize;

    /// <summary>
    /// Gets the number of hash functions used.
    /// </summary>
    public int HashCount => _hashCount;

    /// <summary>
    /// Creates a Bloom filter with the specified expected capacity and false positive rate.
    /// </summary>
    /// <param name="expectedCount">Expected number of elements.</param>
    /// <param name="falsePositiveRate">Desired false positive rate (0.0 to 1.0, e.g., 0.01 for 1%).</param>
    public BloomFilter(int expectedCount, double falsePositiveRate = 0.01)
    {
        if (expectedCount <= 0) throw new ArgumentOutOfRangeException(nameof(expectedCount));
        if (falsePositiveRate is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(falsePositiveRate));

        _bitSize = OptimalBitSize(expectedCount, falsePositiveRate);
        _hashCount = OptimalHashCount(_bitSize, expectedCount);
        _bits = new BitArray(_bitSize);
    }

    /// <summary>
    /// Creates a Bloom filter with explicit bit size and hash count.
    /// </summary>
    public BloomFilter(int bitSize, int hashCount)
    {
        if (bitSize <= 0) throw new ArgumentOutOfRangeException(nameof(bitSize));
        if (hashCount <= 0) throw new ArgumentOutOfRangeException(nameof(hashCount));

        _bitSize = bitSize;
        _hashCount = hashCount;
        _bits = new BitArray(bitSize);
    }

    /// <summary>
    /// Adds an element to the filter.
    /// </summary>
    public void Add(T item)
    {
        var (hash1, hash2) = GetHashes(item);

        for (int i = 0; i < _hashCount; i++)
        {
            int index = GetIndex(hash1, hash2, i);
            _bits[index] = true;
        }

        Count++;
    }

    /// <summary>
    /// Tests if an element might be in the filter.
    /// Returns false if definitely not present; true if possibly present.
    /// </summary>
    public bool MayContain(T item)
    {
        var (hash1, hash2) = GetHashes(item);

        for (int i = 0; i < _hashCount; i++)
        {
            int index = GetIndex(hash1, hash2, i);
            if (!_bits[index]) return false;
        }

        return true;
    }

    /// <summary>
    /// Estimates the current false positive rate.
    /// </summary>
    public double EstimatedFalsePositiveRate()
    {
        double exponent = -(double)_hashCount * Count / _bitSize;
        double probability = Math.Pow(1 - Math.Exp(exponent), _hashCount);
        return probability;
    }

    /// <summary>
    /// Resets the filter.
    /// </summary>
    public void Clear()
    {
        _bits.SetAll(false);
        Count = 0;
    }

    private static (int Hash1, int Hash2) GetHashes(T item)
    {
        int hash1 = item.GetHashCode();

        // CR-L376: derive an independent second hash with the Murmur3 fmix32 finalizer and force it odd.
        // The old bit-rotation collapsed hash2 to 0 whenever GetHashCode() returned 0 (and toward hash1
        // when the low/high 16 bits matched), so the double-hashing step h1 + i*h2 mapped every probe to
        // the same bit — wrecking the false-positive rate. An odd, well-mixed hash2 is never 0 and keeps
        // the probes independent.
        uint h = (uint)hash1;
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        int hash2 = (int)h | 1;

        return (hash1, hash2);
    }

    private int GetIndex(int hash1, int hash2, int i)
    {
        // Double hashing: h(i) = h1 + i*h2
        long combined = (long)hash1 + (long)i * hash2;
        return (int)((combined % _bitSize + _bitSize) % _bitSize);
    }

    private static int OptimalBitSize(int n, double p)
    {
        return (int)Math.Ceiling(-n * Math.Log(p) / (Math.Log(2) * Math.Log(2)));
    }

    private static int OptimalHashCount(int m, int n)
    {
        return Math.Max(1, (int)Math.Round((double)m / n * Math.Log(2)));
    }
}
