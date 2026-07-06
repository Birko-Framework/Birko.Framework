using System;
using Birko.Data.SQL.PostgreSQL.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Data.SQL.PostgreSQL
{
    /// <summary>
    /// DI helpers for wiring the PostgreSQL store factory — the cross-provider counterpart of
    /// <c>AddSqLiteStores</c> (TASK-033).
    /// </summary>
    public static class PostgreSQLServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a singleton <see cref="IPostgreSQLStoreFactory"/> configured by <paramref name="configure"/>.
        /// </summary>
        public static IServiceCollection AddPostgreSqlStores(
            this IServiceCollection services,
            Action<PostgreSQLStoreFactoryOptions> configure)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new PostgreSQLStoreFactoryOptions();
            configure(options);

            var factory = new PostgreSQLStoreFactory(options);
            services.AddSingleton<IPostgreSQLStoreFactory>(factory);
            return services;
        }
    }
}
