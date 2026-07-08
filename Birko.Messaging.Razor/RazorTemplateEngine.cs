using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.Messaging.Templates;
using RazorLight;

namespace Birko.Messaging.Razor;

/// <summary>
/// Razor-based template engine using RazorLight. Supports inline string templates
/// and file-based .cshtml templates via <see cref="RazorFileTemplateProvider"/>.
/// </summary>
/// <remarks>
/// The consuming project must add the RazorLight NuGet package:
/// <c>&lt;PackageReference Include="RazorLight" Version="2.*" /&gt;</c>
/// </remarks>
public sealed class RazorTemplateEngine : ITemplateEngine, IDisposable
{
    private readonly RazorLightEngine _engine;
    private readonly RazorFileTemplateProvider? _fileProvider;
    private readonly ConcurrentDictionary<string, string> _inlineTemplateKeys = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new RazorTemplateEngine with the given options.
    /// </summary>
    public RazorTemplateEngine(RazorTemplateOptions? options = null)
    {
        options ??= new RazorTemplateOptions();

        var builder = new RazorLightEngineBuilder();

        // Install the compiled-template cache only when caching is enabled. RazorLight has no
        // caching provider unless one is registered (EngineHandler.IsCachingEnabled == cache != null),
        // so omitting it genuinely disables caching. The previous code called this unconditionally
        // and then AGAIN in the !EnableCaching branch — an inverted/dead condition that made
        // EnableCaching=false a no-op (caching could never be turned off).
        if (options.EnableCaching)
        {
            builder.UseMemoryCachingProvider();
        }

        if (!string.IsNullOrWhiteSpace(options.TemplateBasePath))
        {
            builder.UseFileSystemProject(options.TemplateBasePath, options.FileExtension);
            _fileProvider = new RazorFileTemplateProvider(options);
        }
        else
        {
            builder.UseEmbeddedResourcesProject(typeof(RazorTemplateEngine));
        }

        foreach (var ns in options.DefaultNamespaces)
        {
            builder.AddDefaultNamespaces(ns);
        }

        _engine = builder.Build();
    }

    /// <summary>
    /// Creates a new RazorTemplateEngine with a pre-built RazorLightEngine instance.
    /// </summary>
    public RazorTemplateEngine(RazorLightEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Renders an inline Razor template string with the given model.
    /// </summary>
    public async Task<string> RenderAsync(string template, object model, CancellationToken ct = default)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            // Generate a stable cache key for the template content
            var cacheKey = _inlineTemplateKeys.GetOrAdd(template, _ => $"inline_{Guid.NewGuid():N}");

            var result = await _engine.CompileRenderStringAsync(cacheKey, template, model).ConfigureAwait(false);
            return result;
        }
        catch (TemplateRenderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
        {
            throw new TemplateRenderException("inline", ex.Message, ex);
        }
    }

    /// <summary>
    /// Renders a template from an <see cref="IMessageTemplate"/>.
    /// If a file provider is configured and a file matching <see cref="IMessageTemplate.Name"/> exists,
    /// the file template is used. Otherwise, <see cref="IMessageTemplate.BodyTemplate"/> is rendered inline.
    /// </summary>
    public async Task<string> RenderAsync(IMessageTemplate messageTemplate, object model, CancellationToken ct = default)
    {
        if (messageTemplate == null)
        {
            throw new ArgumentNullException(nameof(messageTemplate));
        }
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            // Try file-based template first if a file provider is available
            if (_fileProvider != null)
            {
                try
                {
                    var fileContent = await _fileProvider.GetTemplateAsync(messageTemplate.Name, ct).ConfigureAwait(false);
                    var cacheKey = $"file_{messageTemplate.Name}";
                    return await _engine.CompileRenderStringAsync(cacheKey, fileContent, model).ConfigureAwait(false);
                }
                catch (TemplateRenderException)
                {
                    // File not found — fall through to inline rendering
                }
            }

            // Fall back to inline BodyTemplate
            return await RenderAsync(messageTemplate.BodyTemplate, model, ct).ConfigureAwait(false);
        }
        catch (TemplateRenderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
        {
            throw new TemplateRenderException(messageTemplate.Name, ex.Message, ex);
        }
    }

    /// <summary>
    /// Renders a file-based .cshtml template by name.
    /// Requires <see cref="RazorTemplateOptions.TemplateBasePath"/> to be configured.
    /// </summary>
    /// <param name="templateName">Template name (e.g., "OrderConfirmation" or "Emails/Welcome").</param>
    /// <param name="model">The model object passed to the template.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> RenderFileAsync(string templateName, object model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new ArgumentException("Template name cannot be null or empty.", nameof(templateName));
        }
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_fileProvider == null)
        {
            throw new TemplateRenderException(templateName, "No TemplateBasePath configured. Set RazorTemplateOptions.TemplateBasePath to use file-based templates.");
        }

        try
        {
            var fileContent = await _fileProvider.GetTemplateAsync(templateName, ct).ConfigureAwait(false);
            var cacheKey = $"file_{templateName}";
            return await _engine.CompileRenderStringAsync(cacheKey, fileContent, model).ConfigureAwait(false);
        }
        catch (TemplateRenderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
        {
            throw new TemplateRenderException(templateName, ex.Message, ex);
        }
    }

    /// <summary>
    /// Invalidates a cached file template, forcing recompilation on next render.
    /// </summary>
    public void InvalidateFileCache(string templateName)
    {
        _fileProvider?.InvalidateCache(templateName);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _inlineTemplateKeys.Clear();
            _fileProvider?.ClearCache();
            _disposed = true;
        }
    }
}
