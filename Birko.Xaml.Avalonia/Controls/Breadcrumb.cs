using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Birko.Xaml.Core.Navigation;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Breadcrumb trail (the XAML port of <c>b-breadcrumb</c>): renders <see cref="ItemsSource"/> as
/// token-styled crumbs separated by <see cref="Separator"/>, with the last crumb emphasized (the
/// current location). Token-driven; built in code from the items.
/// <para>
/// Items may be plain values (rendered as static text, via <c>ToString()</c>) or
/// <see cref="BreadcrumbItem"/>s. A non-last <see cref="BreadcrumbItem"/> that carries a
/// <see cref="BreadcrumbItem.Run"/> action or an <see cref="BreadcrumbItem.Href"/> is rendered as a
/// clickable link (styled via the <c>BBreadcrumbLink</c> ControlTheme). Clicking it invokes
/// <see cref="BreadcrumbItem.Run"/> and raises <see cref="ItemInvoked"/> — parity with the web
/// component's clickable crumbs. The last crumb is the current location and is never clickable.
/// </para>
/// </summary>
public class Breadcrumb : ContentControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<Breadcrumb, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> SeparatorProperty =
        AvaloniaProperty.Register<Breadcrumb, string>(nameof(Separator), "/");

    /// <summary>Raised when a non-last crumb backed by a <see cref="BreadcrumbItem"/> is clicked
    /// (after its <see cref="BreadcrumbItem.Run"/> runs). A shell can route on the item's
    /// <see cref="BreadcrumbItem.Href"/> here.</summary>
    public event EventHandler<BreadcrumbItem>? ItemInvoked;

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
                panel.Children.Add(BuildCrumb(items[i], last));
            }
        }
        Content = panel;
    }

    private Control BuildCrumb(object? item, bool last)
    {
        var model = item as BreadcrumbItem;
        string? label = model?.Label ?? item?.ToString();

        // Non-last crumbs with a navigation target become clickable links; the last crumb is the
        // current location and is always rendered as emphasized, static text.
        if (!last && model is not null && (model.Run is not null || model.Href is not null))
            return BuildLink(model, label);

        var crumb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = last ? FontWeight.SemiBold : FontWeight.Normal,
        };
        Themed(crumb, TextBlock.ForegroundProperty, last ? "BTextBrush" : "BTextSecondaryBrush");
        return crumb;
    }

    private Control BuildLink(BreadcrumbItem model, string? label)
    {
        var link = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current is { } app &&
            app.TryGetResource("BBreadcrumbLink", null, out var theme) &&
            theme is ControlTheme linkTheme)
        {
            link.Theme = linkTheme;
        }
        link.Click += (_, _) =>
        {
            model.Run?.Invoke();
            ItemInvoked?.Invoke(this, model);
        };
        return link;
    }

    private static void Themed(Control control, AvaloniaProperty property, string tokenKey) =>
        control.Bind(property, control.GetResourceObservable(tokenKey));
}
