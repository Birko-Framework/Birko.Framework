using System;
using System.Text;

namespace Birko.Messaging.Razor;

public class RazorTemplateOptions
{
    /// <summary>
    /// Base directory path for .cshtml template files.
    /// When null, only inline string templates and embedded resources are supported.
    /// </summary>
    public string? TemplateBasePath { get; set; }

    /// <summary>
    /// Default file extension for template files. Defaults to ".cshtml".
    /// </summary>
    public string FileExtension { get; set; } = ".cshtml";

    /// <summary>
    /// Whether to cache compiled templates for reuse. Defaults to true.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Encoding used when reading template files. Defaults to UTF-8.
    /// </summary>
    public Encoding FileEncoding { get; set; } = Encoding.UTF8;

    /// <summary>
    /// Default namespaces to include in all templates (e.g., "System.Linq").
    /// </summary>
    public string[] DefaultNamespaces { get; set; } = Array.Empty<string>();
}
