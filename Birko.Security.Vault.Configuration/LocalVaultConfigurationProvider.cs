using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Birko.Security.Vault.Configuration;

/// <summary>
/// Pulls a single Vault KV path (and, recursively, its sub-paths) into the
/// <see cref="ConfigurationProvider"/> data bag. Keys containing the <c>--</c> separator
/// are rewritten to <see cref="ConfigurationPath.KeyDelimiter"/> so nested structures
/// bind cleanly to POCOs — e.g. a Vault field <c>Security--DevCertificate--Fingerprint</c>
/// ends up as <c>config["Security:DevCertificate:Fingerprint"]</c>.
/// </summary>
public sealed class LocalVaultConfigurationProvider : ConfigurationProvider
{
    private readonly VaultSecretProvider _client;
    private readonly string _path;
    private readonly Action<string> _diagnostics;

    public LocalVaultConfigurationProvider(VaultSecretProvider client, string path, Action<string>? diagnostics = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _path = (path ?? string.Empty).Trim('/');
        // CR-L356: diagnostics are routed through an injectable sink (default Console.WriteLine) instead of
        // hard-coding Console, so hosts can capture them via ILogger/etc.
        _diagnostics = diagnostics ?? Console.WriteLine;
    }

    /// <summary>
    /// CR-L356: load failures are intentionally NON-FATAL (fail-open) — a Vault outage or auth failure
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
            _diagnostics($"LocalVault: error loading '{_path}': {ex.Message}");
        }
    }

    private async Task LoadAsync(string path, string prefix)
    {
        try
        {
            var pairs = await _client.GetSecretPairsAsync(path).ConfigureAwait(false);
            if (pairs is not null)
            {
                foreach (var kv in pairs)
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? kv.Key
                        : prefix + ConfigurationPath.KeyDelimiter + kv.Key;
                    // Allow `--` inside a single vault field to denote config-path nesting.
                    key = key.Replace("--", ConfigurationPath.KeyDelimiter);
                    Set(key, kv.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _diagnostics($"LocalVault: warning reading '{path}': {ex.Message}");
        }

        // Recurse into sub-paths so nested KV folders become nested config keys.
        System.Collections.Generic.IReadOnlyList<string> children;
        try
        {
            children = await _client.ListSecretsAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics($"LocalVault: warning listing '{path}': {ex.Message}");
            return;
        }

        foreach (var raw in children)
        {
            var childName = raw.TrimEnd('/');
            if (string.IsNullOrEmpty(childName)) continue;
            var childPath   = string.IsNullOrEmpty(path)   ? childName : path + "/" + childName;
            var childPrefix = string.IsNullOrEmpty(prefix) ? childName : prefix + ConfigurationPath.KeyDelimiter + childName;
            await LoadAsync(childPath, childPrefix).ConfigureAwait(false);
        }
    }
}
