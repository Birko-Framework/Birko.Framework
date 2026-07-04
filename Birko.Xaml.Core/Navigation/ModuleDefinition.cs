namespace Birko.Xaml.Core.Navigation;

/// <summary>
/// One navigable module/route (the XAML analogue of Birko.Web's <c>ModuleManifest</c> +
/// <c>buildModuleRoutes</c>). Platform-neutral: the shell nav lists these, and activating one calls
/// <see cref="CreateViewModel"/> to build its page ViewModel. Views are resolved from the VM by the
/// Avalonia <c>ViewLocator</c>.
/// </summary>
public sealed class ModuleDefinition
{
    /// <summary>Stable route id.</summary>
    public required string Id { get; init; }

    /// <summary>Sidebar/ribbon label (may be an i18n key resolved by the shell).</summary>
    public required string Label { get; init; }

    /// <summary>Optional glyph for the nav entry.</summary>
    public string? Icon { get; init; }

    /// <summary>Factory for this module's page ViewModel (built fresh on navigation).</summary>
    public required Func<object> CreateViewModel { get; init; }

    /// <summary>Optional permission gating visibility/activation (the host decides enforcement).</summary>
    public string? Permission { get; init; }
}
