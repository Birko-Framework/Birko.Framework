using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Templates;

public sealed partial class StringTemplateEngine : ITemplateEngine
{
    [GeneratedRegex(@"\{\{(\w+(?:\.\w+)*)\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderPattern();

    public Task<string> RenderAsync(string template, object model, CancellationToken ct = default)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var result = PlaceholderPattern().Replace(template, match =>
        {
            var path = match.Groups[1].Value;
            var value = ResolvePropertyPath(model, path);
            return value?.ToString() ?? string.Empty;
        });

        return Task.FromResult(result);
    }

    public async Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, CancellationToken ct = default)
    {
        if (messageTemplate == null)
        {
            throw new ArgumentNullException(nameof(messageTemplate));
        }

        return await RenderAsync(messageTemplate.BodyTemplate, model, ct).ConfigureAwait(false);
    }

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
