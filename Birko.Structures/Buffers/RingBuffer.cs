using System;
using System.Collections;
using System.Collections.Generic;

namespace Birko.Structures.Buffers;

/// <summary>
/// Fixed-capacity circular buffer with overwrite-oldest semantics.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class RingBuffer<T> : IEnumerable<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _tail;
    private bool _full;

    /// <summary>
    /// Gets the maximum capacity.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the current number of elements.
    /// </summary>
    public int Count => _full ? Capacity : (_tail >= _head ? _tail - _head : Capacity - _head + _tail);

    /// <summary>
    /// Gets whether the buffer is full.
    /// </summary>
    public bool IsFull => _full;

    /// <summary>
    /// Gets whether the buffer is empty.
    /// </summary>
    public bool IsEmpty => !_full && _head == _tail;

    /// <summary>
    /// Creates a ring buffer with the specified capacity.
    /// </summary>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        Capacity = capacity;
        _buffer = new T[capacity];
    }

    /// <summary>
    /// Writes an element to the buffer. Overwrites the oldest element if full.
    /// </summary>
    public void Write(T item)
    {
        _buffer[_tail] = item;
        _tail = (_tail + 1) % Capacity;

        if (_full)
        {
            _head = _tail;
        }
        else if (_tail == _head)
        {
            _full = true;
        }
    }

    /// <summary>
    /// Reads and removes the oldest element.
    /// </summary>
    public T Read()
    {
        if (IsEmpty) throw new InvalidOperationException("Buffer is empty.");

        var item = _buffer[_head];
        _buffer[_head] = default!;
        _head = (_head + 1) % Capacity;
        _full = false;
        return item;
    }

    /// <summary>
    /// Peeks at the oldest element without removing it.
    /// </summary>
    public T Peek()
    {
        if (IsEmpty) throw new InvalidOperationException("Buffer is empty.");
        return _buffer[_head];
    }

    /// <summary>
    /// Peeks at the newest element.
    /// </summary>
    public T PeekLast()
    {
        if (IsEmpty) throw new InvalidOperationException("Buffer is empty.");
        int index = (_tail - 1 + Capacity) % Capacity;
        return _buffer[index];
    }

    /// <summary>
    /// Gets an element by index (0 = oldest).
    /// </summary>
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new IndexOutOfRangeException();
            return _buffer[(_head + index) % Capacity];
        }
    }

    /// <summary>
    /// Clears the buffer.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_buffer, 0, Capacity);
        _head = 0;
        _tail = 0;
        _full = false;
    }

    /// <summary>
    /// Copies elements to an array in order (oldest to newest).
    /// </summary>
    public T[] ToArray()
    {
        var result = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            result[i] = _buffer[(_head + i) % Capacity];
        }
        return result;
    }

    /// <summary>
    /// Enumerates elements from oldest to newest.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        int count = Count;
        for (int i = 0; i < count; i++)
        {
            yield return _buffer[(_head + i) % Capacity];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
