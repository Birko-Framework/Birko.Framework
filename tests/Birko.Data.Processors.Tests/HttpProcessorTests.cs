using System.Net;
using System.Text;
using Birko.Data.Processors;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

public class HttpProcessorTests
{
    /// <summary>Stub handler that returns a fixed body for any request (sync + async paths).</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;

        private HttpResponseMessage Build() => new(HttpStatusCode.OK)
        {
            Content = new StringContent(_body, Encoding.UTF8, "text/csv"),
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Build());

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => Build();
    }

    // CR-L163: the successful download -> temp file -> inner.ProcessStream(Async) -> cleanup flow.
    [Fact]
    public async Task ProcessAsync_HappyPath_ProducesItems_AndDeletesTempFile()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"birko_http_{Guid.NewGuid():N}");
        try
        {
            var inner = new TestCsvProcessor();
            using var httpClient = new HttpClient(new StubHandler("Name,Value\nAlice,100\n"));
            using var processor = new HttpProcessor<TestCsvProcessor, TestItem>(
                inner, "https://example.com/data.csv", downloadPath, "data.csv", httpClient);

            var names = new List<string>();
            processor.OnElementValue = (col, value) => { if (col == "0") inner.CurrentItem.Name = value; };
            processor.OnItemProcessed = (item, _) => { names.Add(item.Name); return Task.CompletedTask; };

            await processor.ProcessAsync();

            names.Should().ContainSingle().Which.Should().Be("Alice"); // header row skipped
            File.Exists(Path.Combine(downloadPath, "data.csv")).Should().BeFalse("the temp file is deleted in the finally block");
        }
        finally
        {
            if (Directory.Exists(downloadPath)) Directory.Delete(downloadPath, true);
        }
    }

    [Fact]
    public void Process_HappyPath_ProducesItems_AndDeletesTempFile()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"birko_http_{Guid.NewGuid():N}");
        try
        {
            var inner = new TestCsvProcessor();
            using var httpClient = new HttpClient(new StubHandler("Name,Value\nBob,200\n"));
            using var processor = new HttpProcessor<TestCsvProcessor, TestItem>(
                inner, "https://example.com/data.csv", downloadPath, "data.csv", httpClient);

            var names = new List<string>();
            processor.OnElementValue = (col, value) => { if (col == "0") inner.CurrentItem.Name = value; };
            processor.OnItemProcessedSync = item => names.Add(item.Name);

            processor.Process();

            names.Should().ContainSingle().Which.Should().Be("Bob");
            File.Exists(Path.Combine(downloadPath, "data.csv")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(downloadPath)) Directory.Delete(downloadPath, true);
        }
    }

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
