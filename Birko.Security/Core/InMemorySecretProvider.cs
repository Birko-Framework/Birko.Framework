using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security;

/// <summary>
/// An <see cref="ISecretProvider"/> over an in-process dictionary (TASK-532): for local runs, development and tests, and
/// for secrets that arrive through configuration. Before it, the only implementations were Azure Key Vault and Vault, so
/// every consumer without one wrote its own (DraCode's <c>ConfigSecretProvider</c>).
/// </summary>
/// <remarks>
/// <para>
/// Seed it from configuration with
/// <c>new InMemorySecretProvider(configuration.GetSection("Secrets").AsEnumerable(makePathsRelative: true))</c>: a
/// snapshot, so a later configuration reload is not seen. Entries with a <c>null</c> value (configuration sections) are
/// skipped. Names are flat (<see cref="IsHierarchical"/> is false) and case-sensitive; <see cref="ListSecretsAsync"/>
/// answers the full names that start with the path, as Azure Key Vault does.
/// </para>
/// <para>Nothing is persisted and nothing is encrypted in memory: swap in a vault provider for production secrets.</para>
/// </remarks>
public sealed class InMemorySecretProvider : ISecretProvider
{
    private readonly ConcurrentDictionary<string, SecretResult> _secrets = new(StringComparer.Ordinal);

    public InMemorySecretProvider()
    {
    }

    public InMemorySecretProvider(IEnumerable<KeyValuePair<string, string?>> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var now = DateTime.UtcNow;
        foreach (var (key, value) in secrets)
        {
            if (value is null || string.IsNullOrEmpty(key)) continue;
            _secrets[key] = new SecretResult { Key = key, Value = value, CreatedAt = now, UpdatedAt = now };
        }
    }

    public bool IsHierarchical => false;

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(_secrets.TryGetValue(key, out var secret) ? secret.Value : null);
    }

    public Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(_secrets.TryGetValue(key, out var secret) ? secret : null);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        var now = DateTime.UtcNow;
        _secrets.AddOrUpdate(key,
            _ => new SecretResult { Key = key, Value = value, CreatedAt = now, UpdatedAt = now },
            (_, old) => new SecretResult { Key = key, Value = value, CreatedAt = old.CreatedAt, UpdatedAt = now });
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        _secrets.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListSecretsAsync(string? path = null, CancellationToken ct = default)
    {
        IReadOnlyList<string> keys = _secrets.Keys
            .Where(k => string.IsNullOrEmpty(path) || k.StartsWith(path, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(keys);
    }
}
