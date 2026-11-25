using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Repositories;
using Birko.Data.Stores;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// Hands out async stores and repositories for one SQL database without the caller naming its provider (TASK-988).
    /// Built by <see cref="SqlStoreFactory.Create"/> from a provider name and a connection string, so the same code runs
    /// on SQLite in tests and PostgreSQL in production. Stores from one factory share one connector (Birko caches it by
    /// the settings id), so getting a fresh store per call is cheap. It is also the SQL kind of the backend-neutral
    /// <see cref="IStoreFactory"/> (TASK-990), with schema creation (<see cref="ISchemaStoreFactory"/>), so a SQL provider
    /// registers with <see cref="StoreFactory.Create(string, string?, Action{StoreProviders})"/> beside the NoSQL backends.
    /// </summary>
    public interface ISqlStoreFactory : ISchemaStoreFactory
    {
        /// <summary>The provider name this factory was registered under (e.g. <c>SqLite</c>, <c>PostgreSQL</c>).</summary>
        string Provider { get; }

        /// <summary>The settings every store from this factory uses; the provider's own settings type.</summary>
        new PasswordSettings Settings { get; }

        /// <summary>The shared connector: schema work (<c>EnsureColumns</c>, a migration runner), raw SQL.</summary>
        AbstractConnector GetConnector();

        /// <summary>An async store for <typeparamref name="T"/> on the configured database.</summary>
        new IAsyncSqlBulkStore<T> GetAsyncStore<T>() where T : AbstractModel;

        /// <summary>An async bulk repository over a fresh store from <see cref="GetAsyncStore{T}"/>.</summary>
        AsyncSqlModelRepository<T> GetAsyncRepository<T>() where T : AbstractModel;

    }
}
