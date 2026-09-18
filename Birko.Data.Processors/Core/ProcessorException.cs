namespace Birko.Data.Processors;

public class ProcessorException : Exception
{
    public ProcessorException(string message) : base(message) { }
    public ProcessorException(string message, Exception? innerException) : base(message, innerException) { }
}

public class ProcessorDownloadException : ProcessorException
{
    public string Url { get; }

    public ProcessorDownloadException(string url, Exception innerException)
        : base($"Failed to download: {url}", innerException)
    {
        Url = url;
    }
}

public class ProcessorParseException : ProcessorException
{
    public string? Element { get; }

    public ProcessorParseException(string message, string? element = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Element = element;
    }
}
