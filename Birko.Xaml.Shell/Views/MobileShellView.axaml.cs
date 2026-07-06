using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Markup.Xaml;

namespace Birko.Xaml.Shell.Views;

/// <summary>
/// Mobile app-shell chrome (BMobileAppShell equivalent): fixed top bar + scrolling content + fixed
/// bottom nav, bound to a <c>ShellViewModel</c>. Reads the platform safe-area insets and pads the top
/// bar / bottom nav so it clears the status-bar/notch and the home indicator on mobile; on desktop the
/// insets manager is absent, so it's a no-op.
/// </summary>
public partial class MobileShellView : UserControl
{
    private IInsetsManager? _insets;

    public MobileShellView() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _insets = TopLevel.GetTopLevel(this)?.InsetsManager;
        if (_insets is null) return; // desktop / headless: no insets → no-op
        _insets.SafeAreaChanged += OnSafeAreaChanged;
        ApplySafeArea(_insets.SafeAreaPadding);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_insets is not null)
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
        _insets = null;
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplySafeArea(e.SafeAreaPadding);

    private void ApplySafeArea(Thickness inset)
    {
        if (this.FindControl<Border>("TopBar") is { } top)
            top.Padding = new Thickness(top.Padding.Left, inset.Top, top.Padding.Right, top.Padding.Bottom);
        if (this.FindControl<Border>("BottomNav") is { } bottom)
            bottom.Padding = new Thickness(bottom.Padding.Left, bottom.Padding.Top, bottom.Padding.Right, inset.Bottom);
    }
}
