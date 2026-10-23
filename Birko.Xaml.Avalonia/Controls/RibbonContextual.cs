using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Birko.Xaml.Core.Ribbon;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Contextual tabs (TASK-760, as b-ribbon's TASK-734): tabs that appear only while something is selected, like Office's
/// "Table Design". A tab names its set with <see cref="RibbonTab.ContextualSet"/>; the app declares the sets
/// (<see cref="SetContextualSets"/>) and shows or hides them. Shown sets come after the normal tabs, in declaration
/// order, each marked in its theme colour in one of the three looks the user kept (band, pill, edge).
/// </summary>
/// <remarks>
/// <see cref="Ribbon.SelectedIndex"/> counts the tabs that are SHOWN: normal ones first, so showing or hiding a set never
/// moves a normal tab, and the ribbon keeps the open tab open across a change by finding it again.
/// </remarks>
public partial class Ribbon
{
    public static readonly StyledProperty<RibbonContextualStyle> ContextualStyleProperty =
        AvaloniaProperty.Register<Ribbon, RibbonContextualStyle>(nameof(ContextualStyle));

    /// <summary>How shown sets are marked, unless a set says otherwise; band by default, as Office.</summary>
    public RibbonContextualStyle ContextualStyle
    {
        get => GetValue(ContextualStyleProperty);
        set => SetValue(ContextualStyleProperty, value);
    }

    private List<RibbonContextualSet> _ctxSets = new();
    private readonly HashSet<string> _ctxShown = new();
    private string? _lastNormalTab;
    private List<Button> _tabList = new();

    /// <summary>Declares the contextual sets: each one's name, colour and, optionally, its own style.</summary>
    public void SetContextualSets(IEnumerable<RibbonContextualSet> sets) =>
        ChangeShownTabs(() => _ctxSets = sets.ToList(), fallback: null);

    /// <summary>Shows a set's tabs after the normal ones; <paramref name="select"/> also opens its first tab.</summary>
    public void ShowContextual(string id, bool select = false)
    {
        ChangeShownTabs(() => _ctxShown.Add(id), fallback: null);
        if (!select) return;
        int first = VisibleTabs().FindIndex(t => t.ContextualSet == id);
        if (first >= 0) SelectedIndex = first;
    }

    /// <summary>
    /// Hides a set's tabs. If one of them was open, the ribbon goes back to the normal tab that was open before it (or the
    /// first tab), as b-ribbon does (decided by the user).
    /// </summary>
    public void HideContextual(string id)
    {
        if (!_ctxShown.Contains(id)) return;
        ChangeShownTabs(() => _ctxShown.Remove(id), fallback: _lastNormalTab);
    }

    public bool IsContextualShown(string id) => _ctxShown.Contains(id);

    /// <summary>The tabs the strip shows, in order: the normal ones, then each shown declared set's.</summary>
    private List<RibbonTab> VisibleTabs()
    {
        var all = Tabs?.ToList() ?? new List<RibbonTab>();
        var shown = all.Where(t => t.ContextualSet is null).ToList();
        foreach (var set in _ctxSets.Where(s => _ctxShown.Contains(s.Id)))
            shown.AddRange(all.Where(t => t.ContextualSet == set.Id));
        return shown;
    }

    /// <summary>Applies a change to which tabs show, keeping the open tab open (or going to <paramref name="fallback"/>).</summary>
    private void ChangeShownTabs(System.Action change, string? fallback)
    {
        var before = VisibleTabs();
        string? open = SelectedIndex >= 0 && SelectedIndex < before.Count ? before[SelectedIndex].Id : null;
        change();
        var after = VisibleTabs();
        int index = after.FindIndex(t => t.Id == open);
        if (index < 0) index = System.Math.Max(0, after.FindIndex(t => t.Id == fallback));
        if (index != SelectedIndex) SelectedIndex = index; // rebuilds
        else Rebuild();
    }

    /// <summary>
    /// Puts the tab buttons in the strip: a normal tab directly, a contextual one inside its set's mark. The set's name
    /// is decoration; each of its tabs carries it as help text, which is what a screen reader announces with the tab.
    /// </summary>
    private void ArrangeTabs(Panel strip, List<RibbonTab> tabs, List<Button> buttons, int selected)
    {
        _tabList = buttons;
        for (int i = 0; i < tabs.Count;)
        {
            if (tabs[i].ContextualSet is not { } setId || _ctxSets.FirstOrDefault(s => s.Id == setId) is not { } set)
            {
                strip.Children.Add(buttons[i++]);
                continue;
            }
            var members = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            string color = set.Color.ToString();
            for (; i < tabs.Count && tabs[i].ContextualSet == setId; i++)
            {
                var button = buttons[i];
                global::Avalonia.Automation.AutomationProperties.SetHelpText(button, set.Label);
                button.Bind(ForegroundProperty, button.GetResourceObservable($"BColor{color}TextBrush"));
                if (i == selected)
                {
                    // The open contextual tab is underlined in the set's colour, as the web's; its text is already coloured.
                    button.BorderThickness = new Thickness(0, 0, 0, 2);
                    button.CornerRadius = default; // a straight line, not the button's rounded corners curling up
                    button.Bind(BorderBrushProperty, button.GetResourceObservable($"BColor{color}Brush"));
                }
                members.Children.Add(button);
            }
            strip.Children.Add(ContextualMark(set, members));
        }
    }

    private Control ContextualMark(RibbonContextualSet set, Control members)
    {
        string color = set.Color.ToString();
        string tint = set.Color == RibbonContextualColor.Primary ? "BColorPrimaryLightBrush" : $"BColor{color}AlphaBgBrush";
        string frame = set.Color == RibbonContextualColor.Primary ? "BColorPrimaryBrush" : $"BColor{color}AlphaBorderBrush";
        var name = new TextBlock { Text = set.Label, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        name.Bind(TextBlock.FontSizeProperty, name.GetResourceObservable("BTextXs"));
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable($"BColor{color}TextBrush"));
        var mark = new Border { Margin = new Thickness(4, 0, 0, 0), Tag = $"ctx:{set.Id}" };
        global::Avalonia.Automation.AutomationProperties.SetAccessibilityView(name, global::Avalonia.Automation.AccessibilityView.Raw);

        switch (set.Style ?? ContextualStyle)
        {
            case RibbonContextualStyle.Band:
                mark.Bind(Border.BackgroundProperty, mark.GetResourceObservable(tint));
                mark.Bind(Border.BorderBrushProperty, mark.GetResourceObservable($"BColor{color}Brush"));
                mark.Bind(Border.BorderThicknessProperty, mark.GetResourceObservable("BRibbonContextAccent", v => v is double d ? new Thickness(0, d, 0, 0) : default));
                name.Margin = new Thickness(8, 2, 8, 0);
                mark.Child = new StackPanel { Children = { name, members } };
                break;
            case RibbonContextualStyle.Pill:
                var pill = new Border { Child = name, Padding = new Thickness(8, 2), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9999), VerticalAlignment = VerticalAlignment.Center };
                pill.Bind(Border.BackgroundProperty, pill.GetResourceObservable(tint));
                pill.Bind(Border.BorderBrushProperty, pill.GetResourceObservable(frame));
                mark.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { pill, members } };
                break;
            default:
                mark.Bind(Border.BorderBrushProperty, mark.GetResourceObservable($"BColor{color}Brush"));
                mark.Bind(Border.BorderThicknessProperty, mark.GetResourceObservable("BRibbonContextAccent", v => v is double d ? new Thickness(0, d, 0, 0) : default));
                mark.Child = members;
                break;
        }
        return mark;
    }
}
