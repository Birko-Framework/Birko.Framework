using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    public static readonly StyledProperty<bool> IsPinnedProperty =
        AvaloniaProperty.Register<Ribbon, bool>(nameof(IsPinned), true);

    /// <summary>
    /// Whether the body stays in the layout (<c>true</c>, the default) or is revealed **temporarily as an
    /// overlay** when a tab is clicked (<c>false</c>) — Office's "Show Tabs" mode, which `b-ribbon` has had
    /// as its <c>pinned</c> attribute all along.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c> because that is exactly what this control did before TASK-101: an existing
    /// app is unaffected. Unpinned, the body never pushes page content down, and it re-collapses as soon as
    /// a command runs or focus goes elsewhere.
    /// </remarks>
    public bool IsPinned { get => GetValue(IsPinnedProperty); set => SetValue(IsPinnedProperty, value); }

    public static readonly StyledProperty<double> NarrowThresholdProperty =
        AvaloniaProperty.Register<Ribbon, double>(nameof(NarrowThreshold), 240);

    /// <summary>
    /// Below this width the ribbon stops being a ribbon and becomes a menu: a ☰ button plus the active
    /// tab's name, whose overlay lists every tab, group and item (TASK-102).
    /// </summary>
    /// <remarks>
    /// Measured against the <b>ribbon's own</b> width, not the window's, so a ribbon in a narrow pane
    /// behaves like one in a narrow window. Scaling (TASK-099) handles everything above it; this is only
    /// what happens once scaling has nothing left to give.
    /// <para>
    /// <b>Deliberately 240, not the web's 768.</b> <c>b-ribbon</c>'s 48rem is a *touch-layout* breakpoint —
    /// below it you are on a phone and a ribbon is the wrong interaction model regardless of whether it
    /// would fit. A desktop control has no phone, so copying that number would replace a perfectly usable
    /// 700px ribbon with a menu; the measured floor for a six-group tab is 166px. This threshold answers
    /// "can this still work as a ribbon", which is a different question. Set it to 768 for literal web
    /// parity if a consumer wants it.
    /// </para>
    /// </remarks>
    public double NarrowThreshold { get => GetValue(NarrowThresholdProperty); set => SetValue(NarrowThresholdProperty, value); }

    static Ribbon()
    {
        TabsProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        SelectedIndexProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        IsCollapsedProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        PreferredGroupSizeProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        IsPinnedProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());

        // Narrow/wide is a rebuild, so it is driven off the size change rather than polled — and only when
        // the state actually flips, otherwise every pixel of a drag would rebuild the whole chrome.
        BoundsProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.OnWidthChanged());
    }

    private void OnWidthChanged()
    {
        bool narrow = Bounds.Width > 0 && Bounds.Width < NarrowThreshold;
        if (narrow == _isNarrow) return;
        _isNarrow = narrow;
        Rebuild();
    }

    private bool _isNarrow;

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

        // Below the threshold the ribbon stops being a ribbon (TASK-102). Scaling has nothing left to give
        // by then, so continuing to draw a tab strip and a groups row would only clip them.
        if (_isNarrow && tabs.Count > 0)
        {
            Content = BuildNarrowChrome(tabs, selected);
            return;
        }

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
            // Clicking the already-active tab toggles collapse (Office-style). Otherwise: pinned selects
            // and expands for good; UNPINNED selects and reveals only temporarily, leaving IsCollapsed
            // alone — "Show Tabs" is a mode you leave by pinning, not by clicking a tab (TASK-101).
            tabButton.Click += (_, _) =>
            {
                if (index == SelectedIndex && IsPinned) { IsCollapsed = !IsCollapsed; return; }

                SelectedIndex = index;
                if (IsPinned) IsCollapsed = false;
                else RevealTemporarily();
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

        var pin = new Button
        {
            Content = new TextBlock { Text = IsPinned ? "\U0001F4CC" : "\U0001F4CD" },
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 6),
            [ToolTip.TipProperty] = IsPinned ? "Unpin the ribbon" : "Pin the ribbon open",
        };
        pin.Bind(ForegroundProperty, pin.GetResourceObservable("BTextSecondaryBrush"));
        pin.Click += (_, _) => IsPinned = !IsPinned;

        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(8, 4, 8, 0) };
        Grid.SetColumn(tabScroller, 0);
        Grid.SetColumn(chevron, 1);
        Grid.SetColumn(pin, 2);
        strip.Children.Add(tabScroller);
        strip.Children.Add(chevron);
        strip.Children.Add(pin);

        var body = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        body.Children.Add(strip);

        // Active tab groups — in flow only when PINNED. Unpinned, the body must not push page content
        // down, so it is built into the reveal popup instead (RevealTemporarily).
        _revealTabs = tabs;
        _revealSelected = selected;
        if (!IsPinned) _reveal?.Close();
        if (!IsCollapsed && IsPinned && selected >= 0)
        {
            var groupsPanel = BuildGroupsRow(tabs[selected], onInvoke: null);
            body.Children.Add(groupsPanel);
            _groupsPanel = groupsPanel;
        }

        if (IsCollapsed || !IsPinned) _groupsPanel = null; // no in-flow groups row, so report no variants

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

    /// <summary>
    /// The active tab's groups, with progressive scaling applied (TASK-099): each group is built at every
    /// variant and <see cref="RibbonGroupsPanel"/> picks per group, so narrowing degrades
    /// <c>Medium → Small → Popup</c> rather than clipping.
    /// </summary>
    /// <param name="onInvoke">Ran after a command, so a temporary reveal can close itself.</param>
    /// <remarks>
    /// No scroller, deliberately: the ribbon body resizes, it never scrolls. With the Popup variant there is
    /// always something narrower for a group to become, so nothing is unreachable — and the panel measures
    /// against the real constraint instead of a ScrollViewer's infinite width.
    /// </remarks>
    private RibbonGroupsPanel BuildGroupsRow(RibbonTab tab, System.Action? onInvoke)
    {
        var panel = new RibbonGroupsPanel { Margin = new Thickness(8), Preferred = PreferredGroupSize };
        panel.Bind(RibbonGroupsPanel.GapProperty, panel.GetResourceObservable("BRibbonGroupGap"));

        foreach (var group in tab.Groups)
            panel.AddGroup(new RibbonGroupsPanel.GroupVariants
            {
                Controls = new Dictionary<RibbonGroupSize, Control>
                {
                    [RibbonGroupSize.Large] = BuildGroup(group, RibbonGroupSize.Large, onInvoke),
                    [RibbonGroupSize.Medium] = BuildGroup(group, RibbonGroupSize.Medium, onInvoke),
                    [RibbonGroupSize.Small] = BuildGroup(group, RibbonGroupSize.Small, onInvoke),
                    [RibbonGroupSize.Popup] = BuildGroup(group, RibbonGroupSize.Popup, onInvoke),
                },
                CompactPopup = BuildChunk(group, labelled: false),
                ScalingPriority = group.ScalingPriority,
                MinSize = group.MinSize,
            });

        return panel;
    }

    /// <summary>
    /// The narrow fallback (TASK-102): ☰ plus the active tab's name, whose overlay lists every tab, group
    /// and item — mirroring <c>b-ribbon</c>'s sub-48rem hamburger dialog.
    /// </summary>
    /// <remarks>
    /// Below <see cref="NarrowThreshold"/> a ribbon is the wrong shape entirely: scaling has nothing left to
    /// give, so drawing a tab strip and a groups row would only clip them. Becoming a menu keeps every
    /// command reachable at any width, which is the guarantee the whole story rests on.
    /// </remarks>
    private Control BuildNarrowChrome(List<RibbonTab> tabs, int selected)
    {
        var burger = new Button
        {
            Content = new TextBlock { Text = "☰", FontSize = 18 },
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 6),
            [ToolTip.TipProperty] = "Open the ribbon menu",
        };
        burger.Bind(ForegroundProperty, burger.GetResourceObservable("BTextBrush"));

        var active = new TextBlock
        {
            Text = selected >= 0 ? tabs[selected].Label : string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        active.Bind(TextBlock.ForegroundProperty, active.GetResourceObservable("BTextBrush"));

        burger.Click += (_, _) => OpenNarrowMenu(tabs, selected, burger);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
        row.Children.Add(burger);
        row.Children.Add(active);

        var chrome = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) };
        chrome.Bind(Border.BackgroundProperty, chrome.GetResourceObservable("BBgBrush"));
        chrome.Bind(Border.BorderBrushProperty, chrome.GetResourceObservable("BBorderBrush"));
        return chrome;
    }

    /// <summary>Every tab → group → item as a flat scrollable menu, the active tab first.</summary>
    private void OpenNarrowMenu(List<RibbonTab> tabs, int selected, Control anchor)
    {
        _reveal?.Close();

        var list = new StackPanel { Margin = new Thickness(8), Spacing = 2 };
        // Active tab first: it is what the ☰ label says you are looking at, so it should not need scrolling to.
        foreach (var tab in tabs.OrderByDescending(t => selected >= 0 && t == tabs[selected]))
        {
            var heading = new TextBlock { Text = tab.Label, FontSize = 12, Margin = new Thickness(0, 6, 0, 2) };
            heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("BColorPrimaryBrush"));
            list.Children.Add(heading);

            foreach (var group in tab.Groups)
            {
                var groupLabel = new TextBlock { Text = group.Label, FontSize = 11, Margin = new Thickness(8, 2, 0, 0) };
                groupLabel.Bind(TextBlock.ForegroundProperty, groupLabel.GetResourceObservable("BTextMutedBrush"));
                list.Children.Add(groupLabel);

                foreach (var item in group.Items)
                {
                    var entry = new Button
                    {
                        Content = new TextBlock { Text = (item.Icon is null ? "" : item.Icon + "  ") + item.Label },
                        Background = Brushes.Transparent,
                        Padding = new Thickness(16, 6),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                    };
                    entry.Bind(ForegroundProperty, entry.GetResourceObservable("BTextBrush"));
                    var captured = item;
                    entry.Click += (_, _) => { captured.Run?.Invoke(); _reveal?.Close(); };
                    list.Children.Add(entry);
                }
            }
        }

        var scroller = new ScrollViewer
        {
            Content = list,
            MaxHeight = 420,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var surface = new Border { Child = scroller, BorderThickness = new Thickness(1), MinWidth = 220 };
        surface.Bind(Border.BackgroundProperty, surface.GetResourceObservable("BBgElevatedBrush"));
        surface.Bind(Border.BorderBrushProperty, surface.GetResourceObservable("BBorderBrush"));

        _reveal = new Popup
        {
            Child = surface,
            PlacementTarget = anchor,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        };
        ((ISetLogicalParent)_reveal).SetParent(this);
        // Light dismiss covers click-away only. Escape is handled at the top level (OnTopLevelKeyDown),
        // because a Popup is not a FlyoutBase and does not bring Escape with it.
        _reveal.Closed += (_, _) => anchor.Focus();
        _reveal.Open();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Ribbon shortcuts are WINDOW shortcuts, so they are handled at the top level rather than in
        // OnKeyDown. A ContentControl is not focusable, so the ribbon never has keyboard focus and an
        // OnKeyDown override was simply unreachable — Ctrl+F1 did nothing in the gallery, and Escape never
        // closed the narrow menu. (Both were "covered" by tests that raised the event straight at the
        // control, which proved the handler ran and nothing about whether a keystroke could reach it.)
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(
            global::Avalonia.Input.InputElement.KeyDownEvent, OnTopLevelKeyDown,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel | global::Avalonia.Interactivity.RoutingStrategies.Bubble);

        // Click-away is handled here too, for the same reason as Escape: Popup.IsLightDismissEnabled did
        // not actually dismiss on a press elsewhere in the app. Between this and Escape, light dismiss has
        // now failed to deliver twice — so the overlay's dismissal is owned outright rather than assumed.
        _topLevel?.AddHandler(
            global::Avalonia.Input.InputElement.PointerPressedEvent, OnTopLevelPointerPressed,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel?.RemoveHandler(global::Avalonia.Input.InputElement.KeyDownEvent, OnTopLevelKeyDown);
        _topLevel?.RemoveHandler(global::Avalonia.Input.InputElement.PointerPressedEvent, OnTopLevelPointerPressed);
        _topLevel = null;
        _reveal?.Close();
    }

    private TopLevel? _topLevel;

    /// <summary>
    /// Dismiss an open overlay when the press lands outside it — and outside the ribbon, whose own click
    /// handlers decide what a press on a tab or on ☰ means.
    /// </summary>
    private void OnTopLevelPointerPressed(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
    {
        if (_reveal?.IsOpen != true) return;
        if (e.Source is not Visual source) return;

        // Inside the overlay: a press on a command is handled by the command; a press on empty space in it
        // should not dismiss. (With a native popup host these events never reach here anyway — defensive.)
        if (_reveal.Child is Visual content
            && (ReferenceEquals(source, content) || source.GetVisualAncestors().Contains(content)))
            return;

        // On the ribbon itself: the tab button or ☰ decides, otherwise a re-open would fight this close.
        if (ReferenceEquals(source, this) || source.GetVisualAncestors().Contains(this)) return;

        _reveal.Close();
    }

    private void OnTopLevelKeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        // Escape closes an open overlay first: while the narrow menu or a temporary reveal is showing, that
        // is what Escape means. A raw Popup does NOT do this for you — IsLightDismissEnabled is
        // pointer-only; Escape handling lives in FlyoutBase, which a Popup is not.
        if (e.Key == global::Avalonia.Input.Key.Escape && _reveal?.IsOpen == true)
        {
            var anchor = _reveal.PlacementTarget;
            _reveal.Close();
            anchor?.Focus();
            e.Handled = true;
            return;
        }

        // Ctrl+F1 is the shortcut users actually try, and Office has had it for two decades.
        if (e.Key == global::Avalonia.Input.Key.F1
            && e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control))
        {
            IsCollapsed = !IsCollapsed;
            e.Handled = true;
        }
    }

    private Popup? _reveal;

    /// <summary>
    /// The overlay currently shown over the page — the narrow menu, or an unpinned ribbon's temporary
    /// reveal — or <c>null</c> when nothing is open.
    /// </summary>
    /// <remarks>
    /// Public for the same reason as <see cref="ResolvedGroupSizes"/>: a shell has real cause to reach it
    /// (dismiss ribbon overlays when navigating, or when opening a dialog), and it lets the behaviour be
    /// asserted without a test-only back door. A <c>Popup</c> hosts its child in a separate visual root, so
    /// there is otherwise no route to it from the ribbon.
    /// </remarks>
    public Control? OpenOverlay => _reveal?.IsOpen == true ? _reveal.Child as Control : null;

    /// <summary>Dismiss the narrow menu or temporary reveal, if one is open.</summary>
    public void CloseOverlay() => _reveal?.Close();
    private List<RibbonTab> _revealTabs = new();
    private int _revealSelected = -1;

    /// <summary>
    /// Show the active tab's groups **over** the page, without changing <see cref="IsCollapsed"/> — Office's
    /// temporary reveal for an unpinned ribbon. Light-dismiss closes it, and so does invoking a command.
    /// </summary>
    /// <remarks>
    /// A <see cref="Popup"/> rather than a re-parented body: it overlays instead of participating in layout
    /// (the whole point), and it brings light dismiss and Escape with it rather than needing hit-testing and
    /// key handling written by hand.
    /// </remarks>
    private void RevealTemporarily()
    {
        if (_revealSelected < 0 || _revealSelected >= _revealTabs.Count) return;

        _reveal?.Close();
        var groups = BuildGroupsRow(_revealTabs[_revealSelected], () => _reveal?.Close());

        var surface = new Border
        {
            Child = groups,
            BorderThickness = new Thickness(1),
            // Full ribbon width, matching b-ribbon's `left: 0; right: 0` and Office: a temporary reveal
            // should read as the ribbon body appearing, not as a dropdown sized to its contents. MinWidth
            // rather than Width so unusually wide content can still grow past it.
            MinWidth = Bounds.Width,
        };
        surface.Bind(Border.BackgroundProperty, surface.GetResourceObservable("BBgElevatedBrush"));
        surface.Bind(Border.BorderBrushProperty, surface.GetResourceObservable("BBorderBrush"));

        _reveal = new Popup
        {
            Child = surface,
            PlacementTarget = this,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        };
        ((ISetLogicalParent)_reveal).SetParent(this);
        _reveal.Open();
    }

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
    private Control BuildGroup(
        RibbonGroup group, RibbonGroupSize size, System.Action? onInvoke = null, bool wrapItems = false)
    {
        if (size == RibbonGroupSize.Popup) return BuildChunk(group);

        Control items = size == RibbonGroupSize.Large
            ? Row(group.Items, size, onInvoke, wrapItems)
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
            // BSpaceXs horizontally, none vertically — b-ribbon's .ribbon-group is
            // `padding: 0 var(--b-space-xs)`. The old 8/4 gave every group extra inner padding the web
            // side does not have, which is what made the two look differently spaced inside a group.
            Padding = new Thickness(4, 0),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = stack,
        };
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable("BBorderBrush"));
        return box;
    }

    /// <summary>
    /// Items in one horizontal run. <paramref name="wrap"/> lets it reflow onto further lines instead —
    /// used inside a collapsed group's flyout, which has to fit a window narrow enough to have collapsed the
    /// group in the first place. A single run of Large items is easily wider than the ribbon at that point,
    /// and a flyout anchored near the right edge then has nowhere to go and gets cut off.
    /// </summary>
    private Control Row(
        IReadOnlyList<RibbonItem> items, RibbonGroupSize size, System.Action? onInvoke, bool wrap = false)
    {
        if (wrap)
        {
            var wrapped = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var item in items) wrapped.Children.Add(BuildItem(item, size, onInvoke));
            return wrapped;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Bind(StackPanel.SpacingProperty, row.GetResourceObservable("BRibbonItemGap"));
        foreach (var item in items) row.Children.Add(BuildItem(item, size, onInvoke));
        return row;
    }

    /// <summary>Items in columns of <paramref name="perColumn"/>, columns flowing left to right.</summary>
    private Control Columns(IReadOnlyList<RibbonItem> items, RibbonGroupSize size, int perColumn, System.Action? onInvoke)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        host.Bind(StackPanel.SpacingProperty, host.GetResourceObservable("BRibbonItemGap"));
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
            // BSpaceSm / BSpaceXs — the same pair b-ribbon's .ribbon-item uses
            // (padding: var(--b-space-xs) var(--b-space-sm)). Was 6/4, which read as visibly tighter
            // than the web side when the two were compared directly.
            Padding = new Thickness(8, 4),
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
    /// <summary>Border + padding a <c>FlyoutPresenter</c> adds around its content, measured from the theme.</summary>
    private const double FlyoutChrome = 26;

    private Control BuildChunk(RibbonGroup group, bool labelled = true)
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
        // The extreme: even a row of labelled chunk buttons can be too wide, because a chunk shows
        // its group NAME and a name has a minimum width. Dropping it takes the six-group minimum from
        // ~500px to ~250px. The tooltip still carries the name, so the command is never anonymous —
        // the same trade the Small variant already makes for items.
        if (labelled) content.Children.Add(label);

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            // A compact chunk is already at the row's last resort, so it sheds every spare pixel: the
            // wrapper padding and the group separator below go too. Each one is multiplied by the group
            // count, so trimming ~20px per chunk moves the six-group floor by well over 100px.
            Padding = labelled ? new Thickness(6, 4) : new Thickness(2, 4),
        };
        button.Bind(ForegroundProperty, button.GetResourceObservable("BTextBrush"));
        button.Bind(TextBlock.FontSizeProperty, button.GetResourceObservable("BRibbonIconSmall"));
        if (labelled) button.Bind(MinWidthProperty, button.GetResourceObservable("BRibbonChunkWidth"));
        ToolTip.SetTip(button, group.Label);

        // Declared before the content so the items can dismiss the flyout they were invoked from — Office
        // closes a collapsed group's flyout as soon as a command runs.
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Content = BuildGroup(group, RibbonGroupSize.Large, onInvoke: () => flyout.Hide());
        button.Flyout = flyout;

        var box = new Border
        {
            Padding = labelled ? new Thickness(8, 4) : new Thickness(1, 4),
            BorderThickness = labelled ? new Thickness(0, 0, 1, 0) : default,
            Child = button,
        };
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable("BBorderBrush"));
        return box;
    }
}
