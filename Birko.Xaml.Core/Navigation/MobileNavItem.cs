using CommunityToolkit.Mvvm.ComponentModel;

namespace Birko.Xaml.Core.Navigation;

/// <summary>
/// A bottom-nav entry for the mobile app-shell — the XAML analogue of Birko.Web's <c>Surface</c>.
/// Projected from a <see cref="ModuleDefinition"/> (no new nav-model type is invented); it adds only
/// the observable <see cref="IsActive"/> the bottom-nav binds its highlight to. Avalonia-free.
/// </summary>
public partial class MobileNavItem : ObservableObject
{
    public MobileNavItem(string id, string label, string? icon = null)
    {
        Id = id;
        Label = label;
        Icon = icon;
    }

    /// <summary>Stable route id (matches <see cref="ModuleDefinition.Id"/>).</summary>
    public string Id { get; }

    /// <summary>Bottom-nav label.</summary>
    public string Label { get; }

    /// <summary>Optional glyph shown above the label.</summary>
    public string? Icon { get; }

    /// <summary>True when this is the active surface — drives the nav item's highlight.</summary>
    [ObservableProperty]
    private bool _isActive;
}
