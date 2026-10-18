using System;
using System.IO;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Security.OAuth.Server.SqLite.Stores;
using Birko.Security.OAuth.Server.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.OAuth.Server.SqLite.Extensions;

/// <summary>DI registration of the SQLite OAuth server stores.</summary>
public static class OAuthServerSqLiteServiceCollectionExtensions
{
    /// <summary>
    /// Registers one shared SQLite store per OAuth store interface, all on the database <paramref name="settings"/>
    /// names. Tables are created on first use. Singletons: a device-code record is created in one request and read in
    /// later ones, and the atomic claims need one connector per database.
    /// </summary>
    public static IServiceCollection AddOAuthServerSqLiteStores(this IServiceCollection services, SqLiteSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        services.AddSingleton<IOAuthClientStore>(_ => new SqLiteOAuthClientStore(settings));
        services.AddSingleton<IAuthorizationCodeStore>(_ => new SqLiteAuthorizationCodeStore(settings));
        services.AddSingleton<IRefreshTokenStore>(_ => new SqLiteRefreshTokenStore(settings));
        services.AddSingleton<IDeviceCodeStore>(_ => new SqLiteDeviceCodeStore(settings));
        services.AddSingleton<IConsentStore>(_ => new SqLiteConsentStore(settings));
        return services;
    }

    /// <summary>
    /// <see cref="AddOAuthServerSqLiteStores(IServiceCollection, SqLiteSettings)"/> on the database file at
    /// <paramref name="databasePath"/>; its directory is created if missing.
    /// </summary>
    public static IServiceCollection AddOAuthServerSqLiteStores(this IServiceCollection services, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A database path is required.", nameof(databasePath));
        var full = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(full) ?? ".";
        Directory.CreateDirectory(directory);
        return services.AddOAuthServerSqLiteStores(new SqLiteSettings(directory, Path.GetFileName(full)));
    }
}
