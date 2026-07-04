using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Birko.Xaml.Shell.Views;

public partial class ShellView : UserControl
{
    public ShellView() => AvaloniaXamlLoader.Load(this);
}
