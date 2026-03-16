using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Birko.Data.Processors;

/// <summary>
/// XML stream processor — reads elements sequentially via XmlReader,
/// firing events for element start/value/end.
/// </summary>
public class XmlProcessor<T> : AbstractProcessor<T>, IStreamProcessor where T : new()
{
    private readonly string? _sourceFile;
    private readonly Encoding _encoding;

    public XmlProcessor(string? sourceFile = null, Encoding? encoding = null, ILogger? logger = null)
        : base(logger)
    {
        _sourceFile = sourceFile;
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
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
        };

        using var reader = XmlReader.Create(new StreamReader(stream, _encoding), settings);
        string? currentElement = null;

        while (reader.Read())
        {
            currentElement = ProcessNode(reader, currentElement);
        }

        InvokeProcessFinished();
    }

    public virtual async Task ProcessStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
        };

        using var reader = XmlReader.Create(new StreamReader(stream, _encoding), settings);
        string? currentElement = null;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentElement = ProcessNode(reader, currentElement);
        }

        await InvokeProcessFinishedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Process a single XML node. Override to customize element handling.
    /// Returns the current element name for tracking context.
    /// </summary>
    protected virtual string? ProcessNode(XmlReader reader, string? currentElement)
    {
        switch (reader.NodeType)
        {
            case XmlNodeType.Element:
                currentElement = reader.Name;
                OnElementStart?.Invoke(reader.Name);
                break;

            case XmlNodeType.Text:
            case XmlNodeType.CDATA:
                var value = reader.Value?.Trim();
                if (!string.IsNullOrEmpty(value) && currentElement != null)
                {
                    OnElementValue?.Invoke(currentElement, value);
                }
                break;

            case XmlNodeType.EndElement:
                OnElementEnd?.Invoke(reader.Name);
                currentElement = null;
                break;
        }

        return currentElement;
    }
}
