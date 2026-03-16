using System.Text;
using Birko.Data.Processors;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

public class XmlProcessorTests
{
    private static MemoryStream ToStream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public async Task ProcessStreamAsync_ParsesElements()
    {
        var xml = "<items><item><name>Widget</name><price>9.99</price></item></items>";
        using var stream = ToStream(xml);

        var processor = new TestXmlProcessor();
        var items = new List<XmlTestItem>();

        processor.OnElementValue = (name, value) =>
        {
            switch (name)
            {
                case "name": processor.CurrentItem.Name = value; break;
                case "price": processor.CurrentItem.Price = value; break;
            }
        };
        processor.OnElementEnd = name =>
        {
            if (name == "item")
            {
                items.Add(new XmlTestItem { Name = processor.CurrentItem.Name, Price = processor.CurrentItem.Price });
                processor.CurrentItem = new XmlTestItem();
            }
        };

        await processor.ProcessStreamAsync(stream);

        items.Should().HaveCount(1);
        items[0].Name.Should().Be("Widget");
        items[0].Price.Should().Be("9.99");
    }

    [Fact]
    public void ProcessStream_Sync_ParsesElements()
    {
        var xml = "<root><item><name>Test</name></item></root>";
        using var stream = ToStream(xml);

        var processor = new TestXmlProcessor();
        var names = new List<string>();

        processor.OnElementValue = (name, value) =>
        {
            if (name == "name") names.Add(value);
        };

        processor.ProcessStream(stream);

        names.Should().ContainSingle().Which.Should().Be("Test");
    }

    [Fact]
    public async Task ProcessStreamAsync_FiresProcessFinished()
    {
        var xml = "<root><item>test</item></root>";
        using var stream = ToStream(xml);

        var processor = new TestXmlProcessor();
        var finished = false;

        processor.OnProcessFinished = _ => { finished = true; return Task.CompletedTask; };

        await processor.ProcessStreamAsync(stream);

        finished.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessStreamAsync_MultipleItems()
    {
        var xml = "<items><item><name>A</name></item><item><name>B</name></item><item><name>C</name></item></items>";
        using var stream = ToStream(xml);

        var processor = new TestXmlProcessor();
        var names = new List<string>();

        processor.OnElementValue = (name, value) =>
        {
            if (name == "name") names.Add(value);
        };

        await processor.ProcessStreamAsync(stream);

        names.Should().BeEquivalentTo(["A", "B", "C"]);
    }

    [Fact]
    public async Task ProcessStreamAsync_CdataContent()
    {
        var xml = "<root><data><![CDATA[Some <special> content]]></data></root>";
        using var stream = ToStream(xml);

        var processor = new TestXmlProcessor();
        var values = new List<string>();

        processor.OnElementValue = (_, value) => values.Add(value);

        await processor.ProcessStreamAsync(stream);

        values.Should().ContainSingle().Which.Should().Be("Some <special> content");
    }

    [Fact]
    public async Task ProcessStreamAsync_SupportsCancellation()
    {
        var xml = "<items><item><name>A</name></item><item><name>B</name></item></items>";
        using var stream = ToStream(xml);

        var processor = new TestXmlProcessor();
        var cts = new CancellationTokenSource();

        processor.OnElementStart = name =>
        {
            if (name == "item") cts.Cancel();
        };

        var act = () => processor.ProcessStreamAsync(stream, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessAsync_WithoutSourceFile_Throws()
    {
        var processor = new TestXmlProcessor();

        var act = () => processor.ProcessAsync();
        await act.Should().ThrowAsync<ProcessorException>()
            .WithMessage("*Source file path*");
    }
}
