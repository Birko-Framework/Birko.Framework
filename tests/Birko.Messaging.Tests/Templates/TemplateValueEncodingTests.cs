using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Messaging.Templates;
using FluentAssertions;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Xunit;

namespace Birko.Messaging.Tests.Templates;

/// <summary>
/// TASK-586: a merged value is encoded per render call, except the raw paths the caller declares. The Markdown cases
/// render through Markdig with Symbio's pipeline, because "no formatting" is a claim about the rendered output.
/// </summary>
public class TemplateValueEncodingTests
{
    private readonly StringTemplateEngine _engine = new();

    // Symbio.Infrastructure MarkdownRenderer's pipeline: HTML off, bare-URL autolinks, a newline is a line break.
    private static readonly MarkdownPipeline SymbioPipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks(new AutoLinkOptions { AllowDomainWithoutPeriod = true, UseHttpsForWWWLinks = true })
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private static TemplateRenderOptions Markdown(params string[] rawPaths) => new()
    {
        Encoder = new MarkdownValueEncoder(),
        RawPaths = new HashSet<string>(rawPaths, StringComparer.Ordinal),
    };

    private sealed record Order(string Number, string? TrackingUrl);
    private sealed record Model(string Name, string? Note, string? TrackingUrl, Order Order, int Count);

    private static readonly Model Hostile = new(
        Name: "# Ján\n- Novák\n1) Third\n> quote\n| a | b |",
        Note: "**free** _gift_ [click](https://evil.example) <https://evil.example> www.evil.example ![img](https://x/y.png) `c` &amp;",
        TrackingUrl: "https://shop.example/track?o=AB_12&t=x-1",
        Order: new Order("A-1", "https://shop.example/t/AB_12"),
        Count: 0);

    [Fact]
    public async Task WithoutAnEncoder_TheOutputIsByteIdenticalToTheOverloadWithoutOptions()
    {
        const string template = "{{Name}} | {{Note}} | {{TrackingUrl}}{{#if Count}} n={{Count}}{{/if}} **kept**";

        var plain = await _engine.RenderAsync(template, Hostile);
        var withEmptyOptions = await _engine.RenderAsync(template, Hostile, new TemplateRenderOptions());

        withEmptyOptions.Should().Be(plain);
        plain.Should().Contain("**free**", "no encoder means values are merged verbatim, as before TASK-586");
    }

    [Fact]
    public async Task TheEncoderTouchesMergedValues_NotTheTemplateText()
    {
        var result = await _engine.RenderAsync("**Order** {{Order.Number}}: {{Note}}", Hostile, Markdown());

        result.Should().StartWith("**Order** A\\-1: ");
        result.Should().Contain("\\*\\*free\\*\\*");
    }

    [Fact]
    public async Task ARawPath_IsMergedUnencoded_MatchedByItsExactPath()
    {
        var result = await _engine.RenderAsync("{{TrackingUrl}} {{Order.TrackingUrl}}", Hostile, Markdown("Order.TrackingUrl"));

        result.Should().Be("https\\:\\/\\/shop\\.example\\/track\\?o\\=AB\\_12\\&t\\=x\\-1 https://shop.example/t/AB_12");
    }

    [Fact]
    public async Task RawPaths_AreCaseSensitive_SoAMisspeltEntryFailsSafe()
    {
        var result = await _engine.RenderAsync("{{TrackingUrl}}", Hostile, Markdown("trackingurl"));

        result.Should().StartWith("https\\:", "a path not listed exactly is encoded: the link shows as text, never as unescaped data");
    }

    [Fact]
    public async Task SectionsAreDecidedOnTheRawValue_AndEmptyValuesStayEmpty()
    {
        var model = Hostile with { Note = null, Count = 3 };

        var result = await _engine.RenderAsync("[{{#if Note}}has note{{/if}}][{{Note}}]{{#if Count}}{{Count}}{{/if}}", model, Markdown());

        result.Should().Be("[][]3");
    }

    [Fact]
    public void EveryPrintableAsciiCharacter_RendersAsLiteralTextInMarkdown()
    {
        // From '!': a paragraph trims leading whitespace, which is not formatting.
        var all = new string(Enumerable.Range(33, 94).Select(i => (char)i).ToArray());

        var html = Markdig.Markdown.ToHtml(new MarkdownValueEncoder().Encode(all), SymbioPipeline).Trim();

        html.Should().StartWith("<p>").And.EndWith("</p>");
        WebUtility.HtmlDecode(html[3..^4]).Should().Be(all, "every character must come back as itself, with no markup");
    }

