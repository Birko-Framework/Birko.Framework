using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Core.Ribbon;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Galleries (TASK-761, as b-ribbon's TASK-740): a row of visual choices with the full grid behind it, like Office's
/// Styles, in the three looks the user kept (framed, open, dropdown). Picking a tile makes it the current choice, runs it
/// and raises <see cref="GalleryItemPicked"/>; ▲ ▼ (or ‹ ›) step the row by a row's worth; the grid works by arrows,
/// Enter and Escape. The state is the ribbon's, by tab/group/item, and every view of a gallery (each size, a folded
/// group's popup, the narrow menu) is redrawn from it.
/// </summary>
public partial class Ribbon
{
    public static readonly StyledProperty<RibbonGalleryStyle> GalleryStyleProperty =
        AvaloniaProperty.Register<Ribbon, RibbonGalleryStyle>(nameof(GalleryStyle));

    /// <summary>How galleries are drawn, unless a gallery says otherwise; framed by default, as Office.</summary>
    public RibbonGalleryStyle GalleryStyle
    {
        get => GetValue(GalleryStyleProperty);
        set => SetValue(GalleryStyleProperty, value);
    }

    /// <summary>Raised when a tile is picked, after its <c>Run</c>: <c>ItemId</c> is the choice, <c>ParentId</c> the gallery.</summary>
    public event System.EventHandler<RibbonMenuItemInvokedEventArgs>? GalleryItemPicked;

    private readonly Dictionary<string, string> _gallerySel = new();
    private readonly Dictionary<string, int> _galleryOffset = new();
    private readonly Dictionary<string, List<System.Action<string?>>> _galleryViews = new();
    private Flyout? _galleryGrid;

    /// <summary>Sets a gallery's current choice from the app (<c>null</c>: none), without an event.</summary>
    public void SetGallerySelection(string itemId, string? choiceId, string? tabId = null)
    {
        foreach (var (tab, group, item) in Galleries(itemId, tabId))
        {
            string key = PressKey(tab, group, item.Id);
            _gallerySel[key] = choiceId ?? "";
            RefreshGallery(key, null);
        }
    }

    /// <summary>A gallery's current choice, or <c>null</c>.</summary>
    public string? GetGallerySelection(string itemId, string? tabId = null) =>
        Galleries(itemId, tabId).Select(f => GalleryChoice(PressKey(f.Tab, f.Group, f.Item.Id), f.Item)).FirstOrDefault() is { Length: > 0 } c ? c : null;

    private IEnumerable<(string Tab, string Group, RibbonItem Item)> Galleries(string itemId, string? tabId)
    {
        foreach (var tab in Tabs ?? System.Array.Empty<RibbonTab>())
        {
            if (tabId is not null && tab.Id != tabId) continue;
            for (int g = 0; g < tab.Groups.Count; g++)
                foreach (var item in tab.Groups[g].Items.Where(i => i.Id == itemId && i.Gallery is not null))
                    yield return (tab.Id, g.ToString(System.Globalization.CultureInfo.InvariantCulture), item);
        }
    }

    private string GalleryChoice(string key, RibbonItem item) =>
        _gallerySel.TryGetValue(key, out var chosen) ? chosen : item.Gallery?.Selected ?? "";

    private void RefreshGallery(string key, string? focusKey)
    {
        if (!_galleryViews.TryGetValue(key, out var views)) return;
        foreach (var view in views.ToList()) view(focusKey);
    }

    private static string TileKey(string group, string item, string choice) => $"tile:{group}/{item}/{choice}";

    private static string MoreKey(string group, string item) => $"more:{group}/{item}";

    /// <summary>
    /// A gallery at one size: a host redrawn in place from the ribbon's state, so a pick or a step never rebuilds the
    /// ribbon (which would close an unpinned reveal or a folded group's popup).
    /// </summary>
    private Control BuildGallery(RibbonItem item, RibbonGroupSize size, System.Action? onInvoke, string? groupKey)
    {
        string tab = _buildTab ?? "", group = groupKey ?? "";
        string key = PressKey(tab, group, item.Id);
        var host = new Border { VerticalAlignment = VerticalAlignment.Center };

        void Render(string? focusKey)
        {
            var focused = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() as Control;
            focusKey ??= focused is not null && host.IsVisualAncestorOf(focused) ? focused.Tag as string : null;
            host.Child = GalleryBody(item, size, onInvoke, tab, group, key, host);
            if (focusKey is null) return;
            Dispatcher.UIThread.Post(() =>
            {
                var buttons = NavigableButtons(host);
                // The same stop; else a step that reached its end goes to the other step, and a tile the row no longer
                // shows (picked from the grid) to the expand button, as the web's.
                bool fromStep = focusKey.StartsWith("step:", System.StringComparison.Ordinal);
                var back = buttons.FirstOrDefault(b => b.Tag as string == focusKey)
                    ?? (fromStep ? buttons.FirstOrDefault(b => b.Tag is string t && t.StartsWith("step:", System.StringComparison.Ordinal)) : null)
                    ?? buttons.FirstOrDefault(b => b.Tag as string == MoreKey(group, item.Id));
                back?.Focus(NavigationMethod.Directional);
                if (host.FindAncestorOfType<RibbonGroupsPanel>() is { } panel) ApplyRoving(panel);
            }, DispatcherPriority.Loaded);
        }

        Render(null);
        if (!_galleryViews.TryGetValue(key, out var views)) _galleryViews[key] = views = new List<System.Action<string?>>();
        views.Add(Render);
        return host;
    }

    private Control GalleryBody(RibbonItem item, RibbonGroupSize size, System.Action? onInvoke, string tab, string group, string key, Control host)
    {
        var g = item.Gallery!;
        string selected = GalleryChoice(key, item);
        int visible = System.Math.Max(1, g.Visible);
        if (size == RibbonGroupSize.Small) visible = System.Math.Min(visible, 2); // two tiles in a small group, as the web's
        int offset = System.Math.Min(_galleryOffset.GetValueOrDefault(key), System.Math.Max(0, g.Items.Count - visible));
        string showAll = $"Show all: {item.Label}";

        Button Expand(Control content)
        {
            var expand = new Button { Content = content, Tag = MoreKey(group, item.Id), Background = Brushes.Transparent, Padding = new Thickness(0) };
            expand.HorizontalContentAlignment = HorizontalAlignment.Center;
            expand.VerticalContentAlignment = VerticalAlignment.Center;
            expand.Bind(ForegroundProperty, expand.GetResourceObservable("BTextMutedBrush"));
            AddFocusRing(expand);
            Describe(expand, showAll);
            AttachTip(expand, showAll, null, null);
            expand.Click += (_, _) => OpenGalleryGrid(item, tab, group, key, host, onInvoke);
            return expand;
        }

        if ((g.Style ?? GalleryStyle) == RibbonGalleryStyle.Dropdown)
        {
            var current = g.Items.FirstOrDefault(c => c.Id == selected) ?? g.Items.FirstOrDefault();
            var face = TileFace(current?.Preview ?? Initials(current?.Label), (current?.Label ?? item.Label) + " ▾");
            var frame = new Border { Child = face, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(2) };
            frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("BBorderBrush"));
            TileSize(frame);
            var dropdown = Expand(frame);
            dropdown.Bind(ForegroundProperty, dropdown.GetResourceObservable("BTextSecondaryBrush"));
            return dropdown;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        global::Avalonia.Automation.AutomationProperties.SetName(row, item.Label);
        foreach (var choice in g.Items.Skip(offset).Take(visible))
            row.Children.Add(GalleryTile(item, choice, choice.Id == selected, tab, group, key, onInvoke));

        bool atStart = offset == 0, atEnd = offset + visible >= g.Items.Count;
        Button Step(int dir, string glyph)
        {
            string label = dir < 0 ? "Previous choices" : "More choices";
            var step = new Button
            {
                Content = new TextBlock { Text = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Tag = $"step:{group}/{item.Id}/{(dir < 0 ? "prev" : "next")}",
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                IsEnabled = dir < 0 ? !atStart : !atEnd,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            step.Bind(ForegroundProperty, step.GetResourceObservable("BTextMutedBrush"));
            step.Bind(WidthProperty, step.GetResourceObservable("BRibbonGalleryButtonWidth"));
            AddFocusRing(step);
            Describe(step, label);
            AttachTip(step, label, null, null);
            step.Click += (_, _) =>
            {
                int max = System.Math.Max(0, g.Items.Count - visible);
                _galleryOffset[key] = System.Math.Clamp(_galleryOffset.GetValueOrDefault(key) + dir * visible, 0, max);
                RefreshGallery(key, step.Tag as string);
            };
            return step;
        }

        if ((g.Style ?? GalleryStyle) == RibbonGalleryStyle.Open)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Children = { Step(-1, "‹"), row, Step(1, "›") } };
            var moreFrame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0), Child = new TextBlock { Text = "More ▾", FontSize = 11 } };
            moreFrame.Bind(Border.BorderBrushProperty, moreFrame.GetResourceObservable("BBorderBrush"));
            var more = Expand(moreFrame);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Bind(ForegroundProperty, more.GetResourceObservable("BTextSecondaryBrush"));
            return new StackPanel { Spacing = 2, Children = { line, more } };
        }

        // Framed (Office): the row in a frame, with ▲ ▼ ▾ stacked beside it behind a divider.
        var controls = new Grid { RowDefinitions = new RowDefinitions("*,*,*") };
        var up = Step(-1, "▲");
        var down = Step(1, "▼");
        var expandAll = Expand(new TextBlock { Text = "▾", HorizontalAlignment = HorizontalAlignment.Center });
        expandAll.Bind(WidthProperty, expandAll.GetResourceObservable("BRibbonGalleryButtonWidth"));
        foreach (var (b, r) in new[] { (up, 0), (down, 1), (expandAll, 2) })
        {
            b.FontSize = 9;
            b.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetRow(b, r);
            controls.Children.Add(b);
        }
        var divider = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(2, 0, 0, 0), Child = controls };
        divider.Bind(Border.BorderBrushProperty, divider.GetResourceObservable("BBorderBrush"));
        var framed = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { row, divider } },
        };
        framed.Bind(Border.BorderBrushProperty, framed.GetResourceObservable("BBorderBrush"));
        framed.Bind(Border.BackgroundProperty, framed.GetResourceObservable("BBgBrush"));
        return framed;
    }

    private static string Initials(string? label) => label is null ? "" : label.Length <= 2 ? label : label[..2];

    /// <summary>A tile's face: the preview drawn large over the name, as the web's.</summary>
    private static Control TileFace(string preview, string name)
    {
        var big = new TextBlock { Text = preview, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        big.Bind(TextBlock.FontSizeProperty, big.GetResourceObservable("BTextBase"));
        var small = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        small.Bind(TextBlock.FontSizeProperty, small.GetResourceObservable("BText2xs"));
        global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(big, global::Avalonia.Automation.AccessibilityView.Raw);
        global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(small, global::Avalonia.Automation.AccessibilityView.Raw);
        return new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { big, small } };
    }

    private static void TileSize(Control c)
    {
        c.Bind(WidthProperty, c.GetResourceObservable("BRibbonGalleryTileWidth"));
        c.Bind(MinHeightProperty, c.GetResourceObservable("BRibbonGalleryTileHeight"));
    }

    /// <summary>
    /// One tile, a radio button to assistive technology. The picked one has the pressed look of a toggle (primary tint,
    /// primary text, a 1px primary frame inside the focus ring), so picking never shifts the row.
    /// </summary>
    private Button GalleryTile(RibbonItem item, RibbonGalleryItem choice, bool on, string tab, string group, string key, System.Action? onInvoke)
    {
        var frame = new Border
        {
            Child = TileFace(choice.Preview ?? Initials(choice.Label), choice.Label),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2),
        };
        TileSize(frame);
        var tile = new RibbonToggleCommand
        {
            IsRadio = true,
            IsPressed = on,
            Content = frame,
            Tag = TileKey(group, item.Id, choice.Id),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
        };
        if (on)
        {
            frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("BColorPrimaryLightBrush"));
            frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("BColorPrimaryBrush"));
            tile.Bind(ForegroundProperty, tile.GetResourceObservable("BColorPrimaryTextBrush"));
        }
        else
        {
            frame.BorderBrush = Brushes.Transparent;
            tile.Bind(ForegroundProperty, tile.GetResourceObservable("BTextSecondaryBrush"));
        }
        AddFocusRing(tile);
        Describe(tile, choice.Label);
        AttachTip(tile, choice.Label, null, null);
        tile.Click += (_, _) => PickGallery(item, choice, tab, group, key, onInvoke);
        return tile;
    }

    private void PickGallery(RibbonItem item, RibbonGalleryItem choice, string tab, string group, string key, System.Action? onInvoke)
    {
        _galleryGrid?.Hide();
        _galleryGrid = null;
        _gallerySel[key] = choice.Id;
        // Back on the picked tile when the row shows it, else on the expand button, as the web's.
        RefreshGallery(key, TileKey(group, item.Id, choice.Id));
        choice.Run?.Invoke();
        GalleryItemPicked?.Invoke(this, new RibbonMenuItemInvokedEventArgs(tab, choice.Id, item.Id));
        onInvoke?.Invoke(); // closes a folded group's popup or an unpinned reveal, as a command does
    }

    /// <summary>
    /// The full grid, over the gallery (its top-left on the gallery's, as the web's popover), columns from the gallery.
    /// Opened from a folded group's popup it opens over that popup. Focus goes to the current choice.
    /// </summary>
    private void OpenGalleryGrid(RibbonItem item, string tab, string group, string key, Control host, System.Action? onInvoke)
    {
        var g = item.Gallery!;
        if (_galleryGrid?.IsOpen == true) { _galleryGrid.Hide(); return; }
        int columns = System.Math.Clamp(g.Columns, 1, 12);
        string selected = GalleryChoice(key, item);
        var grid = new Grid();
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < (g.Items.Count + columns - 1) / columns; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        global::Avalonia.Automation.AutomationProperties.SetName(grid, item.Label);
        var tiles = new List<Button>();
        for (int i = 0; i < g.Items.Count; i++)
        {
            var tile = GalleryTile(item, g.Items[i], g.Items[i].Id == selected, tab, group, key, onInvoke);
            tile.Margin = new Thickness(2);
            Grid.SetRow(tile, i / columns);
            Grid.SetColumn(tile, i % columns);
            grid.Children.Add(tile);
            tiles.Add(tile);
        }
        var flyout = new Flyout
        {
            Content = grid,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };
        var expand = NavigableButtons(host).FirstOrDefault(b => b.Tag as string == MoreKey(group, item.Id));
        grid.KeyDown += (_, args) =>
        {
            int i = tiles.FindIndex(t => ReferenceEquals(t, args.Source) || (args.Source is Visual v && v.GetVisualAncestors().Contains(t)));
            if (i < 0) return;
            if (args.Key == Key.Escape)
            {
                flyout.Hide();
                expand?.Focus(NavigationMethod.Directional);
                args.Handled = true;
                return;
            }
            int next = args.Key switch
            {
                Key.Right => i + 1,
                Key.Left => i - 1,
                Key.Down => i + columns,
                Key.Up => i - columns,
                Key.Home => 0,
                Key.End => tiles.Count - 1,
                _ => int.MinValue,
            };
            if (next == int.MinValue) return;
            tiles[System.Math.Clamp(next, 0, tiles.Count - 1)].Focus(NavigationMethod.Directional);
            args.Handled = true;
        };
        flyout.Closed += (_, _) => { if (ReferenceEquals(_galleryGrid, flyout)) _galleryGrid = null; };
        _galleryGrid = flyout;
        flyout.ShowAt(host);
        Dispatcher.UIThread.Post(() =>
            (tiles.FirstOrDefault(t => t is RibbonToggleCommand { IsPressed: true }) ?? tiles.FirstOrDefault())?.Focus(NavigationMethod.Directional),
            DispatcherPriority.Loaded);
    }

    /// <summary>The open grid, for key tips (its letters) and tests.</summary>
    internal Flyout? OpenGalleryGridFlyout => _galleryGrid?.IsOpen == true ? _galleryGrid : null;

    /// <summary>A gallery in the narrow menu: its name, then its choices as radio entries.</summary>
    private void AddNarrowGallery(StackPanel list, RibbonTab tab, string group, RibbonItem item)
    {
        var head = new TextBlock { Text = item.Label, FontSize = 12, Margin = new Thickness(16, 4, 0, 0) };
        head.Bind(TextBlock.ForegroundProperty, head.GetResourceObservable("BTextMutedBrush"));
        list.Children.Add(head);
        string key = PressKey(tab.Id, group, item.Id);
        string selected = GalleryChoice(key, item);
        foreach (var choice in item.Gallery!.Items)
        {
            var c = choice;
            var entry = new RibbonToggleCommand
            {
                IsRadio = true,
                IsPressed = c.Id == selected,
                Content = new TextBlock { Text = (c.Id == selected ? "✓  " : "    ") + c.Label },
                Background = Brushes.Transparent,
                Padding = new Thickness(32, 6, 16, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            entry.Bind(ForegroundProperty, entry.GetResourceObservable(c.Id == selected ? "BColorPrimaryTextBrush" : "BTextBrush"));
            Describe(entry, c.Label);
            entry.Click += (_, _) =>
            {
                _reveal?.Close();
                PickGallery(item, c, tab.Id, group, key, null);
            };
            list.Children.Add(entry);
        }
    }
}
