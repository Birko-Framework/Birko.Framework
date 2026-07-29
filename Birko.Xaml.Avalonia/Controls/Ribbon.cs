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
        PreferredGroupSizeProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
    }

    public IEnumerable<RibbonTab>? Tabs { get => GetValue(TabsProperty); set => SetValue(TabsProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    /// <summary>When true, only the tab strip shows (the "tabs-only" / minimized ribbon). The chevron
    /// at the strip's end toggles it; clicking the active tab also toggles.</summary>
    public bool IsCollapsed { get => GetValue(IsCollapsedProperty); set => SetValue(IsCollapsedProperty, value); }

    public static readonly StyledProperty<RibbonGroupSize> PreferredGroupSizeProperty =
        AvaloniaProperty.Register<Ribbon, RibbonGroupSize>(nameof(PreferredGroupSize), RibbonGroupSize.Medium);

    /// <summary>
    /// The roomiest variant groups may take — the ribbon's look at full width. Groups degrade from here as
    /// the window narrows (STORY-049).
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="RibbonGroupSize.Medium"/>: it matches what both skins rendered before the
    /// scaling pass existed, so an existing app's ribbon does not change height on upgrade.
    /// <see cref="RibbonGroupSize.Large"/> is the Office-like look and is opt-in.
    /// </remarks>
    public RibbonGroupSize PreferredGroupSize
    {
        get => GetValue(PreferredGroupSizeProperty);
        set => SetValue(PreferredGroupSizeProperty, value);
    }

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
        var tabScroller = WrapScrollable(tabButtons, "Scroll tabs left", "Scroll tabs right", _tabRow, out var tabScrollViewer);
        CarryTabScrollAcrossRebuild(tabScrollViewer, tabButtons, selected);

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
            // Progressive scaling (TASK-099): each group is built at every variant and the panel picks
            // per group, so narrowing degrades Medium -> Small rather than clipping.
            var groupsPanel = new RibbonGroupsPanel { Margin = new Thickness(8), Preferred = PreferredGroupSize };
            groupsPanel.Bind(RibbonGroupsPanel.GapProperty, groupsPanel.GetResourceObservable("BRibbonGroupGap"));
            if (selected >= 0)
                foreach (var group in tabs[selected].Groups)
                    groupsPanel.AddGroup(new RibbonGroupsPanel.GroupVariants
                    {
                        Controls = new Dictionary<RibbonGroupSize, Control>
                        {
                            [RibbonGroupSize.Large] = BuildGroup(group, RibbonGroupSize.Large),
                            [RibbonGroupSize.Medium] = BuildGroup(group, RibbonGroupSize.Medium),
                            [RibbonGroupSize.Small] = BuildGroup(group, RibbonGroupSize.Small),
                            [RibbonGroupSize.Popup] = BuildGroup(group, RibbonGroupSize.Popup),
                        },
                        ScalingPriority = group.ScalingPriority,
                        MinSize = group.MinSize,
                    });

            // No scroller here, deliberately: the ribbon BODY resizes, it never scrolls. With the Popup
            // variant in place there is always something narrower for a group to become, so nothing can be
            // unreachable — and the panel now measures against the real constraint instead of a
            // ScrollViewer's infinite width. Keeping a scroller also made the scaling history-dependent,
            // because the chevrons' hysteresis fed back into the viewport the pass scales against.
            body.Children.Add(groupsPanel);
            _groupsPanel = groupsPanel;
        }

        if (IsCollapsed) _groupsPanel = null; // no groups row exists, so report no variants

        var chrome = new Border { Child = body, BorderThickness = new Thickness(0, 0, 0, 1) };
        chrome.Bind(Border.BackgroundProperty, chrome.GetResourceObservable("BBgBrush"));
        chrome.Bind(Border.BorderBrushProperty, chrome.GetResourceObservable("BBorderBrush"));
        Content = chrome;
    }

    /// <summary>
    /// The variant each group of the active tab currently renders at, left to right — the outcome of the
    /// progressive-scaling pass. Empty while collapsed or before the first layout.
    /// </summary>
    /// <remarks>
    /// Public because it is genuinely useful to a consumer (a shell might surface "compact ribbon", or key
    /// a tutorial overlay off whether labels are showing) and because it lets the behaviour be asserted
    /// without a test-only back door into the panel.
    /// </remarks>
    public IReadOnlyList<RibbonGroupSize> ResolvedGroupSizes =>
        _groupsPanel?.Chosen ?? (IReadOnlyList<RibbonGroupSize>)System.Array.Empty<RibbonGroupSize>();

    private RibbonGroupsPanel? _groupsPanel;

    /// <summary>Tab-strip scroll offset, carried across <see cref="Rebuild"/> (which discards the tree).</summary>
    private double _tabOffsetX;

    /// <summary>
    /// Whether a row overflows, carried across <see cref="Rebuild"/> for the same reason as the offset.
    /// Re-deriving it per rebuild un-reserved the chevron slots for a frame — so the row reflowed on
    /// every rebuild (the very flicker the reservation exists to prevent), and the restored scroll offset
    /// was clamped against a viewport 40px too wide.
    /// </summary>
    private sealed class RowScrollState { public bool Overflowing; }

    private readonly RowScrollState _tabRow = new();

    private int _lastSelected = -1;

    /// <summary>
    /// Keep the tab strip where the user left it across a <see cref="Rebuild"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Rebuild"/> throws the whole visual tree away, so every rebuild produced a brand-new
    /// <see cref="ScrollViewer"/> sitting at offset 0. Selecting a tab you had scrolled to therefore
    /// snapped the strip back to the first tab, leaving the tab you had just clicked off-screen. (The web
    /// <c>b-ribbon</c> never had this: it morphs the DOM, so the track element and its <c>scrollLeft</c>
    /// survive.)
    /// <para>
    /// Restoring the offset is not enough on its own — a selection made from off-screen (keyboard, or
    /// code setting <see cref="SelectedIndex"/>) must still scroll into view. So the remembered offset is
    /// restored first, and the active tab is brought into view only when the selection actually changed:
    /// after a click it is already visible, so that call is a no-op and the strip does not jump. On a
    /// rebuild with the same selection (a collapse toggle, say) nothing moves at all.
    /// </para>
    /// </remarks>
    private void CarryTabScrollAcrossRebuild(ScrollViewer scroller, Panel tabButtons, int selected)
    {
        bool selectionChanged = selected != _lastSelected;
        _lastSelected = selected;

        // Captured now: the pre-restore layout pass raises ScrollChanged at offset 0, which would
        // otherwise overwrite the very value being restored.
        double desired = _tabOffsetX;

        // Retried across layout passes, not done once: Sync() re-reserves the chevron slots on the first
        // pass, but that only narrows the viewport on a LATER pass — so a single attempt clamps `desired`
        // against a viewport ~40px too wide and lands short of where the user actually was. Bounded, so a
        // genuinely unreachable offset (the tab set shrank) cannot keep the handler alive.
        int attempts = 0;
        EventHandler? restore = null;
        restore = (_, _) =>
        {
            double max = System.Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
            double target = System.Math.Clamp(desired, 0, max);
            if (System.Math.Abs(scroller.Offset.X - target) > 0.5)
                scroller.Offset = new Vector(target, 0);

            bool reached = target >= desired - 0.5;
            if (!reached && ++attempts < 4) return;

            scroller.LayoutUpdated -= restore;

            if (selectionChanged && selected >= 0 && selected < tabButtons.Children.Count)
                tabButtons.Children[selected].BringIntoView();

            // Only start remembering once the restore has settled, for the same reason.
            scroller.ScrollChanged += (_, _) => _tabOffsetX = scroller.Offset.X;
        };
        scroller.LayoutUpdated += restore;
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
    private Control WrapScrollable(Control content, string leftTip, string rightTip, RowScrollState state, out ScrollViewer scrollViewer)
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
            double over = scroller.Extent.Width - scroller.Viewport.Width;

            // Hysteresis on "does this row overflow at all", with a dead zone the width of the two
            // reserved slots. Revealing them shrinks the viewport, which would otherwise let the slots'
            // own width decide whether they are needed — a bistable boundary that can oscillate while
            // the user drags the window edge. The slots are given back only once the content fits with
            // more room to spare than they occupy.
            double reserved = state.Overflowing ? left.Bounds.Width + right.Bounds.Width : 0;
            if (reserved <= 0) reserved = 40;
            state.Overflowing = state.Overflowing ? over > -reserved : over > 1;

            // Once overflowing, BOTH slots stay in the layout and only their opacity / hit-testing
            // changes — so scrolling to either end never reflows the row and never moves the click
            // target. Collapsing a chevron's box let the adjacent content slide into its slot, which is
            // how the web side ended up swallowing clicks on an unpinned ribbon.
            if (left.IsVisible != state.Overflowing) left.IsVisible = state.Overflowing;
            if (right.IsVisible != state.Overflowing) right.IsVisible = state.Overflowing;
            SetActive(left, state.Overflowing && scroller.Offset.X > 1);
            SetActive(right, state.Overflowing && scroller.Offset.X + scroller.Viewport.Width < scroller.Extent.Width - 1);
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
        scrollViewer = scroller;
        return host;
    }

    /// <summary>
    /// A reserved-but-inactive chevron keeps its box so the row never reflows; only its opacity and
    /// hit-testing change. <see cref="Visual.IsVisible"/> would collapse the box.
    /// </summary>
    private static void SetActive(Button chevron, bool active)
    {
        chevron.Opacity = active ? 1 : 0;
        chevron.IsHitTestVisible = active;
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

    /// <summary>
    /// One group at one variant. <c>Large</c> lays items out in a single row of icon-above-label buttons;
    /// <c>Medium</c> and <c>Small</c> stack three per column and flow the columns horizontally, as Office
    /// does — which is what makes them narrower rather than merely smaller.
    /// </summary>
    private Control BuildGroup(RibbonGroup group, RibbonGroupSize size, System.Action? onInvoke = null)
    {
        if (size == RibbonGroupSize.Popup) return BuildChunk(group);

        Control items = size == RibbonGroupSize.Large
            ? Row(group.Items, size, onInvoke)
            : Columns(group.Items, size, perColumn: 3, onInvoke);

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

    private Control Row(IReadOnlyList<RibbonItem> items, RibbonGroupSize size, System.Action? onInvoke)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var item in items) row.Children.Add(BuildItem(item, size, onInvoke));
        return row;
    }

    /// <summary>Items in columns of <paramref name="perColumn"/>, columns flowing left to right.</summary>
    private Control Columns(IReadOnlyList<RibbonItem> items, RibbonGroupSize size, int perColumn, System.Action? onInvoke)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        StackPanel? column = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (i % perColumn == 0)
            {
                column = new StackPanel { Spacing = 1 };
                host.Children.Add(column);
            }
            column!.Children.Add(BuildItem(items[i], size, onInvoke));
        }
        return host;
    }

    private Control BuildItem(RibbonItem item, RibbonGroupSize size, System.Action? onInvoke = null)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 4),
        };
        button.Bind(ForegroundProperty, button.GetResourceObservable("BTextBrush"));
        button.Click += (_, _) => { item.Run?.Invoke(); onInvoke?.Invoke(); };

        var icon = new TextBlock { Text = item.Icon ?? "•" };
        icon.Bind(TextBlock.FontSizeProperty, button.GetResourceObservable(
            size == RibbonGroupSize.Large ? "BRibbonIconLarge" : "BRibbonIconSmall"));

        if (size == RibbonGroupSize.Small)
        {
            // Icon only. The label is not drawn, so the tooltip has to carry the name — an icon-only
            // command with no tooltip is unidentifiable, which would trade one accessibility problem
            // (unreachable) for another (unnameable).
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            button.Content = icon;
            ToolTip.SetTip(button, item.Label);
            return button;
        }

        var label = new TextBlock { Text = item.Label, FontSize = 11 };

        if (size == RibbonGroupSize.Large)
        {
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextWrapping = TextWrapping.Wrap;
            label.TextAlignment = TextAlignment.Center;
            var stacked = new StackPanel { Spacing = 2, MinWidth = 52 };
            stacked.Children.Add(icon);
            stacked.Children.Add(label);
            button.Content = stacked;
            return button;
        }

        // Medium — icon then label on one line.
        var inline = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        inline.Children.Add(icon);
        inline.Children.Add(label);
        button.Content = inline;
        return button;
    }

    /// <summary>
    /// The <see cref="RibbonGroupSize.Popup"/> variant: the whole group folded into one button whose flyout
    /// holds its items at <see cref="RibbonGroupSize.Large"/>.
    /// </summary>
    /// <remarks>
    /// Lossless, and that is the point — the group keeps its identity and its position in the row, which is
    /// what separates this from a flat overflow menu that dumps every leftover command into one list. It is
    /// also what lets the ribbon body stop scrolling entirely: there is always something narrower to become.
    /// </remarks>
    private Control BuildChunk(RibbonGroup group)
    {
        var icon = new TextBlock { Text = group.Icon ?? "▦", HorizontalAlignment = HorizontalAlignment.Center };
        var label = new TextBlock
        {
            Text = group.Label + " ⌄",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(icon);
        content.Children.Add(label);

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 4),
        };
        button.Bind(ForegroundProperty, button.GetResourceObservable("BTextBrush"));
        button.Bind(TextBlock.FontSizeProperty, button.GetResourceObservable("BRibbonIconSmall"));
        button.Bind(MinWidthProperty, button.GetResourceObservable("BRibbonChunkWidth"));
        ToolTip.SetTip(button, group.Label);

        // Declared before the content so the items can dismiss the flyout they were invoked from — Office
        // closes a collapsed group's flyout as soon as a command runs.
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Content = BuildGroup(group, RibbonGroupSize.Large, onInvoke: () => flyout.Hide());
        button.Flyout = flyout;

        var box = new Border { Padding = new Thickness(8, 4), BorderThickness = new Thickness(0, 0, 1, 0), Child = button };
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable("BBorderBrush"));
        return box;
    }
}
