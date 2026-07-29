using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
