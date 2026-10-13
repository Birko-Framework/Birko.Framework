using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Templates;

public interface ITemplateEngine
{
    Task<string> RenderAsync(string template, object model, CancellationToken ct = default);

    Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, CancellationToken ct = default);

    /// <summary>
    /// Renders with per-call value treatment (<see cref="TemplateRenderOptions"/>). The default implementation serves an
    /// engine that has no value encoding: without an encoder it renders as the overload without options does, and with
    /// one it throws rather than silently merging the values unencoded.
    /// </summary>
    /// <exception cref="NotSupportedException">An encoder was given to an engine that cannot apply it.</exception>
    Task<string> RenderAsync(string template, object model, TemplateRenderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfEncoderUnsupported(options);
        return RenderAsync(template, model, ct);
    }

    /// <inheritdoc cref="RenderAsync(string, object, TemplateRenderOptions, CancellationToken)"/>
    Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, TemplateRenderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfEncoderUnsupported(options);
        return RenderAsync(messageTemplate, model, ct);
    }

    private void ThrowIfEncoderUnsupported(TemplateRenderOptions options)
    {
        if (options.Encoder == null)
        {
            return;
        }
        throw new NotSupportedException(
            $"{GetType().Name} does not support a value encoder ({options.Encoder.GetType().Name}); its values would be merged unencoded.");
    }
}
