using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.Grants.Internal;

/// <summary>
/// One async mutex per key, serialising grant writes on one resource within this process. An entry lives only while
/// someone holds or awaits it and is removed on the last release, so the map never accumulates per-caller state (rule 36).
/// It cannot serialise across processes; the last-owner rule has a post-write check for that.
/// </summary>
internal sealed class KeyedLock
{
    /// <summary>Process-wide instance: two services over the same store must share one lock per resource.</summary>
    public static readonly KeyedLock Shared = new();

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out var found))
            {
                found = new Entry();
                _entries[key] = found;
            }
            found.Users++;
            entry = found;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }
        return new Releaser(this, key, entry);
    }

    /// <summary>Number of live entries; for tests proving entries are removed.</summary>
    internal int Count
    {
        get { lock (_entries) return _entries.Count; }
    }

    private void Leave(string key, Entry entry)
    {
        lock (_entries)
        {
            entry.Users--;
            if (entry.Users == 0)
                _entries.Remove(key);
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyedLock _owner;
        private readonly string _key;
        private Entry? _entry;

        public Releaser(KeyedLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry == null) return;
            entry.Semaphore.Release();
            _owner.Leave(_key, entry);
        }
    }
}
