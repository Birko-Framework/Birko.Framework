using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Birko.Xaml.Shell.Views;

/// <summary>Ribbon-chrome app shell (the <c>BAppShell</c> analogue): a <c>Ribbon</c> of command tabs
/// over a transitioning content region, in place of <see cref="ShellView"/>'s sidebar. Binds to the
/// same <c>ShellViewModel</c> (uses its <c>RibbonTabs</c>, nav, palette).</summary>
public partial class RibbonShellView : UserControl
{
    public RibbonShellView() => AvaloniaXamlLoader.Load(this);
}
