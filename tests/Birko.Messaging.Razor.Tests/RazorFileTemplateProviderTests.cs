using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Messaging.Razor;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Razor.Tests;

public class RazorFileTemplateProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RazorFileTemplateProvider _provider;

    public RazorFileTemplateProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"birko_razor_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _provider = new RazorFileTemplateProvider(new RazorTemplateOptions
        {
            TemplateBasePath = _tempDir,
            EnableCaching = true
        });
    }

    [Fact]
    public async Task GetTemplateAsync_ExistingFile_ReturnsContent()
    {
        var expected = "<h1>Hello @Model.Name</h1>";
        File.WriteAllText(Path.Combine(_tempDir, "Welcome.cshtml"), expected);

        var result = await _provider.GetTemplateAsync("Welcome");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetTemplateAsync_WithExtension_ReturnsContent()
    {
        var expected = "<p>Test</p>";
        File.WriteAllText(Path.Combine(_tempDir, "Test.cshtml"), expected);

        var result = await _provider.GetTemplateAsync("Test.cshtml");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetTemplateAsync_Subdirectory_ReturnsContent()
    {
        var subDir = Path.Combine(_tempDir, "Emails");
        Directory.CreateDirectory(subDir);
        var expected = "<h1>Order</h1>";
        File.WriteAllText(Path.Combine(subDir, "Order.cshtml"), expected);

        var result = await _provider.GetTemplateAsync("Emails/Order");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetTemplateAsync_NonExistentFile_ThrowsTemplateRenderException()
    {
        var act = () => _provider.GetTemplateAsync("DoesNotExist");

        await act.Should().ThrowAsync<TemplateRenderException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetTemplateAsync_DirectoryTraversal_ThrowsTemplateRenderException()
    {
        var act = () => _provider.GetTemplateAsync("../../etc/passwd");

        await act.Should().ThrowAsync<TemplateRenderException>()
            .WithMessage("*escapes*");
    }

    [Fact]
    public async Task GetTemplateAsync_EmptyName_ThrowsArgumentException()
    {
        var act = () => _provider.GetTemplateAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetTemplateAsync_CachesContent()
    {
        var filePath = Path.Combine(_tempDir, "Cached.cshtml");
        File.WriteAllText(filePath, "Version1");

        var result1 = await _provider.GetTemplateAsync("Cached");

        // Modify file on disk
        File.WriteAllText(filePath, "Version2");

        var result2 = await _provider.GetTemplateAsync("Cached");

        result1.Should().Be("Version1");
        result2.Should().Be("Version1", "cached version should be returned");
    }

    [Fact]
    public async Task InvalidateCache_ReloadsFromDisk()
    {
        var filePath = Path.Combine(_tempDir, "Invalidate.cshtml");
        File.WriteAllText(filePath, "Original");

        await _provider.GetTemplateAsync("Invalidate");

        File.WriteAllText(filePath, "Updated");
        _provider.InvalidateCache("Invalidate");

        var result = await _provider.GetTemplateAsync("Invalidate");

        result.Should().Be("Updated");
    }

    [Fact]
    public async Task ClearCache_ReloadsAllFromDisk()
    {
        var filePath = Path.Combine(_tempDir, "ClearAll.cshtml");
        File.WriteAllText(filePath, "Original");

        await _provider.GetTemplateAsync("ClearAll");

        File.WriteAllText(filePath, "Updated");
        _provider.ClearCache();

        var result = await _provider.GetTemplateAsync("ClearAll");

        result.Should().Be("Updated");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new RazorFileTemplateProvider(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullBasePath_ThrowsArgumentException()
    {
        var act = () => new RazorFileTemplateProvider(new RazorTemplateOptions
        {
            TemplateBasePath = null
        });

        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
