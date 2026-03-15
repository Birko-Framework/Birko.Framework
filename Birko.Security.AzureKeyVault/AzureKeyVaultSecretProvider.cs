using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.AzureKeyVault;

/// <summary>
/// Azure Key Vault implementation of <see cref="ISecretProvider"/>.
/// Uses the Key Vault REST API directly with OAuth2 client credentials — no Azure.Security.KeyVault.Secrets dependency required.
/// </summary>
public class AzureKeyVaultSecretProvider : ISecretProvider, IDisposable
{
    private const string ApiVersion = "7.4";

    private readonly AzureKeyVaultSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _accessToken;
    private DateTime _tokenExpiresAt;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>
    /// Creates a new Azure Key Vault secret provider with the specified settings.
    /// </summary>
    public AzureKeyVaultSecretProvider(AzureKeyVaultSettings settings) : this(settings, null)
    {
    }

    /// <summary>
    /// Creates a new Azure Key Vault secret provider with the specified settings and optional HttpClient.
    /// </summary>
    public AzureKeyVaultSecretProvider(AzureKeyVaultSettings settings, HttpClient? httpClient)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.VaultUri))
            throw new ArgumentException("VaultUri is required", nameof(settings));

        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var result = await GetSecretWithMetadataAsync(key, ct).ConfigureAwait(false);
        return result?.Value;
    }

    /// <inheritdoc />
    public async Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Get,
            $"{BaseUri}secrets/{key}?api-version={ApiVersion}", ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseSecretResponse(key, json);
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var payload = JsonSerializer.Serialize(new { value });
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Put,
            $"{BaseUri}secrets/{key}?api-version={ApiVersion}", ct).ConfigureAwait(false);
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Delete,
            $"{BaseUri}secrets/{key}?api-version={ApiVersion}", ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? path = null, CancellationToken ct = default)
    {
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Get,
            $"{BaseUri}secrets?api-version={ApiVersion}", ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Array.Empty<string>();

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var results = new List<string>();
        if (doc.RootElement.TryGetProperty("value", out var secrets))
        {
            foreach (var secret in secrets.EnumerateArray())
            {
                if (secret.TryGetProperty("id", out var id))
                {
                    var name = ExtractSecretName(id.GetString());
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (string.IsNullOrEmpty(path) || name.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(name);
                        }
                    }
                }
            }
        }

        return results.AsReadOnly();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        _tokenLock.Dispose();
    }

    #region Private Helpers

    private string BaseUri => _settings.VaultUri.TrimEnd('/') + "/";

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string uri, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-5))
            return _accessToken;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-5))
                return _accessToken;

            if (string.IsNullOrEmpty(_settings.TenantId) ||
                string.IsNullOrEmpty(_settings.ClientId) ||
                string.IsNullOrEmpty(_settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "TenantId, ClientId, and ClientSecret are required for Azure Key Vault authentication");
            }

            var tokenEndpoint = $"https://login.microsoftonline.com/{_settings.TenantId}/oauth2/v2.0/token";
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["scope"] = "https://vault.azure.net/.default"
            });

            var response = await _httpClient.PostAsync(tokenEndpoint, tokenRequest, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            _accessToken = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("No access_token in response");

            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static SecretResult ParseSecretResponse(string key, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var value = root.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "";
        var attributes = root.TryGetProperty("attributes", out var attr) ? attr : default;

        DateTime? created = null, updated = null, expires = null;
        if (attributes.ValueKind != JsonValueKind.Undefined)
        {
            if (attributes.TryGetProperty("created", out var c))
                created = DateTimeOffset.FromUnixTimeSeconds(c.GetInt64()).UtcDateTime;
            if (attributes.TryGetProperty("updated", out var u))
                updated = DateTimeOffset.FromUnixTimeSeconds(u.GetInt64()).UtcDateTime;
            if (attributes.TryGetProperty("exp", out var e))
                expires = DateTimeOffset.FromUnixTimeSeconds(e.GetInt64()).UtcDateTime;
        }

        var version = root.TryGetProperty("id", out var id) ? ExtractVersion(id.GetString()) : null;

        var metadata = new Dictionary<string, string>();
        if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in tags.EnumerateObject())
            {
                metadata[prop.Name] = prop.Value.GetString() ?? "";
            }
        }

        return new SecretResult
        {
            Key = key,
            Value = value,
            CreatedAt = created,
            UpdatedAt = updated,
            ExpiresAt = expires,
            Version = version,
            Metadata = metadata
        };
    }

    private static string? ExtractSecretName(string? secretId)
    {
        if (string.IsNullOrEmpty(secretId)) return null;
        var uri = new Uri(secretId);
        var segments = uri.Segments;
        // /secrets/name or /secrets/name/version
        return segments.Length >= 3 ? segments[2].TrimEnd('/') : null;
    }

    private static string? ExtractVersion(string? secretId)
    {
        if (string.IsNullOrEmpty(secretId)) return null;
        var uri = new Uri(secretId);
        var segments = uri.Segments;
        return segments.Length >= 4 ? segments[3].TrimEnd('/') : null;
    }

    #endregion
}
