using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Razor;

/// <summary>
/// Loads .cshtml template content from disk, with optional in-memory caching.
/// </summary>
public class RazorFileTemplateProvider
{
    private readonly string _basePath;
    private readonly string _fileExtension;
    private readonly Encoding _encoding;
    private readonly bool _enableCaching;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public RazorFileTemplateProvider(RazorTemplateOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.TemplateBasePath))
        {
            throw new ArgumentException("TemplateBasePath must be set when using RazorFileTemplateProvider.", nameof(options));
        }

        _basePath = options.TemplateBasePath;
        _fileExtension = options.FileExtension;
        _encoding = options.FileEncoding;
        _enableCaching = options.EnableCaching;
    }

    /// <summary>
    /// Resolves a template name to a file path and reads its content.
    /// </summary>
    /// <param name="templateName">Template name (e.g., "OrderConfirmation" or "Emails/Welcome").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw template content.</returns>
    public async Task<string> GetTemplateAsync(string templateName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new ArgumentException("Template name cannot be null or empty.", nameof(templateName));
        }

        if (_enableCaching && _cache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var filePath = ResolveFilePath(templateName);

        if (!File.Exists(filePath))
        {
            throw new TemplateRenderException(templateName, $"Template file not found: {filePath}");
        }

        var content = await ReadFileAsync(filePath, ct).ConfigureAwait(false);

        if (_enableCaching)
        {
            _cache.TryAdd(templateName, content);
        }

        return content;
    }

    /// <summary>
    /// Invalidates a cached template, forcing it to be reloaded from disk on next access.
    /// </summary>
    public void InvalidateCache(string templateName)
    {
        _cache.TryRemove(templateName, out _);
    }

    /// <summary>
    /// Clears all cached templates.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    private string ResolveFilePath(string templateName)
    {
        var fileName = templateName.EndsWith(_fileExtension, StringComparison.OrdinalIgnoreCase)
            ? templateName
            : templateName + _fileExtension;

        // Normalize path separators
        fileName = fileName.Replace('/', Path.DirectorySeparatorChar)
                           .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, fileName));

        // Prevent directory traversal
        var normalizedBase = Path.GetFullPath(_basePath);
        if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new TemplateRenderException(templateName, "Template path escapes the base directory.");
        }

        return fullPath;
    }

    private async Task<string> ReadFileAsync(string filePath, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var reader = new StreamReader(stream, _encoding);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }
}
