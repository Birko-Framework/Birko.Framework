using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Birko.Xaml.Core.Ribbon;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Ribbon toolbar (the XAML port of <c>b-ribbon</c> / the <c>BAppShell</c> chrome): a tab strip whose
/// active tab shows labeled groups of icon+label command buttons. Model-driven via <see cref="Tabs"/>;
/// clicking an item runs its <see cref="RibbonItem.Run"/>. Token-styled and rebuilt on tab change.
/// </summary>
public class Ribbon : ContentControl
{
    public static readonly StyledProperty<IEnumerable<RibbonTab>?> TabsProperty =
        AvaloniaProperty.Register<Ribbon, IEnumerable<RibbonTab>?>(nameof(Tabs));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<Ribbon, int>(nameof(SelectedIndex));

    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<Ribbon, bool>(nameof(IsCollapsed));

    static Ribbon()
    {
        TabsProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        SelectedIndexProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        IsCollapsedProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
    }

    public IEnumerable<RibbonTab>? Tabs { get => GetValue(TabsProperty); set => SetValue(TabsProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    /// <summary>When true, only the tab strip shows (the "tabs-only" / minimized ribbon). The chevron
    /// at the strip's end toggles it; clicking the active tab also toggles.</summary>
    public bool IsCollapsed { get => GetValue(IsCollapsedProperty); set => SetValue(IsCollapsedProperty, value); }

    private void Rebuild()
    {
        var tabs = Tabs?.ToList() ?? new List<RibbonTab>();
        int selected = tabs.Count == 0 ? -1 : System.Math.Clamp(SelectedIndex, 0, tabs.Count - 1);

        // Tab strip (tabs on the left, a collapse chevron on the right)
        var tabButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var tabButton = new Button
            {
                Content = new TextBlock { Text = tabs[i].Label },
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 6),
            };
            tabButton.Bind(ForegroundProperty, tabButton.GetResourceObservable(
                i == selected ? "BColorPrimaryBrush" : "BTextSecondaryBrush"));
            // Selecting a tab shows it; clicking the already-active tab toggles collapse (Office-style).
            tabButton.Click += (_, _) =>
            {
                if (index == SelectedIndex) IsCollapsed = !IsCollapsed;
                else { SelectedIndex = index; IsCollapsed = false; }
            };
            tabButtons.Children.Add(tabButton);
        }

        var chevron = new Button
        {
            Content = new TextBlock { Text = IsCollapsed ? "⌄" : "⌃" },
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            [ToolTip.TipProperty] = IsCollapsed ? "Expand the ribbon" : "Collapse the ribbon",
        };
        chevron.Bind(ForegroundProperty, chevron.GetResourceObservable("BTextSecondaryBrush"));
        chevron.Click += (_, _) => IsCollapsed = !IsCollapsed;

        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 4, 8, 0) };
        Grid.SetColumn(tabButtons, 0);
        Grid.SetColumn(chevron, 1);
        strip.Children.Add(tabButtons);
        strip.Children.Add(chevron);

        var body = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        body.Children.Add(strip);

        // Active tab groups — hidden when collapsed (tabs-only mode)
        if (!IsCollapsed)
        {
            var groupsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
            if (selected >= 0)
                foreach (var group in tabs[selected].Groups)
                    groupsPanel.Children.Add(BuildGroup(group));
            body.Children.Add(groupsPanel);
        }

        var chrome = new Border { Child = body, BorderThickness = new Thickness(0, 0, 0, 1) };
        chrome.Bind(Border.BackgroundProperty, chrome.GetResourceObservable("BBgBrush"));
        chrome.Bind(Border.BorderBrushProperty, chrome.GetResourceObservable("BBorderBrush"));
        Content = chrome;
    }

    private Control BuildGroup(RibbonGroup group)
    {
        var items = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var item in group.Items) items.Children.Add(BuildItem(item));

        var label = new TextBlock
        {
            Text = group.Label,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("BTextMutedBrush"));

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(items);
        stack.Children.Add(label);

        var box = new Border
        {
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = stack,
        };
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable("BBorderBrush"));
        return box;
    }

    private Control BuildItem(RibbonItem item)
    {
        var content = new StackPanel { Spacing = 2, MinWidth = 52 };
        var icon = new TextBlock
        {
            Text = item.Icon ?? "•",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        content.Children.Add(icon);
        content.Children.Add(label);

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 4),
        };
        button.Bind(ForegroundProperty, button.GetResourceObservable("BTextBrush"));
        button.Click += (_, _) => item.Run?.Invoke();
        return button;
    }
}
