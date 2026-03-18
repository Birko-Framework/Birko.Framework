using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Birko.Time;

namespace Birko.Storage.AzureBlob;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IFileStorage"/> and <see cref="IPresignedUrlStorage"/>.
/// Uses the Azure Blob Storage REST API directly with OAuth2 client credentials — no Azure.Storage.Blobs SDK required.
/// </summary>
public sealed class AzureBlobStorage : IFileStorage, IPresignedUrlStorage, IDisposable
{
    private const string ApiVersion = "2023-11-03";
    private const string BlobType = "BlockBlob";

    private readonly AzureBlobSettings _settings;
    private readonly IDateTimeProvider _clock;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _accessToken;
    private DateTime _tokenExpiresAt;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>
    /// Optional storage account name for SAS presigned URL generation.
    /// Only required if <see cref="IPresignedUrlStorage"/> methods are used.
    /// </summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// Optional storage account key (Base64) for SAS presigned URL generation.
    /// Only required if <see cref="IPresignedUrlStorage"/> methods are used.
    /// </summary>
    public string? AccountKey { get; set; }

    public AzureBlobStorage(AzureBlobSettings settings, IDateTimeProvider clock) : this(settings, clock, null)
    {
    }

    public AzureBlobStorage(AzureBlobSettings settings, IDateTimeProvider clock, HttpClient? httpClient)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        if (string.IsNullOrWhiteSpace(settings.StorageAccountUri))
            throw new ArgumentException("StorageAccountUri is required", nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.ContainerName))
            throw new ArgumentException("ContainerName is required", nameof(settings));

        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }

    public async Task<FileReference> UploadAsync(
        string path,
        Stream content,
        string contentType,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);

        var merged = MergeOptions(options);
        ValidateContentType(path, contentType, merged);

        var blobPath = ResolvePath(path);

        if (merged?.MaxFileSize != null && content.CanSeek && content.Length > merged.MaxFileSize.Value)
            throw new FileTooLargeException(path, content.Length, merged.MaxFileSize.Value);

        if (merged?.OverwriteExisting != true)
        {
            if (await ExistsInternalAsync(blobPath, ct).ConfigureAwait(false))
                throw new FileAlreadyExistsException(path);
        }

        var uri = GetBlobUri(blobPath);
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Put, uri, ct).ConfigureAwait(false);
        request.Headers.Add("x-ms-blob-type", BlobType);

        if (merged?.MaxFileSize != null && !content.CanSeek)
        {
            var limited = await ReadWithLimitAsync(content, merged.MaxFileSize.Value, path, ct).ConfigureAwait(false);
            request.Content = new ByteArrayContent(limited);
        }
        else
        {
            request.Content = new StreamContent(content);
        }

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        if (!string.IsNullOrEmpty(merged?.ContentDisposition))
            request.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(merged!.ContentDisposition);

        // Set custom metadata as x-ms-meta-* headers
        if (merged?.Metadata != null)
        {
            foreach (var kvp in merged.Metadata)
            {
                request.Headers.Add($"x-ms-meta-{kvp.Key}", kvp.Value);
            }
        }

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Fetch reference for the uploaded blob
        return await GetReferenceInternalAsync(blobPath, path, ct).ConfigureAwait(false);
    }

    public async Task<StorageResult<Stream>> DownloadAsync(
        string path,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var blobPath = ResolvePath(path);
        var uri = GetBlobUri(blobPath);
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, uri, ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return StorageResult<Stream>.NotFound();

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return StorageResult<Stream>.Success(stream);
    }

    public async Task<bool> DeleteAsync(string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var blobPath = ResolvePath(path);
        var uri = GetBlobUri(blobPath);
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Delete, uri, ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var blobPath = ResolvePath(path);
        return await ExistsInternalAsync(blobPath, ct).ConfigureAwait(false);
    }

    public async Task<StorageResult<FileReference>> GetReferenceAsync(
        string path,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var blobPath = ResolvePath(path);
        var uri = GetBlobUri(blobPath);
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Head, uri, ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return StorageResult<FileReference>.NotFound();

        response.EnsureSuccessStatusCode();

        var reference = ParseBlobProperties(path, blobPath, response);
        return StorageResult<FileReference>.Success(reference);
    }

    public async Task<IReadOnlyList<FileReference>> ListAsync(
        string? prefix = null,
        int? maxResults = null,
        CancellationToken ct = default)
    {
        var resolvedPrefix = prefix != null ? ResolvePath(prefix) : GetPrefixPath();
        var uri = $"{BaseUri}{_settings.ContainerName}?restype=container&comp=list&prefix={Uri.EscapeDataString(resolvedPrefix)}";

        if (maxResults.HasValue)
            uri += $"&maxresults={maxResults.Value}";

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, uri, ct).ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Array.Empty<FileReference>();

        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseBlobList(xml, resolvedPrefix);
    }

    public async Task<FileReference> CopyAsync(
        string sourcePath,
        string destinationPath,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);

        var merged = MergeOptions(options);
        var srcBlobPath = ResolvePath(sourcePath);
        var dstBlobPath = ResolvePath(destinationPath);

        if (merged?.OverwriteExisting != true)
        {
            if (await ExistsInternalAsync(dstBlobPath, ct).ConfigureAwait(false))
                throw new FileAlreadyExistsException(destinationPath);
        }

        var sourceUri = GetBlobUri(srcBlobPath);
        var destUri = GetBlobUri(dstBlobPath);

        var request = await CreateAuthorizedRequestAsync(HttpMethod.Put, destUri, ct).ConfigureAwait(false);
        request.Headers.Add("x-ms-copy-source", sourceUri);
        request.Content = new ByteArrayContent(Array.Empty<byte>());
        request.Content.Headers.ContentLength = 0;

        // Set metadata on destination if provided
        if (merged?.Metadata != null)
        {
            foreach (var kvp in merged.Metadata)
            {
                request.Headers.Add($"x-ms-meta-{kvp.Key}", kvp.Value);
            }
        }

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await GetReferenceInternalAsync(dstBlobPath, destinationPath, ct).ConfigureAwait(false);
    }

    public async Task<FileReference> MoveAsync(
        string sourcePath,
        string destinationPath,
        StorageOptions? options = null,
        CancellationToken ct = default)
    {
        var reference = await CopyAsync(sourcePath, destinationPath, options, ct).ConfigureAwait(false);
        await DeleteAsync(sourcePath, ct).ConfigureAwait(false);
        return reference;
    }

    public Task<Uri> GetDownloadUrlAsync(
        string path,
        PresignedUrlOptions? options = null,
        CancellationToken ct = default)
    {
        ValidateSasCredentials();
        var blobPath = ResolvePath(path);
        var expiry = options?.Expiry ?? TimeSpan.FromHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            _settings.StorageAccountUri,
            _settings.ContainerName,
            blobPath,
            AccountName!,
            AccountKey!,
            "r", // read
            _clock.OffsetUtcNow.Add(expiry),
            _clock.OffsetUtcNow,
            options?.ContentDisposition,
            options?.ContentType);

        return Task.FromResult(uri);
    }

    public Task<Uri> GetUploadUrlAsync(
        string path,
        PresignedUrlOptions? options = null,
        CancellationToken ct = default)
    {
        ValidateSasCredentials();
        var blobPath = ResolvePath(path);
        var expiry = options?.Expiry ?? TimeSpan.FromHours(1);

        var uri = AzureBlobPresignedUrlProvider.GenerateSasUri(
            _settings.StorageAccountUri,
            _settings.ContainerName,
            blobPath,
            AccountName!,
            AccountKey!,
            "w", // write
            _clock.OffsetUtcNow.Add(expiry),
            _clock.OffsetUtcNow,
            options?.ContentDisposition,
            options?.ContentType);

        return Task.FromResult(uri);
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

    private string BaseUri => _settings.StorageAccountUri.TrimEnd('/') + "/";

    private string GetBlobUri(string blobPath) =>
        $"{BaseUri}{_settings.ContainerName}/{blobPath}";

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidPathException(path ?? string.Empty);

        // Reject traversal attempts
        if (path.Contains("..") || path.Contains('\0'))
            throw new InvalidPathException(path);

        // Normalize: trim leading slashes, use forward slashes
        var normalized = path.Replace('\\', '/').TrimStart('/');

        return CombinePrefix(normalized);
    }

    private string GetPrefixPath()
    {
        if (string.IsNullOrEmpty(_settings.PathPrefix))
            return string.Empty;

        return _settings.PathPrefix.Replace('\\', '/').TrimStart('/');
    }

    private string CombinePrefix(string path)
    {
        if (string.IsNullOrEmpty(_settings.PathPrefix))
            return path;

        var prefix = _settings.PathPrefix.Replace('\\', '/').TrimStart('/');
        if (!prefix.EndsWith('/'))
            prefix += "/";

        return prefix + path;
    }

    private string StripPrefix(string blobPath)
    {
        if (string.IsNullOrEmpty(_settings.PathPrefix))
            return blobPath;

        var prefix = _settings.PathPrefix.Replace('\\', '/').TrimStart('/');
        if (!prefix.EndsWith('/'))
            prefix += "/";

        return blobPath.StartsWith(prefix, StringComparison.Ordinal)
            ? blobPath[prefix.Length..]
            : blobPath;
    }

    private StorageOptions? MergeOptions(StorageOptions? perCall)
    {
        if (perCall != null) return perCall;
        return _settings.DefaultOptions;
    }

    private static void ValidateContentType(string path, string contentType, StorageOptions? options)
    {
        if (options?.AllowedContentTypes == null || options.AllowedContentTypes.Count == 0)
            return;

        if (!options.AllowedContentTypes.Any(t =>
            string.Equals(t, contentType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ContentTypeNotAllowedException(path, contentType);
        }
    }

    private void ValidateSasCredentials()
    {
        if (string.IsNullOrEmpty(AccountName) || string.IsNullOrEmpty(AccountKey))
            throw new InvalidOperationException(
                "AccountName and AccountKey must be set for presigned URL generation");
    }

    private async Task<bool> ExistsInternalAsync(string blobPath, CancellationToken ct)
    {
        var uri = GetBlobUri(blobPath);
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Head, uri, ct).ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        return response.StatusCode != HttpStatusCode.NotFound && response.IsSuccessStatusCode;
    }

    private async Task<FileReference> GetReferenceInternalAsync(string blobPath, string logicalPath, CancellationToken ct)
    {
        var uri = GetBlobUri(blobPath);
        var request = await CreateAuthorizedRequestAsync(HttpMethod.Head, uri, ct).ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return ParseBlobProperties(logicalPath, blobPath, response);
    }

    private FileReference ParseBlobProperties(string logicalPath, string blobPath, HttpResponseMessage response)
    {
        var headers = response.Headers;
        var contentHeaders = response.Content.Headers;

        var reference = new FileReference
        {
            Path = logicalPath,
            FileName = GetFileName(logicalPath),
            ContentType = contentHeaders.ContentType?.MediaType ?? string.Empty,
            Size = contentHeaders.ContentLength ?? 0,
        };

        if (contentHeaders.LastModified.HasValue)
            reference.LastModifiedAt = contentHeaders.LastModified.Value;

        if (headers.TryGetValues("x-ms-creation-time", out var creationValues))
        {
            if (DateTimeOffset.TryParse(creationValues.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
                reference.CreatedAt = created;
        }
        else if (reference.LastModifiedAt.HasValue)
        {
            reference.CreatedAt = reference.LastModifiedAt.Value;
        }

        if (headers.ETag != null)
            reference.ETag = headers.ETag.Tag?.Trim('"');

        // Parse x-ms-meta-* headers
        foreach (var header in headers)
        {
            if (header.Key.StartsWith("x-ms-meta-", StringComparison.OrdinalIgnoreCase))
            {
                var key = header.Key["x-ms-meta-".Length..];
                reference.Metadata[key] = header.Value.FirstOrDefault() ?? string.Empty;
            }
        }

        return reference;
    }

    private IReadOnlyList<FileReference> ParseBlobList(string xml, string resolvedPrefix)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var blobs = doc.Descendants(ns + "Blob");

        var results = new List<FileReference>();
        foreach (var blob in blobs)
        {
            var name = blob.Element(ns + "Name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var properties = blob.Element(ns + "Properties");
            var logicalPath = StripPrefix(name);

            var reference = new FileReference
            {
                Path = logicalPath,
                FileName = GetFileName(logicalPath),
                ContentType = properties?.Element(ns + "Content-Type")?.Value ?? string.Empty,
            };

            if (long.TryParse(properties?.Element(ns + "Content-Length")?.Value, out var size))
                reference.Size = size;

            if (DateTimeOffset.TryParse(properties?.Element(ns + "Last-Modified")?.Value,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastModified))
            {
                reference.LastModifiedAt = lastModified;
            }

            if (DateTimeOffset.TryParse(properties?.Element(ns + "Creation-Time")?.Value,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
            {
                reference.CreatedAt = created;
            }
            else if (reference.LastModifiedAt.HasValue)
            {
                reference.CreatedAt = reference.LastModifiedAt.Value;
            }

            var etag = properties?.Element(ns + "Etag")?.Value;
            if (!string.IsNullOrEmpty(etag))
                reference.ETag = etag.Trim('"');

            // Parse metadata from XML
            var metadata = blob.Element(ns + "Metadata");
            if (metadata != null)
            {
                foreach (var el in metadata.Elements())
                {
                    reference.Metadata[el.Name.LocalName] = el.Value;
                }
            }

            results.Add(reference);
        }

        return results.AsReadOnly();
    }

    private static string GetFileName(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    private static async Task<byte[]> ReadWithLimitAsync(Stream source, long maxSize, string path, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            totalRead += read;
            if (totalRead > maxSize)
                throw new FileTooLargeException(path, totalRead, maxSize);

            ms.Write(buffer, 0, read);
        }

        return ms.ToArray();
    }

    #endregion

    #region OAuth2 Authentication

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string uri, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-ms-version", ApiVersion);
        return request;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && _clock.UtcNow < _tokenExpiresAt.AddMinutes(-5))
            return _accessToken;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_accessToken != null && _clock.UtcNow < _tokenExpiresAt.AddMinutes(-5))
                return _accessToken;

            if (string.IsNullOrEmpty(_settings.TenantId) ||
                string.IsNullOrEmpty(_settings.ClientId) ||
                string.IsNullOrEmpty(_settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "TenantId, ClientId, and ClientSecret are required for Azure Blob Storage authentication");
            }

            var tokenEndpoint = $"https://login.microsoftonline.com/{_settings.TenantId}/oauth2/v2.0/token";
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["scope"] = "https://storage.azure.com/.default"
            });

            var response = await _httpClient.PostAsync(tokenEndpoint, tokenRequest, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            _accessToken = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("No access_token in response");

            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiresAt = _clock.UtcNow.AddSeconds(expiresIn);

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    #endregion
}
