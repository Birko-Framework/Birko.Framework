using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Templates;

/// <summary>
/// Renders <c>{{Property.Path}}</c> placeholders by reflection, plus optional sections
/// <c>{{#if Property.Path}}…{{/if}}</c> that are kept only when the value is present.
/// </summary>
/// <remarks>
/// <para><b>Present</b> means: not <c>null</c>, not an empty or whitespace-only string, and not <c>false</c>.
/// Anything else (a number, a date, an object) is present, so <c>0</c> keeps its section — a quantity of zero is
/// a value, not an absence.</para>
/// <para>Sections do not nest and have no else branch. A <c>{{#if}}</c> without its <c>{{/if}}</c>, a stray
/// <c>{{/if}}</c> or a nested <c>{{#if}}</c> throws <see cref="TemplateRenderException"/> rather than shipping
/// the marker text to a recipient.</para>
/// <para><b>Filters</b> (TASK-815): <c>{{Property.Path|name}}</c> or <c>{{Property.Path|name:Argument.Path}}</c>, where the
/// argument is a model path resolved like a placeholder, e.g. <c>{{TotalAmount|money:CurrencyCode}}</c>. They come from
/// <see cref="TemplateRenderOptions.Filters"/>; a name the options do not carry, or any filter rendered through an
/// overload without options, throws <see cref="TemplateRenderException"/>. The encoder runs on the filter's output.</para>
/// </remarks>
public sealed partial class StringTemplateEngine : ITemplateEngine
{
    [GeneratedRegex(@"\{\{(\w+(?:\.\w+)*)(?:\|(\w+)(?::(\w+(?:\.\w+)*))?)?\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{\{#if\s+(\w+(?:\.\w+)*)\s*\}\}(.*?)\{\{/if\}\}", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"\{\{\s*[#/]", RegexOptions.Compiled)]
    private static partial Regex SectionMarkerPattern();

    public Task<string> RenderAsync(string template, object model, CancellationToken ct = default)
        => Task.FromResult(Render(template, model, options: null));

    /// <summary>
    /// Renders with <paramref name="options"/>: each merged value goes through <see cref="TemplateRenderOptions.Encoder"/>
    /// unless its path is in <see cref="TemplateRenderOptions.RawPaths"/>. <c>{{#if}}</c> presence is decided on the raw
    /// value, so encoding never changes which sections are kept.
    /// </summary>
    public Task<string> RenderAsync(string template, object model, TemplateRenderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.FromResult(Render(template, model, options));
    }

    public async Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, TemplateRenderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);
        return await RenderAsync(messageTemplate.BodyTemplate, model, options, ct).ConfigureAwait(false);
    }

    private static string Render(string template, object model, TemplateRenderOptions? options)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var withSections = SectionPattern().Replace(template, match =>
        {
            var body = match.Groups[2].Value;
            if (SectionMarkerPattern().IsMatch(body))
            {
                throw new TemplateRenderException("inline", "Nested '{{#if}}' sections are not supported.");
            }
            return IsPresent(ResolvePropertyPath(model, match.Groups[1].Value)) ? body : string.Empty;
        });

        // Whatever marker is left had no partner: an unclosed {{#if}}, a stray {{/if}}, or a misspelt one.
        if (SectionMarkerPattern().IsMatch(withSections))
        {
            throw new TemplateRenderException("inline", "Unbalanced '{{#if}}' / '{{/if}}' section.");
        }

        return PlaceholderPattern().Replace(withSections, match =>
        {
            var path = match.Groups[1].Value;
            var value = ResolvePropertyPath(model, path);
            var text = match.Groups[2].Success
                ? ApplyFilter(match.Groups[2].Value, value,
                    match.Groups[3].Success ? ResolvePropertyPath(model, match.Groups[3].Value) : null, options)
                : value?.ToString() ?? string.Empty;
            var encoder = options?.Encoder;
            var rawPaths = options?.RawPaths;
            if (encoder == null || text.Length == 0 || (rawPaths != null && rawPaths.Contains(path)))
            {
                return text;
            }
            return encoder.Encode(text);
        });
    }

    public async Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, CancellationToken ct = default)
    {
        if (messageTemplate == null)
        {
            throw new ArgumentNullException(nameof(messageTemplate));
        }

        return await RenderAsync(messageTemplate.BodyTemplate, model, ct).ConfigureAwait(false);
    }

    private static string ApplyFilter(string name, object? value, object? argument, TemplateRenderOptions? options)
    {
        if (options == null || !options.Filters.TryGetValue(name, out var filter))
        {
            throw new TemplateRenderException("inline", $"Unknown filter '{name}'.");
        }
        return filter.Apply(value, argument, CultureInfo.CurrentCulture);
    }

    private static bool IsPresent(object? value) => value switch
    {
        null => false,
        string s => !string.IsNullOrWhiteSpace(s),
        bool b => b,
        _ => true,
    };

    private static object? ResolvePropertyPath(object model, string path)
    {
        var parts = path.Split('.');
        object? current = model;

        foreach (var part in parts)
        {
            if (current == null)
            {
                return null;
            }

            var type = current.GetType();
            var property = type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
            {
                throw new TemplateRenderException(
                    "inline",
                    $"Property '{part}' not found on type '{type.Name}' (path: '{path}').");
            }

            current = property.GetValue(current);
        }

        return current;
    }
}
