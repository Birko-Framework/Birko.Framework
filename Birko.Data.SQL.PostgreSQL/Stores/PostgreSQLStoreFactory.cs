using System;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.PostgreSQL.Stores
{
    /// <summary>
    /// Default <see cref="IPostgreSQLStoreFactory"/>: builds one shared <see cref="PostgreSqlSettings"/>
    /// from <see cref="PostgreSQLStoreFactoryOptions"/> and hands out stores + the shared connector.
    /// The cross-provider counterpart of <c>SqLiteStoreFactory</c> (TASK-033), minus the file-path logic.
    /// </summary>
    public sealed class PostgreSQLStoreFactory : IPostgreSQLStoreFactory
    {
        /// <inheritdoc />
        public PostgreSqlSettings Settings { get; }

        /// <summary>Builds the factory from <paramref name="options"/>.</summary>
        public PostgreSQLStoreFactory(PostgreSQLStoreFactoryOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Settings = new PostgreSqlSettings(options.Location, options.Name, options.UserName, options.Password, options.Port, options.UseSecure)
            {
                CommandTimeout = options.CommandTimeout,
                UseBinaryImport = options.UseBinaryImport,
            };
        }

        /// <inheritdoc />
        public AsyncPostgreSQLStore<T> GetAsyncStore<T>() where T : AbstractModel
        {
            var store = new AsyncPostgreSQLStore<T>();
            store.SetSettings(Settings);
            return store;
        }

        /// <inheritdoc />
        public AbstractConnector GetConnector() => DataBase.GetConnector<PostgreSQLConnector>(Settings);
    }
}
