using System;
using Microsoft.Extensions.Configuration;

namespace Birko.Security.Configuration;

/// <summary>
/// <see cref="IConfigurationSource"/> that creates a <see cref="SecretConfigurationProvider"/>
/// backed by any <see cref="ISecretProvider"/>.
/// </summary>
public sealed class SecretConfigurationSource : IConfigurationSource
{
    private readonly ISecretProvider _provider;
    private readonly string _path;
    private readonly bool _recursive;

    public SecretConfigurationSource(ISecretProvider provider, string path, bool recursive = true)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _path = path ?? string.Empty;
        _recursive = recursive;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new SecretConfigurationProvider(_provider, _path, _recursive);
}
