using System.IO.Compression;
using System.Text;
using Birko.Data.Processors;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

public class ZipProcessorTests
{
    private static MemoryStream CreateZipWithCsv(string csvContent)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("data.csv");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(csvContent);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ProcessStreamAsync_ExtractsAndProcesses()
    {
        using var zipStream = CreateZipWithCsv("Name,Value\nAlice,100\n");
        var extractPath = Path.Combine(Path.GetTempPath(), $"birko_test_{Guid.NewGuid():N}");

        try
        {
            var csvProcessor = new TestCsvProcessor();
            var zipProcessor = new ZipProcessor<TestCsvProcessor, TestItem>(
                csvProcessor, extractPath: extractPath);
            var items = new List<string>();

            zipProcessor.OnElementValue = (col, value) =>
            {
                if (col == "0") csvProcessor.CurrentItem.Name = value;
            };
            zipProcessor.OnItemProcessed = (item, _) =>
            {
                items.Add(item.Name);
                return Task.CompletedTask;
            };

            await zipProcessor.ProcessStreamAsync(zipStream);

            items.Should().ContainSingle().Which.Should().Be("Alice");
        }
        finally
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
        }
    }

    [Fact]
    public void ProcessStream_Sync_ExtractsAndProcesses()
    {
        using var zipStream = CreateZipWithCsv("Name\nBob\n");
        var extractPath = Path.Combine(Path.GetTempPath(), $"birko_test_{Guid.NewGuid():N}");

        try
        {
            var csvProcessor = new TestCsvProcessor();
            var zipProcessor = new ZipProcessor<TestCsvProcessor, TestItem>(
                csvProcessor, extractPath: extractPath);
            var items = new List<string>();

            zipProcessor.OnElementValue = (col, value) =>
            {
                if (col == "0") csvProcessor.CurrentItem.Name = value;
            };
            zipProcessor.OnItemProcessedSync = item => items.Add(item.Name);

            zipProcessor.ProcessStream(zipStream);

            items.Should().ContainSingle().Which.Should().Be("Bob");
        }
        finally
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
        }
    }

    // CR-M127: a nested entry (e.g. "export/data.csv") must still extract. The Zip Slip hardening
    // flattens the entry to its file name, so it lands directly in _extractPath (whose parent exists)
    // rather than a missing subdirectory that would throw DirectoryNotFoundException.
    [Fact]
    public async Task ProcessStreamAsync_NestedFolderEntry_Extracts()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("export/data.csv");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("Name\nCarol\n");
        }
        ms.Position = 0;

        var extractPath = Path.Combine(Path.GetTempPath(), $"birko_test_{Guid.NewGuid():N}");
        try
        {
            var csvProcessor = new TestCsvProcessor();
            var zipProcessor = new ZipProcessor<TestCsvProcessor, TestItem>(csvProcessor, extractPath: extractPath);
            var items = new List<string>();
            zipProcessor.OnElementValue = (col, value) => { if (col == "0") csvProcessor.CurrentItem.Name = value; };
            zipProcessor.OnItemProcessed = (item, _) => { items.Add(item.Name); return Task.CompletedTask; };

            var act = () => zipProcessor.ProcessStreamAsync(ms);

            await act.Should().NotThrowAsync();
            items.Should().ContainSingle().Which.Should().Be("Carol");
        }
        finally
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
        }
    }

    [Fact]
    public async Task ProcessStreamAsync_EmptyZip_Throws()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // No entries
        }
        ms.Position = 0;

        var csvProcessor = new TestCsvProcessor();
        var zipProcessor = new ZipProcessor<TestCsvProcessor, TestItem>(csvProcessor);

        var act = () => zipProcessor.ProcessStreamAsync(ms);
        await act.Should().ThrowAsync<ProcessorException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task ProcessStreamAsync_InvalidEntryIndex_Throws()
    {
        using var zipStream = CreateZipWithCsv("a\n1\n");

        var csvProcessor = new TestCsvProcessor();
        var zipProcessor = new ZipProcessor<TestCsvProcessor, TestItem>(csvProcessor)
        {
            EntryIndex = 5
        };

        var act = () => zipProcessor.ProcessStreamAsync(zipStream);
        await act.Should().ThrowAsync<ProcessorException>()
            .WithMessage("*out of range*");
    }

    [Fact]
    public async Task ProcessStreamAsync_CleansUpExtractedFile()
    {
        using var zipStream = CreateZipWithCsv("Name\nAlice\n");
        var extractPath = Path.Combine(Path.GetTempPath(), $"birko_test_{Guid.NewGuid():N}");

        try
        {
            var csvProcessor = new TestCsvProcessor();
            var zipProcessor = new ZipProcessor<TestCsvProcessor, TestItem>(
                csvProcessor, extractPath: extractPath);
            zipProcessor.OnItemProcessed = (_, _) => Task.CompletedTask;

            await zipProcessor.ProcessStreamAsync(zipStream);

            var files = Directory.Exists(extractPath)
                ? Directory.GetFiles(extractPath)
                : [];
            files.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
        }
    }
}
