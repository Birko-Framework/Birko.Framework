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

/// <summary><c>Ribbon.FieldChanged</c>: an input's value was committed (Enter, a pick, or leaving the box).</summary>
public sealed class RibbonFieldChangedEventArgs : System.EventArgs
{
    public RibbonFieldChangedEventArgs(string tabId, string itemId, string value)
    {
        TabId = tabId;
        ItemId = itemId;
        Value = value;
    }

    public string TabId { get; }
    public string ItemId { get; }

    /// <summary>The committed value: the text, the number as typed, or the picked option's value (a typed one when free).</summary>
    public string Value { get; }
}

/// <summary>
/// Inputs (TASK-762, as b-ribbon's TASK-741): small boxes in the ribbon, like Office's font name and size. A text or
/// number box (a <see cref="TextBox"/>; ↑ / ↓ step a number), a dropdown (a <see cref="ComboBox"/>), or a dropdown that
/// also takes a typed value (an <see cref="AutoCompleteBox"/>, decided per dropdown). Captioned in the three looks the
/// user kept. What is typed is a draft; a value is committed by Enter, a pick or leaving the box, and reported once.
/// Escape puts the committed value back. Every box of one input (each size, a folded group's popup, the narrow menu)
/// shows the same value.
/// </summary>
public partial class Ribbon
{
    public static readonly StyledProperty<RibbonFieldCaption> FieldCaptionProperty =
        AvaloniaProperty.Register<Ribbon, RibbonFieldCaption>(nameof(FieldCaption));

    /// <summary>How inputs are captioned, unless an input says otherwise; none by default, as Office.</summary>
    public RibbonFieldCaption FieldCaption
    {
        get => GetValue(FieldCaptionProperty);
        set => SetValue(FieldCaptionProperty, value);
    }

    /// <summary>Raised once per committed value.</summary>
    public event System.EventHandler<RibbonFieldChangedEventArgs>? FieldChanged;

    private readonly Dictionary<string, string> _fieldDraft = new();
    private readonly Dictionary<string, string> _fieldCommitted = new();
    private readonly Dictionary<string, List<(Control Box, System.Action<string> Show)>> _fieldViews = new();

    /// <summary>Sets an input's value from the app, without an event; every box of it shows it.</summary>
    public void SetFieldValue(string itemId, string value, string? tabId = null)
    {
        foreach (var (tab, group, item) in Fields(itemId, tabId))
        {
            string key = PressKey(tab, group, item.Id);
            _fieldDraft[key] = value;
            _fieldCommitted[key] = value;
            ShowField(key, value, except: null);
        }
    }

    /// <summary>An input's committed value, or <c>null</c> when there is no such input.</summary>
    public string? GetFieldValue(string itemId, string? tabId = null) =>
        Fields(itemId, tabId).Select(f => Committed(PressKey(f.Tab, f.Group, f.Item.Id), f.Item)).FirstOrDefault();

    private IEnumerable<(string Tab, string Group, RibbonItem Item)> Fields(string itemId, string? tabId)
    {
        foreach (var tab in Tabs ?? System.Array.Empty<RibbonTab>())
        {
            if (tabId is not null && tab.Id != tabId) continue;
            for (int g = 0; g < tab.Groups.Count; g++)
                foreach (var item in tab.Groups[g].Items.Where(i => i.Id == itemId && i.Field is not null))
                    yield return (tab.Id, g.ToString(CultureInfo.InvariantCulture), item);
        }
    }

    private string Committed(string key, RibbonItem item) => _fieldCommitted.TryGetValue(key, out var v) ? v : item.Field?.Value ?? "";

    private string Draft(string key, RibbonItem item) => _fieldDraft.TryGetValue(key, out var v) ? v : Committed(key, item);

    private void ShowField(string key, string value, Control? except)
    {
        if (!_fieldViews.TryGetValue(key, out var views)) return;
        foreach (var (box, show) in views) if (!ReferenceEquals(box, except)) show(value);
    }

    private static bool IsFieldBox(Control c) => c.Tag is string t && t.StartsWith("field:", System.StringComparison.Ordinal);

