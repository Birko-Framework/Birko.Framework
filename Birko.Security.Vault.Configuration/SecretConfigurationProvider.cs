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
            LoadAsync(_path, string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _diagnostics($"SecretConfiguration: error loading '{_path}': {ex.Message}");
        }
    }

    private async Task LoadAsync(string path, string prefix)
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
}
