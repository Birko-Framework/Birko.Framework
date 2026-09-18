using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Master–detail layout (the XAML port of <c>b-split-panel</c>): a resizable master column
/// (<see cref="GridSplitter"/>) beside a detail area, with responsive collapse — below
/// <see cref="CollapseWidth"/> the master hides so the detail takes the full width. This is the
/// layout <c>BaseSplitPage</c> composes.
/// </summary>
[PseudoClasses(":collapsed")]
public class SplitPanel : TemplatedControl
{
    public static readonly StyledProperty<object?> MasterProperty =
        AvaloniaProperty.Register<SplitPanel, object?>(nameof(Master));

    public static readonly StyledProperty<object?> DetailProperty =
        AvaloniaProperty.Register<SplitPanel, object?>(nameof(Detail));

    public static readonly StyledProperty<double> MasterWidthProperty =
        AvaloniaProperty.Register<SplitPanel, double>(nameof(MasterWidth), 280d);

    public static readonly StyledProperty<double> CollapseWidthProperty =
        AvaloniaProperty.Register<SplitPanel, double>(nameof(CollapseWidth), 640d);

    public static readonly DirectProperty<SplitPanel, bool> IsCollapsedProperty =
        AvaloniaProperty.RegisterDirect<SplitPanel, bool>(nameof(IsCollapsed), o => o.IsCollapsed);

    private bool _isCollapsed;
    private Grid? _grid;

    public object? Master { get => GetValue(MasterProperty); set => SetValue(MasterProperty, value); }
    public object? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public double MasterWidth { get => GetValue(MasterWidthProperty); set => SetValue(MasterWidthProperty, value); }
    public double CollapseWidth { get => GetValue(CollapseWidthProperty); set => SetValue(CollapseWidthProperty, value); }

    public bool IsCollapsed => _isCollapsed;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _grid = e.NameScope.Find<Grid>("PART_Grid");
        ApplyMasterWidth();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MasterWidthProperty)
            ApplyMasterWidth();
        else if (change.Property == BoundsProperty || change.Property == CollapseWidthProperty)
            UpdateCollapsed();
    }

    // Responsive collapse: hide the master column when the control is narrower than CollapseWidth.
    private void UpdateCollapsed()
    {
        bool collapsed = Bounds.Width > 0 && Bounds.Width < CollapseWidth;
        if (collapsed == _isCollapsed) return;
        SetAndRaise(IsCollapsedProperty, ref _isCollapsed, collapsed);
        PseudoClasses.Set(":collapsed", collapsed);
    }

    // Pixel master column beside a star detail column → the GridSplitter can actually drag it.
    private void ApplyMasterWidth()
    {
        if (_grid is { ColumnDefinitions.Count: >= 1 })
            _grid.ColumnDefinitions[0].Width = new GridLength(MasterWidth, GridUnitType.Pixel);
    }
}
