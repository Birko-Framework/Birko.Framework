using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Messaging.Razor;
using Birko.Messaging.Razor.Tests.TestResources;
using Birko.Messaging.Templates;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Razor.Tests;

public class RazorTemplateEngineTests : IDisposable
{
    private readonly string _tempDir;

    public RazorTemplateEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"birko_razor_engine_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task RenderAsync_InlineTemplate_RendersModel()
    {
        using var engine = new RazorTemplateEngine();

        var result = await engine.RenderAsync(
            "@Model.Name is @Model.Age years old",
            new { Name = "Alice", Age = 30 });

        result.Should().Contain("Alice");
        result.Should().Contain("30");
    }

    [Fact]
    public async Task RenderAsync_RazorConditional_Works()
    {
        using var engine = new RazorTemplateEngine();

        var result = await engine.RenderAsync(
            "@if (Model.IsVip) { <b>VIP</b> } else { <span>Regular</span> }",
            new { IsVip = true });

        result.Should().Contain("<b>VIP</b>");
        result.Should().NotContain("Regular");
    }

    [Fact]
    public async Task RenderAsync_RazorLoop_Works()
    {
        using var engine = new RazorTemplateEngine();

        var result = await engine.RenderAsync(
            "@foreach (var item in Model.Items) { <li>@item</li> }",
            new { Items = new[] { "A", "B", "C" } });

        result.Should().Contain("<li>A</li>");
        result.Should().Contain("<li>B</li>");
        result.Should().Contain("<li>C</li>");
    }

    [Fact]
    public async Task RenderAsync_NullTemplate_ThrowsArgumentNullException()
    {
        using var engine = new RazorTemplateEngine();

        var act = () => engine.RenderAsync((string)null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("template");
    }

    [Fact]
    public async Task RenderAsync_NullModel_ThrowsArgumentNullException()
    {
        using var engine = new RazorTemplateEngine();

        var act = () => engine.RenderAsync("Hello", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("model");
    }

    [Fact]
    public async Task RenderAsync_InvalidRazor_ThrowsTemplateRenderException()
    {
        using var engine = new RazorTemplateEngine();

        var act = () => engine.RenderAsync("@{ throw new System.Exception(\"boom\"); }", new { });

        await act.Should().ThrowAsync<TemplateRenderException>();
    }

    [Fact]
    public async Task RenderAsync_SameTemplateTwice_UsesCachedCompilation()
    {
        using var engine = new RazorTemplateEngine();
        var template = "Hello @Model.Name";

        var result1 = await engine.RenderAsync(template, new { Name = "First" });
        var result2 = await engine.RenderAsync(template, new { Name = "Second" });

        result1.Should().Contain("First");
        result2.Should().Contain("Second");
    }

    [Fact]
    public async Task RenderAsync_MessageTemplate_FallsBackToBodyTemplate()
    {
        using var engine = new RazorTemplateEngine();

        var template = new TestTemplate
        {
            Name = "test",
            Subject = "Subject",
            BodyTemplate = "Hello @Model.Name!",
            IsHtml = true
        };

        var result = await engine.RenderAsync(template, new { Name = "World" });

        result.Should().Contain("Hello World!");
    }

    [Fact]
    public async Task RenderAsync_NullMessageTemplate_ThrowsArgumentNullException()
    {
        using var engine = new RazorTemplateEngine();

        var act = () => engine.RenderAsync((IMessageTemplate)null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageTemplate");
    }

    [Fact]
    public async Task RenderAsync_MessageTemplate_WithFile_UsesFileTemplate()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "OrderEmail.cshtml"),
            "<h1>Order for @Model.Customer</h1>");

        using var engine = new RazorTemplateEngine(new RazorTemplateOptions
        {
            TemplateBasePath = _tempDir
        });

        var template = new TestTemplate
        {
            Name = "OrderEmail",
            Subject = "Order",
            BodyTemplate = "Fallback: @Model.Customer",
            IsHtml = true
        };

        var result = await engine.RenderAsync(template, new { Customer = "Alice" });

        result.Should().Contain("<h1>Order for Alice</h1>");
        result.Should().NotContain("Fallback");
    }

    [Fact]
    public async Task RenderAsync_MessageTemplate_FileMissing_FallsBackToInline()
    {
        using var engine = new RazorTemplateEngine(new RazorTemplateOptions
        {
            TemplateBasePath = _tempDir
        });

        var template = new TestTemplate
        {
            Name = "NonExistentFile",
            Subject = "Test",
            BodyTemplate = "Inline @Model.Value",
            IsHtml = false
        };

        var result = await engine.RenderAsync(template, new { Value = "works" });

        result.Should().Contain("Inline works");
    }

    [Fact]
    public async Task RenderFileAsync_ExistingFile_RendersContent()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "Welcome.cshtml"),
            "<p>Welcome @Model.Name!</p>");

        using var engine = new RazorTemplateEngine(new RazorTemplateOptions
        {
            TemplateBasePath = _tempDir
        });

        var result = await engine.RenderFileAsync("Welcome", new { Name = "Bob" });

        result.Should().Contain("<p>Welcome Bob!</p>");
    }

    [Fact]
    public async Task RenderFileAsync_NoBasePath_ThrowsTemplateRenderException()
    {
        using var engine = new RazorTemplateEngine();

        var act = () => engine.RenderFileAsync("SomeTemplate", new { });

        await act.Should().ThrowAsync<TemplateRenderException>()
            .WithMessage("*TemplateBasePath*");
    }

    [Fact]
    public async Task RenderFileAsync_NonExistentFile_ThrowsTemplateRenderException()
    {
        using var engine = new RazorTemplateEngine(new RazorTemplateOptions
        {
            TemplateBasePath = _tempDir
        });

        var act = () => engine.RenderFileAsync("Missing", new { });

        await act.Should().ThrowAsync<TemplateRenderException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task RenderFileAsync_NullName_ThrowsArgumentException()
    {
        using var engine = new RazorTemplateEngine(new RazorTemplateOptions
        {
            TemplateBasePath = _tempDir
        });

        var act = () => engine.RenderFileAsync("", new { });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var engine = new RazorTemplateEngine();
        engine.Dispose();

        var act = () => engine.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task RenderAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var engine = new RazorTemplateEngine();
        engine.Dispose();

        var act = () => engine.RenderAsync("test", new { });

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullEngine_ThrowsArgumentNullException()
    {
        var act = () => new RazorTemplateEngine((RazorLight.RazorLightEngine)null!);

        act.Should().Throw<ArgumentNullException>();
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
