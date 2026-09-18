using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.MySQL.Stores
{
    /// <summary>
    /// Creates configured MySQL stores over one shared <see cref="MySqlSettings"/>, so callers never
    /// construct settings themselves. The underlying connector is cached by Birko (keyed on the
    /// settings id), so creating a fresh store per call is cheap.
    /// </summary>
    public interface IMySQLStoreFactory
    {
        /// <summary>The shared settings all stores from this factory use.</summary>
        MySqlSettings Settings { get; }

        /// <summary>Returns an async store for <typeparamref name="T"/> wired to the configured database.</summary>
        AsyncMySQLStore<T> GetAsyncStore<T>() where T : AbstractModel;

        /// <summary>The shared connector for the configured database (e.g. for the migration runner).</summary>
        AbstractConnector GetConnector();
    }
}
