using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Birko.Data.Processors;

/// <summary>
/// Decorator that extracts the first file from a ZIP archive,
/// then delegates to an inner IStreamProcessor.
/// Cleans up the extracted file after processing.
/// </summary>
public class ZipProcessor<TProcessor, TModel> : AbstractProcessor<TModel>, IStreamProcessor, IDisposable
    where TProcessor : AbstractProcessor<TModel>, IStreamProcessor
    where TModel : new()
{
    private readonly TProcessor _inner;
    private readonly string? _sourceFile;
    private readonly string _extractPath;
    private readonly Encoding _encoding;
    private bool _disposed;

    /// <summary>
    /// Index of the ZIP entry to extract. Default: 0 (first file).
    /// </summary>
    public int EntryIndex { get; set; }

    public ZipProcessor(
        TProcessor inner,
        string? sourceFile = null,
        string extractPath = ".",
        Encoding? encoding = null,
        ILogger? logger = null)
        : base(logger)
    {
        _inner = inner;
        _sourceFile = sourceFile;
        _extractPath = extractPath;
        _encoding = encoding ?? Encoding.UTF8;
        WireInnerEvents();
    }

    /// <summary>The inner processor for direct configuration access.</summary>
    public TProcessor Inner => _inner;

    public override void Process()
    {
        if (string.IsNullOrEmpty(_sourceFile))
        {
            throw new ProcessorException("Source file path is required.");
        }

        using var stream = new FileStream(
            _sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        ProcessStream(stream);
    }

    public override async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_sourceFile))
        {
            throw new ProcessorException("Source file path is required.");
        }

        await using var stream = new FileStream(
            _sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await ProcessStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public void ProcessStream(Stream stream)
    {
        Directory.CreateDirectory(_extractPath);
        string? extractedFile = ExtractFirstEntry(stream);
        if (extractedFile == null) return;

        try
        {
            using var fileStream = new FileStream(
                extractedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            _inner.ProcessStream(fileStream);
        }
        finally
        {
            if (File.Exists(extractedFile))
            {
                File.Delete(extractedFile);
            }
        }
    }

    public async Task ProcessStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_extractPath);
        string? extractedFile = ExtractFirstEntry(stream);
        if (extractedFile == null) return;

        try
        {
            await using var fileStream = new FileStream(
                extractedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            await _inner.ProcessStreamAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(extractedFile))
            {
                File.Delete(extractedFile);
            }
        }
    }

    private string? ExtractFirstEntry(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, _encoding);

            if (archive.Entries.Count == 0)
            {
                throw new ProcessorException("ZIP archive is empty.");
            }

            if (EntryIndex >= archive.Entries.Count)
            {
                throw new ProcessorException(
                    $"ZIP entry index {EntryIndex} out of range (archive has {archive.Entries.Count} entries).");
            }

            var entry = archive.Entries[EntryIndex];
            var extractedFile = Path.Combine(_extractPath, entry.FullName);
            entry.ExtractToFile(extractedFile, overwrite: true);
            return extractedFile;
        }
        catch (InvalidDataException ex)
        {
            throw new ProcessorException("Invalid ZIP archive.", ex);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_inner as IDisposable)?.Dispose();
    }
}
