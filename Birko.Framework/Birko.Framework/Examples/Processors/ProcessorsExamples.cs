using System.Text;
using Birko.Data.Processors;
using Birko.Helpers;

namespace Birko.Framework.Examples.Processors;

public static class ProcessorsExamples
{
    // ── Example models for demos ──
    private class Product
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    private class CsvProduct : CsvProcessor<Product>
    {
        public Product CurrentItem => _item;
        public CsvProduct(char delimiter = ',') : base(delimiter: delimiter) { }
    }

    private class XmlProduct : XmlProcessor<Product>
    {
        public Product CurrentItem { get => _item; set => _item = value; }
        public XmlProduct() : base() { }
    }

    // ────────────────────────────────────────────────────────
    //  CsvParser (Birko.Helpers)
    // ────────────────────────────────────────────────────────

    public static void RunCsvParserExample()
    {
        ExampleOutput.WriteHeader("CsvParser (Birko.Helpers)");

        var csv = "Name,Price,Category\nWidget,9.99,Tools\n\"Bolt, hex\",1.50,Hardware\n\"She said \"\"hi\"\"\",0,Misc\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var parser = new CsvParser(stream);

        foreach (var row in parser.Parse())
        {
            ExampleOutput.WriteInfo($"Line {parser.Line}", string.Join(" | ", row));
        }

        ExampleOutput.WriteLine();
        ExampleOutput.WriteDim("CsvParser is a standalone utility in Birko.Helpers.");
        ExampleOutput.WriteDim("RFC 4180 compliant: quoted fields, escaped quotes, multiline.");
    }

    // ────────────────────────────────────────────────────────
    //  CsvProcessor
    // ────────────────────────────────────────────────────────

    public static async Task RunCsvProcessorExample()
    {
        ExampleOutput.WriteHeader("CsvProcessor — CSV Stream Processing");

        var csv = "Name,Price,Category\nWidget,9.99,Tools\nBolt,1.50,Hardware\nGadget,24.95,Electronics\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var processor = new CsvProduct();
        var items = new List<Product>();

        processor.OnElementValue = (col, value) =>
        {
            switch (col)
            {
                case "0": processor.CurrentItem.Name = value; break;
                case "1": processor.CurrentItem.Price = decimal.Parse(value); break;
                case "2": processor.CurrentItem.Category = value; break;
            }
        };
        processor.OnItemProcessed = (product, ct) =>
        {
            items.Add(new Product { Name = product.Name, Price = product.Price, Category = product.Category });
            return Task.CompletedTask;
        };

        await processor.ProcessStreamAsync(stream);

        ExampleOutput.WriteInfo("Items parsed", items.Count.ToString());
        foreach (var item in items)
        {
            ExampleOutput.WriteSuccess($"{item.Name} — ${item.Price} ({item.Category})");
        }

        ExampleOutput.WriteLine();
        ExampleOutput.WriteDim("SkipFirst = true (default) skips the header row.");
        ExampleOutput.WriteDim("Column indices passed as strings: \"0\", \"1\", \"2\".");
    }

    // ────────────────────────────────────────────────────────
    //  CsvProcessor — Sync
    // ────────────────────────────────────────────────────────

    public static void RunCsvSyncExample()
    {
        ExampleOutput.WriteHeader("CsvProcessor — Sync Processing");

        var csv = "Name;Price\nAlpha;10.00\nBeta;20.00\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var processor = new CsvProduct(delimiter: ';');
        var items = new List<string>();

        processor.OnElementValue = (col, value) =>
        {
            if (col == "0") processor.CurrentItem.Name = value;
        };
        processor.OnItemProcessedSync = product =>
        {
            items.Add(product.Name);
        };

        processor.ProcessStream(stream);

        ExampleOutput.WriteInfo("Items", string.Join(", ", items));
        ExampleOutput.WriteDim("Sync uses OnItemProcessedSync and ProcessStream().");
    }

    // ────────────────────────────────────────────────────────
    //  XmlProcessor
    // ────────────────────────────────────────────────────────

    public static async Task RunXmlProcessorExample()
    {
        ExampleOutput.WriteHeader("XmlProcessor — XML Stream Processing");

        var xml = @"<?xml version=""1.0""?>
<products>
  <product>
    <name>Widget</name>
    <price>9.99</price>
    <category>Tools</category>
  </product>
  <product>
    <name>Gadget</name>
    <price>24.95</price>
    <category>Electronics</category>
  </product>
  <product>
    <name><![CDATA[Special <Item>]]></name>
    <price>5.00</price>
    <category>Misc</category>
  </product>
</products>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var processor = new XmlProduct();
        var items = new List<Product>();

        processor.OnElementValue = (name, value) =>
        {
            switch (name)
            {
                case "name": processor.CurrentItem.Name = value; break;
                case "price": processor.CurrentItem.Price = decimal.Parse(value); break;
                case "category": processor.CurrentItem.Category = value; break;
            }
        };
        processor.OnElementEnd = name =>
        {
            if (name == "product")
            {
                items.Add(new Product
                {
                    Name = processor.CurrentItem.Name,
                    Price = processor.CurrentItem.Price,
                    Category = processor.CurrentItem.Category
                });
                processor.CurrentItem = new Product();
            }
        };

        await processor.ProcessStreamAsync(stream);

        ExampleOutput.WriteInfo("Items parsed", items.Count.ToString());
        foreach (var item in items)
        {
            ExampleOutput.WriteSuccess($"{item.Name} — ${item.Price} ({item.Category})");
        }

        ExampleOutput.WriteLine();
        ExampleOutput.WriteDim("XmlProcessor fires OnElementStart/Value/End for each XML node.");
        ExampleOutput.WriteDim("CDATA sections are handled transparently.");
    }

