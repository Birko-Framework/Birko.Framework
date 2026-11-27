using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Stores;

namespace Birko.Data.EventSourcing.Stores
{
    /// <summary>
    /// The platform event stores a composition root registers (TASK-995), as it registers backends with
    /// <c>StoreProviders</c>: each <c>Birko.Data.EventSourcing.*</c> project adds its own with an <c>AddX()</c> extension
    /// (<c>o.AddSql()</c>). Neither this project nor a backend's can reference the other, and nothing is kept process-wide.
    /// </summary>
    public sealed class EventStoreOverrides
    {
        private sealed record Entry(string Name, Func<IStoreFactory, bool> Matches, Func<IStoreFactory, IAsyncEventStore> Create);

        private readonly List<Entry> _entries = new();

        /// <summary>The registered override names.</summary>
        public IReadOnlyCollection<string> Names => _entries.Select(e => e.Name).ToList();

        /// <summary>
        /// Registers an override: for a factory <paramref name="matches"/> accepts, <paramref name="create"/> builds the
        /// event store. A name registered twice is refused.
        /// </summary>
        public EventStoreOverrides Add(string name, Func<IStoreFactory, bool> matches, Func<IStoreFactory, IAsyncEventStore> create)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An override name is required.", nameof(name));
            if (matches is null) throw new ArgumentNullException(nameof(matches));
            if (create is null) throw new ArgumentNullException(nameof(create));
            if (_entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Event store override '{name}' is already registered.", nameof(name));
            _entries.Add(new Entry(name, matches, create));
            return this;
        }

        internal IAsyncEventStore? CreateFor(IStoreFactory factory)
        {
            var matching = _entries.Where(e => e.Matches(factory)).ToList();
            if (matching.Count > 1)
                throw new InvalidOperationException(
                    $"Event store overrides {string.Join(", ", matching.Select(e => e.Name))} all accept the {factory.Backend} backend; register one.");
            return matching.Count == 1 ? matching[0].Create(factory) : null;
        }
    }

    /// <summary>The event store for whichever backend a factory is configured for (TASK-995).</summary>
    public static class EventStoreFactoryExtensions
    {
        /// <summary>
        /// The <see cref="IAsyncEventStore"/> for <paramref name="factory"/>'s backend: a registered override for it when
        /// there is one, else <see cref="AsyncStoreEventStore"/> over <c>factory.GetAsyncStore&lt;EventRecord&gt;()</c>.
        /// Application code names no backend: <c>factory.GetEventStore(o => o.AddSql())</c> at the composition root.
        /// </summary>
        /// <remarks>
        /// ⚠ The default needs a backend whose create refuses an existing id across processes. JSON and XML do so within one
        /// process only (measured, TASK-995), so they are refused here rather than handed a store that can lose an event.
        /// </remarks>
        public static IAsyncEventStore GetEventStore(this IStoreFactory factory, Action<EventStoreOverrides>? overrides = null)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            var registered = new EventStoreOverrides();
            overrides?.Invoke(registered);
            var native = registered.CreateFor(factory);
            if (native != null) return native;
            if (SingleProcessBackends.Contains(factory.Backend))
                throw new NotSupportedException(
                    $"The {factory.Backend} backend cannot refuse a second writer of one event version across processes; " +
                    "use a database backend (or InMemory for tests) for an event store.");
            return new AsyncStoreEventStore(factory.GetAsyncStore<EventRecord>());
        }

        private static readonly HashSet<string> SingleProcessBackends = new(StringComparer.OrdinalIgnoreCase) { "Json", "Xml" };
    }
}
