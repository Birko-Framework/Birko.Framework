using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Hands out async stores for one configured backend without the caller naming it (TASK-990), so the same code runs on
    /// InMemory or SQLite in tests and on PostgreSQL, MongoDB or Cosmos DB in production. Built by
    /// <see cref="StoreFactory.Create(string, string?, Action{StoreProviders})"/> from a backend name and a connection
    /// string, or by <see cref="StoreFactory.Create(ISettings, Action{StoreProviders})"/> from the backend's own settings.
    /// A repository over a store: <c>GetAsyncRepository&lt;T&gt;()</c> in Birko.Data.Repositories.
    /// </summary>
    /// <remarks>
    /// Anything beyond CRUD and bulk is a capability to check for, never to assume: a store may implement
    /// <see cref="IAsyncAggregatableStore{T}"/>, and a factory may implement <see cref="ISchemaStoreFactory"/>.
    /// </remarks>
    public interface IStoreFactory
    {
        /// <summary>The backend name this factory was registered under (e.g. <c>SqLite</c>, <c>MongoDB</c>, <c>InMemory</c>).</summary>
        string Backend { get; }

        /// <summary>The settings every store from this factory uses, as the backend's own settings type.</summary>
        ISettings Settings { get; }

        /// <summary>
        /// The async store for <typeparamref name="T"/> on the configured backend. A SQL backend answers a new store over
        /// its cached connector; every other backend answers the same store for a type every time (its store holds the
        /// data or its own client), so two callers see each other's writes and no client is built per call.
        /// </summary>
        IAsyncBulkStore<T> GetAsyncStore<T>() where T : AbstractModel;
    }

    /// <summary>
    /// A factory whose backend can create what its stores need before first use (TASK-990): SQL tables, and on a NoSQL
    /// backend whatever its stores can ensure for themselves. Check for it (<c>factory is ISchemaStoreFactory</c>); a
    /// backend without the capability does not pretend to have it.
    /// </summary>
    public interface ISchemaStoreFactory : IStoreFactory
    {
        /// <summary>Creates what the stores for <paramref name="types"/> need (tables, containers, indexes).</summary>
        Task CreateSchemaAsync(IEnumerable<Type> types, CancellationToken ct = default);
    }
}
