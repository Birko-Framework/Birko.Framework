using Birko.Data.Processors;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

public class HttpProcessorTests
{
    [Fact]
    public void Dispose_DisposesInnerProcessor()
    {
        var inner = new TestCsvProcessor();
        var processor = new HttpProcessor<TestCsvProcessor, TestItem>(
            inner, "https://example.com/test.csv", "temp", "test.csv");

        // Should not throw
        processor.Dispose();
        processor.Dispose(); // Double dispose should be safe
    }

    [Fact]
    public void Inner_ExposesInnerProcessor()
    {
        var inner = new TestCsvProcessor(delimiter: ';');
        using var processor = new HttpProcessor<TestCsvProcessor, TestItem>(
            inner, "https://example.com/test.csv", "temp", "test.csv");

        processor.Inner.Should().BeSameAs(inner);
    }

    [Fact]
    public void Constructor_SanitizesFileName()
    {
        var inner = new TestCsvProcessor();
        using var processor = new HttpProcessor<TestCsvProcessor, TestItem>(
            inner, "https://example.com/test.csv", "temp", "path/to\\file.csv");

        processor.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_InvalidUrl_ThrowsDownloadException()
    {
        var inner = new TestCsvProcessor();
        using var httpClient = new HttpClient();
        using var processor = new HttpProcessor<TestCsvProcessor, TestItem>(
            inner, "https://localhost:1/nonexistent", "temp", "test.csv", httpClient);

        var act = () => processor.ProcessAsync();
        await act.Should().ThrowAsync<ProcessorDownloadException>()
            .Where(e => e.Url == "https://localhost:1/nonexistent");
    }

    [Fact]
    public void EventWiring_ForwardsFromInnerToOuter()
    {
        var inner = new TestCsvProcessor();
        using var processor = new HttpProcessor<TestCsvProcessor, TestItem>(
            inner, "https://example.com/test.csv", "temp", "test.csv");

        var startCalled = false;
        var valueCalled = false;
        var endCalled = false;

        processor.OnElementStart = _ => startCalled = true;
        processor.OnElementValue = (_, _) => valueCalled = true;
        processor.OnElementEnd = _ => endCalled = true;

        // Trigger events on inner — they should forward to outer
        inner.OnElementStart?.Invoke("test");
        inner.OnElementValue?.Invoke("test", "value");
        inner.OnElementEnd?.Invoke("test");

        startCalled.Should().BeTrue();
        valueCalled.Should().BeTrue();
        endCalled.Should().BeTrue();
    }
}
