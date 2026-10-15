using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Birko.Security.Configuration;

/// <summary>
/// Pulls secrets from any <see cref="ISecretProvider"/> into the
/// <see cref="ConfigurationProvider"/> data bag.
/// Uses <see cref="ISecretProvider.GetSecretPairsAsync"/> to read key/value pairs
/// and <see cref="ISecretProvider.ListSecretsAsync"/> to discover child paths.
/// Keys containing the <c>--</c> separator are rewritten to
/// <see cref="ConfigurationPath.KeyDelimiter"/>.
/// </summary>
public sealed class SecretConfigurationProvider : ConfigurationProvider
{
    private readonly ISecretProvider _provider;
    private readonly string _path;
    private readonly bool _recursive;
    private readonly Action<string> _diagnostics;

    public SecretConfigurationProvider(ISecretProvider provider, string path, bool recursive = true, Action<string>? diagnostics = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _path = (path ?? string.Empty).Trim('/');
        _recursive = recursive;
        // CR-L356: injectable diagnostic sink (default Console.WriteLine) instead of hard-coded Console.
        _diagnostics = diagnostics ?? Console.WriteLine;
    }

    /// <summary>
    /// CR-L356: load failures are intentionally NON-FATAL (fail-open) — a provider outage or auth failure
    /// yields an empty configuration section and a diagnostic message rather than crashing startup. Consumers
    /// that require specific secrets must validate their presence separately; missing secrets will not throw here.
    /// </summary>
    public override void Load()
    {
        try
        {
            if (_provider.IsHierarchical)
                LoadAsync(_path, string.Empty).GetAwaiter().GetResult();
            else
                LoadFlatAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _diagnostics($"SecretConfiguration: error loading '{_path}': {ex.Message}");
        }
    }

    /// <summary>The separator a flat provider's names use for levels (Azure Key Vault: <c>app--db--password</c>).</summary>
    private const string FlatSeparator = "--";

    /// <summary>
    /// A flat provider (<see cref="ISecretProvider.IsHierarchical"/> false): one listing, whose names are FULL names.
    /// </summary>
    /// <remarks>
    /// SH-M254: the recursive walk joined each listed name to the path as if it were relative, so with Azure Key Vault
    /// and path <c>app</c> it asked for <c>app/app--db</c>, which is no secret, and the fail-open load swallowed that:
    /// an empty section. It also re-listed the whole vault at every level. Here a secret named the path itself loads at
    /// the root, and a name under it (<c>path--rest</c>) loads at <c>rest</c>, its <c>--</c> read as levels, which is
    /// where the walk puts the same secret on a hierarchical provider.
    /// </remarks>
    private async Task LoadFlatAsync()
    {
        if (!string.IsNullOrEmpty(_path))
            await LoadPairsAsync(_path, string.Empty).ConfigureAwait(false);

        if (!_recursive) return;

        IReadOnlyList<string> names;
        try
        {
            names = await _provider.ListSecretsAsync(string.IsNullOrEmpty(_path) ? null : _path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics($"SecretConfiguration: warning listing '{_path}': {ex.Message}");
            return;
        }

        var under = string.IsNullOrEmpty(_path) ? string.Empty : _path + FlatSeparator;
        foreach (var name in names)
        {
            if (!name.StartsWith(under, StringComparison.OrdinalIgnoreCase) || name.Length == under.Length) continue;
            var relative = name.Substring(under.Length);
            await LoadPairsAsync(name, relative.Replace(FlatSeparator, ConfigurationPath.KeyDelimiter)).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(string path, string prefix)
    {
        await LoadPairsAsync(path, prefix).ConfigureAwait(false);

        if (!_recursive) return;

        IReadOnlyList<string> children;
        try
        {
            children = await _provider.ListSecretsAsync(string.IsNullOrEmpty(path) ? null : path)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics($"SecretConfiguration: warning listing '{path}': {ex.Message}");
            return;
        }

        foreach (var raw in children)
        {
            var childName = raw.TrimEnd('/');
            if (string.IsNullOrEmpty(childName)) continue;
            var childPath = string.IsNullOrEmpty(path) ? childName : path + "/" + childName;
            var childPrefix = string.IsNullOrEmpty(prefix) ? childName : prefix + ConfigurationPath.KeyDelimiter + childName;
            await LoadAsync(childPath, childPrefix).ConfigureAwait(false);
        }
    }

    private async Task LoadPairsAsync(string path, string prefix)
    {
        try
        {
            var pairs = await _provider.GetSecretPairsAsync(path).ConfigureAwait(false);
            if (pairs is not null)
            {
                foreach (var kv in pairs)
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? kv.Key
                        : prefix + ConfigurationPath.KeyDelimiter + kv.Key;
                    key = key.Replace("--", ConfigurationPath.KeyDelimiter);
                    Set(key, kv.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _diagnostics($"SecretConfiguration: warning reading '{path}': {ex.Message}");
        }
    }
}
