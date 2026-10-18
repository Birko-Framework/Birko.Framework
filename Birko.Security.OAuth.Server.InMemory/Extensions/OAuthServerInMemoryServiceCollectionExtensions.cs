using Birko.Security.OAuth.Server.InMemory.Stores;
using Birko.Security.OAuth.Server.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.OAuth.Server.InMemory.Extensions;

/// <summary>DI registration of the in-memory OAuth server stores.</summary>
public static class OAuthServerInMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers one shared in-memory instance per OAuth store interface. Singletons on purpose: a device-code record
    /// is created in one request and read in later ones, and an authorization code is issued and redeemed in two.
    /// Nothing survives a restart; use the SQLite stores (<c>Birko.Security.OAuth.Server.SqLite</c>) to persist.
    /// </summary>
    public static IServiceCollection AddOAuthServerInMemoryStores(this IServiceCollection services)
    {
        services.AddSingleton<IOAuthClientStore, InMemoryOAuthClientStore>();
        services.AddSingleton<IAuthorizationCodeStore, InMemoryAuthorizationCodeStore>();
        services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        services.AddSingleton<IDeviceCodeStore, InMemoryDeviceCodeStore>();
        services.AddSingleton<IConsentStore, InMemoryConsentStore>();
        return services;
    }
}
