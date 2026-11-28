using System;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Stores;
using Birko.Security.OAuth.Server.Stores.Factory;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.OAuth.Server.Extensions;

/// <summary>DI registration of the OAuth server stores over an <see cref="IStoreFactory"/> (TASK-1013).</summary>
public static class OAuthServerStoreFactoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers one store per OAuth store interface, all on <paramref name="factory"/>'s backend, e.g.
    /// <c>services.AddOAuthServerStores(StoreFactory.Create(cfg.Backend, cfg.ConnectionString, p =&gt; p.AddPostgreSQL()))</c>.
    /// Singletons, as the SQLite and InMemory registrations: a device-code record is created in one request and read in
    /// later ones, and a code is issued and redeemed in two. Tables, collections or indexes are created on first use.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The backend cannot claim a code or a refresh token atomically (JSON, XML): refused here, at registration, not at
    /// the first token request.
    /// </exception>
    public static IServiceCollection AddOAuthServerStores(this IServiceCollection services, IStoreFactory factory)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        var clients = new FactoryOAuthClientStore(factory);
        var codes = new FactoryAuthorizationCodeStore(factory);
        var refreshes = new FactoryRefreshTokenStore(factory);
        var devices = new FactoryDeviceCodeStore(factory);
        var consents = new FactoryConsentStore(factory);
        services.AddSingleton<IOAuthClientStore>(clients);
        services.AddSingleton<IAuthorizationCodeStore>(codes);
        services.AddSingleton<IRefreshTokenStore>(refreshes);
        services.AddSingleton<IDeviceCodeStore>(devices);
        services.AddSingleton<IConsentStore>(consents);
        return services;
    }
}
