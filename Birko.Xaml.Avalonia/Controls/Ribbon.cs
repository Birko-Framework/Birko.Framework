using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Core.Ribbon;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Ribbon toolbar (the XAML port of <c>b-ribbon</c> / the <c>BAppShell</c> chrome): a tab strip whose
/// active tab shows labeled groups of icon+label command buttons. Model-driven via <see cref="Tabs"/>;
/// clicking an item runs its <see cref="RibbonItem.Run"/>. Token-styled and rebuilt on tab change.
/// </summary>
public partial class Ribbon : ContentControl
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

    public static readonly StyledProperty<KeyGesture?> CollapseShortcutProperty =
        AvaloniaProperty.Register<Ribbon, KeyGesture?>(nameof(CollapseShortcut), new KeyGesture(Key.F1, KeyModifiers.Control));

    /// <summary>
    /// The app-wide shortcut that collapses the ribbon and brings it back (TASK-757, as b-ribbon's
    /// <c>collapse-shortcut</c>): Ctrl+F1 by default, as in Office; null turns it off.
    /// </summary>
    public KeyGesture? CollapseShortcut { get => GetValue(CollapseShortcutProperty); set => SetValue(CollapseShortcutProperty, value); }

    /// <summary>
    /// Collapses the ribbon to its tabs, or brings it back pinned open — what Ctrl+F1 and a double-click on a tab do
    /// (TASK-757, as b-ribbon's <c>toggleRibbon</c>). Expanded and pinned, it collapses; collapsed or unpinned, it pins
    /// the ribbon open.
    /// </summary>
    public void ToggleRibbon() => ApplyToggle(expanded: IsPinned && !IsCollapsed);

    private void ApplyToggle(bool expanded)
    {
        if (expanded) { IsCollapsed = true; return; }
        IsPinned = true;
        IsCollapsed = false;
    }

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

    public static readonly StyledProperty<object?> LeadingContentProperty =
        AvaloniaProperty.Register<Ribbon, object?>(nameof(LeadingContent));

    public static readonly StyledProperty<object?> TrailingContentProperty =
        AvaloniaProperty.Register<Ribbon, object?>(nameof(TrailingContent));

    /// <summary>
    /// Content at the start of the tab strip, before the first tab — typically the app's name. With
    /// <see cref="TrailingContent"/> it lets an app fold its title bar into the ribbon's tab row, as Office does,
    /// instead of stacking a separate header above it.
    /// </summary>
    public object? LeadingContent { get => GetValue(LeadingContentProperty); set => SetValue(LeadingContentProperty, value); }

    /// <summary>
    /// Content at the end of the tab strip, between the tabs and the collapse/pin buttons — typically app-level
    /// commands (search, settings, account). Also shown on the narrow ☰ chrome, so it never disappears.
    /// </summary>
    public object? TrailingContent { get => GetValue(TrailingContentProperty); set => SetValue(TrailingContentProperty, value); }

    // The chrome is rebuilt on every tab/size change, but a control can have only one parent, so the slot content
    // lives in hosts that persist across rebuilds and are only re-parented. Content changes never rebuild.
    private readonly ContentControl _leadingHost = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _trailingHost = new() { VerticalAlignment = VerticalAlignment.Center };

    private static ContentControl Reparent(ContentControl host)
    {
        (host.Parent as Panel)?.Children.Remove(host);
        return host;
    }

    static Ribbon()
    {
        LeadingContentProperty.Changed.AddClassHandler<Ribbon>((r, e) => r._leadingHost.Content = e.NewValue);
        TrailingContentProperty.Changed.AddClassHandler<Ribbon>((r, e) => r._trailingHost.Content = e.NewValue);
        // New tabs: their Pressed and gallery Selected values are the state again (TASK-754, TASK-761), as b-ribbon's setTabs.
        TabsProperty.Changed.AddClassHandler<Ribbon>((r, _) =>
        {
            r._pressed.Clear();
            r._gallerySel.Clear();
            r._galleryOffset.Clear();
            r._fieldDraft.Clear();
            r._fieldCommitted.Clear();
            r.Rebuild();
        });
        SelectedIndexProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        IsCollapsedProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        PreferredGroupSizeProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        IsPinnedProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        QuickAccessPositionProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        ContextualStyleProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        GalleryStyleProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());
        FieldCaptionProperty.Changed.AddClassHandler<Ribbon>((r, _) => r.Rebuild());

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
        // Captured before the tree is discarded — see the focus restore below.
        bool focusWasInside = ContainsFocus();
        _toggleViews.Clear(); // every command is rebuilt, its views with it
        _galleryViews.Clear();
        _fieldViews.Clear();

        // The SHOWN tabs: a contextual set's only while the app shows it (TASK-760). SelectedIndex counts these.
        var tabs = VisibleTabs();
        int selected = tabs.Count == 0 ? -1 : System.Math.Clamp(SelectedIndex, 0, tabs.Count - 1);
        if (selected >= 0 && tabs[selected].ContextualSet is null) _lastNormalTab = tabs[selected].Id;

        // Below the threshold the ribbon stops being a ribbon (TASK-102). Scaling has nothing left to give
        // by then, so continuing to draw a tab strip and a groups row would only clip them.
        if (_isNarrow && tabs.Count > 0)
        {
            _tabList = new List<Button>();
            Content = BuildNarrowChrome(tabs, selected);
            return;
        }

        // Tab strip (tabs on the left, a collapse chevron on the right)
        var tabButtons = Sized(new StackPanel { Orientation = Orientation.Horizontal }, StackPanel.SpacingProperty, "BRibbonTabSpacing");
        var buttons = new List<Button>();
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var tabButton = Sized(new Button
            {
                Content = new TextBlock { Text = tabs[i].Label },
                Background = Brushes.Transparent,
            }, PaddingProperty, "BRibbonTabPadding");
            tabButton.Bind(ForegroundProperty, tabButton.GetResourceObservable(
                i == selected ? "BColorPrimaryBrush" : "BTextSecondaryBrush"));
            Describe(tabButton, tabs[i].Label);
            // Clicking the already-active tab toggles collapse (Office-style). Otherwise: pinned selects
            // and expands for good; UNPINNED selects and reveals only temporarily, leaving IsCollapsed
            // alone — "Show Tabs" is a mode you leave by pinning, not by clicking a tab (TASK-101).
            tabButton.Click += (_, _) =>
            {
                // The second click of a double-click: the double-click has already toggled the ribbon (TASK-757).
                if (_swallowTabClick) { _swallowTabClick = false; return; }
                if (index == SelectedIndex && IsPinned) { IsCollapsed = !IsCollapsed; return; }

                SelectedIndex = index;
                if (IsPinned) IsCollapsed = false;
                else RevealTemporarily();
            };
            AddFocusRing(tabButton);
            buttons.Add(tabButton);
        }
        ArrangeTabs(tabButtons, tabs, buttons, selected);

        var chevron = Sized(new Button
        {
            Content = new TextBlock { Text = IsCollapsed ? "⌄" : "⌃" },
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            [ToolTip.TipProperty] = IsCollapsed ? "Expand the ribbon" : "Collapse the ribbon",
        }, PaddingProperty, "BRibbonControlPadding");
        chevron.Bind(ForegroundProperty, chevron.GetResourceObservable("BTextSecondaryBrush"));
        Describe(chevron, IsCollapsed ? "Expand the ribbon" : "Collapse the ribbon");
        chevron.Click += (_, _) => IsCollapsed = !IsCollapsed;
        AddFocusRing(chevron);

        // The tab strip scrolls when the tabs overflow (Office Web / Fluent do this; the ribbon *body*
        // deliberately does not — see TASK-099). The collapse chevron lives outside the scroller so it
        // stays pinned at the right edge and can never scroll out of reach.
        // Tab-strip keys, matching b-ribbon: Left/Right move ALONG the strip and select as they go (a tab
        // strip activates automatically — deliberately NOT the generic focus-only helper used for commands),
        // Home/End jump to the ends, and Down drops out of the strip into the commands, which is what makes
        // the groups row reachable without tabbing through the chrome in between.
        tabButtons.KeyDown += (_, args) =>
        {
            int count = buttons.Count;
            if (count == 0) return;

            if (args.Key is Key.Left or Key.Right)
            {
                // Selecting rebuilds the strip, which destroys these buttons — the focus restore in Rebuild()
                // is what puts focus on the newly selected tab, so arrowing does not drop focus.
                int current = System.Math.Clamp(SelectedIndex, 0, count - 1);
                SelectedIndex = args.Key == Key.Right
                    ? (current + 1) % count
                    : (current - 1 + count) % count;
                args.Handled = true;
                return;
            }

            if (args.Key is Key.Home or Key.End)
            {
                SelectedIndex = args.Key == Key.Home ? 0 : count - 1;
                args.Handled = true;
                return;
            }

            if (args.Key != Key.Down) return;
            // Into the panel at its one Tab stop: the command focused there last, else the first (TASK-752).
            if (_groupsPanel is { } panel && RovingStop(panel) is { } stop)
            {
                stop.Focus(NavigationMethod.Directional);
                args.Handled = true;
            }
        };

        var tabScroller = WrapScrollable(tabButtons, "Scroll tabs left", "Scroll tabs right", _tabRow, out var tabScrollViewer);
        CarryTabScrollAcrossRebuild(tabScrollViewer, buttons, selected);

        var pin = Sized(new Button
        {
            Content = new TextBlock { Text = IsPinned ? "\U0001F4CC" : "\U0001F4CD" },
            Background = Brushes.Transparent,
            [ToolTip.TipProperty] = IsPinned ? "Unpin the ribbon" : "Pin the ribbon open",
        }, PaddingProperty, "BRibbonPinPadding");
        pin.Bind(ForegroundProperty, pin.GetResourceObservable("BTextSecondaryBrush"));
        Describe(pin, IsPinned ? "Unpin the ribbon" : "Pin the ribbon open");
        pin.Click += (_, _) => IsPinned = !IsPinned;
        AddFocusRing(pin);

        var strip = Sized(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto") }, MarginProperty, "BRibbonStripMargin");
        var leading = Reparent(_leadingHost);
        var trailing = Reparent(_trailingHost);
        Grid.SetColumn(leading, 0);
        Grid.SetColumn(tabScroller, 2);
        Grid.SetColumn(trailing, 3);
        Grid.SetColumn(chevron, 4);
        Grid.SetColumn(pin, 5);
        strip.Children.Add(leading);
        strip.Children.Add(tabScroller);
        strip.Children.Add(trailing);
        strip.Children.Add(chevron);
        strip.Children.Add(pin);

        // The Quick Access Toolbar (TASK-759): in the tab row before the tabs, or on a row of its own.
        var qat = BuildQuickAccess();
        if (qat is not null && QuickAccessPosition == RibbonQuickAccessPosition.Tabs)
        {
            Sized(qat, MarginProperty, "BRibbonQatTabsMargin");
            Grid.SetColumn(qat, 1);
            strip.Children.Add(qat);
        }

        var body = new DockPanel();
        if (qat is not null && QuickAccessPosition == RibbonQuickAccessPosition.Above)
        {
            Sized(qat, MarginProperty, "BRibbonQatRowMargin");
            qat.HorizontalAlignment = HorizontalAlignment.Left;
            DockPanel.SetDock(qat, Dock.Top);
            body.Children.Add(qat);
        }
        DockPanel.SetDock(strip, Dock.Top);
        body.Children.Add(strip);
        if (qat is not null && QuickAccessPosition == RibbonQuickAccessPosition.Below)
        {
            Sized(qat, MarginProperty, "BRibbonQatRowMargin");
            qat.HorizontalAlignment = HorizontalAlignment.Left;
            DockPanel.SetDock(qat, Dock.Bottom);
            body.Children.Add(qat);
        }

        // Active tab groups — in flow only when PINNED. Unpinned, the body must not push page content
        // down, so it is built into the reveal popup instead (RevealTemporarily).
        _revealTabs = tabs;
        _revealSelected = selected;
        if (!IsPinned) _reveal?.Close();
        if (!IsCollapsed && IsPinned && selected >= 0)
        {
            var groupsPanel = BuildGroupsRow(tabs[selected], onInvoke: null);
            WirePanelKeyboard(groupsPanel);
            if (groupsPanel is RibbonGroupsPanel scaling)
            {
                // Posted, not called inline: this fires from inside a MEASURE pass, and hiding a popup
                // synchronously there re-enters layout.
                scaling.GroupPromoted += () => Dispatcher.UIThread.Post(CloseChunkFlyoutOnPromotion);
                scaling.FocusDisplaced += key => Dispatcher.UIThread.Post(() => RefocusAfterRescale(scaling, key), DispatcherPriority.Loaded);
            }
            body.Children.Add(groupsPanel);
            _groupsPanel = groupsPanel;
        }

        if (IsCollapsed || !IsPinned) _groupsPanel = null; // no in-flow groups row, so report no variants

        // Rebuild() discards the whole tree, so the focused control is destroyed with it and focus falls back
        // to the window root. Activating a tab BY KEYBOARD therefore threw focus out of the ribbon entirely:
        // the next Tab restarted from the top of the window, and the groups the tab had just opened were
        // unreachable. Put focus back on the tab that was activated so Tab continues from there, exactly as it
        // does after a mouse click. Posted at Loaded priority — the new tree is not laid out yet.
        if (focusWasInside && selected >= 0 && selected < buttons.Count)
        {
            var restore = buttons[selected];
            Dispatcher.UIThread.Post(() => restore.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }

        var chrome = SizedBy(new Border { Child = body }, Border.BorderThicknessProperty, s => new Thickness(0, 0, 0, s.D("BBorderWidth")), "BBorderWidth");
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
        _buildTab = tab.Id;
        var panel = Sized(new RibbonGroupsPanel { Preferred = PreferredGroupSize }, MarginProperty, "BRibbonPanelMargin");
        panel.Bind(RibbonGroupsPanel.GapProperty, panel.GetResourceObservable("BRibbonGroupGap"));
        panel.Bind(RibbonGroupsPanel.DividerBrushProperty, panel.GetResourceObservable("BBorderBrush"));

        for (int g = 0; g < tab.Groups.Count; g++)
        {
            var group = tab.Groups[g];
            string key = g.ToString(System.Globalization.CultureInfo.InvariantCulture);
            panel.AddGroup(new RibbonGroupsPanel.GroupVariants
            {
                Controls = new Dictionary<RibbonGroupSize, Control>
                {
                    [RibbonGroupSize.Large] = BuildGroup(group, RibbonGroupSize.Large, onInvoke, groupKey: key),
                    [RibbonGroupSize.Medium] = BuildGroup(group, RibbonGroupSize.Medium, onInvoke, groupKey: key),
                    [RibbonGroupSize.Small] = BuildGroup(group, RibbonGroupSize.Small, onInvoke, groupKey: key),
                    [RibbonGroupSize.Popup] = BuildGroup(group, RibbonGroupSize.Popup, onInvoke, groupKey: key),
                },
                CompactPopup = BuildChunk(group, labelled: false, groupKey: key),
                ScalingPriority = group.ScalingPriority,
                MinSize = group.MinSize,
            });
        }

        WireQuickAccessContext(panel, tab.Id);
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
            Content = Sized(new TextBlock { Text = "☰" }, TextBlock.FontSizeProperty, "BRibbonBurgerGlyph"),
            Background = Brushes.Transparent,
            [ToolTip.TipProperty] = "Open the ribbon menu",
        };
        Sized(burger, PaddingProperty, "BRibbonControlPadding");
        burger.Bind(ForegroundProperty, burger.GetResourceObservable("BTextBrush"));
        Describe(burger, "Open the ribbon menu");

        var active = new TextBlock
        {
            Text = selected >= 0 ? tabs[selected].Label : string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Sized(active, MarginProperty, "BRibbonNarrowLabelMargin");
        active.Bind(TextBlock.ForegroundProperty, active.GetResourceObservable("BTextBrush"));

        burger.Click += (_, _) => OpenNarrowMenu(tabs, selected, burger);

        var row = Sized(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") }, MarginProperty, "BRibbonNarrowBarMargin");
        var leading = Reparent(_leadingHost);
        var trailing = Reparent(_trailingHost);
        Grid.SetColumn(leading, 0);
        Grid.SetColumn(burger, 1);
        Grid.SetColumn(active, 2);
        Grid.SetColumn(trailing, 3);
        row.Children.Add(leading);
        row.Children.Add(burger);
        row.Children.Add(active);
        row.Children.Add(trailing);

        var chrome = SizedBy(new Border { Child = row }, Border.BorderThicknessProperty, s => new Thickness(0, 0, 0, s.D("BBorderWidth")), "BBorderWidth");
        chrome.Bind(Border.BackgroundProperty, chrome.GetResourceObservable("BBgBrush"));
        chrome.Bind(Border.BorderBrushProperty, chrome.GetResourceObservable("BBorderBrush"));
        return chrome;
    }

    /// <summary>Every tab → group → item as a flat scrollable menu, the active tab first.</summary>
    /// <summary>A plain narrow-menu entry: an indented button that runs <paramref name="run"/> and closes the menu.</summary>
    /// <summary>An entry of the narrow menu, indented <paramref name="level"/> steps (a choice under its command is 2).</summary>
    private Button NarrowEntry(string? icon, string label, int level, System.Action run)
    {
        var entry = new Button
        {
            Content = new TextBlock { Text = (icon is null ? "" : icon + "  ") + label },
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        entry.Bind(ForegroundProperty, entry.GetResourceObservable("BTextBrush"));
        NarrowPadding(entry, level);
        Describe(entry, label);
        entry.Click += (_, _) => { run(); _reveal?.Close(); };
        return entry;
    }

    /// <summary>
    /// A narrow-menu entry's padding: the entry padding with its Left as many times over as the level, less the 1px pressed
    /// frame a toggle draws inside it (<paramref name="framed"/>), so a toggle lines up with its neighbours.
    /// </summary>
    private static void NarrowPadding(Control entry, int level, bool framed = false) =>
        SizedBy(entry, PaddingProperty, s =>
        {
            var p = s.T("BRibbonNarrowEntryPadding");
            double f = framed ? s.D("BBorderWidth") : 0;
            return new Thickness(p.Left * level - f, p.Top - f, p.Right - f, p.Bottom - f);
        }, "BRibbonNarrowEntryPadding", "BBorderWidth");

    /// <summary>A toggle in the narrow menu: the same state and look as in the ribbon (TASK-754).</summary>
    private Control NarrowToggle(RibbonTab tab, RibbonGroup group, RibbonItem item)
    {
        string g = tab.Groups.ToList().IndexOf(group).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string key = PressKey(tab.Id, g, item.Id);
        var entry = new RibbonToggleCommand
        {
            IsRadio = item.ToggleGroup is not null,
            Content = new TextBlock { Text = (item.Icon is null ? "" : item.Icon + "  ") + item.Label },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        NarrowPadding(entry, 1, framed: true);
        Describe(entry, item.Label);
        entry.Click += (_, _) => { ClickToggle(tab.Id, g, item); item.Run?.Invoke(); };
        return FinishToggle(entry, key, item);
    }

    private void OpenNarrowMenu(List<RibbonTab> tabs, int selected, Control anchor)
    {
        _reveal?.Close();

        var list = Sized(Sized(new StackPanel(), MarginProperty, "BRibbonNarrowListMargin"), StackPanel.SpacingProperty, "BRibbonNarrowSpacing");
        // Active tab first: it is what the ☰ label says you are looking at, so it should not need scrolling to.
        foreach (var tab in tabs.OrderByDescending(t => selected >= 0 && t == tabs[selected]))
        {
            var heading = Sized(Sized(new TextBlock { Text = tab.Label }, TextBlock.FontSizeProperty, "BRibbonNarrowHeadingSize"), MarginProperty, "BRibbonNarrowTabMargin");
            heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("BColorPrimaryBrush"));
            list.Children.Add(heading);

            foreach (var group in tab.Groups)
            {
                var groupLabel = Sized(Sized(new TextBlock { Text = group.Label }, TextBlock.FontSizeProperty, "BTextXs"), MarginProperty, "BRibbonNarrowGroupMargin");
                groupLabel.Bind(TextBlock.ForegroundProperty, groupLabel.GetResourceObservable("BTextMutedBrush"));
                list.Children.Add(groupLabel);

                if (group.Launcher)
                {
                    var g = group;
                    var t = tab;
                    list.Children.Add(NarrowEntry(null, LauncherName(g), 1, () => RaiseLauncher(t.Id, g)));
                }

                foreach (var item in group.Items)
                {
                    if (item.Gallery is { Items.Count: > 0 })
                    {
                        AddNarrowGallery(list, tab, tab.Groups.ToList().IndexOf(group).ToString(System.Globalization.CultureInfo.InvariantCulture), item);
                        continue;
                    }
                    if (item.Field is not null)
                    {
                        // An input: its caption beside the box, as the web's mobile list (TASK-762).
                        _buildTab = tab.Id;
                        list.Children.Add(BuildField(item, RibbonGroupSize.Medium, null,
                            tab.Groups.ToList().IndexOf(group).ToString(System.Globalization.CultureInfo.InvariantCulture), narrow: true));
                        continue;
                    }
                    if (IsToggle(item))
                    {
                        list.Children.Add(NarrowToggle(tab, group, item));
                        continue;
                    }
                    if (item.Menu is { Count: > 0 } entries)
                    {
                        // A menu's choices are listed under their command; a menu-only command is just their heading.
                        if (item.Split) list.Children.Add(NarrowEntry(item.Icon, item.Label, 1, () => item.Run?.Invoke()));
                        else
                        {
                            var head = Sized(Sized(new TextBlock { Text = item.Label }, TextBlock.FontSizeProperty, "BRibbonNarrowHeadingSize"), MarginProperty, "BRibbonNarrowItemMargin");
                            head.Bind(TextBlock.ForegroundProperty, head.GetResourceObservable("BTextMutedBrush"));
                            list.Children.Add(head);
                        }
                        foreach (var choice in entries.OfType<RibbonMenuItem>())
                        {
                            var c = choice;
                            var choiceEntry = NarrowEntry(c.Icon, c.Label, 2, () =>
                            {
                                c.Run?.Invoke();
                                MenuItemInvoked?.Invoke(this, new RibbonMenuItemInvokedEventArgs(tab.Id, c.Id, item.Id));
                            });
                            choiceEntry.IsEnabled = !c.Disabled;
                            list.Children.Add(choiceEntry);
                        }
                        continue;
                    }
                    var entry = new Button
                    {
                        Content = new TextBlock { Text = (item.Icon is null ? "" : item.Icon + "  ") + item.Label },
                        Background = Brushes.Transparent,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                    };
                    entry.Bind(ForegroundProperty, entry.GetResourceObservable("BTextBrush"));
                    NarrowPadding(entry, 1);
                    var captured = item;
                    entry.Click += (_, _) => { captured.Run?.Invoke(); _reveal?.Close(); };
                    list.Children.Add(entry);
                }
            }
        }

        var scroller = new ScrollViewer
        {
            Content = list,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        Sized(scroller, MaxHeightProperty, "BRibbonNarrowMaxHeight");
        var surface = SizedBy(Sized(new Border { Child = scroller }, MinWidthProperty, "BRibbonNarrowMinWidth"),
            Border.BorderThicknessProperty, s => new Thickness(s.D("BBorderWidth")), "BBorderWidth");
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
        // Key tips first, so a letter while they show is theirs before any other shortcut sees it (TASK-758).
        if (_topLevel is not null) AttachKeyTips(_topLevel);
        _topLevel?.AddHandler(
            global::Avalonia.Input.InputElement.KeyDownEvent, OnTopLevelKeyDown,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel | global::Avalonia.Interactivity.RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnTabPointerPressed, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

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
        ExitKeyTips();
        if (_topLevel is not null) DetachKeyTips(_topLevel);
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

    private FlyoutBase? _openChunkFlyout;
    private Button? _openChunkButton;

    private void OnTopLevelKeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        // A collapsed group's flyout first: while it is open, that is what Escape means.
        if (e.Key == global::Avalonia.Input.Key.Escape && _openChunkFlyout is { } chunkFlyout)
        {
            var owner = _openChunkButton;
            chunkFlyout.Hide();
            owner?.Focus();
            e.Handled = true;
            return;
        }

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

        // Ctrl+F1 is the shortcut users actually try, and Office has had it for two decades. Rebindable, or off
        // (TASK-757); an app that handled the key first wins.
        if (!e.Handled && CollapseShortcut is { } gesture && gesture.Matches(e))
        {
            ToggleRibbon();
            e.Handled = true;
        }
    }

    private Popup? _reveal;

    // ── Double-click a tab (TASK-757, as b-ribbon's TASK-735) ──

    /// <summary>Whether the ribbon was expanded and pinned when the first click of a possible double-click went down.</summary>
    private bool _expandedAtFirstPress;
    private bool _swallowTabClick;

    private long _lastTabPress = -1;
    private Point _lastTabPressAt;

    /// <summary>
    /// A double-click on a tab collapses the ribbon, or pins it back open, as in Office. Two things make it work with
    /// the single click, which already selects (and, on the active tab, toggles): the toggle is decided from the state
    /// BEFORE the first click, and the click that ends the double-click is swallowed.
    /// </summary>
    /// <remarks>
    /// Timed here, not read from <c>ClickCount</c>: the first click rebuilds the tab strip, so the second press lands
    /// on a NEW button, and Avalonia counts a double-click only on the same element, so <c>ClickCount</c> was always 1
    /// (measured in the headless tests, and so in the app too).
    /// </remarks>
    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual v || !_tabList.Any(t => ReferenceEquals(t, v) || v.GetVisualAncestors().Contains(t))) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var settings = TopLevel.GetTopLevel(this)?.PlatformSettings;
        var time = settings?.GetDoubleTapTime(e.Pointer.Type) ?? System.TimeSpan.FromMilliseconds(500);
        var size = settings?.GetDoubleTapSize(e.Pointer.Type) ?? new Size(4, 4); // size-ok: the platform's double-tap slop when it reports none, not drawn
        var at = e.GetPosition(this);
        // The system clock, not e.Timestamp: the headless platform stamps every event 0.
        long now = System.Environment.TickCount64;
        bool second = _lastTabPress >= 0 && now - _lastTabPress <= (long)time.TotalMilliseconds
            && System.Math.Abs(at.X - _lastTabPressAt.X) <= size.Width && System.Math.Abs(at.Y - _lastTabPressAt.Y) <= size.Height;
        if (!second)
        {
            _lastTabPress = now;
            _lastTabPressAt = at;
            _expandedAtFirstPress = IsPinned && !IsCollapsed;
            return;
        }
        _lastTabPress = -1; // a third press starts a new pair
        _swallowTabClick = true;
        ApplyToggle(_expandedAtFirstPress);
    }

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
        _revealPanel = groups; // key tips reach an unpinned ribbon's commands here (TASK-758)

        groups.KeyDown += (_, args) =>
        {
            if (args.Key != global::Avalonia.Input.Key.Escape) return;
            _reveal?.Close();
            args.Handled = true;
        };

        var surface = new Border
        {
            Child = groups,
            // Full ribbon width, matching b-ribbon's `left: 0; right: 0` and Office: a temporary reveal
            // should read as the ribbon body appearing, not as a dropdown sized to its contents. MinWidth
            // rather than Width so unusually wide content can still grow past it.
            MinWidth = Bounds.Width,
        };
        SizedBy(surface, Border.BorderThicknessProperty, s => new Thickness(s.D("BBorderWidth")), "BBorderWidth");
        surface.Bind(Border.BackgroundProperty, surface.GetResourceObservable("BBgElevatedBrush"));
        surface.Bind(Border.BorderBrushProperty, surface.GetResourceObservable("BBorderBrush"));

        _reveal = new Popup
        {
            Child = surface,
            PlacementTarget = this,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            // The press that dismisses it still reaches what is under it (TASK-757): without this, the second click
            // of a double-click on a tab only closed the reveal, so an unpinned ribbon could never be pinned that way.
            // It also lets a click on another tab switch to it in one press, as Office does.
            OverlayDismissEventPassThrough = true,
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
    private void CarryTabScrollAcrossRebuild(ScrollViewer scroller, IReadOnlyList<Button> tabButtons, int selected)
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

            if (selectionChanged && selected >= 0 && selected < tabButtons.Count)
                tabButtons[selected].BringIntoView();

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
    /// Give a ribbon button a visible focus indicator.
    /// </summary>
    /// <remarks>
    /// The Avalonia skin has no focus visual on <c>Button</c> at all — only <c>Inputs.axaml</c> styles
    /// <c>:focus</c> — so keyboard focus was invisible on every ribbon command. "Keyboard reachable" is
    /// worth nothing if the user cannot see where they are (WCAG 2.4.7), and it is what made a keyboard pass
    /// look like Tab was stuck. The web side has had <c>--b-focus-ring</c> on its tabs and items throughout.
    /// <para>
    /// The border thickness is constant and only the BRUSH changes, so gaining focus cannot reflow the row —
    /// a focus ring that shifts layout would be its own bug, and this row is measured for scaling.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Give <paramref name="control"/> an accessible name, and hide a decorative glyph from assistive tech.
    /// </summary>
    /// <remarks>
    /// Every focusable thing in the ribbon needs this <b>explicitly</b>. Avalonia derives a button's name
    /// from its content only when that content is a string; a ribbon item's content is a panel holding an
    /// icon and a label, so it derived nothing and a screen reader announced the bare control type — the
    /// reviewer heard commands as "ribbon action group button" with no idea which command they were on.
    /// An unnamed command is unusable, not merely under-described.
    /// <para>
    /// The icon is marked <c>Raw</c> so it drops out of the accessibility tree: these glyphs are emoji, and
    /// "clipboard Paste" is noise at best. The name carries the meaning; the glyph is decoration.
    /// </para>
    /// </remarks>
    private static void Describe(Control control, string? name, Control? decorativeGlyph = null)
    {
        if (!string.IsNullOrWhiteSpace(name)) global::Avalonia.Automation.AutomationProperties.SetName(control, name);
        if (decorativeGlyph is not null)
            global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(
                decorativeGlyph, global::Avalonia.Automation.AccessibilityView.Raw);
    }

    /// <summary>
    /// The stops inside <paramref name="scope"/> a keyboard user can actually land on — enabled, visible,
    /// and not parked off-screen by the scaling panel: the buttons, and the input boxes (TASK-762).
    /// </summary>
    private static List<Control> NavigableButtons(Visual scope) =>
        scope.GetVisualDescendants().OfType<Control>()
            .Where(c => c is Button || IsFieldBox(c))
            .Where(b => b.IsEffectivelyEnabled && b.IsVisible && b.Opacity > 0)
            .Where(b => b.TranslatePoint(default, scope) is { X: > -1000 })
            .ToList();

    /// <summary>Hover delay before a command's tooltip shows, as b-ribbon's (TASK-738).</summary>
    internal const int TooltipDelayMs = 500;

    /// <summary>
    /// A command's tooltip (TASK-753, as b-ribbon's TASK-738): its name, the shortcut beside it and the description
    /// under it, in the light look the user picked for the ribbon (surface, text and border tokens, not the dark
    /// tooltip ones). Shown after a hover delay, and at once on keyboard focus; the first Escape hides it. The
    /// description is the automation help text and the shortcut the accelerator key.
    /// </summary>
    private static void AttachTip(Control target, string heading, string? text, string? shortcut)
    {
        var title = new TextBlock { TextWrapping = TextWrapping.Wrap };
        title.Inlines!.Add(new global::Avalonia.Controls.Documents.Run(heading) { FontWeight = FontWeight.SemiBold });
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var keys = new global::Avalonia.Controls.Documents.Run($" ({shortcut})");
            keys.Bind(global::Avalonia.Controls.Documents.TextElement.ForegroundProperty, target.GetResourceObservable("BTextMutedBrush"));
            title.Inlines.Add(keys);
        }
        var content = new StackPanel();
        content.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var line = Sized(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, MarginProperty, "BRibbonTipLineMargin", from: target);
            line.Bind(TextBlock.ForegroundProperty, target.GetResourceObservable("BTextSecondaryBrush"));
            content.Children.Add(line);
        }

        var tip = Sized(new ToolTip { Content = content }, PaddingProperty, "BRibbonTipPadding", from: target);
        tip.Bind(BackgroundProperty, target.GetResourceObservable("BBgElevatedBrush"));
        tip.Bind(ForegroundProperty, target.GetResourceObservable("BTextBrush"));
        tip.Bind(BorderBrushProperty, target.GetResourceObservable("BBorderBrush"));
        tip.Bind(FontSizeProperty, target.GetResourceObservable("BTextSm"));
        ToolTip.SetTip(target, tip);
        ToolTip.SetShowDelay(target, TooltipDelayMs);

        if (!string.IsNullOrWhiteSpace(text)) global::Avalonia.Automation.AutomationProperties.SetHelpText(target, text);
        if (!string.IsNullOrWhiteSpace(shortcut)) global::Avalonia.Automation.AutomationProperties.SetAcceleratorKey(target, shortcut);

        // Keyboard focus shows it at once (Avalonia tooltips are pointer-only); a click does not.
        target.GotFocus += (_, args) =>
        {
            if (args.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) ToolTip.SetIsOpen(target, true);
        };
        target.LostFocus += (_, _) => ToolTip.SetIsOpen(target, false);
    }

    /// <summary>Hides the tooltip of the control a key came from, if it is showing. True when one was hidden.</summary>
    private static bool HideOpenTip(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v is not Control c || ToolTip.GetTip(c) is null) continue;
            if (!ToolTip.GetIsOpen(c)) return false;
            ToolTip.SetIsOpen(c, false);
            return true;
        }
        return false;
    }

    private static string? StopKeyGroup(string key) => StopKey.GroupOf(key);

    // ── Sizes (TASK-763) ──

    /// <summary>
    /// Binds <paramref name="property"/> to a size resource: a generated token, or one of the ribbon's own sizes in
    /// <c>Controls/Ribbon.axaml</c>. Every size the ribbon draws goes through here or <see cref="SizedBy"/>, never a
    /// literal (<c>RibbonSizeTests</c>).
    /// </summary>
    private static T Sized<T>(T control, AvaloniaProperty property, string key, Control? from = null) where T : Control
    {
        control.Bind(property, (from ?? control).GetResourceObservable(key));
        return control;
    }

    /// <summary>
    /// A size computed from size resources: a desktop size derived from a token, or two sizes combined. A binding, as
    /// <see cref="Sized"/>'s, so a later local value still replaces it, as it replaced the literal it stands for.
    /// </summary>
    private static T SizedBy<T>(T control, AvaloniaProperty property, System.Func<SizeLookup, object> compute, params string[] keys) where T : Control
    {
        control.Bind(property, new SizeObservable(control, keys, compute));
        return control;
    }

    /// <summary>The resolved sizes a <see cref="SizedBy"/> computation reads; a missing one reads as nothing.</summary>
    private readonly struct SizeLookup
    {
        private readonly Dictionary<string, object?> _values;
        public SizeLookup(Dictionary<string, object?> values) => _values = values;
        public double D(string key) => _values.TryGetValue(key, out var v) && v is double d ? d : 0;
        public Thickness T(string key) => _values.TryGetValue(key, out var v) && v is Thickness t ? t : default;
    }

    /// <summary>Recomputes a size whenever one of its resources changes (a theme switch, an app's override).</summary>
    private sealed class SizeObservable : System.IObservable<object?>
    {
        private readonly Control _host;
        private readonly string[] _keys;
        private readonly System.Func<SizeLookup, object> _compute;

        public SizeObservable(Control host, string[] keys, System.Func<SizeLookup, object> compute)
        {
            _host = host;
            _keys = keys;
            _compute = compute;
        }

        public System.IDisposable Subscribe(System.IObserver<object?> observer)
        {
            var latest = new Dictionary<string, object?>();
            var subscriptions = _keys.Select(key => _host.GetResourceObservable(key).Subscribe(new SizeObserver(v =>
            {
                latest[key] = v;
                if (latest.Count == _keys.Length) observer.OnNext(_compute(new SizeLookup(latest)));
            }))).ToList();
            return new SizeSubscriptions(subscriptions);
        }
    }

    private sealed class SizeObserver : System.IObserver<object?>
    {
        private readonly System.Action<object?> _next;
        public SizeObserver(System.Action<object?> next) => _next = next;
        public void OnNext(object? value) => _next(value);
        public void OnError(System.Exception error) { }
        public void OnCompleted() { }
    }

    private sealed class SizeSubscriptions : System.IDisposable
    {
        private readonly List<System.IDisposable> _subscriptions;
        public SizeSubscriptions(List<System.IDisposable> subscriptions) => _subscriptions = subscriptions;
        public void Dispose() { foreach (var s in _subscriptions) s.Dispose(); }
    }

    /// <summary>A panel stop's identity, kept in its <c>Tag</c>: a command (group/item) or a folded group.</summary>
    private static class StopKey
    {
        public static string Item(string group, string item) => $"item:{group}/{item}";
        public static string Chunk(string group) => $"chunk:{group}";
        public static string Arrow(string group, string item) => $"arrow:{group}/{item}";
        public static string Launcher(string group) => $"launcher:{group}";
        public static string? GroupOf(string key) =>
            key.StartsWith("chunk:", System.StringComparison.Ordinal) ? key[6..]
            : key.StartsWith("item:", System.StringComparison.Ordinal) && key.IndexOf('/') is var slash and > 5 ? key[5..slash]
            : key.StartsWith("arrow:", System.StringComparison.Ordinal) && key.IndexOf('/') is var a and > 6 ? key[6..a]
            : key.StartsWith("launcher:", System.StringComparison.Ordinal) ? key[9..]
            // A gallery's tiles, steps and expand button (TASK-761): tile:{g}/{item}/{choice}, step:…, more:{g}/{item}.
            : (key.StartsWith("tile:", System.StringComparison.Ordinal) || key.StartsWith("step:", System.StringComparison.Ordinal)
                || key.StartsWith("more:", System.StringComparison.Ordinal)) && key.IndexOf('/') is var s and > 5 ? key[5..s]
            // An input box (TASK-762): field:{g}/{item}.
            : key.StartsWith("field:", System.StringComparison.Ordinal) && key.IndexOf('/') is var fs and > 6 ? key[6..fs]
            : null;
    }

    /// <summary>The panel stop focused last, by key: the one Tab stop, and where ↓ from a tab enters (TASK-752).</summary>
    private string? _rovingKey;

    /// <summary>The stop holding the panel's one Tab stop: the one focused last, else the first.</summary>
    private Control? RovingStop(Visual panel)
    {
        var stops = NavigableButtons(panel);
        return stops.FirstOrDefault(b => b.Tag as string == _rovingKey) ?? stops.FirstOrDefault();
    }

    /// <summary>Only the roving stop is in the Tab order, so the panel is one Tab stop, as b-ribbon's is.</summary>
    private void ApplyRoving(Visual panel)
    {
        var current = RovingStop(panel);
        foreach (var b in NavigableButtons(panel))
        {
            bool stop = ReferenceEquals(b, current);
            if (KeyboardNavigation.GetIsTabStop(b) != stop) KeyboardNavigation.SetIsTabStop(b, stop);
        }
    }

    /// <summary>
    /// The groups row's keyboard (TASK-752, as b-ribbon's TASK-731). One Tab stop (roving); ←/→ move to the nearest
    /// command that way by on-screen position, across groups, wrapping at the ends; ↑/↓ stay in the column, and ↑
    /// from the top row goes back to the active tab; Home/End go to the first and last command; ↓ on a folded group
    /// opens it and moves focus into its popup.
    /// </summary>
    private void WirePanelKeyboard(Control panel)
    {
        panel.LayoutUpdated += (_, _) => ApplyRoving(panel);
        panel.GotFocus += (_, args) =>
        {
            if (StopOf(panel, args.Source) is not { Tag: string key }) return;
            _rovingKey = key;
            ApplyRoving(panel);
        };
        panel.KeyDown += (_, args) =>
        {
            var stops = NavigableButtons(panel);
            if (StopOf(panel, args.Source) is not { } from || stops.Count == 0) return;
            // Keys inside an input edit it: the arrows, Home and End never leave the box (TASK-762, as the web's).
            if (IsFieldBox(from) && args.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End) return;
            Control? next = null;
            switch (args.Key)
            {
                case Key.Home: next = stops[0]; break;
                case Key.End: next = stops[^1]; break;
                case Key.Escape:
                    // A showing tooltip takes the first Escape (TASK-753, WCAG 1.4.13, as b-ribbon); the next one goes
                    // back to the active tab, as b-ribbon's panel does (TASK-752 follow-up).
                    if (!HideOpenTip(from) && SelectedIndex >= 0 && SelectedIndex < _tabList.Count)
                        _tabList[SelectedIndex].Focus(NavigationMethod.Directional);
                    args.Handled = true;
                    return;
                case Key.Down when from is RibbonChunkButton { Flyout: Flyout flyout } chunk:
                    flyout.ShowAt(chunk);
                    if (flyout.Content is Visual content)
                        Dispatcher.UIThread.Post(() => NavigableButtons(content).FirstOrDefault()?.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
                    args.Handled = true;
                    return;
                case Key.Left or Key.Right or Key.Up or Key.Down:
                    next = NearestInDirection(from, stops, panel, args.Key);
                    if (next is null && args.Key == Key.Right) next = stops[0];
                    if (next is null && args.Key == Key.Left) next = stops[^1];
                    if (next is null && args.Key == Key.Up && SelectedIndex >= 0 && SelectedIndex < _tabList.Count)
                    {
                        _tabList[SelectedIndex].Focus(NavigationMethod.Directional);
                        args.Handled = true;
                        return;
                    }
                    break;
                default: return;
            }
            if (next is null) return;
            next.Focus(NavigationMethod.Directional);
            args.Handled = true;
        };
    }

    /// <summary>
    /// Arrows inside a folded group's popup: by position, Left/Right wrapping as in the row, Home/End; Escape is
    /// handled by the popup itself.
    /// </summary>
    private static void WireFlyoutNavigation(Control scope)
    {
        scope.KeyDown += (_, args) =>
        {
            var items = NavigableButtons(scope);
            if (StopOf(scope, args.Source) is not { } from || items.Count == 0) return;
            if (IsFieldBox(from) && args.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End) return;
            Control? next = args.Key switch
            {
                Key.Home => items[0],
                Key.End => items[^1],
                Key.Left or Key.Right or Key.Up or Key.Down => NearestInDirection(from, items, scope, args.Key)
                    ?? (args.Key == Key.Right ? items[0] : args.Key == Key.Left ? items[^1] : null),
                _ => null,
            };
            if (next is null) return;
            next.Focus(NavigationMethod.Directional);
            args.Handled = true;
        };
    }

    /// <summary>The navigable stop the event came from (its source may be the stop's content).</summary>
    private static Control? StopOf(Visual scope, object? source) =>
        NavigableButtons(scope).FirstOrDefault(b => ReferenceEquals(b, source)
            || (source is Visual v && v.GetVisualAncestors().Contains(b)));

    /// <summary>
    /// The stop nearest to <paramref name="from"/> in a direction, by on-screen position (a port of b-ribbon's
    /// <c>nearestInDirection</c>): Left/Right take anything WHOLLY beyond it that way, weighting vertical distance so
    /// the same row wins; Up/Down stay in <paramref name="from"/>'s column, so they never jump to a neighbouring group.
    /// </summary>
    private static Control? NearestInDirection(Control from, IReadOnlyList<Control> stops, Visual frame, Key dir)
    {
        Rect RectOf(Control b) => b.TranslatePoint(default, frame) is { } p ? new Rect(p, b.Bounds.Size) : default;
        var a = RectOf(from);
        double ax = a.X + a.Width / 2, ay = a.Y + a.Height / 2;
        Control? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (var b in stops)
        {
            if (ReferenceEquals(b, from)) continue;
            var r = RectOf(b);
            if (r.Width == 0 && r.Height == 0) continue;
            double dx = r.X + r.Width / 2 - ax, dy = r.Y + r.Height / 2 - ay;
            double score;
            if (dir is Key.Left or Key.Right)
            {
                if (dir == Key.Right ? r.Left < a.Right - 2 : r.Right > a.Left + 2) continue;
                score = System.Math.Abs(dx) + System.Math.Abs(dy) * 4;
            }
            else
            {
                if (dir == Key.Down ? dy <= 1 : dy >= -1) continue;
                if (r.Right <= a.Left || r.Left >= a.Right) continue;
                score = System.Math.Abs(dy) + System.Math.Abs(dx) * 4;
            }
            if (score < bestScore) { bestScore = score; best = b; }
        }
        return best;
    }

    /// <summary>
    /// Put focus back after a rescale parked the focused command (TASK-752): on the same command at its new size,
    /// or, when its group folded, on the group's button; a folded group that opened up gives focus to its first
    /// command. Disabling the parked variant had dropped focus out of the ribbon.
    /// </summary>
    private void RefocusAfterRescale(Visual panel, string key)
    {
        var stops = NavigableButtons(panel);
        string? group = StopKey.GroupOf(key);
        var target = stops.FirstOrDefault(b => b.Tag as string == key)
            ?? (group is null ? null : stops.FirstOrDefault(b => b.Tag as string == StopKey.Chunk(group)))
            ?? (group is null ? null : stops.FirstOrDefault(b => b.Tag is string t && StopKey.GroupOf(t) == group));
        if (target is null) return;
        _rovingKey = target.Tag as string;
        target.Focus(NavigationMethod.Directional);
    }

    /// <summary>
    /// Close an open collapsed-group flyout because its group is no longer collapsed.
    /// </summary>
    /// <remarks>
    /// Focus goes back to the ribbon only if it was inside the flyout — a flyout closing because the window
    /// was resized must not steal focus from wherever the user actually is.
    /// </remarks>
    private void CloseChunkFlyoutOnPromotion()
    {
        if (_openChunkFlyout is not { } flyout) return;
        bool focusWasInFlyout = flyout is Flyout { Content: Visual content }
            && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused
            && (ReferenceEquals(focused, content) || focused.GetVisualAncestors().Contains(content));

        var chunk = _openChunkButton; // cleared by the flyout's Closed handler, so capture it first
        flyout.Hide();
        if (!focusWasInFlyout) return;

        // Into the group that was just promoted — NOT the first button in the row. Focusing the row's start
        // teleported the user from Export's flyout to Clipboard's first command, losing their place in a
        // control whose entire purpose is that commands stay where you remember them.
        var scope = chunk is not null && _groupsPanel is RibbonGroupsPanel scaling
            ? scaling.AppliedFor(chunk)
            : null;
        var landing = scope ?? _groupsPanel;
        if (landing is not null) NavigableButtons(landing).FirstOrDefault()?.Focus(NavigationMethod.Directional);
    }

    /// <summary>Whether keyboard focus is currently on this ribbon or inside it.</summary>
    private bool ContainsFocus()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused) return false;
        return ReferenceEquals(focused, this) || focused.GetVisualAncestors().Contains(this);
    }

    /// <summary>Last-resort focus colour, for when no theme is merged. Matches the light theme's primary.</summary>
    private static readonly IBrush FocusRingFallback = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));

    private static void AddFocusRing(Button button)
    {
        SizedBy(button, BorderThicknessProperty, s => new Thickness(s.D("BBorderWidthThick")), "BBorderWidthThick");
        button.BorderBrush = Brushes.Transparent;

        button.GotFocus += (_, e) =>
        {
            // Keyboard focus only, as the web's :focus-visible: a click focuses the button too, and the ring then sat
            // around a toggle's pressed frame as a second border (seen by the user, 2026-10-23).
            if (e.NavigationMethod == NavigationMethod.Pointer) return;
            // Token first, but NEVER nothing: a focus indicator that disappears because a theme did not load
            // is an accessibility failure dressed up as a styling detail. Caught by a test running without
            // the Birko theme merged, where the resource lookup yields null.
            button.BorderBrush = button.TryFindResource("BColorPrimaryBrush", out var brush) && brush is IBrush b
                ? b
                : FocusRingFallback;
        };
        button.LostFocus += (_, _) => button.BorderBrush = Brushes.Transparent;
    }

    /// <summary>
    /// A reserved-but-inactive chevron keeps its box so the row never reflows; only its opacity and
    /// hit-testing change. <see cref="Visual.IsVisible"/> would collapse the box.
    /// </summary>
    private static void SetActive(Button chevron, bool active)
    {
        chevron.Opacity = active ? 1 : 0;
        chevron.IsHitTestVisible = active;
        // IsEnabled too, or the invisible-but-reserved chevron stays in the TAB ORDER — a keyboard user lands
        // on a button they cannot see and whose activation does nothing. Opacity and hit-testing hide a control
        // from the MOUSE, not from Tab; the same defect as the parked size variants.
        chevron.IsEnabled = active;
    }

    private Button ScrollChevron(string glyph, string tip)
    {
        var button = new Button
        {
            Content = Sized(new TextBlock { Text = glyph }, TextBlock.FontSizeProperty, "BTextXs"),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsVisible = false, // Sync() reveals it once layout proves the row overflows.
            [ToolTip.TipProperty] = tip,
        };
        Sized(button, PaddingProperty, "BRibbonScrollPadding");
        button.Bind(ForegroundProperty, button.GetResourceObservable("BTextSecondaryBrush"));
        // The tip doubles as the accessible name — a bare "◂" announces as the glyph or as nothing at all.
        Describe(button, tip);
        return button;
    }

    /// <summary>
    /// One group at one variant. <c>Large</c> lays items out in a single row of icon-above-label buttons;
    /// <c>Medium</c> and <c>Small</c> stack three per column and flow the columns horizontally, as Office
    /// does — which is what makes them narrower rather than merely smaller.
    /// </summary>
    private Control BuildGroup(
        RibbonGroup group, RibbonGroupSize size, System.Action? onInvoke = null, bool wrapItems = false, string? groupKey = null)
    {
        if (size == RibbonGroupSize.Popup) return BuildChunk(group, groupKey: groupKey);

        Control items = size == RibbonGroupSize.Large
            ? Row(group.Items, size, onInvoke, wrapItems, groupKey)
            : Columns(group.Items, size, perColumn: 3, onInvoke, groupKey);
        // Centred over the group's name when the name is the wider of the two (a Small group's icons under
        // "Records"), so the space splits evenly instead of all sitting before the next divider (TASK-751, as
        // b-ribbon since TASK-746). A Large row keeps its commands at the top, as b-ribbon's flex-start does.
        items.HorizontalAlignment = HorizontalAlignment.Center;
        if (size == RibbonGroupSize.Large) items.VerticalAlignment = VerticalAlignment.Top;

        var label = new TextBlock
        {
            Text = group.Label,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Sized(label, TextBlock.FontSizeProperty, "BTextXs");
        // b-ribbon's .ribbon-group-label is line-height: 1; the default line box added ~4px per group.
        Sized(label, TextBlock.LineHeightProperty, "BTextXs");
        // The bottom 3px are the descenders': the line box is the font size, and the row clips at its own
        // bounds (parked variants live off-screen), so the "p" of "Clipboard" was cut off (TASK-751).
        Sized(label, MarginProperty, "BRibbonGroupLabelMargin");
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("BTextMutedBrush"));

        // The name, and the dialog launcher beside it at the bottom right (TASK-756). The name is padded by the
        // launcher's width on BOTH sides, so it stays centred under the group, as b-ribbon's label is.
        Control bottom = label;
        if (group.Launcher)
        {
            var launcher = BuildLauncher(group, groupKey, onInvoke);
            SizedBy(label, MarginProperty, s =>
            {
                var m = s.T("BRibbonGroupLabelMargin");
                double inset = s.D("BRibbonLauncherSize") + s.D("BRibbonLauncherGap");
                return new Thickness(inset, m.Top, inset, m.Bottom);
            }, "BRibbonGroupLabelMargin", "BRibbonLauncherSize", "BRibbonLauncherGap");
            var row = new Panel();
            row.Children.Add(label);
            row.Children.Add(launcher);
            bottom = row;
        }

        // Docked to the bottom, so every group's name sits on one line under the row however many commands the
        // group has (TASK-751, as b-ribbon's TASK-730): the panel arranges every group at the row's full height.
        DockPanel.SetDock(bottom, Dock.Bottom);

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(bottom);
        dock.Children.Add(items);

        // BSpaceXs horizontally, none vertically — b-ribbon's .ribbon-group is `padding: 0 var(--b-space-xs)`.
        // No border: RibbonGroupsPanel draws the divider halfway into the gap (TASK-751). As this group's right
        // border it sat on the group's edge with the whole gap after it, the mirror of the web defect TASK-730
        // fixed.
        return SizedBy(new Border { Child = dock }, Border.PaddingProperty, s => new Thickness(s.D("BSpaceXs"), 0), "BSpaceXs");
    }

    /// <summary>
    /// Items in one horizontal run. <paramref name="wrap"/> lets it reflow onto further lines instead —
    /// used inside a collapsed group's flyout, which has to fit a window narrow enough to have collapsed the
    /// group in the first place. A single run of Large items is easily wider than the ribbon at that point,
    /// and a flyout anchored near the right edge then has nowhere to go and gets cut off.
    /// </summary>
    private Control Row(
        IReadOnlyList<RibbonItem> items, RibbonGroupSize size, System.Action? onInvoke, bool wrap = false, string? groupKey = null)
    {
        if (wrap)
        {
            var wrapped = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var item in items) wrapped.Children.Add(BuildItem(item, size, onInvoke, groupKey));
            return wrapped;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Bind(StackPanel.SpacingProperty, row.GetResourceObservable("BRibbonItemGap"));
        foreach (var item in items) row.Children.Add(BuildItem(item, size, onInvoke, groupKey));
        return row;
    }

    /// <summary>
    /// Items in columns of <paramref name="perColumn"/>, columns flowing left to right, in <paramref name="perColumn"/>
    /// EQUAL rows (TASK-751, as b-ribbon's TASK-730). Rows sized by their own content drifted with each command's
    /// icon, so a command sat lower than the one beside it in the next group; with every group arranged at the row's
    /// height, equal rows line up across groups. A group with fewer commands leaves its last rows empty rather than
    /// spreading the ones it has.
    /// </summary>
    private Control Columns(
        IReadOnlyList<RibbonItem> items, RibbonGroupSize size, int perColumn, System.Action? onInvoke, string? groupKey = null)
    {
        var grid = new Grid();
        for (int r = 0; r < perColumn; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        // A gallery takes a column of its own at every row (TASK-761, as the web's spans the group's rows), so the
        // commands around it continue in the next column.
        var cells = new List<(int Row, int Column, bool Gallery)>();
        int nextRow = 0, nextColumn = 0;
        var spans = new List<int>();
        foreach (var it in items)
        {
            if (it.Gallery is { Items.Count: > 0 })
            {
                if (nextRow > 0) { nextColumn++; nextRow = 0; }
                cells.Add((0, nextColumn++, true));
                spans.Add(perColumn);
                continue;
            }
            // A captioned-above input takes two rows (TASK-762, as the web's grid-row: span 2).
            int rows = it.Field is { } fld && (fld.Caption ?? FieldCaption) == RibbonFieldCaption.Above ? System.Math.Min(2, perColumn) : 1;
            if (nextRow + rows > perColumn) { nextRow = 0; nextColumn++; }
            cells.Add((nextRow, nextColumn, rows > 1));
            spans.Add(rows);
            nextRow += rows;
            if (nextRow >= perColumn) { nextRow = 0; nextColumn++; }
        }
        int columns = nextRow > 0 ? nextColumn + 1 : nextColumn;
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int i = 0; i < items.Count; i++)
        {
            var item = BuildItem(items[i], size, onInvoke, groupKey);
            var (row, column, spanning) = cells[i];
            Grid.SetRow(item, row);
            Grid.SetColumn(item, column);
            if (spanning) Grid.SetRowSpan(item, spans[i]);
            // Column spacing is the BRibbonItemGap token, as before; rows keep their BRibbonRowGap.
            if (column > 0 || row > 0)
                SizedBy(item, MarginProperty, s => new Thickness(column > 0 ? s.D("BRibbonItemGap") : 0, row > 0 ? s.D("BRibbonRowGap") : 0, 0, 0),
                    "BRibbonItemGap", "BRibbonRowGap");
            if (!spanning && items[i].Field is null) item.VerticalAlignment = VerticalAlignment.Stretch;
            grid.Children.Add(item);
        }
        return grid;
    }

    /// <summary>Which part of a menu command <see cref="BuildItem"/> is drawing (TASK-755).</summary>
    private enum MenuPart { None, MenuOnly, SplitMain }

    private Control BuildItem(RibbonItem item, RibbonGroupSize size, System.Action? onInvoke = null, string? groupKey = null)
    {
        if (item.Gallery is { Items.Count: > 0 }) return BuildGallery(item, size, onInvoke, groupKey);
        if (item.Field is not null) return BuildField(item, size, onInvoke, groupKey);
        if (item.Menu is { Count: > 0 }) return BuildMenuCommand(item, size, onInvoke, groupKey);
        return BuildCommand(item, size, onInvoke, groupKey, MenuPart.None);
    }

    private Button BuildCommand(RibbonItem item, RibbonGroupSize size, System.Action? onInvoke, string? groupKey, MenuPart part)
    {
        string? pressKey = part == MenuPart.None && IsToggle(item) && groupKey is not null && _buildTab is not null
            ? PressKey(_buildTab, groupKey, item.Id) : null;
        Button button = part == MenuPart.MenuOnly ? new RibbonMenuButton()
            : pressKey is null ? new Button() : new RibbonToggleCommand { IsRadio = item.ToggleGroup is not null };
        // The command's identity across size variants (TASK-752): the same command is a different Button at each size,
        // and focus has to follow the command, not the control, when the row rescales.
        button.Tag = groupKey is null ? null : StopKey.Item(groupKey, item.Id);
        button.Background = Brushes.Transparent;
        // b-ribbon's .ribbon-item box is padding var(--b-space-xs) var(--b-space-sm) = 4/8, and its focus ring is an
        // outline, which takes no space. Here the ring is a permanent 2px border (AddFocusRing), so the padding is the
        // web pair MINUS that border: border + padding = 8/4, the same content inset. At 8/4 on top of the border every
        // row was 4px taller than the web's, 12px per three-row group. A toggle gives one more pixel to its pressed
        // outline (below), so it is the same size as its neighbours.
        SizedBy(button, PaddingProperty, s =>
        {
            var p = s.T("BRibbonCommandPadding");
            double f = pressKey is null ? 0 : s.D("BBorderWidth");
            return new Thickness(p.Left - f, p.Top - f, p.Right - f, p.Bottom - f);
        }, "BRibbonCommandPadding", "BBorderWidth");
        if (pressKey is null) button.Bind(ForegroundProperty, button.GetResourceObservable("BTextBrush"));
        if (part == MenuPart.MenuOnly) { } // the whole button opens its menu; the command itself has nothing to run
        else if (pressKey is null)
            button.Click += (_, _) => { item.Run?.Invoke(); onInvoke?.Invoke(); };
        else
        {
            string tab = _buildTab!, group = groupKey!;
            button.Click += (_, _) => { ClickToggle(tab, group, item); item.Run?.Invoke(); onInvoke?.Invoke(); };
        }
        AddFocusRing(button);

        var icon = new TextBlock { Text = item.Icon ?? "•" };
        icon.Bind(TextBlock.FontSizeProperty, button.GetResourceObservable(
            size == RibbonGroupSize.Large ? "BRibbonIconLarge" : "BRibbonIconSmall"));

        // Before the early returns below, so no variant can be left unnamed by a later edit.
        Describe(button, item.Label, icon);
        // Every size, not only Small: the tooltip carries the description and the shortcut too (TASK-753). At Small
        // it is also the only place the name is drawn.
        AttachTip(button, item.Label, item.Description, item.Shortcut);

        if (size == RibbonGroupSize.Small)
        {
            // Icon only. The label is not drawn, so the tooltip has to carry the name — an icon-only
            // command with no tooltip is unidentifiable, which would trade one accessibility problem
            // (unreachable) for another (unnameable).
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            button.Content = part == MenuPart.MenuOnly ? WithCaret(icon, button) : icon;
            return FinishToggle(button, pressKey, item);
        }

        var label = Sized(new TextBlock { Text = item.Label }, TextBlock.FontSizeProperty, "BTextXs");
        // Large split: the main part is the icon, its name sits in the ▾ part under it (b-ribbon's "Paste ▾").
        if (size == RibbonGroupSize.Large && part == MenuPart.SplitMain)
        {
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            button.Content = Sized(new Border { Child = icon }, MinWidthProperty, "BRibbonItemMinWidth");
            return button;
        }

        if (size == RibbonGroupSize.Large)
        {
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.TextWrapping = TextWrapping.Wrap;
            label.TextAlignment = TextAlignment.Center;
            var stacked = Sized(Sized(new StackPanel(), StackPanel.SpacingProperty, "BSpace2xs"), MinWidthProperty, "BRibbonItemMinWidth");
            stacked.Children.Add(icon);
            stacked.Children.Add(part == MenuPart.MenuOnly ? WithCaret(label, button) : label);
            button.Content = stacked;
            return FinishToggle(button, pressKey, item);
        }

        // Medium — icon then label on one line. Left-aligned: the button stretches to its column's width
        // and the shared Button theme centres content, which staggered labels of different lengths.
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        // Centred on one line, as b-ribbon's flex row is: top-aligned, the 11px label sat above the middle of
        // its 16px icon. The icon also gets a slot at least the icon size wide with the glyph centred in it —
        // emoji glyphs vary in width, and a narrow one (an arrow) pulled its label left of its neighbours'.
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.TextAlignment = TextAlignment.Center;
        icon.Bind(MinWidthProperty, button.GetResourceObservable("BRibbonIconSmall"));
        // CSS line-height: normal is ~1.2em; emoji glyphs come from a fallback font whose own line box is ~1.4em,
        // which made each Medium row ~3px taller than b-ribbon's for no visible gain.
        icon.Bind(TextBlock.LineHeightProperty, button.GetResourceObservable(
            "BRibbonIconSmall", v => v is double px ? System.Math.Round(px * 1.2) : double.NaN));
        label.VerticalAlignment = VerticalAlignment.Center;
        var inline = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Sized(inline, StackPanel.SpacingProperty, "BSpaceXs");
        inline.Children.Add(icon);
        inline.Children.Add(label);
        if (part == MenuPart.MenuOnly) inline.Children.Add(Caret(button));
        button.Content = inline;
        return FinishToggle(button, pressKey, item);
    }

    // ── Dialog launcher (TASK-756, as b-ribbon's TASK-733) ──

    /// <summary>Raised when a group's dialog launcher is pressed: open that group's full dialog.</summary>
    public event System.EventHandler<RibbonGroupLauncherEventArgs>? GroupLauncherInvoked;

    private static string LauncherName(RibbonGroup group) => group.LauncherLabel ?? $"More {group.Label} options";

    /// <summary>
    /// The corner arrow at the group's bottom right (D4, the user's pick: Office's). A small button sized by the
    /// <c>BRibbonLauncherSize</c> token with a vector arrow at <c>BRibbonLauncherGlyph</c>; named, tooltipped and a
    /// keyboard stop. A press raises <see cref="GroupLauncherInvoked"/>, never a command click.
    /// </summary>
    private Button BuildLauncher(RibbonGroup group, string? groupKey, System.Action? onInvoke)
    {
        var arrow = new global::Avalonia.Controls.Shapes.Path
        {
            // A diagonal with its corner, as b-ribbon's SVG: Office's "open the full dialog" glyph.
            Data = Geometry.Parse("M 1,1 L 8,8 M 8,3.5 L 8,8 L 3.5,8"),
            StrokeThickness = 1.4,
            Stretch = Stretch.Uniform,
        };
        arrow.Bind(global::Avalonia.Controls.Shapes.Shape.StrokeProperty, arrow.GetResourceObservable("BTextMutedBrush"));
        arrow.Bind(WidthProperty, arrow.GetResourceObservable("BRibbonLauncherGlyph"));
        arrow.Bind(HeightProperty, arrow.GetResourceObservable("BRibbonLauncherGlyph"));

        var button = new Button
        {
            Tag = groupKey is null ? null : StopKey.Launcher(groupKey),
            Content = arrow,
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Bind(WidthProperty, button.GetResourceObservable("BRibbonLauncherSize"));
        button.Bind(HeightProperty, button.GetResourceObservable("BRibbonLauncherSize"));
        AddFocusRing(button);
        string name = LauncherName(group);
        Describe(button, name);
        AttachTip(button, name, null, null);
        string tab = _buildTab ?? "";
        button.Click += (_, _) => { RaiseLauncher(tab, group); onInvoke?.Invoke(); };
        return button;
    }

    private void RaiseLauncher(string tabId, RibbonGroup group) =>
        GroupLauncherInvoked?.Invoke(this, new RibbonGroupLauncherEventArgs(tabId, group.Id ?? group.Label));

    // ── Split and menu buttons (TASK-755, as b-ribbon's TASK-732) ──

    /// <summary>Raised when a menu choice is picked, after its own <c>Run</c>: the choice and the command it belongs to.</summary>
    public event System.EventHandler<RibbonMenuItemInvokedEventArgs>? MenuItemInvoked;

    /// <summary>The ▾ that marks a menu: smaller and muted, as b-ribbon's <c>.ribbon-caret</c>.</summary>
    private static TextBlock Caret(Control owner)
    {
        var caret = Sized(new TextBlock { Text = "▾", VerticalAlignment = VerticalAlignment.Center }, TextBlock.FontSizeProperty, "BRibbonCaretSize");
        caret.Bind(TextBlock.ForegroundProperty, owner.GetResourceObservable("BTextMutedBrush"));
        global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(caret, global::Avalonia.Automation.AccessibilityView.Raw);
        return caret;
    }

    private static Control WithCaret(Control content, Control owner)
    {
        var row = Sized(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }, StackPanel.SpacingProperty, "BRibbonSplitGap");
        row.Children.Add(content);
        row.Children.Add(Caret(owner));
        return row;
    }

    /// <summary>
    /// A command with a menu. Without <c>Split</c>, one button that opens the menu. With it, the button runs the command
    /// and a separate ▾ part, named "More options: {label}", opens the menu; a divider between them shows on hover or
    /// focus (the look the user picked, D3 option A). Large stacks the parts, its name in the ▾ part. Alt+↓ opens the
    /// menu from either part.
    /// </summary>
    private Control BuildMenuCommand(RibbonItem item, RibbonGroupSize size, System.Action? onInvoke, string? groupKey)
    {
        string tab = _buildTab ?? "";
        if (!item.Split)
        {
            var only = BuildCommand(item, size, onInvoke, groupKey, MenuPart.MenuOnly);
            only.Flyout = BuildMenu(item, tab, onInvoke);
            WireAltDown(only, only);
            return only;
        }

        var main = BuildCommand(item, size, onInvoke, groupKey, MenuPart.SplitMain);
        bool large = size == RibbonGroupSize.Large;
        string more = $"More options: {item.Label}";
        var arrow = new RibbonMenuButton
        {
            Tag = groupKey is null ? null : StopKey.Arrow(groupKey, item.Id),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Sized(arrow, PaddingProperty, large ? "BRibbonSplitArrowPaddingLarge" : "BRibbonSplitArrowPadding");
        arrow.Bind(ForegroundProperty, arrow.GetResourceObservable("BTextBrush"));
        AddFocusRing(arrow);
        Describe(arrow, more);
        AttachTip(arrow, more, null, null);
        if (large)
        {
            var name = Sized(new TextBlock { Text = item.Label, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }, TextBlock.FontSizeProperty, "BTextXs");
            global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(name, global::Avalonia.Automation.AccessibilityView.Raw);
            arrow.Content = WithCaret(name, arrow);
            arrow.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else arrow.Content = Caret(arrow);
        arrow.Flyout = BuildMenu(item, tab, onInvoke);

        var divider = new Border { IsHitTestVisible = false, Background = Brushes.Transparent };
        Sized(divider, large ? HeightProperty : WidthProperty, "BBorderWidth");
        var frame = new StackPanel { Orientation = large ? Orientation.Vertical : Orientation.Horizontal };
        if (large) main.HorizontalAlignment = HorizontalAlignment.Stretch;
        frame.Children.Add(main);
        frame.Children.Add(divider);
        frame.Children.Add(arrow);

        // The divider shows while the split is hovered or holds focus, as b-ribbon's border-left does.
        void Sync()
        {
            bool on = frame.IsPointerOver || main.IsKeyboardFocusWithin || arrow.IsKeyboardFocusWithin;
            divider.Background = on && frame.TryFindResource("BBorderBrush", frame.ActualThemeVariant, out var b) && b is IBrush brush
                ? brush : Brushes.Transparent;
        }
        frame.PointerEntered += (_, _) => Sync();
        frame.PointerExited += (_, _) => Sync();
        frame.GotFocus += (_, _) => Sync();
        frame.LostFocus += (_, _) => Dispatcher.UIThread.Post(Sync);
        WireAltDown(main, arrow);
        WireAltDown(arrow, arrow);
        return frame;
    }

    /// <summary>Alt+↓ on <paramref name="from"/> opens <paramref name="trigger"/>'s menu at its first choice, as in Office.</summary>
    private static void WireAltDown(Control from, Button trigger)
    {
        from.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Down || !args.KeyModifiers.HasFlag(KeyModifiers.Alt) || trigger.Flyout is null) return;
            trigger.Flyout.ShowAt(trigger);
            args.Handled = true;
        };
    }

    /// <summary>
    /// A command's menu as a <see cref="MenuFlyout"/>, so arrows, Enter and Escape and the menu's automation roles are
    /// the toolkit's: a choice with its icon, label and right-aligned shortcut, disabled ones greyed, separators.
    /// </summary>
    private MenuFlyout BuildMenu(RibbonItem item, string tabId, System.Action? onInvoke)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var entry in item.Menu!)
        {
            if (entry is not RibbonMenuItem choice) { menu.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = choice.Label, IsEnabled = !choice.Disabled, Tag = choice.Keytip };
            if (choice.Icon is { } glyph) mi.Icon = new TextBlock { Text = glyph };
            if (!string.IsNullOrWhiteSpace(choice.Shortcut))
            {
                try { mi.InputGesture = KeyGesture.Parse(choice.Shortcut); } // display only, as on the web
                catch (System.ArgumentException) { }
                global::Avalonia.Automation.AutomationProperties.SetAcceleratorKey(mi, choice.Shortcut);
            }
            global::Avalonia.Automation.AutomationProperties.SetName(mi, choice.Label);
            mi.Click += (_, _) =>
            {
                choice.Run?.Invoke();
                MenuItemInvoked?.Invoke(this, new RibbonMenuItemInvokedEventArgs(tabId, choice.Id, item.Id));
                onInvoke?.Invoke();
            };
            menu.Items.Add(mi);
        }
        return menu;
    }

    // ── Toggles and one-of sets (TASK-754, as b-ribbon's TASK-739) ──

    /// <summary>Raised when a toggle or one-of member is pressed, with its new state, before its <c>Run</c>.</summary>
    public event System.EventHandler<RibbonItemToggledEventArgs>? ItemToggled;

    /// <summary>The state by tab/group/item, seeded from <see cref="RibbonItem.Pressed"/> and reset by new <see cref="Tabs"/>.</summary>
    private readonly Dictionary<string, bool> _pressed = new();
    /// <summary>Every on-screen copy of each toggle (one per size, the popup's, the narrow menu's), to keep in step.</summary>
    private readonly Dictionary<string, List<Button>> _toggleViews = new();
    private string? _buildTab;

    private static bool IsToggle(RibbonItem item) => item.Toggle || item.ToggleGroup is not null;

    private static string PressKey(string tab, string group, string item) => $"{tab}/{group}/{item}";

    private bool PressedOf(string key, RibbonItem item) => _pressed.TryGetValue(key, out bool on) ? on : item.Pressed;

    /// <summary>Turns a toggle on or off from the app, without an event; a one-of member turned on releases the others.</summary>
    public void SetPressed(string itemId, bool pressed, string? tabId = null)
    {
        foreach (var (tab, g, group, item) in Find(itemId, tabId))
            SetPressedState(tab.Id, g, group, item, pressed);
    }

    /// <summary>Whether a toggle or one-of member is on.</summary>
    public bool IsPressed(string itemId, string? tabId = null)
    {
        foreach (var (tab, g, _, item) in Find(itemId, tabId))
            return PressedOf(PressKey(tab.Id, g, item.Id), item);
        return false;
    }

    private IEnumerable<(RibbonTab Tab, string Group, RibbonGroup GroupModel, RibbonItem Item)> Find(string itemId, string? tabId)
    {
        foreach (var tab in Tabs ?? System.Array.Empty<RibbonTab>())
        {
            if (tabId is not null && tab.Id != tabId) continue;
            for (int g = 0; g < tab.Groups.Count; g++)
                foreach (var item in tab.Groups[g].Items)
                    if (item.Id == itemId && IsToggle(item))
                        yield return (tab, g.ToString(System.Globalization.CultureInfo.InvariantCulture), tab.Groups[g], item);
        }
    }

    private void ClickToggle(string tabId, string group, RibbonItem item)
    {
        var tab = (Tabs ?? System.Array.Empty<RibbonTab>()).FirstOrDefault(t => t.Id == tabId);
        if (tab is null || !int.TryParse(group, out int g) || g >= tab.Groups.Count) return;
        // A one-of member is turned ON by a press, never off: pressing the one that is on keeps it on.
        bool pressed = item.ToggleGroup is not null || !PressedOf(PressKey(tabId, group, item.Id), item);
        SetPressedState(tabId, group, tab.Groups[g], item, pressed);
        ItemToggled?.Invoke(this, new RibbonItemToggledEventArgs(tabId, item.Id, pressed));
    }

    private void SetPressedState(string tabId, string group, RibbonGroup model, RibbonItem item, bool pressed)
    {
        if (pressed && item.ToggleGroup is { } set)
            foreach (var other in model.Items.Where(i => i != item && i.ToggleGroup == set))
                Apply(PressKey(tabId, group, other.Id), false);
        Apply(PressKey(tabId, group, item.Id), pressed);

        void Apply(string key, bool on)
        {
            _pressed[key] = on;
            if (_toggleViews.TryGetValue(key, out var views))
                foreach (var view in views) ApplyPressedLook(view, on);
        }
    }

    /// <summary>Registers a toggle's copy and gives it its frame and its current state.</summary>
    private Button FinishToggle(Button button, string? key, RibbonItem item)
    {
        if (key is null || button is not RibbonToggleCommand) return button;
        // The pressed outline is a 1px frame inside the focus ring, always there and transparent when off, so pressing
        // never shifts the row (b-ribbon draws it as an inset box-shadow).
        var inner = button.Content as Control;
        button.Content = null; // detached first: a control has one parent
        var frame = Sized(SizedBy(new Border { Child = inner }, Border.BorderThicknessProperty, s => new Thickness(s.D("BBorderWidth")), "BBorderWidth"),
            Border.CornerRadiusProperty, "BRibbonFrameRadius");
        // The frame spans the whole button inside the focus ring, with the button's padding inside it, so the tint and
        // the outline are one shape at the button's edge, as the web's inset box-shadow. Inside the padding, the tint
        // on the button spilled out past the frame (seen by the user in the Gallery, 2026-10-23). The negative margin
        // takes back exactly what the padding gives, so the button's size does not change.
        void Span()
        {
            var p = button.Padding;
            frame.Margin = new Thickness(-p.Left, -p.Top, -p.Right, -p.Bottom);
            frame.Padding = p;
        }
        Span();
        button.PropertyChanged += (_, e) => { if (e.Property == PaddingProperty) Span(); };
        // The frame fills the button (a Medium command is as wide as its column); the content keeps its alignment
        // inside it.
        if (inner is not null)
        {
            inner.HorizontalAlignment = button.HorizontalContentAlignment;
            inner.VerticalAlignment = button.VerticalContentAlignment;
        }
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Stretch;
        button.Content = frame;
        if (!_toggleViews.TryGetValue(key, out var views)) _toggleViews[key] = views = new List<Button>();
        views.Add(button);
        ApplyPressedLook(button, PressedOf(key, item));
        button.ActualThemeVariantChanged += (_, _) => ApplyPressedLook(button, ((RibbonToggleCommand)button).IsPressed);
        return button;
    }

    /// <summary>
    /// The pressed look the user picked (D10, option A): the primary tint, the primary text colour and a 1px primary
    /// outline inside the button, the same size off and on.
    /// </summary>
    private static void ApplyPressedLook(Button button, bool on)
    {
        if (button is RibbonToggleCommand toggle) toggle.IsPressed = on;
        IBrush? Find(string key) => button.TryFindResource(key, button.ActualThemeVariant, out var v) ? v as IBrush : null;
        // The tint is the frame's, not the button's: the button's own background is the larger rounded shape around
        // the focus ring, which showed as a second, bigger outline behind the frame.
        button.Background = Brushes.Transparent;
        button.Foreground = on ? Find("BColorPrimaryTextBrush") ?? Find("BColorPrimaryBrush") : Find("BTextBrush");
        if (button.Content is not Border frame) return;
        frame.Background = on ? Find("BColorPrimaryLightBrush") ?? Brushes.Transparent : Brushes.Transparent;
        frame.BorderBrush = on ? Find("BColorPrimaryBrush") : Brushes.Transparent;
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
    private const double FlyoutChrome = 26; // size-ok: what the FlyoutPresenter theme adds, measured; never drawn

    private Control BuildChunk(RibbonGroup group, bool labelled = true, string? groupKey = null)
    {
        // A group without an icon shows its name (labelled), or its initial in a small tile (compact), as b-ribbon
        // does since TASK-743, which the user picked over one generic glyph for every group. This used to be ▦ for
        // all of them, so a row of compact chunks was a row of identical squares.
        Control icon = group.Icon is { } glyph
            ? new TextBlock { Text = glyph, HorizontalAlignment = HorizontalAlignment.Center }
            : labelled ? new Panel() : ChunkInitial(group.Label);
        var label = new TextBlock
        {
            Text = group.Label + " ⌄",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        Sized(label, TextBlock.FontSizeProperty, "BTextXs");

        var content = Sized(new StackPanel { VerticalAlignment = VerticalAlignment.Center }, StackPanel.SpacingProperty, "BSpace2xs");
        content.Children.Add(icon);
        // The extreme: even a row of labelled chunk buttons can be too wide, because a chunk shows
        // its group NAME and a name has a minimum width. Dropping it takes the six-group minimum from
        // ~500px to ~250px. The tooltip still carries the name, so the command is never anonymous —
        // the same trade the Small variant already makes for items.
        if (labelled) content.Children.Add(label);

        var button = new RibbonChunkButton
        {
            Tag = groupKey is null ? null : StopKey.Chunk(groupKey),
            Content = content,
            Background = Brushes.Transparent,
            // A compact chunk is already at the row's last resort, so it sheds every spare pixel: the
            // wrapper padding and the group separator below go too. Each one is multiplied by the group
            // count, so trimming ~20px per chunk moves the six-group floor by well over 100px.
        };
        Sized(button, PaddingProperty, labelled ? "BRibbonChunkPadding" : "BRibbonChunkPaddingCompact");
        button.Bind(ForegroundProperty, button.GetResourceObservable("BTextBrush"));
        button.Bind(TextBlock.FontSizeProperty, button.GetResourceObservable("BRibbonIconSmall"));
        if (labelled) button.Bind(MinWidthProperty, button.GetResourceObservable("BRibbonChunkWidth"));
        AttachTip(button, group.Label, null, null);

        // The accessible name is the GROUP's name. A compact chunk draws no label at all, so without this a
        // screen reader would announce "button" and nothing more; RibbonChunkButton's peer adds the
        // collapsed/expandable state alongside it.
        Describe(button, group.Label, icon);
        // Narrator reads HelpText after the name and control type, so this is where the *affordance* goes.
        // Without it the announcement says what the thing is but not that there is anything to do with it —
        // and a collapsed group is the only route to its commands, since Birko has no KeyTips yet.
        global::Avalonia.Automation.AutomationProperties.SetHelpText(
            button, "Press Enter to show this group's commands.");
        AddFocusRing(button);

        // Declared before the content so the items can dismiss the flyout they were invoked from — Office
        // closes a collapsed group's flyout as soon as a command runs.
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var flyoutContent = BuildGroup(group, RibbonGroupSize.Large, onInvoke: () => flyout.Hide(), groupKey: groupKey);
        flyout.Content = flyoutContent;
        button.Flyout = flyout;

        // Escape must be handled ON THE POPUP CONTENT, not only at the ribbon's TopLevel. A popup is its own
        // visual root, so once focus is inside the flyout the key never reaches the main window — which is
        // precisely the case a keyboard user is in. The TopLevel handler covers Escape pressed while focus is
        // still outside; this covers the case that actually matters.
        flyoutContent.KeyDown += (_, args) =>
        {
            if (args.Key != global::Avalonia.Input.Key.Escape) return;
            if (HideOpenTip(args.Source)) { args.Handled = true; return; } // a showing tooltip takes the first Escape
            flyout.Hide();
            button.Focus();
            args.Handled = true;
        };
        WireFlyoutNavigation(flyoutContent);

        // Remembered so Escape can close it. A Flyout does NOT dismiss on Escape by itself — verified, not
        // assumed, after light dismiss failed to deliver both Escape and click-away for the reveal popup.
        // Without this, a keyboard user who opens a collapsed group has no way back out.
        flyout.Opened += (_, _) => { _openChunkFlyout = flyout; _openChunkButton = button; };
        flyout.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_openChunkFlyout, flyout)) return;
            _openChunkFlyout = null;
            _openChunkButton = null;
        };

        // No border: RibbonGroupsPanel draws the dividers (TASK-751). The chunk fills the row's height, as
        // b-ribbon's folded group is one full-height button.
        button.VerticalAlignment = VerticalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        return Sized(new Border { Child = button }, Border.PaddingProperty, labelled ? "BRibbonChunkMargin" : "BRibbonChunkMarginCompact");
    }

    /// <summary>A compact folded group's initial: the first letter of its name in a small tile (TASK-751).</summary>
    private static Control ChunkInitial(string label)
    {
        string text = label.Trim();
        string initial = text.Length == 0 ? ""
            : char.IsSurrogate(text, 0) && text.Length > 1 ? text[..2]
            : text[..1].ToUpperInvariant();
        var letter = new TextBlock
        {
            Text = initial,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Sized(letter, TextBlock.FontSizeProperty, "BTextXs");
        var tile = SizedBy(new Border { Child = letter, HorizontalAlignment = HorizontalAlignment.Center },
            Border.PaddingProperty, s => new Thickness(s.D("BSpace2xs")), "BSpace2xs");
        tile.Bind(Border.BackgroundProperty, tile.GetResourceObservable("BBgTertiaryBrush"));
        tile.Bind(Border.CornerRadiusProperty, tile.GetResourceObservable("BRadiusSm"));
        tile.Bind(MinWidthProperty, tile.GetResourceObservable("BRibbonIconSmall"));
        tile.Bind(MinHeightProperty, tile.GetResourceObservable("BRibbonIconSmall"));
        return tile;
    }
}
