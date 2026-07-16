using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Birko.Serialization;
using Birko.Serialization.Json;
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
    private readonly ISerializer _serializer;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseUri;

    /// <summary>
    /// Creates a new Vault secret provider with the specified settings.
    /// </summary>
    public VaultSecretProvider(VaultSettings settings) : this(settings, null)
    {
    }

    /// <summary>
    /// Creates a new Vault secret provider with the specified settings and optional HttpClient.
    /// </summary>
    public VaultSecretProvider(VaultSettings settings, HttpClient? httpClient, ISerializer? serializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _serializer = serializer ?? new SystemJsonSerializer();
        _baseUri = new Uri(_settings.Address.TrimEnd('/') + "/");

        // CR-L354: only mutate the client we own. An injected/shared client (e.g. from IHttpClientFactory or
        // reused across providers) keeps its own BaseAddress/Timeout/headers — every request built by
        // SendCoreAsync uses an absolute URI and attaches the Vault token/namespace as per-request headers, so
        // nothing on the shared client is overwritten and constructing two providers over one client no longer
        // throws on duplicate DefaultRequestHeaders.
        if (_ownsHttpClient)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        }
    }

    /// <summary>
    /// Sends a Vault API request against an absolute URI with per-request token/namespace headers, so a
    /// caller-owned HttpClient is never mutated (CR-L354). <paramref name="relativePath"/> is resolved
    /// against the Vault base address (e.g. "v1/secret/data/foo").
    /// </summary>
    private Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string relativePath, HttpContent? content, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, relativePath));
        if (content != null)
        {
            request.Content = content;
        }
        if (!string.IsNullOrEmpty(_settings.Token))
        {
            request.Headers.Add("X-Vault-Token", _settings.Token);
        }
        if (!string.IsNullOrEmpty(_settings.Namespace))
        {
            request.Headers.Add("X-Vault-Namespace", _settings.Namespace);
        }
        return _httpClient.SendAsync(request, ct);
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
        var response = await SendCoreAsync(HttpMethod.Get, $"v1/{path}", null, ct).ConfigureAwait(false);

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
            ? _serializer.Serialize(new { data = new Dictionary<string, string> { ["value"] = value } })
            : _serializer.Serialize(new Dictionary<string, string> { ["value"] = value });

        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await SendCoreAsync(HttpMethod.Post, $"v1/{path}", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var path = _settings.KvVersion == 2
            ? $"{_settings.MountPath}/metadata/{key}"
            : $"{_settings.MountPath}/{key}";

        var response = await SendCoreAsync(HttpMethod.Delete, $"v1/{path}", null, ct).ConfigureAwait(false);

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

        var response = await SendCoreAsync(HttpMethod.Get, $"v1/{listPath}?list=true", null, ct).ConfigureAwait(false);

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
    /// Reads every key/value pair stored under the given Vault KV path (KV v1 or v2).
    /// Useful when the secret is a bag of fields rather than a single <c>value</c> —
    /// e.g. Spring-Boot-style application configuration trees or nested credential blobs.
    /// Returns <c>null</c> when the path doesn't exist. Values are JSON-stringified when
    /// they aren't already strings (numbers, booleans, objects).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>?> GetSecretPairsAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var path = BuildDataPath(key);
        var response = await SendCoreAsync(HttpMethod.Get, $"v1/{path}", null, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        // CR-M240: a 200 with an unexpected/empty body (or a KV2 response missing the inner data node)
        // must return null gracefully, not surface a raw KeyNotFoundException from GetProperty.
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;

        var inner = data;
        if (_settings.KvVersion == 2 && !data.TryGetProperty("data", out inner))
            return null;

        var result = new Dictionary<string, string>();
        foreach (var prop in inner.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                JsonValueKind.Null   => string.Empty,
                _                    => prop.Value.GetRawText(),
            };
        }
        return result;
    }

    /// <summary>
    /// Checks if the Vault server is healthy.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await SendCoreAsync(HttpMethod.Get, "v1/sys/health", null, ct).ConfigureAwait(false);
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
        // CR-M240: tolerate a malformed/empty KV2 body (missing data / inner-data) with an empty value
        // instead of a raw KeyNotFoundException from GetProperty.
        if (!root.TryGetProperty("data", out var data))
            return new SecretResult { Key = key, Value = "" };
        var innerData = data.TryGetProperty("data", out var inner) ? inner : default;
        var metadata = data.TryGetProperty("metadata", out var meta) ? meta : default;

        var value = innerData.ValueKind == JsonValueKind.Object && innerData.TryGetProperty("value", out var val)
            ? val.GetString() ?? ""
            : "";

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
            // KV v2 version metadata exposes only "created_time"; there is no distinct update
            // timestamp, so do NOT copy created_time here (that made UpdatedAt always equal
            // CreatedAt). Use a real update field if one is ever present, otherwise leave it null.
            UpdatedAt = metadata.ValueKind != JsonValueKind.Undefined
                && (metadata.TryGetProperty("updated_time", out var ut) || metadata.TryGetProperty("mtime", out ut))
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
