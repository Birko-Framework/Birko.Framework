using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security;

/// <summary>
/// Provides access to secrets from an external secret management system.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Gets a secret value by its key/path.
    /// </summary>
    /// <param name="key">The secret key or path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The secret value, or null if not found.</returns>
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Gets a secret with its full metadata.
    /// </summary>
    /// <param name="key">The secret key or path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The secret result with value and metadata, or null if not found.</returns>
    Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets or updates a secret value.
    /// </summary>
    /// <param name="key">The secret key or path.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSecretAsync(string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Deletes a secret.
    /// </summary>
    /// <param name="key">The secret key or path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteSecretAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Lists secret keys at the specified path/prefix.
    /// </summary>
    /// <param name="path">The path or prefix to list. Use null or empty for root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of secret keys.</returns>
    Task<IReadOnlyList<string>> ListSecretsAsync(string? path = null, CancellationToken ct = default);
}

/// <summary>
/// Represents a secret value with its metadata.
/// </summary>
public class SecretResult
{
    /// <summary>The secret key/path.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The secret value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>When the secret was created (UTC).</summary>
    public System.DateTime? CreatedAt { get; init; }

    /// <summary>When the secret was last updated (UTC).</summary>
    public System.DateTime? UpdatedAt { get; init; }

    /// <summary>When the secret expires (UTC), or null if it does not expire.</summary>
    public System.DateTime? ExpiresAt { get; init; }

    /// <summary>The secret version, if supported by the provider.</summary>
    public string? Version { get; init; }

    /// <summary>Additional metadata key-value pairs.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
