using System;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.MSSql.Stores
{
    /// <summary>
    /// Default <see cref="IMSSqlStoreFactory"/>: builds one shared <see cref="MSSqlSettings"/> from
    /// <see cref="MSSqlStoreFactoryOptions"/> and hands out stores + the shared connector. The
    /// cross-provider counterpart of <c>SqLiteStoreFactory</c> (TASK-033), minus the file-path logic.
    /// </summary>
    public sealed class MSSqlStoreFactory : IMSSqlStoreFactory
    {
        /// <inheritdoc />
        public MSSqlSettings Settings { get; }

        /// <summary>Builds the factory from <paramref name="options"/>.</summary>
        public MSSqlStoreFactory(MSSqlStoreFactoryOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Settings = new MSSqlSettings(options.Location, options.Name, options.UserName, options.Password, options.Port, options.UseSecure)
            {
                CommandTimeout = options.CommandTimeout,
                MultipleActiveResultSets = options.MultipleActiveResultSets,
                TrustServerCertificate = options.TrustServerCertificate,
            };
        }

        /// <inheritdoc />
        public AsyncMSSqlStore<T> GetAsyncStore<T>() where T : AbstractModel
        {
            var store = new AsyncMSSqlStore<T>();
            store.SetSettings(Settings);
            return store;
        }

        /// <inheritdoc />
        public AbstractConnector GetConnector() => DataBase.GetConnector<MSSqlConnector>(Settings);
    }
}