    // ────────────────────────────────────────────────────────
    //  Decorator Composition
    // ────────────────────────────────────────────────────────

    public static void RunCompositionExample()
    {
        ExampleOutput.WriteHeader("Decorator Composition Pattern");

        ExampleOutput.WriteDim("Processors compose via generic type parameters:");
        ExampleOutput.WriteLine();
        ExampleOutput.WriteLine("  HttpProcessor<ZipProcessor<XmlProcessor<T>, T>, T>");
        ExampleOutput.WriteLine("       |              |              |");
        ExampleOutput.WriteLine("       |              |              +-- Innermost: parse XML");
        ExampleOutput.WriteLine("       |              +-- Middle: extract from ZIP");
        ExampleOutput.WriteLine("       +-- Outermost: download via HTTP");
        ExampleOutput.WriteLine();

        ExampleOutput.WriteDim("Example code:");
        ExampleOutput.WriteLine();
        ExampleOutput.WriteLine("  using var processor = new HttpProcessor<");
        ExampleOutput.WriteLine("      ZipProcessor<XmlProcessor<Product>, Product>,");
        ExampleOutput.WriteLine("      Product>(");
        ExampleOutput.WriteLine("      new ZipProcessor<XmlProcessor<Product>, Product>(");
        ExampleOutput.WriteLine("          new XmlProcessor<Product>(),");
        ExampleOutput.WriteLine("          extractPath: \"temp\"),");
        ExampleOutput.WriteLine("      url: \"https://example.com/feed.zip\",");
        ExampleOutput.WriteLine("      downloadPath: \"temp\",");
        ExampleOutput.WriteLine("      fileName: \"feed.zip\");");
        ExampleOutput.WriteLine();
        ExampleOutput.WriteLine("  processor.OnItemProcessed = async (p, ct) =>");
        ExampleOutput.WriteLine("      await store.CreateAsync(p);");
        ExampleOutput.WriteLine();
        ExampleOutput.WriteLine("  await processor.ProcessAsync(cancellationToken);");
        ExampleOutput.WriteLine();

        ExampleOutput.WriteDim("Events wire from inner to outer — subscribe only on outermost.");
        ExampleOutput.WriteDim("Use .Inner property to access nested processors.");
    }

    // ────────────────────────────────────────────────────────
    //  ZipProcessor
    // ────────────────────────────────────────────────────────

    public static async Task RunZipProcessorExample()
    {
        ExampleOutput.WriteHeader("ZipProcessor — ZIP + CSV Composition");

        // Create a ZIP with a CSV in memory
        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
            zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("data.csv");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("Name,Price\nZipped Widget,12.99\nZipped Gadget,34.50\n");
        }
        zipStream.Position = 0;

        var extractPath = Path.Combine(Path.GetTempPath(), $"birko_demo_{Guid.NewGuid():N}");

        try
        {
            var csvProcessor = new CsvProduct();
            var zipProcessor = new ZipProcessor<CsvProduct, Product>(
                csvProcessor, extractPath: extractPath);
            var items = new List<string>();

            zipProcessor.OnElementValue = (col, value) =>
            {
                if (col == "0") csvProcessor.CurrentItem.Name = value;
                if (col == "1") csvProcessor.CurrentItem.Price = decimal.Parse(value);
            };
            zipProcessor.OnItemProcessed = (item, ct) =>
            {
                items.Add($"{item.Name} (${item.Price})");
                return Task.CompletedTask;
            };

            await zipProcessor.ProcessStreamAsync(zipStream);

            ExampleOutput.WriteInfo("Items from ZIP", items.Count.ToString());
            foreach (var item in items)
            {
                ExampleOutput.WriteSuccess(item);
            }
        }
        finally
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
        }

        ExampleOutput.WriteLine();
        ExampleOutput.WriteDim("ZipProcessor extracts first entry, passes to inner processor.");
        ExampleOutput.WriteDim("Extracted file is cleaned up automatically.");
    }

    // ────────────────────────────────────────────────────────
    //  Error Handling
    // ────────────────────────────────────────────────────────

    public static void RunErrorHandlingExample()
    {
        ExampleOutput.WriteHeader("Error Handling");

        // Missing source file
        try
        {
            var processor = new XmlProduct();
            processor.Process();
        }
        catch (ProcessorException ex)
        {
            ExampleOutput.WriteInfo("ProcessorException", ex.Message);
        }

        // HTTP download error
        try
        {
            using var processor = new HttpProcessor<CsvProduct, Product>(
                new CsvProduct(), "https://localhost:1/nonexistent", "temp", "test.csv");
            processor.Process();
        }
        catch (ProcessorDownloadException ex)
        {
            ExampleOutput.WriteInfo("ProcessorDownloadException", $"URL: {ex.Url}");
            ExampleOutput.WriteInfo("Message", ex.Message);
        }

        ExampleOutput.WriteLine();
        ExampleOutput.WriteDim("ProcessorException — base for all processor errors.");
        ExampleOutput.WriteDim("ProcessorDownloadException — HTTP failures with URL context.");
        ExampleOutput.WriteDim("ProcessorParseException — parse errors with element context.");
    }
}
