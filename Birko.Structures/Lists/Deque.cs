using System;
using System.Collections;
using System.Collections.Generic;

namespace Birko.Structures.Lists;

/// <summary>
/// Double-ended queue backed by a circular array.
/// O(1) amortized push/pop from both ends.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class Deque<T> : IEnumerable<T>
{
    private T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;

    private const int DefaultCapacity = 8;

    /// <summary>
    /// Gets the number of elements.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets whether the deque is empty.
    /// </summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// Creates a deque with the default initial capacity.
    /// </summary>
    public Deque() : this(DefaultCapacity) { }

    /// <summary>
    /// Creates a deque with the specified initial capacity.
    /// </summary>
    public Deque(int capacity)
    {
        if (capacity <= 0) capacity = DefaultCapacity;
        _buffer = new T[capacity];
    }

    /// <summary>
    /// Pushes an element to the front.
    /// </summary>
    public void PushFront(T item)
    {
        EnsureCapacity();
        _head = (_head - 1 + _buffer.Length) % _buffer.Length;
        _buffer[_head] = item;
        _count++;
    }

    /// <summary>
    /// Pushes an element to the back.
    /// </summary>
    public void PushBack(T item)
    {
        EnsureCapacity();
        _buffer[_tail] = item;
        _tail = (_tail + 1) % _buffer.Length;
        _count++;
    }

    /// <summary>
    /// Removes and returns the front element.
    /// </summary>
    public T PopFront()
    {
        if (_count == 0) throw new InvalidOperationException("Deque is empty.");

        var item = _buffer[_head];
        _buffer[_head] = default!;
        _head = (_head + 1) % _buffer.Length;
        _count--;
        return item;
    }

    /// <summary>
    /// Removes and returns the back element.
    /// </summary>
    public T PopBack()
    {
        if (_count == 0) throw new InvalidOperationException("Deque is empty.");

        _tail = (_tail - 1 + _buffer.Length) % _buffer.Length;
        var item = _buffer[_tail];
        _buffer[_tail] = default!;
        _count--;
        return item;
    }

    /// <summary>
    /// Peeks at the front element.
    /// </summary>
    public T PeekFront()
    {
        if (_count == 0) throw new InvalidOperationException("Deque is empty.");
        return _buffer[_head];
    }

    /// <summary>
    /// Peeks at the back element.
    /// </summary>
    public T PeekBack()
    {
        if (_count == 0) throw new InvalidOperationException("Deque is empty.");
        return _buffer[(_tail - 1 + _buffer.Length) % _buffer.Length];
    }

    /// <summary>
    /// Gets an element by index (0 = front).
    /// </summary>
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count) throw new IndexOutOfRangeException();
            return _buffer[(_head + index) % _buffer.Length];
        }
    }

    /// <summary>
    /// Clears the deque.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>
    /// Checks if the deque contains an element.
    /// </summary>
    public bool Contains(T item)
    {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < _count; i++)
        {
            if (comparer.Equals(_buffer[(_head + i) % _buffer.Length], item))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Copies elements to an array (front to back).
    /// </summary>
    public T[] ToArray()
    {
        var result = new T[_count];
        for (int i = 0; i < _count; i++)
        {
            result[i] = _buffer[(_head + i) % _buffer.Length];
        }
        return result;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _buffer[(_head + i) % _buffer.Length];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void EnsureCapacity()
    {
        if (_count < _buffer.Length) return;

        var newCapacity = _buffer.Length * 2;
        var newBuffer = new T[newCapacity];

        for (int i = 0; i < _count; i++)
        {
            newBuffer[i] = _buffer[(_head + i) % _buffer.Length];
        }

        _buffer = newBuffer;
        _head = 0;
        _tail = _count;
    }
}
