using System;
using Birko.Data.SQL.MSSql.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Data.SQL.MSSql
{
    /// <summary>
    /// DI helpers for wiring the SQL Server store factory — the cross-provider counterpart of
    /// <c>AddSqLiteStores</c> (TASK-033).
    /// </summary>
    public static class MSSqlServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a singleton <see cref="IMSSqlStoreFactory"/> configured by <paramref name="configure"/>.
        /// Resolve <see cref="IMSSqlStoreFactory"/> to get stores / the shared connector.
        /// </summary>
        public static IServiceCollection AddMSSqlStores(
            this IServiceCollection services,
            Action<MSSqlStoreFactoryOptions> configure)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new MSSqlStoreFactoryOptions();
            configure(options);

            var factory = new MSSqlStoreFactory(options);
            services.AddSingleton<IMSSqlStoreFactory>(factory);
            return services;
        }
    }
}
