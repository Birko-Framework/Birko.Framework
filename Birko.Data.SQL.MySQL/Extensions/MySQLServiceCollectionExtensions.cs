using System;
using Birko.Data.SQL.MySQL.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Data.SQL.MySQL
{
    /// <summary>
    /// DI helpers for wiring the MySQL store factory — the cross-provider counterpart of
    /// <c>AddSqLiteStores</c> (TASK-033).
    /// </summary>
    public static class MySQLServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a singleton <see cref="IMySQLStoreFactory"/> configured by <paramref name="configure"/>.
        /// </summary>
        public static IServiceCollection AddMySqlStores(
            this IServiceCollection services,
            Action<MySQLStoreFactoryOptions> configure)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new MySQLStoreFactoryOptions();
            configure(options);

            var factory = new MySQLStoreFactory(options);
            services.AddSingleton<IMySQLStoreFactory>(factory);
            return services;
        }
    }
}
