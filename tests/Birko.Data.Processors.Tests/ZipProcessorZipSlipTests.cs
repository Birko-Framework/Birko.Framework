using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Processors;
using FluentAssertions;

namespace Birko.Data.Processors.Tests;

/// <summary>
/// CR-H076: ZipProcessor.ExtractFirstEntry built the destination via
/// Path.Combine(_extractPath, entry.FullName) with the attacker-controlled entry name, allowing a
/// classic Zip Slip (rooted or ../ entry names escaping the extract directory). These tests feed
/// malicious entry names and assert the write stays inside the extract directory.
/// </summary>
public class ZipProcessorZipSlipTests
{
    public class Dummy { }

    /// <summary>Minimal inner stream processor that records the stream content it receives.</summary>
    private class CapturingProcessor : AbstractProcessor<Dummy>, IStreamProcessor
    {
        public string? Captured { get; private set; }

        public void ProcessStream(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Captured = reader.ReadToEnd();
        }

        public Task ProcessStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            ProcessStream(stream);
            return Task.CompletedTask;
        }

        public override void Process() { }
        public override Task ProcessAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static MemoryStream MakeZip(string entryName, string content)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void TraversalEntryName_DoesNotEscapeExtractDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "birko-zipslip-" + Guid.NewGuid().ToString("N"));
        var extractDir = Path.Combine(root, "extract");
        Directory.CreateDirectory(extractDir);
        var escapedTarget = Path.Combine(root, "pwned.txt"); // one level above extractDir

        try
        {
            using var zip = MakeZip("../pwned.txt", "malicious");
            var inner = new CapturingProcessor();
            using var proc = new ZipProcessor<CapturingProcessor, Dummy>(inner, extractPath: extractDir);

            proc.ProcessStream(zip);

            File.Exists(escapedTarget).Should().BeFalse("the entry must not be written outside the extract dir");
            inner.Captured.Should().Be("malicious", "the entry is still extracted, but into the safe directory");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RootedEntryName_DoesNotEscapeExtractDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "birko-zipslip-" + Guid.NewGuid().ToString("N"));
        var extractDir = Path.Combine(root, "extract");
        Directory.CreateDirectory(extractDir);

        try
        {
            // A rooted second arg to Path.Combine would previously discard _extractPath entirely.
            using var zip = MakeZip("rooted-name.txt", "payload");
            var inner = new CapturingProcessor();
            using var proc = new ZipProcessor<CapturingProcessor, Dummy>(inner, extractPath: extractDir);

            proc.ProcessStream(zip);

            inner.Captured.Should().Be("payload");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NormalEntry_IsExtractedAndProcessed()
    {
        var extractDir = Path.Combine(Path.GetTempPath(), "birko-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);

        try
        {
            using var zip = MakeZip("data.csv", "a,b,c");
            var inner = new CapturingProcessor();
            using var proc = new ZipProcessor<CapturingProcessor, Dummy>(inner, extractPath: extractDir);

            proc.ProcessStream(zip);

            inner.Captured.Should().Be("a,b,c");
        }
        finally
        {
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        }
    }
}
