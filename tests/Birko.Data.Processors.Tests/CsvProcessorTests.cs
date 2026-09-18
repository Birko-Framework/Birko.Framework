using System.Text;
using Birko.Data.Processors;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

public class TestItem
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Test subclass that exposes _item for verification.</summary>
public class TestCsvProcessor : CsvProcessor<TestItem>
{
    public TestItem CurrentItem => _item;

    public TestCsvProcessor(char delimiter = ',', char? enclosure = '"', Encoding? encoding = null)
        : base(delimiter: delimiter, enclosure: enclosure, encoding: encoding)
    {
    }
}

public class TestXmlProcessor : XmlProcessor<XmlTestItem>
{
    public XmlTestItem CurrentItem { get => _item; set => _item = value; }

    public TestXmlProcessor() : base() { }
}

public class XmlTestItem
{
    public string Name { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
}

public class CsvProcessorTests
{
    private static MemoryStream ToStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ProcessStreamAsync_SkipsHeader_ProcessesDataRows()
    {
        using var stream = ToStream("Name,Value\nAlice,100\nBob,200\n");
        var processor = new TestCsvProcessor();
        var items = new List<TestItem>();

        processor.OnElementValue = (col, value) =>
        {
            switch (col)
            {
                case "0": processor.CurrentItem.Name = value; break;
                case "1": processor.CurrentItem.Value = value; break;
            }
        };
        processor.OnItemProcessed = (item, ct) =>
        {
            items.Add(new TestItem { Name = item.Name, Value = item.Value });
            return Task.CompletedTask;
        };

        await processor.ProcessStreamAsync(stream);

        items.Should().HaveCount(2);
        items[0].Name.Should().Be("Alice");
        items[0].Value.Should().Be("100");
        items[1].Name.Should().Be("Bob");
        items[1].Value.Should().Be("200");
    }

    [Fact]
    public void ProcessStream_Sync_ProcessesDataRows()
    {
        using var stream = ToStream("Name,Value\nAlice,100\n");
        var processor = new TestCsvProcessor();
        var items = new List<TestItem>();

        processor.OnElementValue = (col, value) =>
        {
            switch (col)
            {
                case "0": processor.CurrentItem.Name = value; break;
                case "1": processor.CurrentItem.Value = value; break;
            }
        };
        processor.OnItemProcessedSync = item =>
        {
            items.Add(new TestItem { Name = item.Name, Value = item.Value });
        };

        processor.ProcessStream(stream);

        items.Should().HaveCount(1);
        items[0].Name.Should().Be("Alice");
    }

    [Fact]
    public async Task ProcessStreamAsync_NoSkipFirst_IncludesHeader()
    {
        using var stream = ToStream("Name,Value\nAlice,100\n");
        var processor = new TestCsvProcessor { SkipFirst = false };
        var count = 0;

        processor.OnItemProcessed = (_, _) => { count++; return Task.CompletedTask; };

        await processor.ProcessStreamAsync(stream);

        count.Should().Be(2);
    }

    [Fact]
    public async Task ProcessStreamAsync_FiresProcessFinished()
    {
        using var stream = ToStream("a\n1\n");
        var processor = new TestCsvProcessor();
        var finished = false;

        processor.OnProcessFinished = _ => { finished = true; return Task.CompletedTask; };

        await processor.ProcessStreamAsync(stream);

        finished.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessStreamAsync_SupportsCancellation()
    {
        using var stream = ToStream("Name\nAlice\nBob\nCharlie\n");
        var processor = new TestCsvProcessor();
        var cts = new CancellationTokenSource();
        var count = 0;

        processor.OnItemProcessed = (_, _) =>
        {
            count++;
            if (count >= 1) cts.Cancel();
            return Task.CompletedTask;
        };

        var act = () => processor.ProcessStreamAsync(stream, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessStreamAsync_CustomDelimiter()
    {
        using var stream = ToStream("Name;Value\nAlice;100\n");
        var processor = new TestCsvProcessor(delimiter: ';');
        var items = new List<string>();

        processor.OnElementValue = (col, value) =>
        {
            if (col == "0") items.Add(value);
        };
        processor.OnItemProcessed = (_, _) => Task.CompletedTask;

        await processor.ProcessStreamAsync(stream);

        items.Should().ContainSingle().Which.Should().Be("Alice");
    }
}
