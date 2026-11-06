using System;
using Microsoft.Extensions.Configuration;

namespace Birko.Security.Vault.Configuration;

/// <summary>
/// <see cref="IConfigurationSource"/> that wraps a <see cref="LocalVaultConfigurationProvider"/>
/// rooted at the given KV path (relative to the already-configured mount of the supplied
/// <see cref="VaultSecretProvider"/>).
/// </summary>
/// <remarks>
/// Who owns the client (SH-L232): a source given a <see cref="VaultSecretProvider"/> leaves it to the caller, who made it.
/// A source given <see cref="VaultSettings"/> (what <c>AddLocalVaultConfiguration</c> builds) has each provider create its
/// own client and dispose it when the configuration root disposes the provider.
/// </remarks>
public sealed class LocalVaultConfigurationSource : IConfigurationSource
{
    private readonly VaultSecretProvider? _client;
    private readonly VaultSettings? _settings;
    private readonly string _path;
    private readonly Action<string>? _diagnostics;

    /// <summary>A source over a client the caller owns and disposes.</summary>
    public LocalVaultConfigurationSource(VaultSecretProvider client, string path, Action<string>? diagnostics = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _path = path ?? string.Empty;
        _diagnostics = diagnostics;
    }

    /// <summary>A source whose providers create, own and dispose their client.</summary>
    public LocalVaultConfigurationSource(VaultSettings settings, string path, Action<string>? diagnostics = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _path = path ?? string.Empty;
        _diagnostics = diagnostics;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => _client != null
        ? new LocalVaultConfigurationProvider(_client, _path, _diagnostics)
        : new LocalVaultConfigurationProvider(new VaultSecretProvider(_settings!), _path, _diagnostics, ownsClient: true);
}
