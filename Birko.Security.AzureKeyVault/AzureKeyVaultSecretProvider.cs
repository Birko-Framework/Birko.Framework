using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Birko.Serialization;
using Birko.Serialization.Json;
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
    private readonly ISerializer _serializer;
    private readonly bool _ownsHttpClient;
    // CR-L339: _accessToken is read on the lock-free fast path of GetAccessTokenAsync, so publish writes
    // via volatile. _tokenExpiresAt (a DateTime, not atomically readable on 32-bit) is left non-volatile:
    // a torn/stale read there only ever causes a redundant token fetch under the lock (the double-check
    // inside the lock is authoritative) or use of a token still within its 5-minute expiry buffer — benign.
    private volatile string? _accessToken;
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
    public AzureKeyVaultSecretProvider(AzureKeyVaultSettings settings, HttpClient? httpClient, ISerializer? serializer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.VaultUri))
            throw new ArgumentException("VaultUri is required", nameof(settings));

        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        // Only configure the timeout on a client we own. Mutating a caller-owned (shared) HttpClient
        // is a surprising side effect and throws InvalidOperationException if it already issued a
        // request — so respect the caller's configuration for injected clients.
        if (_ownsHttpClient)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        }
        _serializer = serializer ?? new SystemJsonSerializer();

        if (_settings.AllowInsecureSecretIds)
        {
            // Recorded AND traced. A setting that weakens a security check and announces itself
            // nowhere is one nobody notices; this project has no logger dependency, so the trace is
            // the zero-dependency half and the property is the half a health check can read.
            System.Diagnostics.Trace.TraceWarning(
                "AzureKeyVaultSecretProvider: AllowInsecureSecretIds is enabled, so http:// secret " +
                "ids are accepted alongside https://. A real Key Vault is always TLS - this should " +
                "be set only for a local emulator or a test double.");
        }
    }

    /// <summary>
    /// Whether this provider was configured to accept <c>http://</c> secret ids.
    /// </summary>
    /// <remarks>
    /// Exposed so a health check or a start-up banner can report an insecure configuration rather
    /// than it being visible only in trace output.
    /// </remarks>
    public bool InsecureSecretIdsAllowed => _settings.AllowInsecureSecretIds;

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
            $"{BaseUri}secrets/{CheckName(key)}?api-version={ApiVersion}", ct).ConfigureAwait(false);

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

        var payload = _serializer.Serialize(new { value });
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Put,
            $"{BaseUri}secrets/{CheckName(key)}?api-version={ApiVersion}", ct).ConfigureAwait(false);
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Delete,
            $"{BaseUri}secrets/{CheckName(key)}?api-version={ApiVersion}", ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Key Vault has no folders, so <paramref name="path"/> is a name prefix and the answer is FULL secret names
    /// (<see cref="IsHierarchical"/> is false). Every page is read: SH-M260, the <c>nextLink</c> of a vault with more
    /// secrets than one page was never followed, so the rest were silently missing. A <c>nextLink</c> is followed only
    /// on this vault's own address, since the request carries the access token.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? path = null, CancellationToken ct = default)
    {
        var results = new List<string>();
        string? next = $"{BaseUri}secrets?api-version={ApiVersion}";
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (next != null)
        {
            if (!seen.Add(next))
                throw new InvalidOperationException($"Azure Key Vault returned a nextLink already read: '{next}'.");

            var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, next, ct).ConfigureAwait(false);
            var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return results.AsReadOnly();

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var secrets) && secrets.ValueKind == JsonValueKind.Array)
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

            next = doc.RootElement.TryGetProperty("nextLink", out var link) && link.ValueKind == JsonValueKind.String
                ? link.GetString()
                : null;
            if (string.IsNullOrEmpty(next))
            {
                next = null;
            }
            else if (!next.StartsWith(BaseUri, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Azure Key Vault returned a nextLink outside the vault '{BaseUri}'; it was not followed, so the access token is not sent elsewhere.");
            }
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// False: Key Vault names are flat, so <see cref="ListSecretsAsync"/> answers full names, never names relative to
    /// the path listed (SH-M254).
    /// </summary>
    public bool IsHierarchical => false;

    /// <inheritdoc />
    // CR-L338: Azure Key Vault secrets are single-valued, so this matches ISecretProvider's default
    // single-entry implementation. It is re-declared explicitly (rather than inheriting the default
    // interface method) so the member is callable on a concrete AzureKeyVaultSecretProvider reference,
    // not only through the ISecretProvider interface — default interface methods are not visible on the
    // implementing type. Keep the body in sync with the interface default.
    public async Task<IReadOnlyDictionary<string, string>?> GetSecretPairsAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct).ConfigureAwait(false);
        if (value is null) return null;
        return new Dictionary<string, string> { ["value"] = value };
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

    /// <summary>
    /// A secret name as Key Vault defines one: 1 to 127 letters, digits and '-'.
    /// </summary>
    /// <remarks>
    /// SH-M252: the name was interpolated into the route unescaped, so a '/' made the rest of it the VERSION segment
    /// (<c>secrets/{name}/{version}</c>), a '..' collapsed to another route, and '?' or '&amp;' added query parameters,
    /// on a delete too. Nothing outside the alphabet names a secret, so it is refused rather than escaped.
    /// </remarks>
    internal static string CheckName(string key)
    {
        if (key.Length is 0 or > 127 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new ArgumentException(
                $"'{key}' is not an Azure Key Vault secret name: 1-127 characters of letters, digits and '-'.", nameof(key));
        return key;
    }

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

    // instance, not static: the version extractor now consults settings
    private SecretResult ParseSecretResponse(string key, string json)
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

    /// <summary>
    /// Parses a vault secret id, answering false for anything that is not one.
    /// </summary>
    /// <remarks>
    /// TASK-459: <c>UriKind.Absolute</c> alone is NOT the check, and what it accepts is
    /// platform-dependent. On Unix a leading-slash path IS an absolute <c>file:</c> URI, so
    /// <c>/secrets/relative-only</c> parses with three segments and yields the secret name
    /// <c>relative-only</c> — a malformed id silently becoming a real name. On Windows the same
    /// string fails to parse and is skipped, which is why CR-L340's fix read as correct: it had
    /// only ever been executed on Windows. Measured on .NET 10:
    /// <c>Uri.TryCreate("/secrets/relative-only", UriKind.Absolute, out _)</c> is <c>false</c> on
    /// Windows and <c>true</c> with <c>Scheme == "file"</c> on Linux — and this framework deploys
    /// in Linux containers, so the wrong answer was the one that shipped.
    /// <para>
    /// The scheme is therefore part of the identity: a Key Vault secret id is an <b>https</b> URL.
    /// Anything else is not a secret id, however well it parses. <c>http</c> is accepted only when
    /// <see cref="AzureKeyVaultSettings.AllowInsecureSecretIds"/> is set, which exists for local
    /// emulators and test doubles and announces itself at construction.
    /// </para>
    /// </remarks>
    private bool TryParseSecretId(string? secretId, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(secretId, UriKind.Absolute, out var parsed)) return false;

        var acceptable = parsed.Scheme == Uri.UriSchemeHttps
            || (_settings.AllowInsecureSecretIds && parsed.Scheme == Uri.UriSchemeHttp);
        if (!acceptable) return false;

        uri = parsed;
        return true;
    }

    private string? ExtractSecretName(string? secretId)
    {
        // CR-L340: tolerate a relative/malformed id (return null) instead of letting new Uri(...) throw
        // UriFormatException up through ListSecretsAsync/GetSecretWithMetadataAsync.
        if (!TryParseSecretId(secretId, out var uri)) return null;
        var segments = uri!.Segments;
        // /secrets/name or /secrets/name/version
        return segments.Length >= 3 ? segments[2].TrimEnd('/') : null;
    }

    private string? ExtractVersion(string? secretId)
    {
        // CR-L340: tolerate a relative/malformed id (return null) instead of throwing UriFormatException.
        if (!TryParseSecretId(secretId, out var uri)) return null;
        var segments = uri!.Segments;
        return segments.Length >= 4 ? segments[3].TrimEnd('/') : null;
    }

    #endregion
}
