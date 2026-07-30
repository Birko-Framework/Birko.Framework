using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-036: the Ribbon (b-ribbon / BAppShell chrome).</summary>
public class RibbonTests
{
    private static Ribbon Build(out bool[] ran)
    {
        var flags = new bool[1];
        var home = new RibbonTab
        {
            Id = "home", Label = "Home",
            Groups = new[]
            {
                new RibbonGroup { Label = "Clipboard", Items = new[]
                {
                    new RibbonItem { Id = "paste", Label = "Paste", Icon = "\U0001F4CB", Run = () => flags[0] = true },
                    new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂" },
                }},
            },
        };
        var view = new RibbonTab
        {
            Id = "view", Label = "View",
            Groups = new[] { new RibbonGroup { Label = "Zoom", Items = new[] { new RibbonItem { Id = "zin", Label = "Zoom In" } } } },
        };
        ran = flags;
        return new Ribbon { Tabs = new[] { home, view } };
    }

    private static T Show<T>(T control, double width = 700) where T : Control =>
        Show(control, out _, width);

    /// <summary>
    /// Show the control and hand back its window. Keyboard tests must raise at the WINDOW, not the control:
    /// the ribbon's shortcuts are window shortcuts, and a test that raises straight at the control proves the
    /// handler runs while saying nothing about whether a keystroke can reach it. That is exactly how a
    /// non-functional Ctrl+F1 and a dead Escape both shipped "covered".
    /// </summary>
    private static T Show<T>(T control, out Window window, double width = 700) where T : Control
    {
        window = new Window { Content = control, Width = width, Height = 200 };
        window.Show();
        window.Measure(new Size(width, 200));
        window.Arrange(new Rect(0, 0, width, 200));
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static void PressKey(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A ribbon with far more tabs and groups than fit a narrow window (TASK-097 overflow).</summary>
    private static Ribbon BuildCrowded()
    {
        RibbonItem I(string label) => new() { Id = label, Label = label, Icon = "●" };
        RibbonGroup G(string label) => new() { Label = label, Items = new[] { I(label + " A"), I(label + " B") } };

        var home = new RibbonTab
        {
            Id = "home", Label = "Home",
            Groups = new[] { G("Clipboard"), G("Records"), G("Layout"), G("Styles"), G("Review"), G("Export") },
        };
        var others = new[] { "Insert", "Design", "Transitions", "Animations", "SlideShow", "Review", "View", "Developer" }
            .Select(n => new RibbonTab { Id = n.ToLowerInvariant(), Label = n, Groups = new[] { G(n) } });

        return new Ribbon { Tabs = new[] { home }.Concat(others).ToArray() };
    }

    /// <summary>The scroller whose content holds <paramref name="text"/> — tab strip vs groups row.</summary>
    private static ScrollViewer ScrollerContaining(Control root, string text) =>
        root.GetVisualDescendants().OfType<ScrollViewer>()
            .First(s => s.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    private static Button ChevronByTip(Control root, string tip) =>
        root.GetVisualDescendants().OfType<Button>()
            .First(b => (b.GetValue(ToolTip.TipProperty) as string) == tip);

    /// <summary>
    /// A chevron is *shown* only when it is both in the layout and active. Once a row overflows, both
    /// slots stay in the layout (reserved) so the row never reflows; only opacity / hit-testing change.
    /// So IsVisible alone no longer answers "can the user see and click this".
    /// </summary>
    private static bool IsShown(Button chevron) =>
        chevron.IsVisible && chevron.Opacity > 0 && chevron.IsHitTestVisible;

    /// <summary>In the layout at all — i.e. holding its slot open, visible or not.</summary>
    private static bool IsReserved(Button chevron) => chevron.IsVisible;

    /// <summary>The scaling panel. Internal to the library, so resolved by type name rather than a cast.</summary>
    private static Layoutable PanelOf(Control ribbon) =>
        (Layoutable)ribbon.GetVisualDescendants().First(v => v.GetType().Name == "RibbonGroupsPanel");

    private static IEnumerable<string?> Texts(Control c) =>
        c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    /// <summary>
    /// Text inside the ribbon's open overlay (the narrow menu, or an unpinned temporary reveal).
    /// </summary>
    /// <remarks>
    /// A <c>Popup</c> hosts its child in a separate visual root, so <c>GetVisualDescendants</c> on the ribbon
    /// never reaches it. The ribbon sets itself as the popup's LOGICAL parent, which is what makes the
    /// content reachable from a test at all.
    /// </remarks>
    private static IEnumerable<Control> OverlayControls(Ribbon ribbon) =>
        ribbon.OpenOverlay is { } overlay
            ? overlay.GetVisualDescendants().OfType<Control>().Prepend(overlay)
            : Enumerable.Empty<Control>();

    private static IEnumerable<string?> OverlayTexts(Ribbon ribbon) =>
        OverlayControls(ribbon).OfType<TextBlock>().Select(t => t.Text);

    [AvaloniaFact]
    public void Renders_tabs_and_active_group_items()
    {
        var ribbon = Show(Build(out _));
        Texts(ribbon).Should().Contain("Home");
        Texts(ribbon).Should().Contain("View");
        Texts(ribbon).Should().Contain("Clipboard");
        Texts(ribbon).Should().Contain("Paste");
    }

    [AvaloniaFact]
    public void Clicking_an_item_runs_its_action()
    {
        var ribbon = Show(Build(out var ran));
        var paste = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Paste"));
        paste.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ran[0].Should().BeTrue();
    }

    [AvaloniaFact]
    public void Switching_tab_shows_the_other_group()
    {
        var ribbon = Build(out _);
        Show(ribbon);
        Texts(ribbon).Should().Contain("Clipboard");

        ribbon.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Texts(ribbon).Should().Contain("Zoom");
        Texts(ribbon).Should().NotContain("Clipboard", "the Home tab's groups are no longer shown");
    }

    [AvaloniaFact]
    public void Collapsing_hides_the_groups_but_keeps_the_tab_strip()
    {
        var ribbon = Show(Build(out _));
        Texts(ribbon).Should().Contain("Clipboard", "groups show when expanded");

        ribbon.IsCollapsed = true;
        Dispatcher.UIThread.RunJobs();

        Texts(ribbon).Should().NotContain("Clipboard", "groups are hidden when collapsed");
        Texts(ribbon).Should().Contain("Home", "the tab strip stays visible");
    }

    [AvaloniaFact]
    public void Clicking_the_active_tab_toggles_collapse()
    {
        var ribbon = Show(Build(out _)); // Home (index 0) is active
        var home = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Home"));

        home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ribbon.IsCollapsed.Should().BeTrue("clicking the active tab collapses the ribbon");
    }

    // ── TASK-097: overflow must stay reachable at a narrow width ──────────────────

    [AvaloniaFact]
    public void Tab_strip_scrolls_when_the_tabs_overflow()
    {
        var ribbon = Show(BuildCrowded(), width: 320);
        var scroller = ScrollerContaining(ribbon, "Home");

        scroller.Extent.Width.Should().BeGreaterThan(scroller.Viewport.Width,
            "9 tabs cannot fit 320px, so the strip must be scrollable rather than clipped");
        IsShown(ChevronByTip(ribbon, "Scroll tabs right")).Should().BeTrue(
            "an invisible overflow is the defect — there must be a visible affordance");
    }

    [AvaloniaFact]
    public void The_groups_row_degrades_instead_of_scrolling()
    {
        // The design rule on record: the ribbon BODY resizes, it never scrolls. A scroll offset destroys
        // the spatial memory the ribbon exists to provide, so there must be no groups scroller at all --
        // only the tab strip scrolls (the deliberate exception).
        var ribbon = Show(BuildCrowded(), width: 320);

        ribbon.GetVisualDescendants().OfType<ScrollViewer>()
            .Should().HaveCount(1, "only the tab strip scrolls; the groups row degrades instead");

        ribbon.ResolvedGroupSizes.Should().NotBeEmpty();
        ribbon.ResolvedGroupSizes.Should().NotContain(RibbonGroupSize.Large,
            "6 groups cannot sit at their roomiest in 320px");
    }

    [AvaloniaFact]
    public void Clicking_the_chevron_scrolls_and_reveals_the_back_chevron()
    {
        var ribbon = Show(BuildCrowded(), width: 320);
        var scroller = ScrollerContaining(ribbon, "Home");
        var right = ChevronByTip(ribbon, "Scroll tabs right");
        var left = ChevronByTip(ribbon, "Scroll tabs left");

        IsShown(left).Should().BeFalse("nothing is scrolled off to the left yet");
        IsReserved(left).Should().BeTrue("but its slot is held open, so revealing it cannot reflow the row");

        double viewportBefore = scroller.Viewport.Width;
        double rightEdgeBefore = right.Bounds.X;

        right.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        scroller.Offset.X.Should().BeGreaterThan(0);
        IsShown(left).Should().BeTrue("scrolled content to the left is now reachable back");

        // The stability property: activating the back chevron must not reflow the row, or the forward
        // chevron moves out from under the pointer mid-click — the defect reported on the web side.
        scroller.Viewport.Width.Should().BeApproximately(viewportBefore, 0.5,
            "both slots were already reserved, so nothing resized");
        right.Bounds.X.Should().BeApproximately(rightEdgeBefore, 0.5, "the click target did not move");
    }

    [AvaloniaFact]
    public void Selecting_a_scrolled_tab_does_not_snap_the_strip_back_to_the_start()
    {
        // Reported from the gallery: scroll the strip to reach a far-right tab, click it — the tab opens
        // correctly but the strip jumps back to the first tab, so the tab just picked is off-screen.
        // Rebuild() discards the tree, so the new ScrollViewer started at offset 0 every time.
        var ribbon = Show(BuildCrowded(), width: 320);
        var scroller = ScrollerContaining(ribbon, "Home");

        var right = ChevronByTip(ribbon, "Scroll tabs right");
        for (int i = 0; i < 6; i++) // scroll to the far end
        {
            right.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        double scrolledTo = scroller.Offset.X;
        scrolledTo.Should().BeGreaterThan(0, "precondition: the strip is scrolled away from the start");

        var lastTab = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Developer"));
        lastTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Rebuild() replaced the tree, so re-resolve the scroller rather than reusing the stale one.
        var after = ScrollerContaining(ribbon, "Home");
        after.Offset.X.Should().BeApproximately(scrolledTo, 1.0,
            "the strip must stay where the user left it across the rebuild");
    }

    [AvaloniaFact]
    public void A_selection_made_from_offscreen_is_scrolled_into_view()
    {
        // The other half: restoring the offset alone would leave a keyboard/programmatic selection
        // off-screen, so a changed selection is brought into view (a no-op after a click, since the tab
        // clicked is already visible).
        var ribbon = Show(BuildCrowded(), width: 320);

        ribbon.SelectedIndex = 8; // "Developer" — the last tab, off-screen at 320px
        Dispatcher.UIThread.RunJobs();

        var scroller = ScrollerContaining(ribbon, "Home");
        scroller.Offset.X.Should().BeGreaterThan(0,
            "selecting an off-screen tab must scroll it into view, not leave it hidden");
    }

    [AvaloniaFact]
    public void No_chevrons_and_no_vertical_scrollbar_when_everything_fits()
    {
        var ribbon = Show(Build(out _), width: 900);

        IsReserved(ChevronByTip(ribbon, "Scroll tabs right")).Should().BeFalse("a row that fits reserves no slots");
        IsReserved(ChevronByTip(ribbon, "Scroll tabs left")).Should().BeFalse();
        ribbon.GetVisualDescendants().OfType<Button>()
            .Should().NotContain(b => (b.GetValue(ToolTip.TipProperty) as string) == "Scroll groups right",
                "the groups row has no scroller at all — it degrades instead");

        ribbon.GetVisualDescendants().OfType<ScrollViewer>().Should()
            .OnlyContain(s => s.VerticalScrollBarVisibility == global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                "a vertical bar would change the ribbon's height with the window width");
    }

    [AvaloniaFact]
    public void Collapse_chevron_stays_outside_the_tab_scroller()
    {
        var ribbon = Show(BuildCrowded(), width: 320);
        var tabScroller = ScrollerContaining(ribbon, "Home");

        tabScroller.GetVisualDescendants().OfType<Button>()
            .Should().NotContain(b => (b.GetValue(ToolTip.TipProperty) as string) == "Collapse the ribbon",
                "the collapse chevron must stay pinned and never scroll out of reach");
    }

    // ── TASK-098: the new model fields must not change rendering ──────────────────

    [AvaloniaFact]
    public void The_group_icon_reaches_the_collapsed_chunk_button()
    {
        // Supersedes the TASK-098 "the new fields change nothing visible" guard, deliberately: they DO
        // change something now that TASK-099/100 consume them. RibbonGroup.Icon exists precisely to label a
        // group once it collapses, so this asserts it arrives there.
        var group = new RibbonGroup
        {
            Label = "Clipboard",
            Icon = "📋",
            ScalingPriority = 10,
            MinSize = RibbonGroupSize.Popup,
            Items = new[] { new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂" } },
        };
        var ribbon = Show(new Ribbon
        {
            PreferredGroupSize = RibbonGroupSize.Popup,
            Tabs = new[] { new RibbonTab { Id = "h", Label = "Home", Groups = new[] { group } } },
        });

        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Popup);
        Texts(ribbon).Should().Contain("📋", "the group icon labels its chunk button");
        Texts(ribbon).Should().Contain("Clipboard ⌄", "and the chunk button signals that it opens a flyout");
    }

    // ── TASK-099: progressive group scaling ───────────────────────────────────────

    /// <summary>Two groups with differing importance, so priority order is observable.</summary>
    private static Ribbon BuildPrioritised()
    {
        RibbonItem I(string label) => new() { Id = label, Label = label, Icon = "●" };
        RibbonGroup G(string label, int priority) => new()
        {
            Label = label,
            ScalingPriority = priority,
            Items = new[] { I(label + " One"), I(label + " Two"), I(label + " Three") },
        };

        return new Ribbon
        {
            Tabs = new[]
            {
                new RibbonTab { Id = "home", Label = "Home", Groups = new[] { G("Clipboard", 10), G("Export", 0) } },
            },
        };
    }

    [AvaloniaFact]
    public void The_degraded_row_actually_fits_so_no_group_is_clipped()
    {
        // Reported from the gallery: narrowing degrades the groups, but the rightmost one (Export) is
        // simply not visible. The variants were being MEASURED WHILE INVISIBLE, and Avalonia's MeasureCore
        // short-circuits for IsVisible == false — so every non-chosen variant reported zero width, tighter
        // variants looked free, the pass under-degraded, and the row overflowed its slot and got clipped.
        //
        // Asserting the resolved variants was not enough to catch that: the decision looked plausible while
        // resting on garbage widths. What matters is that the row FITS.
        // The threshold is COMPUTED, not guessed. Twice now a hardcoded width demanded the impossible: the
        // row's true minimum is whatever an all-Popup row measures, and that depends on the group LABELS
        // (a chunk button shows its group's name, as in Office), so it moves with the demo data. Deriving it
        // keeps the test honest about what the layout can actually promise.
        var probe = new Ribbon { PreferredGroupSize = RibbonGroupSize.Popup, Tabs = BuildCrowded().Tabs };
        Show(probe, 2000);
        double minimum = System.Math.Ceiling(PanelOf(probe).DesiredSize.Width);

        foreach (double width in new[] { 2000d, 1400d, 900d, 640d, minimum })
        {
            var ribbon = Show(BuildCrowded(), width);

            PanelOf(ribbon).DesiredSize.Width.Should().BeLessThanOrEqualTo(width + 0.5,
                $"at {width}px (minimum {minimum}px) the groups must degrade until the row fits — anything "
                + "wider is clipped, which is the same 'commands you cannot reach' defect this story removes");
        }
    }

    [AvaloniaFact]
    public void At_the_extreme_the_chunk_buttons_drop_their_group_names_and_the_row_still_fits()
    {
        // The last resort. A labelled chunk button cannot be narrower than its group's NAME, which put the
        // six-group minimum around 500px — everything below that clipped. Dropping the name takes it to
        // roughly half that. The name stays in the tooltip, so no command becomes anonymous: the same trade
        // the Small variant already makes for items.
        // Both minima are MEASURED, not guessed — I got a hardcoded threshold wrong three times in this
        // file, because every one of them depends on the demo's label text.
        var labelledProbe = new Ribbon { PreferredGroupSize = RibbonGroupSize.Popup, Tabs = BuildCrowded().Tabs };
        Show(labelledProbe, 2000); // roomy, so the chunks keep their names
        double labelledMinimum = PanelOf(labelledProbe).DesiredSize.Width;

        // A width comfortably below what a labelled row can manage — i.e. one that used to clip.
        double target = System.Math.Ceiling(labelledMinimum * 0.6);
        var ribbon = Show(BuildCrowded(), target);

        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Popup);
        PanelOf(ribbon).DesiredSize.Width.Should().BeLessThanOrEqualTo(target + 0.5,
            $"at {target}px — 60% of the {labelledMinimum}px a labelled row needs — the names come off and "
            + "the row fits, where before it overflowed and the last group was unreachable");

        // The labelled chunks are still in the tree (parked, so they stay measurable), so assert on what is
        // actually SHOWN. Identified by position, not IsHitTestVisible: that flag is set on the wrapping
        // Border, so reading it off the inner Button matches both copies.
        var chunks = ribbon.GetVisualDescendants().OfType<Button>()
            .Where(b => (b.GetValue(ToolTip.TipProperty) as string) == "Export")
            .ToList();
        chunks.Should().HaveCount(2, "both the labelled and the compact chunk exist; one of them is parked");

        var shown = chunks
            .Where(b => b.TranslatePoint(default, ribbon) is { X: > -1000 })
            .ToList();

        shown.Should().HaveCount(1, "exactly one chunk button per collapsed group is on-screen");
        Texts(shown[0]).Should().NotContain("Export ⌄", "the name is dropped from the face of the button");
        (shown[0].GetValue(ToolTip.TipProperty) as string).Should().Be("Export",
            "but it stays reachable as a tooltip, so the button is not anonymous");
    }

    [AvaloniaFact]
    public void Below_the_narrowest_possible_row_every_group_is_at_its_floor()
    {
        // The honest limit. Six groups at Popup are still ~500px (a chunk button has a minimum width and
        // the groups have gaps), so below that the row cannot fit however hard the pass tries. What must
        // hold is that it TRIED everything — nothing is left roomier than it had to be.
        //
        // Office's answer below the minimum is its fourth mechanism: hide the body entirely and fall back
        // to tabs-only. We have that state (IsCollapsed) but only as a manual toggle, never automatic. Left
        // unimplemented deliberately — auto-collapsing a ribbon is a behaviour change that deserves its own
        // decision rather than being smuggled in with a scaling fix.
        var ribbon = Show(BuildCrowded(), width: 360);

        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Popup,
            "the pass degraded everything as far as it could");
    }

    [AvaloniaFact]
    public void Groups_sit_at_Medium_by_default_when_there_is_room()
    {
        var ribbon = Show(BuildPrioritised(), width: 1400);
        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Medium,
            "Medium is the default look, so an existing app's ribbon does not change height on upgrade");
    }

    [AvaloniaFact]
    public void Large_is_available_as_an_opt_in()
    {
        var ribbon = BuildPrioritised();
        ribbon.PreferredGroupSize = RibbonGroupSize.Large;
        Show(ribbon, width: 1400);
        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Large);
    }

