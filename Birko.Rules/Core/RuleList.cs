using System;
using System.Collections;
using System.Collections.Generic;

namespace Birko.Rules;

/// <summary>
/// The rules of a <see cref="RuleSet"/> or a <see cref="RuleGroup"/>: a list that may be edited while it is read.
/// </summary>
/// <remarks>
/// SH-M370 (TASK-156). Rules are loaded from a database or configuration so they can change without a deploy, and the
/// evaluator is used from many requests at once. A plain <c>List</c> edited by a reload while a request enumerated it threw
/// "Collection was modified", and even copying it (<c>ToArray</c>) during an edit threw or captured a cleared slot
/// (measured). This list is copy-on-write: an edit builds a new array under a lock and publishes it; a read enumerates the
/// array current when it started, which no edit changes. Reads take no lock. Edits are rare and the lists are small.
/// </remarks>
public sealed class RuleList : IList<IRule>, IReadOnlyList<IRule>
{
    private readonly object _gate = new();
    private volatile IRule[] _items;

    public RuleList() => _items = Array.Empty<IRule>();

    public RuleList(IEnumerable<IRule> rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        _items = new List<IRule>(rules).ToArray();
    }

    /// <summary>The rules as they are now: an array no later edit changes.</summary>
    public IRule[] Snapshot() => _items;

    public IRule this[int index]
    {
        get => _items[index];
        set => Edit(list => list[index] = value);
    }

    public int Count => _items.Length;

    public bool IsReadOnly => false;

    public void Add(IRule item) => Edit(list => list.Add(item));

    public void AddRange(IEnumerable<IRule> items) => Edit(list => list.AddRange(items));

    public void Clear() => Edit(list => list.Clear());

    public bool Contains(IRule item) => Array.IndexOf(_items, item) >= 0;

    public void CopyTo(IRule[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public int IndexOf(IRule item) => Array.IndexOf(_items, item);

    public void Insert(int index, IRule item) => Edit(list => list.Insert(index, item));

    public bool Remove(IRule item)
    {
        var removed = false;
        Edit(list => removed = list.Remove(item));
        return removed;
    }

    public void RemoveAt(int index) => Edit(list => list.RemoveAt(index));

    public IEnumerator<IRule> GetEnumerator() => ((IEnumerable<IRule>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Edit(Action<List<IRule>> change)
    {
        lock (_gate)
        {
            var copy = new List<IRule>(_items);
            change(copy);
            _items = copy.ToArray();
        }
    }
}
