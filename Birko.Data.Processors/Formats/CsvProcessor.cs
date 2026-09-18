using System.Text;
using Birko.Helpers;
using Microsoft.Extensions.Logging;

namespace Birko.Data.Processors;

/// <summary>
/// CSV stream processor — parses rows and columns, firing events per column.
/// </summary>
public class CsvProcessor<T> : AbstractProcessor<T>, IStreamProcessor where T : new()
{
    private readonly string? _sourceFile;
    private readonly char _delimiter;
    private readonly char? _enclosure;
    private readonly Encoding _encoding;

    /// <summary>Skip the first row (header). Default: true.</summary>
    public bool SkipFirst { get; set; } = true;

    public CsvProcessor(
        string? sourceFile = null,
        char delimiter = ',',
        char? enclosure = '"',
        Encoding? encoding = null,
        ILogger? logger = null)
        : base(logger)
    {
        _sourceFile = sourceFile;
        _delimiter = delimiter;
        _enclosure = enclosure;
        _encoding = encoding ?? Encoding.UTF8;
    }

    public override void Process()
    {
        if (string.IsNullOrEmpty(_sourceFile))
        {
            throw new ProcessorException("Source file path is required for file-based processing.");
        }

        using var stream = new FileStream(_sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        ProcessStream(stream);
    }

    public override async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_sourceFile))
        {
            throw new ProcessorException("Source file path is required for file-based processing.");
        }

        await using var stream = new FileStream(_sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await ProcessStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public virtual void ProcessStream(Stream stream)
    {
        var parser = new CsvParser(stream, _delimiter, _enclosure, _encoding);
        var lineIndex = 0;

        foreach (var columns in parser.Parse())
        {
            if (lineIndex == 0 && SkipFirst)
            {
                lineIndex++;
                continue;
            }

            InitItem();

            for (var i = 0; i < columns.Count; i++)
            {
                var colIndex = i.ToString();
                OnElementStart?.Invoke(colIndex);

                var value = columns[i];
                if (!string.IsNullOrEmpty(value))
                {
                    OnElementValue?.Invoke(colIndex, value);
                }

                OnElementEnd?.Invoke(colIndex);
            }

            PostProcessItem();
            lineIndex++;
        }

        InvokeProcessFinished();
    }

    /// <remarks>
    /// CR-M128: CSV parsing is inherently row-synchronous — this overload runs the synchronous
    /// <see cref="CsvParser.Parse"/> reader and awaits only the per-row callbacks, observing the
    /// <paramref name="cancellationToken"/> between rows. It is a convenience wrapper for async
    /// callers, not genuinely non-blocking I/O; on a network-backed stream it blocks the calling
    /// thread during reads. Prefer the sync <see cref="ProcessStream"/> when there is no async work
    /// in the row callbacks.
    /// </remarks>
    public virtual async Task ProcessStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var parser = new CsvParser(stream, _delimiter, _enclosure, _encoding);
        var lineIndex = 0;

        foreach (var columns in parser.Parse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (lineIndex == 0 && SkipFirst)
            {
                lineIndex++;
                continue;
            }

            InitItem();

            for (var i = 0; i < columns.Count; i++)
            {
                var colIndex = i.ToString();
                OnElementStart?.Invoke(colIndex);

                var value = columns[i];
                if (!string.IsNullOrEmpty(value))
                {
                    OnElementValue?.Invoke(colIndex, value);
                }

                OnElementEnd?.Invoke(colIndex);
            }

            await PostProcessItemAsync(cancellationToken).ConfigureAwait(false);
            lineIndex++;
        }

        await InvokeProcessFinishedAsync(cancellationToken).ConfigureAwait(false);
    }
}