    [Fact]
    public async Task InSymbiosPipeline_EscapedCustomerValuesRenderAsText_WhileTheRawTrackingLinkStillAutolinks()
    {
        const string template = "Hello {{Name}},\nnote: {{Note}}\nTrack: {{TrackingUrl}}\nDone.";

        var html = Markdig.Markdown.ToHtml(await _engine.RenderAsync(template, Hostile, Markdown("TrackingUrl")), SymbioPipeline);

        Regex.Matches(html, "<a ").Should().ContainSingle("only the tracking link is a link");
        html.Should().Contain("<a href=\"https://shop.example/track?o=AB_12&amp;t=x-1\">");
        html.Should().NotContainAny("<strong>", "<em>", "<img", "<code>", "<ul>", "<ol>", "<blockquote>", "<h1>", "<table>");
        html.Should().Contain("Done.", "the name's quote marker must not swallow the rest of the mail");
    }

    [Fact]
    public async Task InSymbiosPipeline_TheSameTemplateWithoutTheEncoder_IsTheDefectThisPrevents()
    {
        // The control: without it, the test above could pass on a renderer that formats nothing.
        const string template = "Hello {{Name}},\nnote: {{Note}}\nTrack: {{TrackingUrl}}\nDone.";

        var html = Markdig.Markdown.ToHtml(await _engine.RenderAsync(template, Hostile), SymbioPipeline);

        html.Should().Contain("<strong>").And.Contain("<blockquote>").And.Contain("<img");
        Regex.Matches(html, "<a ").Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task InSymbiosPipeline_IndentationAndBlankLinesInAValue_CannotStartACodeBlockOrEndTheParagraph()
    {
        // Escaping cannot neutralise line structure: after a blank line, 4 spaces or a tab start an indented code block.
        var model = Hostile with { Note = "    first\n\n    indented code\n\t\ttabbed\r\n\r\nlast" };

        var html = Markdig.Markdown.ToHtml(await _engine.RenderAsync("note: {{Note}}\nDone.", model, Markdown()), SymbioPipeline).Trim();

        html.Should().NotContain("<pre").And.NotContain("<code");
        html.Should().Be("<p>note: first<br />\nindented code<br />\ntabbed<br />\nlast<br />\nDone.</p>",
            "one paragraph, one line per value line, nothing indented");
    }

    [Fact]
    public async Task TheMessageTemplateOverload_AppliesTheOptions()
    {
        var template = new TestTemplate("Note: {{Note}}");

        var result = await _engine.RenderAsync(template, Hostile, Markdown());

        result.Should().StartWith("Note: \\*\\*free");
    }

    [Fact]
    public async Task AnEngineWithoutEncoding_RefusesAnEncoder_AndRendersWithoutOne()
    {
        ITemplateEngine engine = new VerbatimOnlyEngine();

        var refused = () => engine.RenderAsync("{{Note}}", Hostile, Markdown());
        var plain = await engine.RenderAsync("{{Note}}", Hostile, new TemplateRenderOptions());

        await refused.Should().ThrowAsync<NotSupportedException>().WithMessage("*VerbatimOnlyEngine*MarkdownValueEncoder*");
        plain.Should().Be("verbatim");
        var refusedForTemplate = () => engine.RenderAsync(new TestTemplate("x"), Hostile, Markdown());
        await refusedForTemplate.Should().ThrowAsync<NotSupportedException>();
    }

    private sealed record TestTemplate(string BodyTemplate) : IMessageTemplate
    {
        public string Name => "t";
        public string Subject => "s";
        public bool IsHtml => false;
    }

    /// <summary>Implements only the pre-TASK-586 members, as RazorTemplateEngine does.</summary>
    private sealed class VerbatimOnlyEngine : ITemplateEngine
    {
        public Task<string> RenderAsync(string template, object model, CancellationToken ct = default) => Task.FromResult("verbatim");
        public Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, CancellationToken ct = default) => Task.FromResult("verbatim");
    }
}
