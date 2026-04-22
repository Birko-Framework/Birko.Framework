using System;
using System.Collections.Generic;
using Birko.Security.Configuration;
using Microsoft.Extensions.Configuration;

namespace Birko.Security.Vault.Configuration;

/// <summary>
/// Extensions that plug a HashiCorp Vault KV store into <see cref="IConfigurationBuilder"/>
/// using the LocalVault hierarchical-path convention (defaults → env → project → user).
/// <para>
/// Drop-in replacement for the <c>LocalVault</c> NuGet package, minus the
/// <c>VaultSharp</c> dependency — built on <see cref="VaultSecretProvider"/>'s hand-rolled
/// HTTP client.
/// </para>
/// </summary>
public static class LocalVaultConfigurationExtensions
{
    // Environment variable names kept identical to the original LocalVault package so
    // callers can swap packages without rewriting their deploy pipelines.
    public const string EnabledVar     = "LOCAL_VAULT_ENABLED";
    public const string TokenVar       = "LOCAL_VAULT_TOKEN";
    public const string AddressVar     = "LOCAL_VAULT_ADDR";
    public const string UserVar        = "LOCAL_VAULT_USER";
    public const string DomainVar      = "LOCAL_VAULT_DOMAIN";
    public const string EnvironmentVar = "LOCAL_VAULT_ENVIRONMENT";

    /// <summary>
    /// Registers a set of Vault KV paths as <see cref="IConfigurationSource"/>s, ordered so
    /// later sources override earlier ones: project defaults → env-specific defaults →
    /// project → env-specific project → user overrides (when a user is set).
    /// <para>
    /// The whole call is a no-op unless the <c>LOCAL_VAULT_ENABLED</c> environment variable
    /// is <c>"true"</c> (case-insensitive). This keeps CI, production, and offline dev runs
    /// free of Vault traffic by default.
    /// </para>
    /// </summary>
    public static IConfigurationBuilder AddLocalVaultConfiguration(
        this IConfigurationBuilder builder,
        string projectName,
        LocalVaultOptions? options = null)
    {
        if (!IsEnabled()) return builder;
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name is required.", nameof(projectName));

        options = ResolveOptions(options);
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            Console.WriteLine("LocalVault: token is empty — skipping.");
            return builder;
        }

        var client = new VaultSecretProvider(new VaultSettings
        {
            Address        = options.Url,
            Token          = options.Token,
            MountPath      = options.MountPath,
            KvVersion      = options.KvVersion,
            TimeoutSeconds = options.TimeoutSeconds,
        });

        var project = projectName.ToLowerInvariant();
        foreach (var path in BuildPaths(project, options))
        {
            builder.Add(new LocalVaultConfigurationSource(client, path));
        }
        return builder;
    }

    /// <summary>
    /// Registers a single Vault KV path, relative to the mount already configured on the
    /// given <see cref="VaultSecretProvider"/>. Use when you don't need the hierarchical
    /// scheme — e.g. to load one application's settings bundle.
    /// </summary>
    public static IConfigurationBuilder AddVaultPath(
        this IConfigurationBuilder builder,
        VaultSecretProvider client,
        string path)
    {
        return builder.Add(new LocalVaultConfigurationSource(client, path));
    }

    /// <summary>
    /// Registers a single secret path using any <see cref="ISecretProvider"/>.
    /// Works with Vault, Azure Key Vault, and any other provider.
    /// Set <paramref name="recursive"/> to true to traverse sub-paths (for hierarchical providers).
    /// </summary>
    public static IConfigurationBuilder AddVaultPath(
        this IConfigurationBuilder builder,
        ISecretProvider provider,
        string path,
        bool recursive = true)
    {
        return builder.Add(new SecretConfigurationSource(provider, path, recursive));
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private static bool IsEnabled()
        => string.Equals(Environment.GetEnvironmentVariable(EnabledVar), "true", StringComparison.OrdinalIgnoreCase);

    private static LocalVaultOptions ResolveOptions(LocalVaultOptions? seed)
    {
        var o = seed ?? new LocalVaultOptions();

        o.Token       = FirstNonEmpty(o.Token,       Environment.GetEnvironmentVariable(TokenVar),       o.Token);
        o.Url         = FirstNonEmpty(o.Url,         Environment.GetEnvironmentVariable(AddressVar),     o.Url);
        o.User        = FirstNonEmpty(o.User,        Environment.GetEnvironmentVariable(UserVar),        string.Empty);
        o.Domain      = FirstNonEmpty(o.Domain,      Environment.GetEnvironmentVariable(DomainVar),      string.Empty);
        o.Environment = FirstNonEmpty(o.Environment, Environment.GetEnvironmentVariable(EnvironmentVar), string.Empty);

        o.User        = o.User.ToLowerInvariant();
        o.Domain      = o.Domain.ToLowerInvariant();
        o.Environment = o.Environment.ToLowerInvariant();
        return o;
    }

    private static IEnumerable<string> BuildPaths(string project, LocalVaultOptions o)
    {
        // Prefix chain: [<domain>/]projects/...  or  [<domain>/]users/<user>/...
        var scope = string.IsNullOrEmpty(o.Domain) ? string.Empty : o.Domain + "/";
        var env   = o.Environment;
        var hasEnv = !string.IsNullOrEmpty(env);

        yield return $"{scope}projects/defaults";
        if (hasEnv) yield return $"{scope}projects/defaults.{env}";

        yield return $"{scope}projects/{project}";
        if (hasEnv) yield return $"{scope}projects/{project}.{env}";

        if (!string.IsNullOrEmpty(o.User))
        {
            yield return $"{scope}users/{o.User}/{project}";
            if (hasEnv) yield return $"{scope}users/{o.User}/{project}.{env}";
        }
    }

    private static string FirstNonEmpty(string current, string? envValue, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(current)) return current;
        if (!string.IsNullOrWhiteSpace(envValue)) return envValue!;
        return fallback;
    }
}
