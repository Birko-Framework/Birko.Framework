using System;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.MySQL.Stores
{
    /// <summary>
    /// Default <see cref="IMySQLStoreFactory"/>: builds one shared <see cref="MySqlSettings"/> from
    /// <see cref="MySQLStoreFactoryOptions"/> and hands out stores + the shared connector. The
    /// cross-provider counterpart of <c>SqLiteStoreFactory</c> (TASK-033), minus the file-path logic.
    /// </summary>
    public sealed class MySQLStoreFactory : IMySQLStoreFactory
    {
        /// <inheritdoc />
        public MySqlSettings Settings { get; }

        /// <summary>Builds the factory from <paramref name="options"/>.</summary>
        public MySQLStoreFactory(MySQLStoreFactoryOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Settings = new MySqlSettings(options.Location, options.Name, options.UserName, options.Password, options.Port, options.UseSecure)
            {
                CommandTimeout = options.CommandTimeout,
                BulkInsertBatchSize = options.BulkInsertBatchSize,
            };
        }

        /// <inheritdoc />
        public AsyncMySQLStore<T> GetAsyncStore<T>() where T : AbstractModel
        {
            var store = new AsyncMySQLStore<T>();
            store.SetSettings(Settings);
            return store;
        }

        /// <inheritdoc />
        public AbstractConnector GetConnector() => DataBase.GetConnector<MySQLConnector>(Settings);
    }
}
