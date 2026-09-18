using System;
using Microsoft.Extensions.Configuration;

namespace Birko.Security.Vault.Configuration;

/// <summary>
/// <see cref="IConfigurationSource"/> that wraps a <see cref="LocalVaultConfigurationProvider"/>
/// rooted at the given KV path (relative to the already-configured mount of the supplied
/// <see cref="VaultSecretProvider"/>).
/// </summary>
public sealed class LocalVaultConfigurationSource : IConfigurationSource
{
    private readonly VaultSecretProvider _client;
    private readonly string _path;
    private readonly Action<string>? _diagnostics;

    public LocalVaultConfigurationSource(VaultSecretProvider client, string path, Action<string>? diagnostics = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _path = path ?? string.Empty;
        _diagnostics = diagnostics;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new LocalVaultConfigurationProvider(_client, _path, _diagnostics);
}
