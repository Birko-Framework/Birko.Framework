using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.MSSql.Stores
{
    /// <summary>
    /// Creates configured SQL Server stores over one shared <see cref="MSSqlSettings"/>, so callers
    /// never construct settings themselves. The underlying connector is cached by Birko (keyed on the
    /// settings id), so creating a fresh store per call is cheap.
    /// </summary>
    public interface IMSSqlStoreFactory
    {
        /// <summary>The shared settings all stores from this factory use.</summary>
        MSSqlSettings Settings { get; }

        /// <summary>
        /// Returns an async store for <typeparamref name="T"/> wired to the configured database
        /// (async is the natural default for a server database).
        /// </summary>
        AsyncMSSqlStore<T> GetAsyncStore<T>() where T : AbstractModel;

        /// <summary>
        /// The shared connector for the configured database — the same cached instance the stores use.
        /// Useful for the migration runner, which needs the connector to provision the schema.
        /// </summary>
        AbstractConnector GetConnector();
    }
}
