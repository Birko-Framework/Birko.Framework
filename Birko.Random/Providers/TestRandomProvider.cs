using System;
using System.Collections.Generic;

namespace Birko.Random;

/// <summary>
/// Controllable random provider for unit tests. Supports queued return values
/// and deterministic sequences.
/// </summary>
public sealed class TestRandomProvider : IRandomProvider
{
    private readonly Queue<int> _intQueue = new();
    private readonly Queue<long> _longQueue = new();
    private readonly Queue<double> _doubleQueue = new();
    private readonly Queue<bool> _boolQueue = new();
    private readonly Queue<byte[]> _bytesQueue = new();

    private int _defaultInt;
    private long _defaultLong;
    private double _defaultDouble;
    private bool _defaultBool;

    public TestRandomProvider(int defaultInt = 0, long defaultLong = 0L,
        double defaultDouble = 0.5, bool defaultBool = false)
    {
        _defaultInt = defaultInt;
        _defaultLong = defaultLong;
        _defaultDouble = defaultDouble;
        _defaultBool = defaultBool;
    }

    /// <summary>
    /// Enqueues integer values to be returned by NextInt calls.
    /// </summary>
    public void EnqueueInt(params int[] values)
    {
        foreach (var value in values)
        {
            _intQueue.Enqueue(value);
        }
    }

    /// <summary>
    /// Enqueues long values to be returned by NextLong calls.
    /// </summary>
    public void EnqueueLong(params long[] values)
    {
        foreach (var value in values)
        {
            _longQueue.Enqueue(value);
        }
    }

    /// <summary>
    /// Enqueues double values to be returned by NextDouble calls.
    /// </summary>
    public void EnqueueDouble(params double[] values)
    {
        foreach (var value in values)
        {
            _doubleQueue.Enqueue(value);
        }
    }

    /// <summary>
    /// Enqueues boolean values to be returned by NextBool calls.
    /// </summary>
    public void EnqueueBool(params bool[] values)
    {
        foreach (var value in values)
        {
            _boolQueue.Enqueue(value);
        }
    }

    /// <summary>
    /// Enqueues byte arrays to be returned by NextBytes calls.
    /// </summary>
    public void EnqueueBytes(params byte[][] values)
    {
        foreach (var value in values)
        {
            _bytesQueue.Enqueue(value);
        }
    }

    /// <summary>
    /// Sets the default values returned when queues are empty.
    /// </summary>
    public void SetDefaults(int defaultInt = 0, long defaultLong = 0L,
        double defaultDouble = 0.5, bool defaultBool = false)
    {
        _defaultInt = defaultInt;
        _defaultLong = defaultLong;
        _defaultDouble = defaultDouble;
        _defaultBool = defaultBool;
    }

    public int NextInt() => _intQueue.Count > 0 ? _intQueue.Dequeue() : _defaultInt;

    public int NextInt(int maxValue) => _intQueue.Count > 0
        ? Math.Min(_intQueue.Dequeue(), maxValue - 1)
        : Math.Min(_defaultInt, maxValue - 1);

    public int NextInt(int minValue, int maxValue) => _intQueue.Count > 0
        ? Math.Clamp(_intQueue.Dequeue(), minValue, maxValue - 1)
        : Math.Clamp(_defaultInt, minValue, maxValue - 1);

    public long NextLong() => _longQueue.Count > 0 ? _longQueue.Dequeue() : _defaultLong;

    public double NextDouble() => _doubleQueue.Count > 0 ? _doubleQueue.Dequeue() : _defaultDouble;

    public void NextBytes(Span<byte> buffer)
    {
        if (_bytesQueue.Count > 0)
        {
            var bytes = _bytesQueue.Dequeue();
            bytes.AsSpan(0, Math.Min(bytes.Length, buffer.Length)).CopyTo(buffer);
            return;
        }

        buffer.Clear();
    }

    public bool NextBool() => _boolQueue.Count > 0 ? _boolQueue.Dequeue() : _defaultBool;
}
