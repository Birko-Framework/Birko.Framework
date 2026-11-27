using System;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.SQL.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;

namespace Birko.Data.EventSourcing.SQL
{
    /// <summary>Registers <see cref="SqlEventStore{DB}"/> as the event store of every SQL backend (TASK-995).</summary>
    public static class SqlEventStoreOverrides
    {
        /// <summary>The name this override is registered under.</summary>
        public const string Name = "Sql";

        /// <summary>
        /// Makes <c>factory.GetEventStore(o => o.AddSql())</c> answer a <see cref="SqlEventStore{DB}"/> for any
        /// <see cref="ISqlStoreFactory"/> (SQLite, PostgreSQL, MySQL, SQL Server): the <c>__Events</c> table, one
        /// transaction per batch, its unique (aggregate, version) index and a <c>MAX(Version)</c> query.
        /// </summary>
        public static EventStoreOverrides AddSql(this EventStoreOverrides overrides)
        {
            if (overrides is null) throw new ArgumentNullException(nameof(overrides));
            return overrides.Add(Name, f => f is ISqlStoreFactory, f => Create((ISqlStoreFactory)f));
        }

        // The factory's store is an AsyncDataBaseBulkStore<TConnector, DomainEventModel>; its connector type is SqlEventStore's
        // type argument, known only at run time through the interface.
        private static IAsyncEventStore Create(ISqlStoreFactory factory)
        {
            var store = factory.GetAsyncStore<DomainEventModel>();
            for (var t = store.GetType(); t != null; t = t.BaseType)
            {
                if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(AsyncDataBaseBulkStore<,>)) continue;
                var eventStoreType = typeof(SqlEventStore<>).MakeGenericType(t.GetGenericArguments()[0]);
                return (IAsyncEventStore)Activator.CreateInstance(eventStoreType, store)!;
            }
            throw new NotSupportedException(
                $"The {factory.Provider} store ({store.GetType().Name}) is not an AsyncDataBaseBulkStore; SqlEventStore cannot run on it.");
        }
    }
}
