using System;
using System.Threading.Tasks;
using Birko.Messaging.Templates;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Templates;

public class StringTemplateEngineTests
{
    private readonly StringTemplateEngine _engine = new();

    [Fact]
    public async Task RenderAsync_SimplePlaceholder_ReplacesValue()
    {
        var result = await _engine.RenderAsync("Hello {{Name}}!", new { Name = "World" });

        result.Should().Be("Hello World!");
    }

    [Fact]
    public async Task RenderAsync_MultiplePlaceholders_ReplacesAll()
    {
        var result = await _engine.RenderAsync(
            "{{First}} {{Last}}", new { First = "John", Last = "Doe" });

        result.Should().Be("John Doe");
    }

    [Fact]
    public async Task RenderAsync_NestedProperty_ResolvesPath()
    {
        var model = new { Customer = new { Name = "Alice", Address = new { City = "Prague" } } };

        var result = await _engine.RenderAsync(
            "{{Customer.Name}} from {{Customer.Address.City}}", model);

        result.Should().Be("Alice from Prague");
    }

    [Fact]
    public async Task RenderAsync_MissingProperty_ThrowsTemplateRenderException()
    {
        var act = () => _engine.RenderAsync("Hello {{Missing}}!", new { Name = "World" });

        await act.Should().ThrowAsync<TemplateRenderException>()
            .WithMessage("*Missing*");
    }

    [Fact]
    public async Task RenderAsync_NullTemplate_ThrowsArgumentNullException()
    {
        var act = () => _engine.RenderAsync((string)null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("template");
    }

    [Fact]
    public async Task RenderAsync_NullModel_ThrowsArgumentNullException()
    {
        var act = () => _engine.RenderAsync("Hello", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("model");
    }

    [Fact]
    public async Task RenderAsync_NoPlaceholders_ReturnsUnchanged()
    {
        var result = await _engine.RenderAsync("No placeholders here", new { Name = "Test" });

        result.Should().Be("No placeholders here");
    }

    [Fact]
    public async Task RenderAsync_EmptyTemplate_ReturnsEmpty()
    {
        var result = await _engine.RenderAsync("", new { Name = "Test" });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_NullPropertyValue_ReplacesWithEmpty()
    {
        var result = await _engine.RenderAsync("Value: {{Name}}", new { Name = (string?)null });

        result.Should().Be("Value: ");
    }

    [Fact]
    public async Task RenderAsync_NullIntermediateInPath_ReplacesWithEmpty()
    {
        // CR-L297: a null object mid-path (Customer is null) resolves to empty — distinct from a missing
        // property (which throws) and from a null leaf value.
        var result = await _engine.RenderAsync("Hi {{Customer.Name}}!", new { Customer = (object?)null });

        result.Should().Be("Hi !");
    }

    [Fact]
    public async Task RenderAsync_MessageTemplate_RendersBodyTemplate()
    {
        var template = new TestTemplate
        {
            Name = "welcome",
            Subject = "Welcome {{Name}}",
            BodyTemplate = "Hello {{Name}}, welcome!",
            IsHtml = false
        };

        var result = await _engine.RenderAsync(template, new { Name = "Alice" });

        result.Should().Be("Hello Alice, welcome!");
    }

    [Fact]
    public async Task RenderAsync_NullMessageTemplate_ThrowsArgumentNullException()
    {
        var act = () => _engine.RenderAsync((IMessageTemplate)null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageTemplate");
    }

    private class TestTemplate : IMessageTemplate
    {
        public string Name { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;
        public bool IsHtml { get; set; }
    }
}
