namespace Birko.Data.Processors;

/// <summary>
/// Base processor interface for data pipeline operations.
/// </summary>
public interface IProcessor
{
    void Process();
    Task ProcessAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Processor that accepts a stream directly, enabling decorator composition.
/// </summary>
public interface IStreamProcessor : IProcessor
{
    void ProcessStream(Stream stream);
    Task ProcessStreamAsync(Stream stream, CancellationToken cancellationToken = default);
}
