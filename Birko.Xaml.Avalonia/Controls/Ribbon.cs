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

        // The tab strip scrolls when the tabs overflow (Office Web / Fluent do this; the ribbon *body*
        // deliberately does not — see TASK-099). The collapse chevron lives outside the scroller so it
        // stays pinned at the right edge and can never scroll out of reach.
        var tabScroller = WrapScrollable(tabButtons, "Scroll tabs left", "Scroll tabs right");

        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 4, 8, 0) };
        Grid.SetColumn(tabScroller, 0);
        Grid.SetColumn(chevron, 1);
        strip.Children.Add(tabScroller);
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
            // INTERIM (TASK-097): groups scroll so no command is unreachable at a narrow width.
            // TASK-099 replaces this with progressive group scaling and removes the scroller — a
            // scrolling ribbon body destroys the spatial memory the ribbon exists to provide.
            body.Children.Add(WrapScrollable(groupsPanel, "Scroll groups left", "Scroll groups right"));
        }

        var chrome = new Border { Child = body, BorderThickness = new Thickness(0, 0, 0, 1) };
        chrome.Bind(Border.BackgroundProperty, chrome.GetResourceObservable("BBgBrush"));
        chrome.Bind(Border.BorderBrushProperty, chrome.GetResourceObservable("BBorderBrush"));
        Content = chrome;
    }

    /// <summary>
    /// Wraps a horizontally-overflowing row in a hidden-scrollbar <see cref="ScrollViewer"/> flanked by
    /// chevron buttons that show only while there is more content in that direction — the same affordance
    /// <c>b-ribbon</c> uses on the web.
    /// </summary>
    /// <remarks>
    /// The scrollbar is <c>Hidden</c> rather than <c>Auto</c> on purpose: a visible horizontal bar would
    /// add its own height to a 34px tab strip and to the ribbon body, so the ribbon's height would change
    /// with the window width. Chevrons live in <c>Auto</c> columns and are collapsed while invisible, so
    /// they cost no layout at a wide width.
    /// </remarks>
    private Control WrapScrollable(Control content, string leftTip, string rightTip)
    {
        var scroller = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var left = ScrollChevron("◂", leftTip);
        var right = ScrollChevron("▸", rightTip);

        void Step(double direction)
        {
            // Half a viewport per click, matching b-ribbon's `clientWidth * 0.5`.
            double by = System.Math.Max(48, scroller.Viewport.Width * 0.5);
            double max = System.Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
            scroller.Offset = new Vector(System.Math.Clamp(scroller.Offset.X + (direction * by), 0, max), scroller.Offset.Y);
        }

        left.Click += (_, _) => Step(-1);
        right.Click += (_, _) => Step(1);

        void Sync()
        {
            // Extent and Viewport both change when the window resizes, so reacting to them covers a
            // narrowing window with no rebuild — the gap that made overflow silently unreachable.
            bool canLeft = scroller.Offset.X > 1;
            bool canRight = scroller.Offset.X + scroller.Viewport.Width < scroller.Extent.Width - 1;
            if (left.IsVisible != canLeft) left.IsVisible = canLeft;
            if (right.IsVisible != canRight) right.IsVisible = canRight;
        }

        scroller.ScrollChanged += (_, _) => Sync();
        scroller.LayoutUpdated += (_, _) => Sync();

        var host = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(scroller, 1);
        Grid.SetColumn(right, 2);
        host.Children.Add(left);
        host.Children.Add(scroller);
        host.Children.Add(right);
        return host;
    }

    private Button ScrollChevron(string glyph, string tip)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontSize = 11 },
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            IsVisible = false, // Sync() reveals it once layout proves the row overflows.
            [ToolTip.TipProperty] = tip,
        };
        button.Bind(ForegroundProperty, button.GetResourceObservable("BTextSecondaryBrush"));
        return button;
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
