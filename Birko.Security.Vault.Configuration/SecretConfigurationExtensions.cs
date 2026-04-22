using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Birko.Security.Configuration;

/// <summary>
/// Extensions that plug any <see cref="ISecretProvider"/> into <see cref="IConfigurationBuilder"/>.
/// Works with HashiCorp Vault, Azure Key Vault, and any future ISecretProvider implementation.
/// </summary>
public static class SecretConfigurationExtensions
{
    /// <summary>
    /// Registers a single secret path as a configuration source.
    /// Set <paramref name="recursive"/> to true to traverse sub-paths (for hierarchical providers like Vault KV).
    /// </summary>
    public static IConfigurationBuilder AddSecretConfiguration(
        this IConfigurationBuilder builder,
        ISecretProvider provider,
        string path,
        bool recursive = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);
        return builder.Add(new SecretConfigurationSource(provider, path, recursive));
    }

    /// <summary>
    /// Registers multiple secret paths as configuration sources. Later paths override earlier ones.
    /// </summary>
    public static IConfigurationBuilder AddSecretConfiguration(
        this IConfigurationBuilder builder,
        ISecretProvider provider,
        IEnumerable<string> paths,
        bool recursive = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var path in paths)
        {
            builder.Add(new SecretConfigurationSource(provider, path, recursive));
        }
        return builder;
    }
}
