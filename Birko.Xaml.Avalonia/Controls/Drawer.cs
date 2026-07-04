using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Birko.Xaml.Avalonia.Controls;

public enum DrawerPlacement
{
    Left,
    Right,
}

/// <summary>
/// Slide-in overlay panel (the XAML port of <c>b-drawer</c>): a token-styled panel pinned to the
/// left/right edge over a dimming backdrop, shown while <see cref="IsOpen"/>. Place it in a Grid/Panel
/// that spans the region it should overlay. Clicking the backdrop closes it.
/// </summary>
[PseudoClasses(":open", ":left", ":right")]
public class Drawer : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<Drawer, bool>(nameof(IsOpen));

    public static readonly StyledProperty<DrawerPlacement> PlacementProperty =
        AvaloniaProperty.Register<Drawer, DrawerPlacement>(nameof(Placement), DrawerPlacement.Right);

    static Drawer()
    {
        IsOpenProperty.Changed.AddClassHandler<Drawer>((d, _) => d.UpdatePseudoClasses());
        PlacementProperty.Changed.AddClassHandler<Drawer>((d, _) => d.UpdatePseudoClasses());
    }

    public Drawer() => UpdatePseudoClasses();

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public DrawerPlacement Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Control>("PART_Backdrop") is { } backdrop)
            backdrop.PointerPressed += (_, _) => IsOpen = false;
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":open", IsOpen);
        PseudoClasses.Set(":left", Placement == DrawerPlacement.Left);
        PseudoClasses.Set(":right", Placement == DrawerPlacement.Right);
    }
}
