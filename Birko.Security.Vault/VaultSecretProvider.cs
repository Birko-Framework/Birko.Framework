using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.Vault;

/// <summary>
/// HashiCorp Vault implementation of <see cref="ISecretProvider"/>.
/// Uses the Vault HTTP API directly — no VaultSharp dependency required.
/// Supports KV v1 and v2 secrets engines.
/// </summary>
public class VaultSecretProvider : ISecretProvider, IDisposable
{
    private readonly VaultSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new Vault secret provider with the specified settings.
    /// </summary>
    public VaultSecretProvider(VaultSettings settings) : this(settings, null)
    {
    }

    /// <summary>
    /// Creates a new Vault secret provider with the specified settings and optional HttpClient.
    /// </summary>
    public VaultSecretProvider(VaultSettings settings, HttpClient? httpClient)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();

        _httpClient.BaseAddress = new Uri(_settings.Address.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

        if (!string.IsNullOrEmpty(_settings.Token))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Vault-Token", _settings.Token);
        }
        if (!string.IsNullOrEmpty(_settings.Namespace))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Vault-Namespace", _settings.Namespace);
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var result = await GetSecretWithMetadataAsync(key, ct).ConfigureAwait(false);
        return result?.Value;
    }

    /// <inheritdoc />
    public async Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var path = BuildDataPath(key);
        var response = await _httpClient.GetAsync($"v1/{path}", ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (_settings.KvVersion == 2)
        {
            return ParseKv2Response(key, root);
        }
        else
        {
            return ParseKv1Response(key, root);
        }
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var path = BuildDataPath(key);
        var payload = _settings.KvVersion == 2
            ? JsonSerializer.Serialize(new { data = new Dictionary<string, string> { ["value"] = value } })
            : JsonSerializer.Serialize(new Dictionary<string, string> { ["value"] = value });

        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"v1/{path}", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var path = _settings.KvVersion == 2
            ? $"{_settings.MountPath}/metadata/{key}"
            : $"{_settings.MountPath}/{key}";

        var response = await _httpClient.DeleteAsync($"v1/{path}", ct).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? path = null, CancellationToken ct = default)
    {
        var listPath = _settings.KvVersion == 2
            ? $"{_settings.MountPath}/metadata/{path ?? ""}"
            : $"{_settings.MountPath}/{path ?? ""}";

        listPath = listPath.TrimEnd('/');

        var request = new HttpRequestMessage(HttpMethod.Get, $"v1/{listPath}?list=true");
        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Array.Empty<string>();

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("keys", out var keys))
        {
            return keys.EnumerateArray()
                .Select(k => k.GetString() ?? "")
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList()
                .AsReadOnly();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Checks if the Vault server is healthy.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("v1/sys/health", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    #region Private Helpers

    private string BuildDataPath(string key)
    {
        return _settings.KvVersion == 2
            ? $"{_settings.MountPath}/data/{key}"
            : $"{_settings.MountPath}/{key}";
    }

    private static SecretResult ParseKv2Response(string key, JsonElement root)
    {
        var data = root.GetProperty("data");
        var innerData = data.GetProperty("data");
        var metadata = data.TryGetProperty("metadata", out var meta) ? meta : default;

        var value = innerData.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "";

        return new SecretResult
        {
            Key = key,
            Value = value,
            Version = metadata.ValueKind != JsonValueKind.Undefined && metadata.TryGetProperty("version", out var ver)
                ? ver.ToString()
                : null,
            CreatedAt = metadata.ValueKind != JsonValueKind.Undefined && metadata.TryGetProperty("created_time", out var ct)
                ? ParseVaultTime(ct.GetString())
                : null,
            UpdatedAt = metadata.ValueKind != JsonValueKind.Undefined && metadata.TryGetProperty("created_time", out var ut)
                ? ParseVaultTime(ut.GetString())
                : null,
            Metadata = ExtractCustomMetadata(metadata)
        };
    }

    private static SecretResult ParseKv1Response(string key, JsonElement root)
    {
        var data = root.GetProperty("data");
        var value = data.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "";

        return new SecretResult
        {
            Key = key,
            Value = value
        };
    }

    private static DateTime? ParseVaultTime(string? timeStr)
    {
        if (string.IsNullOrEmpty(timeStr))
            return null;
        return DateTime.TryParse(timeStr, out var dt) ? dt.ToUniversalTime() : null;
    }

    private static IReadOnlyDictionary<string, string> ExtractCustomMetadata(JsonElement metadata)
    {
        if (metadata.ValueKind == JsonValueKind.Undefined)
            return new Dictionary<string, string>();

        if (!metadata.TryGetProperty("custom_metadata", out var cm) || cm.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();

        var dict = new Dictionary<string, string>();
        foreach (var prop in cm.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.GetString() ?? "";
        }
        return dict;
    }

    #endregion
}
