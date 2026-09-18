using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.PostgreSQL.Stores
{
    /// <summary>
    /// Creates configured PostgreSQL stores over one shared <see cref="PostgreSqlSettings"/>, so
    /// callers never construct settings themselves. The underlying connector is cached by Birko
    /// (keyed on the settings id), so creating a fresh store per call is cheap.
    /// </summary>
    public interface IPostgreSQLStoreFactory
    {
        /// <summary>The shared settings all stores from this factory use.</summary>
        PostgreSqlSettings Settings { get; }

        /// <summary>Returns an async store for <typeparamref name="T"/> wired to the configured database.</summary>
        AsyncPostgreSQLStore<T> GetAsyncStore<T>() where T : AbstractModel;

        /// <summary>The shared connector for the configured database (e.g. for the migration runner).</summary>
        AbstractConnector GetConnector();
    }
}
