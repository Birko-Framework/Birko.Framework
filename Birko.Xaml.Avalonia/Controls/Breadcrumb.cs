using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Breadcrumb trail (the XAML port of <c>b-breadcrumb</c>): renders <see cref="ItemsSource"/> as
/// token-styled crumbs separated by <see cref="Separator"/>, with the last crumb emphasized (the
/// current location). Token-driven; built in code from the items.
/// </summary>
public class Breadcrumb : ContentControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<Breadcrumb, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> SeparatorProperty =
        AvaloniaProperty.Register<Breadcrumb, string>(nameof(Separator), "/");

    static Breadcrumb()
    {
        ItemsSourceProperty.Changed.AddClassHandler<Breadcrumb>((b, _) => b.Rebuild());
        SeparatorProperty.Changed.AddClassHandler<Breadcrumb>((b, _) => b.Rebuild());
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string Separator
    {
        get => GetValue(SeparatorProperty);
        set => SetValue(SeparatorProperty, value);
    }

    private void Rebuild()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (ItemsSource is not null)
        {
            var items = ItemsSource.Cast<object?>().ToList();
            for (int i = 0; i < items.Count; i++)
            {
                bool last = i == items.Count - 1;
                if (i > 0)
                {
                    var sep = new TextBlock { Text = Separator, VerticalAlignment = VerticalAlignment.Center };
                    Themed(sep, TextBlock.ForegroundProperty, "BTextMutedBrush");
                    panel.Children.Add(sep);
                }
                var crumb = new TextBlock
                {
                    Text = items[i]?.ToString(),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = last ? FontWeight.SemiBold : FontWeight.Normal,
                };
                Themed(crumb, TextBlock.ForegroundProperty, last ? "BTextBrush" : "BTextSecondaryBrush");
                panel.Children.Add(crumb);
            }
        }
        Content = panel;
    }

    private static void Themed(Control control, AvaloniaProperty property, string tokenKey) =>
        control.Bind(property, control.GetResourceObservable(tokenKey));
}
