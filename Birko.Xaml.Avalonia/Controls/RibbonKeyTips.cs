using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Core.Ribbon;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Key tips (TASK-758, as b-ribbon's TASK-736): tapping and releasing Alt shows a letter on every tab; a letter opens
/// the tab and shows letters on its commands; a letter on a folded group or a menu opens it and shows letters inside;
/// a letter on a command runs it. Escape steps back a level; a click, the window losing focus or a letter with no tip
/// ends it. Plain Alt, as desktop Office (decided by the user, 2026-10-23); the web uses Alt+Windows because the browser
/// owns Alt.
/// </summary>
public partial class Ribbon
{
    public static readonly StyledProperty<Key> KeyTipKeyProperty =
        AvaloniaProperty.Register<Ribbon, Key>(nameof(KeyTipKey), Key.LeftAlt);

    /// <summary>
    /// The key whose tap (press and release, nothing else in between) shows the key tips. Alt by default (either Alt
    /// key); <see cref="Key.None"/> turns key tips off.
    /// </summary>
    public Key KeyTipKey { get => GetValue(KeyTipKeyProperty); set => SetValue(KeyTipKeyProperty, value); }

    private enum TipLevel { Tabs, Commands, Popup }

    private TipLevel? _tipLevel;
    private readonly Dictionary<string, Control> _tipTargets = new();
    private readonly List<(AdornerLayer Layer, Control Badge)> _badges = new();
    private readonly HashSet<Interactive> _tipScopes = new();
    private FlyoutBase? _tipPopup;
    private bool _tipArmed;
    private Panel? _revealPanel;

    /// <summary>Whether key tips are showing (for tests and a consumer's own shortcuts).</summary>
    public bool IsKeyTipMode => _tipLevel is not null;

    /// <summary>The letters on screen and what each one reaches, for tests.</summary>
    internal IReadOnlyDictionary<string, Control> KeyTips => _tipTargets;

    private bool IsTipTrigger(Key key) =>
        KeyTipKey != Key.None
        && (key == KeyTipKey || (KeyTipKey is Key.LeftAlt or Key.RightAlt && key is Key.LeftAlt or Key.RightAlt));

    private void AttachKeyTips(TopLevel root)
    {
        root.AddHandler(KeyDownEvent, OnTipKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(KeyUpEvent, OnTipKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(PointerPressedEvent, OnTipPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (root is WindowBase window) window.Deactivated += OnTipWindowDeactivated;
    }

    private void DetachKeyTips(TopLevel root)
    {
        root.RemoveHandler(KeyDownEvent, OnTipKeyDown);
        root.RemoveHandler(KeyUpEvent, OnTipKeyUp);
        root.RemoveHandler(PointerPressedEvent, OnTipPointerPressed);
        if (root is WindowBase window) window.Deactivated -= OnTipWindowDeactivated;
    }

    private void OnTipWindowDeactivated(object? sender, System.EventArgs e) => ExitKeyTips();

    private void OnTipPointerPressed(object? sender, PointerPressedEventArgs e) => ExitKeyTips();

    /// <summary>Whether focus is in a text box: Alt there is the text box's, never the ribbon's.</summary>
    private bool FocusInTextInput() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused
        && (focused is TextBox || focused.GetVisualAncestors().OfType<TextBox>().Any());

    private KeyEventArgs? _lastTipKey;

    private void OnTipKeyDown(object? sender, KeyEventArgs e)
    {
        // One key, one action: where a popup's route also reaches the window, both handlers see the same event.
        if (ReferenceEquals(e, _lastTipKey)) return;
        _lastTipKey = e;
        if (IsTipTrigger(e.Key))
        {
            // Armed by the trigger alone, and only outside a text box; any other key in between disarms it.
            _tipArmed = e.KeyModifiers is KeyModifiers.Alt or KeyModifiers.None && !FocusInTextInput();
            if (_tipLevel is not null) e.Handled = true;
            return;
        }
        _tipArmed = false;
        // While key tips show, the keys are theirs, handled elsewhere or not (Avalonia's access-key handler marks some).
        if (_tipLevel is null) return;
        e.Handled = true;
        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LWin or Key.RWin) return;
        if (e.Key == Key.Escape) { StepBackKeyTips(); return; }
        // A letter with no tip ends the mode and is swallowed rather than typed, as on the web.
        if (TipLetter(e) is not { } letter || !_tipTargets.TryGetValue(letter, out var target)) { ExitKeyTips(); return; }
        ActOnKeyTip(target);
    }

    private void OnTipKeyUp(object? sender, KeyEventArgs e)
    {
        if (!IsTipTrigger(e.Key) || !_tipArmed) return;
        _tipArmed = false;
        if (_tipLevel is null) ShowKeyTips(TipLevel.Tabs); else ExitKeyTips();
        e.Handled = true;
    }

    private static string? TipLetter(KeyEventArgs e)
    {
        if (e.KeySymbol is { Length: > 0 } symbol && Fold(symbol) is { Length: > 0 } folded && char.IsLetterOrDigit(folded[0]))
            return folded[..1];
        if (e.Key is >= Key.A and <= Key.Z) return ((char)('A' + (e.Key - Key.A))).ToString();
        if (e.Key is >= Key.D0 and <= Key.D9) return ((char)('0' + (e.Key - Key.D0))).ToString();
        if (e.Key is >= Key.NumPad0 and <= Key.NumPad9) return ((char)('0' + (e.Key - Key.NumPad0))).ToString();
        return null;
    }

    /// <summary>Upper case with accents folded, so Č is C, as b-ribbon's letters.</summary>
    private static string Fold(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().ToUpperInvariant();
    }

    private const string TipAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>
    /// One letter per target, as b-ribbon assigns them: a declared key tip first, then the first free letter of the
    /// label, then any free letter or digit; at most 36 on a level.
    /// </summary>
    private static Dictionary<string, Control> AssignTips(IEnumerable<(Control El, string Label, string? Keytip)> entries, ISet<string> used)
    {
        var result = new Dictionary<string, Control>();
        var pending = new List<(Control El, string Label)>();
        foreach (var (el, label, keytip) in entries)
        {
            string? want = keytip is { Length: > 0 } ? Fold(keytip)[..1] : null;
            if (want is not null && TipAlphabet.Contains(want) && used.Add(want)) result[want] = el;
            else pending.Add((el, label));
        }
        foreach (var (el, label) in pending)
        {
            string? pick = Fold(label).Select(c => c.ToString()).FirstOrDefault(c => TipAlphabet.Contains(c) && !used.Contains(c))
                ?? TipAlphabet.Select(c => c.ToString()).FirstOrDefault(c => !used.Contains(c));
            if (pick is null) break; // 36 is the most a level can carry
            used.Add(pick);
            result[pick] = el;
        }
        return result;
    }

    private RibbonTab? ShownTab =>
        Tabs?.ToList() is { Count: > 0 } tabs && SelectedIndex >= 0 && SelectedIndex < tabs.Count ? tabs[SelectedIndex] : null;

    /// <summary>The label and declared letter of a ribbon stop, from its key.</summary>
    private (string Label, string? Keytip) DescribeStop(Control stop)
    {
        string name = global::Avalonia.Automation.AutomationProperties.GetName(stop) ?? "";
        if (stop.Tag is not string key || ShownTab is not { } tab) return (name, null);
        string? group = StopKeyGroup(key);
        var model = group is not null && int.TryParse(group, out int g) && g < tab.Groups.Count ? tab.Groups[g] : null;
        if (key.StartsWith("chunk:", System.StringComparison.Ordinal)) return (model?.Label ?? name, model?.Keytip);
        if (key.StartsWith("item:", System.StringComparison.Ordinal) && model is not null)
        {
            string id = key[(key.IndexOf('/') + 1)..];
            var item = model.Items.FirstOrDefault(i => i.Id == id);
            return (item?.Label ?? name, item?.Keytip);
        }
        return (name, null);
    }

    private void ShowKeyTips(TipLevel level, Control? scope = null)
    {
        ClearBadges();
        var used = new HashSet<string>();
        var entries = new List<(Control, string, string?)>();
        if (level == TipLevel.Tabs && _tabButtons is { } strip)
        {
            var tabs = Tabs?.ToList() ?? new List<RibbonTab>();
            for (int i = 0; i < strip.Children.Count && i < tabs.Count; i++)
                entries.Add((strip.Children[i], tabs[i].Label, tabs[i].Keytip));
            // Office numbers the Quick Access Toolbar (TASK-759): 1 to 9, kept off the tabs' letters.
            if (_qatDefaults is not null)
                for (int i = 0; i < _qatButtons.Count && i < 9; i++)
                {
                    string digit = ((char)('1' + i)).ToString();
                    used.Add(digit);
                    PlaceTip(digit, _qatButtons[i], centred: true);
                }
        }
        else if (level == TipLevel.Commands && (_groupsPanel ?? _revealPanel) is { } panel)
        {
            foreach (var stop in NavigableButtons(panel))
            {
                var (label, keytip) = DescribeStop(stop);
                entries.Add((stop, label, keytip));
            }
        }
        else if (level == TipLevel.Popup && _tipPopup is MenuFlyout menu)
        {
            foreach (var mi in menu.Items.OfType<MenuItem>().Where(m => m.IsEffectivelyEnabled))
                entries.Add((mi, mi.Header as string ?? "", mi.Tag as string));
        }
        else if (level == TipLevel.Popup && scope is not null)
        {
            foreach (var stop in NavigableButtons(scope))
            {
                var (label, keytip) = DescribeStop(stop);
                entries.Add((stop, label, keytip));
            }
        }

        foreach (var (letter, target) in AssignTips(entries, used)) PlaceTip(letter, target, centred: level == TipLevel.Tabs);
        _tipLevel = level;
    }

    private void PlaceTip(string letter, Control target, bool centred)
    {
        {
            _tipTargets[letter] = target;
            if (AdornerLayer.GetAdornerLayer(target) is not { } layer) return;
            var badge = Badge(letter, centred, target);
            AdornerLayer.SetAdornedElement(badge, target);
            // Unclipped: an adorner is clipped to its element by default, which cut off the half of the badge that sits
            // outside the command's corner (seen in the screenshot).
            AdornerLayer.SetIsClipEnabled(badge, false);
            layer.Children.Add(badge);
            _badges.Add((layer, badge));
            // Keys pressed while a popup's content has focus never pass the window's handlers: measured, a menu item's
            // visual root IS the window and the key reaches the item, yet the window's tunnel handler never saw it,
            // because the popup's route does not run through the window. So listen on the popup's own presenter.
            if (target.GetVisualAncestors().FirstOrDefault(a => a is FlyoutPresenter or MenuFlyoutPresenter) is Interactive presenter
                && _tipScopes.Add(presenter))
                presenter.AddHandler(KeyDownEvent, OnTipKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    /// <summary>
    /// A key tip badge: a dark square (the tooltip tokens, as the user picked on the web) with the letter, centred
    /// under a tab or on a command's bottom-right corner, half outside so it does not cover the label.
    /// </summary>
    private static Control Badge(string letter, bool centred, Control owner)
    {
        var text = new TextBlock
        {
            Text = letter,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Bind(TextBlock.ForegroundProperty, owner.GetResourceObservable("BTooltipTextBrush"));
        var badge = new Border
        {
            Child = text,
            IsHitTestVisible = false,
            HorizontalAlignment = centred ? HorizontalAlignment.Center : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = centred ? new Thickness(0, 0, 0, -8) : new Thickness(0, 0, -6, -6),
        };
        badge.Bind(Border.BackgroundProperty, owner.GetResourceObservable("BTooltipBgBrush"));
        badge.Bind(Border.CornerRadiusProperty, owner.GetResourceObservable("BRadiusSm"));
        badge.Bind(MinWidthProperty, owner.GetResourceObservable("BRibbonKeytipSize"));
        badge.Bind(MinHeightProperty, owner.GetResourceObservable("BRibbonKeytipSize"));
        badge.Bind(Border.PaddingProperty, owner.GetResourceObservable("BRibbonBadgePadding", v => v is double p ? new Thickness(p, 0) : default));
        return badge;
    }

    private void ActOnKeyTip(Control target)
    {
        // A tab: select it (bringing the panel up if it is not), then the letters of its commands.
        if (_tabButtons is { } strip && strip.Children.IndexOf(target) is var tabIndex and >= 0)
        {
            ClearBadges();
            SelectedIndex = tabIndex;
            if (!IsPinned) RevealTemporarily();
            else IsCollapsed = false;
            ShowAfterLayout(() => ShowKeyTips(TipLevel.Commands));
            return;
        }
        // A folded group or a menu: open it, then the letters inside.
        if (target is Button { Flyout: { } popup } trigger)
        {
            ClearBadges();
            popup.ShowAt(trigger);
            _tipPopup = popup;
            ShowAfterLayout(() => ShowKeyTips(TipLevel.Popup, popup is Flyout { Content: Control content } ? content : null));
            return;
        }
        if (target is MenuItem item)
        {
            var menu = _tipPopup;
            ExitKeyTips();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu?.Hide();
            return;
        }
        // A command: run it, the way a click does (the automation peer goes through the button's own OnClick).
        ExitKeyTips();
        if (target is Button button && ControlAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke) invoke.Invoke();
    }

    /// <summary>Badges are placed on laid-out controls, so a level that opens something waits for its layout.</summary>
    private static void ShowAfterLayout(System.Action show) =>
        Dispatcher.UIThread.Post(() => Dispatcher.UIThread.Post(show, DispatcherPriority.Loaded), DispatcherPriority.Loaded);

    private void StepBackKeyTips()
    {
        switch (_tipLevel)
        {
            case TipLevel.Popup:
                var popup = _tipPopup;
                ClearBadges();
                popup?.Hide();
                _tipPopup = null;
                ShowAfterLayout(() => ShowKeyTips(TipLevel.Commands));
                break;
            case TipLevel.Commands:
                ShowKeyTips(TipLevel.Tabs);
                break;
            default:
                ExitKeyTips();
                break;
        }
    }

    private void ClearBadges()
    {
        foreach (var (layer, badge) in _badges) layer.Children.Remove(badge);
        _badges.Clear();
        _tipTargets.Clear();
    }

    private void ExitKeyTips()
    {
        if (_tipLevel is null && _badges.Count == 0) return;
        ClearBadges();
        foreach (var scope in _tipScopes) scope.RemoveHandler(KeyDownEvent, OnTipKeyDown);
        _tipScopes.Clear();
        _tipPopup = null;
        _tipLevel = null;
    }
}
