using Microsoft.Extensions.Logging;

namespace Birko.Data.Processors;

/// <summary>
/// Decorator that downloads a file via HTTP, then delegates to an inner IStreamProcessor.
/// Cleans up the downloaded file after processing.
/// </summary>
public class HttpProcessor<TProcessor, TModel> : AbstractProcessor<TModel>, IDisposable
    where TProcessor : AbstractProcessor<TModel>, IStreamProcessor
    where TModel : new()
{
    private readonly TProcessor _inner;
    private readonly string _url;
    private readonly string _downloadPath;
    private readonly string _fileName;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public HttpProcessor(
        TProcessor inner,
        string url,
        string downloadPath,
        string fileName,
        HttpClient? httpClient = null,
        ILogger? logger = null)
        : base(logger)
    {
        _inner = inner;
        _url = url;
        _downloadPath = downloadPath;
        _fileName = SanitizeFileName(fileName);
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        WireInnerEvents();
    }

    /// <summary>The inner processor for direct configuration access.</summary>
    public TProcessor Inner => _inner;

    public override void Process()
    {
        Directory.CreateDirectory(_downloadPath);
        var filePath = Path.Combine(_downloadPath, _fileName);

        try
        {
            using var response = _httpClient.Send(
                new HttpRequestMessage(HttpMethod.Get, _url),
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var responseStream = response.Content.ReadAsStream();
            using var fileStream = new FileStream(
                filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            responseStream.CopyTo(fileStream);
        }
        catch (HttpRequestException ex)
        {
            Log(LogLevel.Warning, "Download failed for {Url}: {Message}", _url, ex.Message);
            throw new ProcessorDownloadException(_url, ex);
        }

        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _inner.ProcessStream(stream);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    public override async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_downloadPath);
        var filePath = Path.Combine(_downloadPath, _fileName);

        try
        {
            using var response = await _httpClient.GetAsync(
                _url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = new FileStream(
                filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            Log(LogLevel.Warning, "Download failed for {Url}: {Message}", _url, ex.Message);
            throw new ProcessorDownloadException(_url, ex);
        }

        try
        {
            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await _inner.ProcessStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private void WireInnerEvents()
    {
        _inner.OnItemProcessed = async (item, ct) =>
        {
            if (OnItemProcessed != null)
            {
                await OnItemProcessed(item, ct).ConfigureAwait(false);
            }
        };
        _inner.OnItemProcessedSync = item => OnItemProcessedSync?.Invoke(item);
        _inner.OnProcessFinished = async ct =>
        {
            if (OnProcessFinished != null)
            {
                await OnProcessFinished(ct).ConfigureAwait(false);
            }
        };
        _inner.OnProcessFinishedSync = () => OnProcessFinishedSync?.Invoke();
        _inner.OnElementStart = name => OnElementStart?.Invoke(name);
        _inner.OnElementValue = (name, value) => OnElementValue?.Invoke(name, value);
        _inner.OnElementEnd = name => OnElementEnd?.Invoke(name);
    }

    private static string SanitizeFileName(string name) =>
        name.Replace('/', '_').Replace('\\', '_');

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_inner as IDisposable)?.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