    private static string FieldKey(string group, string item) => $"field:{group}/{item}";

    /// <summary>An input at one size, with its caption in the look picked (a small group drops a caption beside it).</summary>
    private Control BuildField(RibbonItem item, RibbonGroupSize size, System.Action? onInvoke, string? groupKey, bool narrow = false)
    {
        var f = item.Field!;
        string tab = _buildTab ?? "", group = groupKey ?? "";
        var box = FieldBox(item, tab, group, narrow ? null : onInvoke);
        var caption = narrow ? RibbonFieldCaption.Beside : f.Caption ?? FieldCaption;
        if (caption == RibbonFieldCaption.None || (caption == RibbonFieldCaption.Beside && size == RibbonGroupSize.Small)) return box;

        var text = new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.FontSizeProperty, text.GetResourceObservable(narrow ? "BTextSm" : "BTextXs"));
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(narrow ? "BTextBrush" : "BTextSecondaryBrush"));
        // The caption is decoration: the box carries the name, as the web's aria-hidden caption.
        global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(text, global::Avalonia.Automation.AccessibilityView.Raw);
        if (caption == RibbonFieldCaption.Above)
            // Half the web's 2xs gap, as its calc(var(--b-space-2xs) / 2).
            return SizedBy(new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Children = { text, box } },
                StackPanel.SpacingProperty, s => s.D("BSpace2xs") / 2, "BSpace2xs");
        var beside = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        SizedBy(text, MarginProperty, s => new Thickness(0, 0, s.D("BRibbonFieldCaptionGap"), 0), "BRibbonFieldCaptionGap");
        DockPanel.SetDock(text, Dock.Left);
        DockPanel.SetDock(box, Dock.Right);
        box.HorizontalAlignment = HorizontalAlignment.Right;
        beside.Children.Add(text);
        beside.Children.Add(box);
        if (narrow) Sized(beside, MarginProperty, "BRibbonNarrowFieldMargin");
        return beside;
    }

    private Control FieldBox(RibbonItem item, string tab, string group, System.Action? onInvoke)
    {
        var f = item.Field!;
        string key = PressKey(tab, group, item.Id);
        bool seeding = false;
        string Display(string value) => f.Options.FirstOrDefault(o => o.Value == value)?.Label ?? value;
        string ValueOf(string text) => f.Options.FirstOrDefault(o => o.Label == text || o.Value == text)?.Value ?? text;

        Control box;
        System.Action<string> show;
        if (f.Type == RibbonFieldType.Select && !f.FreeText)
        {
            var combo = new ComboBox { ItemsSource = f.Options.Select(o => o.Label).ToList() };
            show = v => { seeding = true; combo.SelectedIndex = f.Options.ToList().FindIndex(o => o.Value == v); seeding = false; };
            bool picked = false;
            combo.SelectionChanged += (_, _) =>
            {
                if (seeding || combo.SelectedIndex < 0) return;
                _fieldDraft[key] = f.Options[combo.SelectedIndex].Value;
                // A pick from the open list commits once it closes; an arrow on the closed box is a draft until Enter or leaving.
                if (combo.IsDropDownOpen) picked = true;
            };
            combo.DropDownClosed += (_, _) =>
            {
                if (!picked) return;
                picked = false;
                CommitField(item, tab, key, combo, explicitly: true, onInvoke);
            };
            box = combo;
        }
        else if (f.Type == RibbonFieldType.Select)
        {
            var auto = new AutoCompleteBox
            {
                ItemsSource = f.Options.Select(o => o.Label).ToList(),
                FilterMode = AutoCompleteFilterMode.StartsWith,
                MinimumPrefixLength = 0,
                Watermark = f.Placeholder,
            };
            show = v => { seeding = true; auto.Text = Display(v); seeding = false; };
            // Birko themes TextBox and ComboBox, not AutoCompleteBox: its frame takes the same tokens here, and its
            // inner box the small height and padding, or the free dropdown stood taller than its neighbours.
            auto.Bind(BackgroundProperty, auto.GetResourceObservable("BBgBrush"));
            auto.Bind(BorderBrushProperty, auto.GetResourceObservable("BBorderBrush"));
            auto.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find<TextBox>("PART_TextBox") is not { } inner) return;
                inner.Bind(MinHeightProperty, inner.GetResourceObservable("BControlMinHeightSm"));
                // The box's padding, its right side the ▾'s slot so typed text never runs under it.
                SizedBy(inner, PaddingProperty, s =>
                {
                    var p = s.T("BRibbonFieldBoxPadding");
                    return new Thickness(p.Left, p.Top, s.D("BRibbonFieldArrowWidth"), p.Bottom);
                }, "BRibbonFieldBoxPadding", "BRibbonFieldArrowWidth");
                // The ▾ the web's free dropdown shows (the user found none, 2026-10-23): without it the list only
                // appeared while typing, filtered to what was typed. Not a Button, so it is no panel stop of its own.
                if (inner.Parent is Panel host) host.Children.Add(FreeDropdownArrow(auto));
            };
            auto.TextChanged += (_, _) => { if (!seeding) _fieldDraft[key] = ValueOf(auto.Text ?? ""); };
            bool picked = false;
            auto.SelectionChanged += (_, _) => { if (!seeding && auto.SelectedItem is string s) { _fieldDraft[key] = ValueOf(s); picked = auto.IsDropDownOpen; } };
            auto.DropDownClosed += (_, _) =>
            {
                if (!picked) return;
                picked = false;
                CommitField(item, tab, key, auto, explicitly: true, onInvoke);
            };
            box = auto;
        }
        else
        {
            var text = new TextBox { Watermark = f.Placeholder };
            show = v => { seeding = true; text.Text = v; seeding = false; };
            text.TextChanged += (_, _) => { if (!seeding) _fieldDraft[key] = text.Text ?? ""; };
            box = text;
        }

        box.Tag = FieldKey(group, item.Id);
        box.VerticalAlignment = VerticalAlignment.Center;
        // Left in its column, as the web's grid cell: a fixed-width box centred in a wider column wandered.
        box.HorizontalAlignment = HorizontalAlignment.Left;
        if (box is TemplatedControl) Sized(box, TemplatedControl.PaddingProperty, "BRibbonFieldBoxPadding"); // the web's size="sm" box
        int chars = System.Math.Clamp(f.Width, 2, 40);
        // Width in characters plus the box's own padding, as the web's calc(chars * 1ch + --b-ribbon-field-padding).
        SizedBy(box, WidthProperty, s => chars * s.D("BRibbonFieldCharWidth") + s.D("BRibbonFieldPadding"), "BRibbonFieldCharWidth", "BRibbonFieldPadding");
        box.Bind(MinHeightProperty, box.GetResourceObservable("BControlMinHeightSm"));
        ((TemplatedControl)box).Bind(TemplatedControl.FontSizeProperty, box.GetResourceObservable("BTextSm"));
        Describe(box, item.Label);
        AttachTip(box, item.Label, item.Description, item.Shortcut);
        show(Draft(key, item));

        // Tunnel, so Enter, Escape and a number's arrows are the ribbon's before the box's own handling.
        box.AddHandler(KeyDownEvent, (_, args) =>
        {
            bool listOpen = box is ComboBox { IsDropDownOpen: true } || box is AutoCompleteBox { IsDropDownOpen: true };
            switch (args.Key)
            {
                case Key.Enter when !listOpen:
                    CommitField(item, tab, key, box, explicitly: true, onInvoke);
                    args.Handled = true;
                    break;
                case Key.Escape when !listOpen:
                    // Back to the committed value; then to the active tab, unless a folded group's popup takes the key.
                    string committed = Committed(key, item);
                    _fieldDraft[key] = committed;
                    show(committed);
                    if (box.FindAncestorOfType<RibbonGroupsPanel>() is null) break;
                    if (SelectedIndex >= 0 && SelectedIndex < _tabList.Count) _tabList[SelectedIndex].Focus(NavigationMethod.Directional);
                    args.Handled = true;
                    break;
                case Key.Down when box is AutoCompleteBox free && args.KeyModifiers.HasFlag(KeyModifiers.Alt):
                    OpenWholeList(free); // Alt+Down, as a list box and Office's font boxes
                    args.Handled = true;
                    break;
                case Key.Up or Key.Down when f.Type == RibbonFieldType.Number:
                    double now = double.TryParse(Draft(key, item), NumberStyles.Float, CultureInfo.CurrentCulture, out var n) ? n : f.Min ?? 0;
                    double next = now + (args.Key == Key.Up ? 1 : -1) * (f.Step ?? 1);
                    if (f.Min is { } min) next = System.Math.Max(min, next);
                    if (f.Max is { } max) next = System.Math.Min(max, next);
                    string stepped = next.ToString(CultureInfo.CurrentCulture);
                    _fieldDraft[key] = stepped;
                    show(stepped);
                    if (box is TextBox t) t.CaretIndex = stepped.Length;
                    args.Handled = true;
                    break;
            }
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) =>
        {
            // Not while focus moves into the box's own list or its parts.
            if (TopLevel.GetTopLevel(box)?.FocusManager?.GetFocusedElement() is Visual v && (ReferenceEquals(v, box) || box.IsVisualAncestorOf(v))) return;
            CommitField(item, tab, key, box, explicitly: false, onInvoke);
        };

        if (!_fieldViews.TryGetValue(key, out var views)) _fieldViews[key] = views = new List<(Control, System.Action<string>)>();
        views.Add((box, show));
        return box;
    }

    /// <summary>A free dropdown's ▾: opens (or closes) its whole list, as the closed list's chevron.</summary>
    private static Control FreeDropdownArrow(AutoCompleteBox auto)
    {
        var chevron = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L5,5 L10,0"),
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Sized(chevron, WidthProperty, "BRibbonFieldChevronWidth");
        Sized(chevron, HeightProperty, "BRibbonFieldChevronHeight");
        chevron.Bind(global::Avalonia.Controls.Shapes.Shape.FillProperty, chevron.GetResourceObservable("BTextSecondaryBrush"));
        var arrow = new Border
        {
            Child = chevron,
            Background = Brushes.Transparent, // hit-testable across its whole slot
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        Sized(arrow, WidthProperty, "BRibbonFieldArrowWidth");
        global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(arrow, global::Avalonia.Automation.AccessibilityView.Raw);
        arrow.PointerPressed += (_, e) =>
        {
            if (auto.IsDropDownOpen) auto.IsDropDownOpen = false;
            else OpenWholeList(auto);
            e.Handled = true;
        };
        return arrow;
    }

    /// <summary>Every choice, not only those starting with what is typed; the typing filter comes back once it closes.</summary>
    private static void OpenWholeList(AutoCompleteBox auto)
    {
        auto.Focus();
        var filter = auto.FilterMode;
        auto.FilterMode = AutoCompleteFilterMode.None;
        void Restore(object? sender, System.EventArgs e)
        {
            auto.DropDownClosed -= Restore;
            auto.FilterMode = filter;
        }
        auto.DropDownClosed += Restore;
        auto.IsDropDownOpen = true;
    }

    /// <summary>
    /// Reports the value when it differs from the last one reported, and shows it in the input's other boxes. An explicit
    /// commit (Enter, a pick) also closes a folded group's popup or an unpinned reveal, as a command does; leaving does not.
    /// </summary>
    private void CommitField(RibbonItem item, string tab, string key, Control box, bool explicitly, System.Action? onInvoke)
    {
        string value = Draft(key, item);
        if (value == Committed(key, item)) return;
        _fieldCommitted[key] = value;
        ShowField(key, value, except: box);
        FieldChanged?.Invoke(this, new RibbonFieldChangedEventArgs(tab, item.Id, value));
        if (explicitly) onInvoke?.Invoke();
    }

    /// <summary>Key tips: the box takes focus with its text selected, ready to type over, as Office's.</summary>
    private static void FocusFieldForTyping(Control box)
    {
        box.Focus(NavigationMethod.Directional);
        (box as TextBox ?? box.GetVisualDescendants().OfType<TextBox>().FirstOrDefault())?.SelectAll();
    }
}