    [AvaloniaFact]
    public void The_least_important_group_degrades_first_as_the_window_narrows()
    {
        var ribbon = BuildPrioritised(); // Clipboard priority 10, Export priority 0
        ribbon.PreferredGroupSize = RibbonGroupSize.Large;
        Show(ribbon, width: 320);

        var sizes = ribbon.ResolvedGroupSizes;
        sizes.Should().HaveCount(2);
        ((int)sizes[1]).Should().BeGreaterThan((int)sizes[0],
            "Export is the least important, so it must be tighter than Clipboard — degrading uniformly " +
            "would leave both the same and is the failure this pass exists to avoid");
    }

    [AvaloniaFact]
    public void Widening_promotes_the_groups_back_up()
    {
        var ribbon = BuildPrioritised();
        ribbon.PreferredGroupSize = RibbonGroupSize.Large;
        var window = new Window { Content = ribbon, Width = 320, Height = 240 };
        window.Show();

        static void LayoutAt(Window w, double width)
        {
            w.Width = width;
            w.Measure(new Size(width, 240));
            w.Arrange(new Rect(0, 0, width, 240));
            Dispatcher.UIThread.RunJobs();
        }

        LayoutAt(window, 320);
        // Not "nothing is Large" — with only two groups the important one legitimately keeps Large while
        // the incidental one gives way. The point is that SOMETHING degraded.
        ribbon.ResolvedGroupSizes.Should().Contain(size => size != RibbonGroupSize.Large,
            "320px is not enough for both groups at their roomiest");

        LayoutAt(window, 1400);
        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Large, "room again at 1400px");
    }

    [AvaloniaFact]
    public void The_same_width_yields_the_same_variants_whichever_direction_it_was_reached_from()
    {
        // The stability criterion, end to end through the real measure pass rather than just the policy.
        var ribbon = BuildPrioritised();
        ribbon.PreferredGroupSize = RibbonGroupSize.Large;
        var window = new Window { Content = ribbon, Width = 900, Height = 240 };
        window.Show();

        var seen = new Dictionary<double, string>();
        void Visit(double width)
        {
            // Settled state, not the first frame. The row sits in a ScrollViewer (the interim last resort
            // until TASK-100), whose viewport width is only known once a pass has run — so the decision
            // lands one pass later. Imperceptible while dragging, but a single Measure/Arrange would read a
            // stale variant set and make this assertion about frame timing rather than about determinism.
            for (int pass = 0; pass < 3; pass++)
            {
                window.Width = width;
                window.Measure(new Size(width, 240));
                window.Arrange(new Rect(0, 0, width, 240));
                Dispatcher.UIThread.RunJobs();
            }
            string key = string.Join(",", ribbon.ResolvedGroupSizes);
            if (seen.TryGetValue(width, out var previous))
                key.Should().Be(previous, $"width {width} must resolve identically in both directions");
            else
                seen[width] = key;
        }

        for (double w = 900; w >= 300; w -= 50) Visit(w);
        for (double w = 300; w <= 900; w += 50) Visit(w);
    }

    [AvaloniaFact]
    public void Small_drops_the_labels_but_keeps_the_name_in_a_tooltip()
    {
        // An icon-only command with no tooltip is unidentifiable — that would trade "unreachable" for
        // "unnameable", so the tooltip is part of the variant, not a nicety.
        var ribbon = BuildPrioritised();
        ribbon.PreferredGroupSize = RibbonGroupSize.Small;
        Show(ribbon, width: 1400);

        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Small);

        // Only inspect the VISIBLE variant: all three are in the tree by design.
        var visibleItemButtons = ribbon.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsVisible && ToolTip.GetTip(b) is string tip && tip.StartsWith("Clipboard "))
            .ToList();

        visibleItemButtons.Should().NotBeEmpty("every Small item carries its label as a tooltip");
        visibleItemButtons.Should().OnlyContain(
            b => b.GetVisualDescendants().OfType<TextBlock>().Count() == 1,
            "a Small item draws its icon only — no label TextBlock");
    }

    // ── TASK-101: pinned vs temporary reveal ──────────────────────────────────────

    [AvaloniaFact]
    public void Pinned_is_the_default_so_an_existing_app_is_unaffected()
    {
        var ribbon = Show(Build(out _));
        ribbon.IsPinned.Should().BeTrue();
        Texts(ribbon).Should().Contain("Clipboard", "the body is in the layout, exactly as before");
    }

    [AvaloniaFact]
    public void Unpinned_keeps_the_body_out_of_the_layout()
    {
        // The whole point of unpinned: the body must not push page content down. So it is not in the tree
        // at all until a tab is clicked, and then only as an overlay.
        var ribbon = Build(out _);
        ribbon.IsPinned = false;
        Show(ribbon);

        Texts(ribbon).Should().Contain("Home", "the tab strip stays");
        Texts(ribbon).Should().NotContain("Clipboard", "but the groups row is not in flow");
        ribbon.ResolvedGroupSizes.Should().BeEmpty("there is no in-flow groups row to report on");
    }

    [AvaloniaFact]
    public void Clicking_a_tab_while_unpinned_does_not_expand_the_ribbon_permanently()
    {
        // Office: "Show Tabs" is a mode you leave by pinning, not by clicking a tab. Before TASK-101 this
        // set IsCollapsed = false and the ribbon stayed open for good.
        var ribbon = Build(out _);
        ribbon.IsPinned = false;
        ribbon.IsCollapsed = true;
        Show(ribbon);

        var view = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "View"));
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        ribbon.SelectedIndex.Should().Be(1, "the tab still becomes active");
        ribbon.IsCollapsed.Should().BeTrue("but the reveal is temporary — the ribbon is still minimised");
    }

    [AvaloniaFact]
    public void Ctrl_F1_toggles_collapse()
    {
        var ribbon = Show(Build(out _), out var window);
        bool before = ribbon.IsCollapsed;

        // At the WINDOW, because that is where the keystroke actually arrives. The ribbon is a
        // ContentControl and never holds focus, so an OnKeyDown override could not have worked.
        PressKey(window, Key.F1, KeyModifiers.Control);

        ribbon.IsCollapsed.Should().Be(!before, "Ctrl+F1 is the shortcut users try, and Office has it");

        PressKey(window, Key.F1, KeyModifiers.Control);
        ribbon.IsCollapsed.Should().Be(before, "and it toggles back");
    }

    // ── TASK-102: the narrow fallback ─────────────────────────────────────────────

    [AvaloniaFact]
    public void Below_the_narrow_threshold_the_ribbon_becomes_a_menu()
    {
        // Scaling has nothing left to give this far down, so drawing a tab strip and a groups row would only
        // clip them. b-ribbon has done this below 48rem all along; the XAML skin never had it (TASK-102).
        var ribbon = Show(BuildCrowded(), width: 200);

        Texts(ribbon).Should().Contain("☰", "the hamburger replaces the chrome");
        Texts(ribbon).Should().Contain("Home", "alongside the active tab's name");
        Texts(ribbon).Should().NotContain("Clipboard", "no groups row is drawn");
        ribbon.GetVisualDescendants().OfType<ScrollViewer>()
            .Should().BeEmpty("and no tab scroller either — there is no tab strip to scroll");
    }

    [AvaloniaFact]
    public void Above_the_narrow_threshold_nothing_changes()
    {
        var ribbon = Show(BuildCrowded(), width: 900);

        Texts(ribbon).Should().NotContain("☰");
        Texts(ribbon).Should().Contain("Clipboard", "the normal scaling ribbon is intact");
        ribbon.ResolvedGroupSizes.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void The_narrow_menu_reaches_every_command_across_every_tab()
    {
        // The guarantee the whole story rests on: no width makes a command unreachable. At this size the
        // menu is the only route, so it has to carry everything — not just the active tab.
        var ran = false;
        var ribbon = new Ribbon
        {
            Tabs = new[]
            {
                new RibbonTab { Id = "home", Label = "Home", Groups = new[]
                    { new RibbonGroup { Label = "Clipboard", Items = new[] { new RibbonItem { Id = "cut", Label = "Cut" } } } } },
                new RibbonTab { Id = "view", Label = "View", Groups = new[]
                    { new RibbonGroup { Label = "Zoom", Items = new[]
                        { new RibbonItem { Id = "zin", Label = "Zoom In", Run = () => ran = true } } } } },
            },
        };
        Show(ribbon, width: 200);

        var burger = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "☰"));
        burger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var entries = OverlayTexts(ribbon).ToList();
        entries.Should().Contain("Cut", "the inactive tab's commands are in the menu too");
        entries.Should().Contain("Zoom In");

        // And invoking one runs the same handler the ribbon would have.
        var zoom = OverlayControls(ribbon).OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("Zoom In")));
        zoom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ran.Should().BeTrue();
    }

    [AvaloniaFact]
    public void An_unpinned_tab_click_reveals_the_groups_as_an_overlay()
    {
        // Closes the other half of TASK-101: not just "IsCollapsed is untouched", but that the body really
        // does appear — over the page, not in it.
        var ribbon = Build(out _);
        ribbon.IsPinned = false;
        ribbon.IsCollapsed = true;
        Show(ribbon);

        ribbon.OpenOverlay.Should().BeNull("nothing is revealed until a tab is clicked");

        var view = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "View"));
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        ribbon.OpenOverlay.Should().NotBeNull("the reveal is an overlay, so it is not in the ribbon's own tree");
        OverlayTexts(ribbon).Should().Contain("Zoom", "and it holds the newly-selected tab's groups");
        Texts(ribbon).Should().NotContain("Zoom", "while the ribbon itself still has no in-flow body");
    }

    [AvaloniaFact]
    public void A_collapsed_group_flyout_runs_its_command_and_dismisses()
    {
        // TASK-100's last unchecked criterion. Previously untestable: a Popup's child lives in its own
        // visual root, so there was no route to it until OpenOverlay existed.
        bool ran = false;
        var group = new RibbonGroup
        {
            Label = "Clipboard",
            Icon = "📋",
            Items = new[] { new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂", Run = () => ran = true } },
        };
        var ribbon = new Ribbon
        {
            PreferredGroupSize = RibbonGroupSize.Popup,
            Tabs = new[] { new RibbonTab { Id = "h", Label = "Home", Groups = new[] { group } } },
        };
        Show(ribbon, width: 900);

        ribbon.ResolvedGroupSizes.Should().AllBeEquivalentTo(RibbonGroupSize.Popup);

        var chunk = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => (b.GetValue(ToolTip.TipProperty) as string) == "Clipboard" && b.IsHitTestVisible);
        chunk.Flyout.Should().NotBeNull("the chunk button opens the group's items in a flyout");

        chunk.Flyout!.ShowAt(chunk);
        Dispatcher.UIThread.RunJobs();

        var flyoutContent = (Control)((Flyout)chunk.Flyout!).Content!;
        var item = flyoutContent.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Cut"));
        item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        ran.Should().BeTrue("invoking from the flyout runs the same handler as the uncollapsed item");
    }

    [AvaloniaFact]
    public void Escape_closes_the_narrow_menu()
    {
        // Reported from the gallery: Escape did nothing. A raw Popup does not handle it —
        // IsLightDismissEnabled is pointer-only, and Escape handling lives in FlyoutBase, which a Popup is
        // not. Raised at the WINDOW, because that is where a keystroke actually arrives.
        var ribbon = Show(BuildCrowded(), out var window, width: 200);

        var burger = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "☰"));
        burger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        ribbon.OpenOverlay.Should().NotBeNull("precondition: the menu is open");

        PressKey(window, Key.Escape);

        ribbon.OpenOverlay.Should().BeNull("Escape closes the menu");
    }

    [AvaloniaFact]
    public void Escape_closes_an_unpinned_temporary_reveal_too()
    {
        var ribbon = Build(out _);
        ribbon.IsPinned = false;
        ribbon.IsCollapsed = true;
        Show(ribbon, out var window);

        var view = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "View"));
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        ribbon.OpenOverlay.Should().NotBeNull("precondition: the reveal is showing");

        PressKey(window, Key.Escape);

        ribbon.OpenOverlay.Should().BeNull("Escape dismisses a temporary reveal as well as the menu");
        ribbon.IsCollapsed.Should().BeTrue("and the ribbon stays minimised, as it was");
    }

    [AvaloniaFact]
    public void Clicking_away_dismisses_the_temporary_reveal()
    {
        // Reported from the gallery: it did not. Popup.IsLightDismissEnabled had already failed to deliver
        // Escape; it does not deliver click-away either, so the ribbon now owns dismissal outright.
        // Driven through the headless input pipeline rather than by raising a synthetic event, because the
        // whole class of bug here is "the handler works but nothing reaches it".
        var ribbon = Build(out _);
        ribbon.IsPinned = false;
        ribbon.IsCollapsed = true;

        var page = new StackPanel();
        var below = new Button { Content = "Something else on the page", Height = 40 };
        page.Children.Add(ribbon);
        page.Children.Add(below);

        var window = new Window { Content = page, Width = 700, Height = 300 };
        window.Show();
        window.Measure(new Size(700, 300));
        window.Arrange(new Rect(0, 0, 700, 300));
        Dispatcher.UIThread.RunJobs();

        var view = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "View"));
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        ribbon.OpenOverlay.Should().NotBeNull("precondition: the reveal is showing");

        // A pointer press on the page below the ribbon, routed through the tree so the tunnel handler on the
        // TopLevel sees it exactly as it would in the app. (Headless MouseDown does not deliver here.)
        var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
        below.RaiseEvent(new PointerPressedEventArgs(
            below, pointer, window, new Point(4, 4), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
        });
        Dispatcher.UIThread.RunJobs();

        ribbon.OpenOverlay.Should().BeNull("pressing elsewhere in the app dismisses the reveal");
        ribbon.IsCollapsed.Should().BeTrue("and the ribbon is still minimised, as it was");
    }

    [AvaloniaFact]
    public void An_unpinned_reveal_spans_the_ribbon_width()
    {
        // Parity finding from the side-by-side review: b-ribbon's unpinned panel is `left: 0; right: 0`, so
        // it spans the ribbon, while Avalonia's popup sized itself to its contents. The web matches Office --
        // a temporary reveal should read as the ribbon body appearing, not as a dropdown.
        var ribbon = Build(out _);
        ribbon.IsPinned = false;
        ribbon.IsCollapsed = true;
        Show(ribbon, width: 900);

        var view = ribbon.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "View"));
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var overlay = ribbon.OpenOverlay;
        overlay.Should().NotBeNull();
        overlay!.MinWidth.Should().BeApproximately(ribbon.Bounds.Width, 0.5,
            "the reveal spans the ribbon rather than hugging its contents");
    }

    // ── TASK-100: a collapsed group must be reachable and announced ───────────────

    /// <summary>One collapsed group, plus the chunk button actually on display for it.</summary>
    private static (Ribbon Ribbon, Button Chunk) CollapsedGroup(out bool[] ran, double width = 900)
    {
        var flags = new bool[1];
        var group = new RibbonGroup
        {
            Label = "Clipboard",
            Icon = "📋",
            Items = new[]
            {
                new RibbonItem { Id = "cut", Label = "Cut", Icon = "✂", Run = () => flags[0] = true },
                new RibbonItem { Id = "copy", Label = "Copy", Icon = "📄" },
            },
        };
        var ribbon = new Ribbon
        {
            PreferredGroupSize = RibbonGroupSize.Popup,
            Tabs = new[] { new RibbonTab { Id = "h", Label = "Home", Groups = new[] { group } } },
        };
        Show(ribbon, width);
        ran = flags;

        var chunk = ribbon.GetVisualDescendants().OfType<Button>()
            .Where(b => (b.GetValue(ToolTip.TipProperty) as string) == "Clipboard")
            .First(b => b.TranslatePoint(default, ribbon) is { X: > -1000 });
        return (ribbon, chunk);
    }

    [AvaloniaFact]
    public void A_collapsed_group_announces_its_name_and_that_it_expands()
    {
        // Without KeyTips, a collapsed group is the ONLY route to its commands — so announcing as a bare
        // "button" would remove them from screen-reader users specifically, which is the same defect this
        // story removes for sighted mouse users. b-ribbon gets this from aria-expanded + aria-haspopup.
        var (_, chunk) = CollapsedGroup(out _);

        global::Avalonia.Automation.AutomationProperties.GetName(chunk).Should().Be("Clipboard",
            "the accessible name is the group's, not the empty content of a compact chunk");

        var peer = global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(chunk);
        peer.Should().BeAssignableTo<global::Avalonia.Automation.Provider.IExpandCollapseProvider>(
            "a collapsed group is an expandable thing, and should say so");

        var expand = (global::Avalonia.Automation.Provider.IExpandCollapseProvider)peer;
        expand.ExpandCollapseState.Should().Be(global::Avalonia.Automation.ExpandCollapseState.Collapsed);

        expand.Expand();
        Dispatcher.UIThread.RunJobs();
        expand.ExpandCollapseState.Should().Be(global::Avalonia.Automation.ExpandCollapseState.Expanded,
            "and the state must track the flyout, or assistive tech reports whatever it saw first");

        expand.Collapse();
        Dispatcher.UIThread.RunJobs();
        expand.ExpandCollapseState.Should().Be(global::Avalonia.Automation.ExpandCollapseState.Collapsed);
    }

    [AvaloniaFact]
    public void A_collapsed_group_is_keyboard_reachable_and_parked_variants_are_not()
    {
        // The parked variants sit off-screen but were still FOCUSABLE, so Tab walked through invisible
        // controls — extra stops on commands a keyboard user cannot see. Disabling takes the whole subtree
        // out of the tab order, which IsHitTestVisible alone does not.
        var (ribbon, chunk) = CollapsedGroup(out _);

        chunk.IsEffectivelyEnabled.Should().BeTrue("the shown chunk is reachable by Tab");
        chunk.Focusable.Should().BeTrue();

        var parked = ribbon.GetVisualDescendants().OfType<Button>()
            .Where(b => b.TranslatePoint(default, ribbon) is { X: < -1000 })
            .ToList();

        parked.Should().NotBeEmpty("the unchosen variants are parked off-screen by design");
        parked.Should().OnlyContain(b => !b.IsEffectivelyEnabled,
            "and none of them may be in the tab order");
    }

    [AvaloniaFact]
    public void Escape_closes_a_collapsed_groups_flyout()
    {
        // The criterion TASK-100 could not tick: it relied on Flyout's own behaviour, unverified.
        var (ribbon, chunk) = CollapsedGroup(out _, width: 900);
        var window = (Window)ribbon.GetVisualRoot()!;

        chunk.Flyout!.ShowAt(chunk);
        Dispatcher.UIThread.RunJobs();
        chunk.Flyout!.IsOpen.Should().BeTrue("precondition");

        PressKey(window, Key.Escape);

        chunk.Flyout!.IsOpen.Should().BeFalse("Escape dismisses the flyout");
    }

    [AvaloniaFact]
    public void Tab_walks_through_the_ribbon_and_skips_the_parked_variants()
    {
        // Asserts the ORDER a keyboard user actually experiences, not just that controls are focusable.
        // (The gallery appeared to fail this; the cause was its TabControl defaulting to TabNavigation=Once,
        // which skips tab content entirely — a host-configuration issue, not the ribbon's. Worth pinning the
        // ribbon's own behaviour so the two can be told apart next time.)
        RibbonItem I(string l) => new() { Id = l, Label = l, Icon = "●" };
        var ribbon = new Ribbon
        {
            Tabs = new[]
            {
                new RibbonTab { Id = "home", Label = "Home", Groups = new[]
                    { new RibbonGroup { Label = "Clipboard", Items = new[] { I("Cut"), I("Copy") } } } },
            },
        };

        var before = new Button { Content = "Before" };
        var after = new Button { Content = "After" };
        var page = new StackPanel();
        page.Children.Add(before);
        page.Children.Add(ribbon);
        page.Children.Add(after);

        var window = new Window { Content = page, Width = 900, Height = 300 };
        window.Show();
        window.Measure(new Size(900, 300));
        window.Arrange(new Rect(0, 0, 900, 300));
        Dispatcher.UIThread.RunJobs();

        var order = new List<string>();
        IInputElement? cur = before;
        for (int i = 0; i < 8; i++)
        {
            cur = KeyboardNavigationHandler.GetNext(cur!, NavigationDirection.Next);
            if (cur is not Button b) break;
            order.Add(string.Concat(b.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
            if (ReferenceEquals(cur, after)) break;
        }

        order.Should().ContainInOrder(new[] { "Home", "●Cut", "●Copy" },
            "Tab reaches the tab strip, then the group's commands, in reading order");
        order.Should().EndWith(new[] { "After" }, "and leaves the ribbon rather than trapping focus");
    }

    [AvaloniaFact]
    public void Activating_a_tab_by_keyboard_leaves_focus_on_that_tab()
    {
        // Reported by hand: Space on a ribbon tab opened the right groups, but the NEXT Tab restarted from the
        // top of the window and the groups were unreachable. Rebuild() discards the whole tree, so the focused
        // control was destroyed and focus fell back to the window root. Asserts the OUTCOME a keyboard user
        // depends on — focus still inside the ribbon, on the tab that was activated — not that a restore ran.
        var ribbon = Show(BuildCrowded(), out var window, 900);
        var tabs = ribbon.GetVisualDescendants().OfType<Button>()
            .Where(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Home" || t.Text == "Insert"))
            .ToList();
        var insert = tabs.First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Insert"));

        insert.Focus(NavigationMethod.Tab).Should().BeTrue("the tab must be keyboard-focusable at all");
        Dispatcher.UIThread.RunJobs();

        // Space on a focused Button is the exact path the reviewer took. It needs KeyUp as well as KeyDown —
        // Avalonia's Button presses on Space-down and only CLICKS on Space-up (unlike Enter, which fires on
        // down). Down alone selected nothing, which is how this test first failed.
        insert.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        insert.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(900, 400));
        window.Arrange(new Rect(0, 0, 900, 400));
        Dispatcher.UIThread.RunJobs();

        ribbon.SelectedIndex.Should().Be(1, "Space must activate the tab in the first place");

        var focused = window.FocusManager?.GetFocusedElement() as Visual;
        focused.Should().NotBeNull();
        focused!.GetVisualAncestors().Should().Contain(ribbon,
            "activating a tab must not throw focus out of the ribbon — the groups it just opened would be unreachable");
        focused.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Should().Contain("Insert", "and focus belongs on the tab that was activated, so Tab continues from there");
    }

    [AvaloniaFact]
    public void A_reserved_but_invisible_scroll_chevron_is_not_in_the_tab_order()
    {
        // Reported by hand: Tab landed on a button that could not be seen. The chevrons reserve BOTH slots once
        // the strip overflows (so showing one cannot reflow the row and let a tab swallow the click), and the
        // inactive one was Opacity 0 + IsHitTestVisible false — which hides it from the MOUSE only. Same
        // species as the parked size variants.
        var ribbon = Show(BuildCrowded(), out _, 420);
        var left = ChevronByTip(ribbon, "Scroll tabs left");

        IsReserved(left).Should().BeTrue("this test is meaningless unless the left chevron is reserved-but-hidden");
        IsShown(left).Should().BeFalse("at scroll offset 0 there is nothing to scroll left to");

        var reachable = new List<IInputElement>();
        IInputElement? cur = ribbon.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyEnabled);
        for (int i = 0; i < 40 && cur is not null; i++)
        {
            reachable.Add(cur);
            cur = KeyboardNavigationHandler.GetNext(cur, NavigationDirection.Next);
            if (reachable.Contains(cur!)) break;
        }

        reachable.Should().NotContain(left,
            "a keyboard user must not land on an invisible chevron whose activation does nothing");
    }

    [AvaloniaFact]
    public void Escape_inside_a_collapsed_groups_flyout_closes_it()
    {
        // The case the earlier Escape test missed: a popup is its own visual root, so once focus is INSIDE
        // the flyout the key never reaches the ribbon's TopLevel handler. The previous test raised Escape at
        // the main window with focus outside, so it passed while the path a keyboard user takes was dead.
        var (_, chunk) = CollapsedGroup(out _);
        var flyout = (Flyout)chunk.Flyout!;

        flyout.ShowAt(chunk);
        Dispatcher.UIThread.RunJobs();
        flyout.IsOpen.Should().BeTrue("precondition");

        // Raised on the flyout's own content, which is where the key actually arrives.
        var content = (Control)flyout.Content!;
        content.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
        });
        Dispatcher.UIThread.RunJobs();

        flyout.IsOpen.Should().BeFalse("Escape from inside the flyout closes it");
    }

    [AvaloniaFact]
    public void A_focused_ribbon_button_is_visibly_focused_without_reflowing_the_row()
    {
        // The Avalonia skin has no focus visual on Button at all (only Inputs.axaml styles :focus), so
        // keyboard focus was invisible on every ribbon command -- which is why a keyboard pass looked like
        // Tab was stuck on the theme buttons. Reachable but invisible is a WCAG 2.4.7 failure, not a polish
        // item. Also asserts the ring cannot reflow the row, since this row is measured for scaling.
        var ribbon = Show(Build(out _));
        // The parked variants also contain a "Cut" button and are DISABLED, so they cannot take focus —
        // picking one silently made this test assert nothing. Choose the one on display.
        var cut = ribbon.GetVisualDescendants().OfType<Button>()
            .Where(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Cut"))
            .First(b => b.TranslatePoint(default, ribbon) is { X: > -1000 });

        var restingBrush = cut.BorderBrush;
        double restingWidth = cut.Bounds.Width;

        cut.Focus().Should().BeTrue("the shown command must be focusable at all");
        Dispatcher.UIThread.RunJobs();

        cut.BorderBrush.Should().NotBeSameAs(restingBrush, "focus must be visible, not just present");
        cut.BorderBrush.Should().NotBe(Brushes.Transparent);
        cut.Bounds.Width.Should().BeApproximately(restingWidth, 0.5,
            "only the brush changes — a focus ring that resized the button would reflow the scaled row");
    }

    [AvaloniaFact]
    public void Capture_ribbon_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        RibbonItem I(string label, string icon) => new() { Id = label, Label = label, Icon = icon };
        var ribbon = new Ribbon
        {
            Tabs = new[]
            {
                new RibbonTab { Id = "home", Label = "Home", Groups = new[]
                {
                    new RibbonGroup { Label = "Clipboard", Items = new[] { I("Paste", "\U0001F4CB"), I("Cut", "✂"), I("Copy", "\U0001F5D0") } },
                    new RibbonGroup { Label = "Records", Items = new[] { I("New", "➕"), I("Delete", "\U0001F5D1") } },
                }},
                new RibbonTab { Id = "view", Label = "View", Groups = new[] { new RibbonGroup { Label = "Zoom", Items = new[] { I("Zoom In", "\U0001F50D") } } } },
            },
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#F1F5F9")), Child = ribbon, Height = 120, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = page, Width = 700, Height = 160 };
        window.Show();
        window.Measure(new Size(700, 160));
        window.Arrange(new Rect(0, 0, 700, 160));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "ribbon.png"));

        // Collapsed (tabs-only) variant
        ribbon.IsCollapsed = true;
        window.Measure(new Size(700, 160));
        window.Arrange(new Rect(0, 0, 700, 160));
        Dispatcher.UIThread.RunJobs();
        global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?
            .Save(Path.Combine(dir, "ribbon-collapsed.png"));

        ribbon.GetVisualDescendants().OfType<Button>().Should().NotBeEmpty();
    }
}
