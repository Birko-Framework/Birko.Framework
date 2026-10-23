using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Birko.Xaml.Core.Ribbon;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>Where the Quick Access Toolbar sits (TASK-759, as b-ribbon's <c>qat-position</c>).</summary>
public enum RibbonQuickAccessPosition
{
    /// <summary>In the tab row, before the tabs (the default, the user's pick on the web).</summary>
    Tabs,
    /// <summary>On a row of its own above the tabs.</summary>
    Above,
    /// <summary>On a row of its own under the ribbon.</summary>
    Below,
}

/// <summary>
/// The Quick Access Toolbar (TASK-759, as b-ribbon's TASK-737): favourite commands as icon buttons, opt-in per app.
/// A right-click (or Shift+F10, or the menu key) on a ribbon command adds it, on a toolbar button removes it; the ▾
/// customises it and resets it. Decided by the user (2026-10-23): the APP saves the choice; the ribbon exposes it as
/// references (<see cref="QuickAccess"/>, <see cref="SetQuickAccessRefs"/>) and raises
/// <see cref="QuickAccessChanged"/>, with no file I/O in the control.
/// </summary>
public partial class Ribbon
{
    public static readonly StyledProperty<RibbonQuickAccessPosition> QuickAccessPositionProperty =
        AvaloniaProperty.Register<Ribbon, RibbonQuickAccessPosition>(nameof(QuickAccessPosition));

    public RibbonQuickAccessPosition QuickAccessPosition
    {
        get => GetValue(QuickAccessPositionProperty);
        set => SetValue(QuickAccessPositionProperty, value);
    }

    /// <summary>Raised when the user changes the toolbar, with its references: save them, and restore them with <see cref="SetQuickAccessRefs"/>.</summary>
    public event System.EventHandler<IReadOnlyList<string>>? QuickAccessChanged;

    private List<RibbonItem>? _qatDefaults;
    private List<string>? _qatRefs;
    private int _qatRoving;

    /// <summary>Whether the toolbar is on: an app that never calls <see cref="SetQuickAccess"/> has none, and no "Add" menu.</summary>
    public bool IsQuickAccessEnabled => _qatDefaults is not null;

    /// <summary>
    /// Turns the toolbar on with the app's default commands (<c>[]</c>: an empty toolbar the user can fill), or off
    /// (<c>null</c>). A default is referred to as <c>qat:{Id}</c>.
    /// </summary>
    public void SetQuickAccess(IEnumerable<RibbonItem>? defaults)
    {
        _qatDefaults = defaults?.ToList();
        Rebuild();
    }

    /// <summary>
    /// The toolbar's commands as references: <c>qat:{id}</c> for an app default, <c>item:{tab}/{group}/{item}</c>
    /// for a ribbon command the user added (group by position). The app saves this list.
    /// </summary>
    public IReadOnlyList<string> QuickAccess => _qatRefs ?? DefaultRefs();

    /// <summary>
    /// Restores a saved toolbar (references from <see cref="QuickAccess"/>); <c>null</c> goes back to the defaults.
    /// References that no longer resolve are dropped, repeats ignored. Raises no event.
    /// </summary>
    public void SetQuickAccessRefs(IEnumerable<string>? refs)
    {
        _qatRefs = refs?.Distinct().Where(r => Resolve(r) is not null).Take(64).ToList();
        Rebuild();
    }

    private List<string> DefaultRefs() => (_qatDefaults ?? new List<RibbonItem>()).Select(d => $"qat:{d.Id}").ToList();

    private static string ItemRef(string tab, string group, string item) => $"item:{tab}/{group}/{item}";

    /// <summary>What a reference names: the command, and for a ribbon command its tab and group.</summary>
    private (RibbonItem Item, string? Tab, string? Group)? Resolve(string reference)
    {
        if (reference.StartsWith("qat:", System.StringComparison.Ordinal))
            return _qatDefaults?.FirstOrDefault(d => d.Id == reference[4..]) is { } d ? (d, null, null) : null;
        if (!reference.StartsWith("item:", System.StringComparison.Ordinal)) return null;
        var parts = reference[5..].Split('/');
        if (parts.Length != 3 || Tabs?.FirstOrDefault(t => t.Id == parts[0]) is not { } tab
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int g) || g >= tab.Groups.Count) return null;
        return tab.Groups[g].Items.FirstOrDefault(i => i.Id == parts[2]) is { } item ? (item, tab.Id, parts[1]) : null;
    }

    /// <summary>Whether a ribbon command can be added: a command that runs something, not a menu-only button or a box.</summary>
    private static bool CanAdd(RibbonItem item) => item.Menu is not { Count: > 0 } || item.Split;

    private void EditQuickAccess(System.Func<List<string>, List<string>> change)
    {
        _qatRefs = change(QuickAccess.ToList());
        QuickAccessChanged?.Invoke(this, QuickAccess);
        Rebuild();
    }

    /// <summary>A default shown again goes back to its declared place among the defaults, as b-ribbon's does.</summary>
    private List<string> InsertRef(List<string> refs, string reference)
    {
        var defaults = DefaultRefs();
        int at = defaults.IndexOf(reference);
        if (at < 0) return refs.Append(reference).ToList();
        var after = defaults.Take(at).ToHashSet();
        int index = refs.FindLastIndex(r => after.Contains(r)) + 1;
        refs.Insert(index, reference);
        return refs;
    }

    /// <summary>The toolbar: a button per command and the ▾ that customises it; null when it is off.</summary>
    private Control? BuildQuickAccess()
    {
        if (_qatDefaults is null) return null;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        global::Avalonia.Automation.AutomationProperties.SetName(bar, "Quick Access Toolbar");
        var buttons = new List<Button>();
        foreach (var reference in QuickAccess)
        {
            if (Resolve(reference) is not var (item, tab, group)) continue;
            var button = BuildQatButton(reference, item, tab, group);
            buttons.Add(button);
            bar.Children.Add(button);
        }
        var more = new Button
        {
            Content = new TextBlock { Text = "▾", FontSize = 10 },
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = "qat-customize",
        };
        more.Bind(ForegroundProperty, more.GetResourceObservable("BTextMutedBrush"));
        more.Bind(MinWidthProperty, more.GetResourceObservable("BRibbonQatMoreWidth"));
        more.Bind(HeightProperty, more.GetResourceObservable("BRibbonQatButtonSize"));
        AddFocusRing(more);
        Describe(more, "Customize Quick Access Toolbar");
        AttachTip(more, "Customize Quick Access Toolbar", null, null);
        var customize = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        customize.Opening += (_, _) => FillCustomize(customize);
        more.Flyout = customize;
        buttons.Add(more);
        bar.Children.Add(more);

        // One Tab stop; the arrows move inside, as b-ribbon's toolbar.
        _qatRoving = System.Math.Clamp(_qatRoving, 0, buttons.Count - 1);
        void Roving() { for (int i = 0; i < buttons.Count; i++) KeyboardNavigation.SetIsTabStop(buttons[i], i == _qatRoving); }
        Roving();
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            buttons[i].GotFocus += (_, _) => { _qatRoving = index; Roving(); };
        }
        bar.KeyDown += (_, args) =>
        {
            int i = buttons.FindIndex(b => ReferenceEquals(b, args.Source) || (args.Source is Visual v && v.GetVisualAncestors().Contains(b)));
            if (i < 0) return;
            int next = args.Key switch
            {
                Key.Right => (i + 1) % buttons.Count,
                Key.Left => (i - 1 + buttons.Count) % buttons.Count,
                Key.Home => 0,
                Key.End => buttons.Count - 1,
                _ => -1,
            };
            if (next < 0) return;
            buttons[next].Focus(NavigationMethod.Directional);
            args.Handled = true;
        };
        _qatButtons = buttons.Take(buttons.Count - 1).ToList();
        return bar;
    }

    private List<Button> _qatButtons = new();

    private Button BuildQatButton(string reference, RibbonItem item, string? tab, string? group)
    {
        string? pressKey = tab is not null && group is not null && IsToggle(item) ? PressKey(tab, group, item.Id) : null;
        Button button = pressKey is null ? new Button() : new RibbonToggleCommand { IsRadio = item.ToggleGroup is not null };
        var glyph = new TextBlock
        {
            Text = item.Icon ?? item.Label[..1],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.Bind(TextBlock.FontSizeProperty, button.GetResourceObservable("BRibbonIconSmall"));
        button.Content = glyph;
        button.Tag = reference;
        button.Background = Brushes.Transparent;
        button.Padding = new Thickness(pressKey is null ? 2 : 1);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Bind(MinWidthProperty, button.GetResourceObservable("BRibbonQatButtonSize"));
        button.Bind(HeightProperty, button.GetResourceObservable("BRibbonQatButtonSize"));
        if (pressKey is null) button.Bind(ForegroundProperty, button.GetResourceObservable("BTextBrush"));
        AddFocusRing(button);
        Describe(button, item.Label, glyph);
        AttachTip(button, item.Label, item.Description, item.Shortcut);
        button.Click += (_, _) =>
        {
            if (pressKey is not null) ClickToggle(tab!, group!, item);
            item.Run?.Invoke();
        };
        button.ContextRequested += (_, e) =>
        {
            OpenQatMenu(button, "Remove from Quick Access Toolbar", () => EditQuickAccess(refs => refs.Where(r => r != reference).ToList()));
            e.Handled = true;
        };
        // A toggle's toolbar copy keeps in step with the ribbon's (TASK-754).
        return pressKey is null ? button : FinishToggle(button, pressKey, item);
    }

    private MenuFlyout? _qatMenu;

    /// <summary>The add/remove menu a right-click last opened, for tests.</summary>
    internal MenuFlyout? QuickAccessMenu => _qatMenu;

    private void OpenQatMenu(Control at, string label, System.Action act)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        _qatMenu = menu;
        var entry = new MenuItem { Header = label };
        global::Avalonia.Automation.AutomationProperties.SetName(entry, label);
        entry.Click += (_, _) => act();
        menu.Items.Add(entry);
        menu.ShowAt(at);
    }

    /// <summary>The ▾ menu: each default with a check (shown or not), the commands the user added, and Reset.</summary>
    private void FillCustomize(MenuFlyout menu)
    {
        menu.Items.Clear();
        var shown = QuickAccess.ToHashSet();
        foreach (var d in _qatDefaults ?? new List<RibbonItem>())
        {
            string reference = $"qat:{d.Id}";
            var mi = new MenuItem { Header = d.Label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = shown.Contains(reference) };
            global::Avalonia.Automation.AutomationProperties.SetName(mi, d.Label);
            mi.Click += (_, _) => EditQuickAccess(refs => refs.Contains(reference) ? refs.Where(r => r != reference).ToList() : InsertRef(refs, reference));
            menu.Items.Add(mi);
        }
        foreach (var reference in QuickAccess.Where(r => r.StartsWith("item:", System.StringComparison.Ordinal)))
        {
            if (Resolve(reference) is not var (item, _, _)) continue;
            var mi = new MenuItem { Header = item.Label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
            global::Avalonia.Automation.AutomationProperties.SetName(mi, item.Label);
            mi.Click += (_, _) => EditQuickAccess(refs => refs.Where(r => r != reference).ToList());
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "Reset" };
        global::Avalonia.Automation.AutomationProperties.SetName(reset, "Reset");
        reset.Click += (_, _) =>
        {
            _qatRefs = null;
            QuickAccessChanged?.Invoke(this, QuickAccess);
            Rebuild();
        };
        menu.Items.Add(reset);
    }

    /// <summary>
    /// A right-click, Shift+F10 or the menu key on a ribbon command (all raise ContextRequested) offers to add it to
    /// the toolbar, or to remove it if it is there. Only while the toolbar is on: without it, nothing is offered.
    /// </summary>
    private void WireQuickAccessContext(Control panel, string tabId)
    {
        panel.ContextRequested += (_, e) =>
        {
            if (_qatDefaults is null || StopOf(panel, e.Source) is not { Tag: string key } stop || stop is RibbonMenuButton) return;
            if (!key.StartsWith("item:", System.StringComparison.Ordinal)) return;
            string group = key[5..key.IndexOf('/')], id = key[(key.IndexOf('/') + 1)..];
            string reference = ItemRef(tabId, group, id);
            if (Resolve(reference) is not var (item, _, _) || !CanAdd(item)) return;
            bool there = QuickAccess.Contains(reference);
            OpenQatMenu(stop, there ? "Remove from Quick Access Toolbar" : "Add to Quick Access Toolbar",
                () => EditQuickAccess(refs => there ? refs.Where(r => r != reference).ToList() : refs.Append(reference).ToList()));
            e.Handled = true;
        };
    }
}
