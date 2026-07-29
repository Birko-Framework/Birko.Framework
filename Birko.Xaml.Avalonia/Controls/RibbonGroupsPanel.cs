using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Birko.Xaml.Core.Ribbon;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Lays out a ribbon tab's groups, degrading each group's variant so the row fits the width it is given
/// (<see cref="RibbonScaling"/> decides which). The Avalonia half of STORY-049's progressive scaling.
/// </summary>
/// <remarks>
/// The degrade pass cannot live in <c>Ribbon.Rebuild()</c>: that runs with no width constraint at all, so
/// it has nothing to reason about. Hence a real panel with <see cref="MeasureOverride"/>.
/// <para>
/// Every variant of every group is built up front and kept as a child; only the chosen one is arranged
/// on-screen. That costs tree size (four controls per group) and buys two things worth more: widths can be
/// measured without constructing controls during layout, and the decision needs no re-render — which is
/// the mistake that produced three separate flicker bugs in TASK-097.
/// </para>
/// <para>
/// <b>The unchosen variants stay <see cref="Visual.IsVisible"/> = true and are parked off-screen instead.</b>
/// Avalonia's <c>MeasureCore</c> short-circuits for an invisible control, leaving its
/// <c>DesiredSize</c> at zero — so hiding them made every tighter variant measure as free, the pass
/// under-degraded, and the row overflowed its slot and clipped the rightmost group. Flipping visibility
/// per pass is also not an option: it invalidates measure, so the panel would re-layout forever.
/// <see cref="ClipToBounds"/> keeps the parked controls from painting.
/// </para>
/// <para>
/// Convergence: <see cref="RibbonScaling.Resolve"/> is a pure function of the measured widths and the
/// available width, so a second measure pass reaches the same answer and the visibility flips stop. It is
/// specifically NOT a function of the currently-applied layout, which is what would oscillate.
/// </para>
/// </remarks>
internal sealed class RibbonGroupsPanel : Panel
{
    /// <summary>One group's pre-built renderings, keyed by variant.</summary>
    internal sealed class GroupVariants
    {
        public required IReadOnlyDictionary<RibbonGroupSize, Control> Controls { get; init; }

        /// <summary>
        /// The chunk button without its group name, used only when even a row of labelled chunks is
        /// too wide. Deliberately NOT a fifth <see cref="RibbonGroupSize"/>: it is the same Popup
        /// variant drawn tighter, so the shared enum — and the policy both skins agree on — stays at
        /// Office's four.
        /// </summary>
        public Control? CompactPopup { get; init; }

        public int ScalingPriority { get; init; }
        public RibbonGroupSize MinSize { get; init; } = RibbonGroupSize.Popup;
    }

    private readonly List<GroupVariants> _groups = new();
    private RibbonGroupSize[] _chosen = System.Array.Empty<RibbonGroupSize>();
    private bool _compact;

    /// <summary>The roomiest variant any group may take — the ribbon's look at full width.</summary>
    public RibbonGroupSize Preferred { get; set; } = RibbonGroupSize.Medium;

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<RibbonGroupsPanel, double>(nameof(Gap), 8);

    /// <summary>
    /// Space between adjacent groups. A styled property so it can bind to the <c>BRibbonGroupGap</c> token
    /// rather than duplicating the value — and so a theme swap re-measures the row.
    /// </summary>
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    static RibbonGroupsPanel()
    {
        AffectsMeasure<RibbonGroupsPanel>(GapProperty);
    }

    public void AddGroup(GroupVariants group)
    {
        ClipToBounds = true; // parked variants live off-screen; do not let them paint
        _groups.Add(group);
        foreach (var control in group.Controls.Values) Children.Add(control);
        if (group.CompactPopup is not null) Children.Add(group.CompactPopup);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_groups.Count == 0) return default;

        // Measure every variant unconstrained to learn what each would cost.
        var metrics = new List<RibbonGroupMetrics>(_groups.Count);
        foreach (var group in _groups)
        {
            var widths = new Dictionary<RibbonGroupSize, double>(group.Controls.Count);
            foreach (var pair in group.Controls)
            {
                pair.Value.Measure(Size.Infinity);
                widths[pair.Key] = pair.Value.DesiredSize.Width;
            }

            metrics.Add(new RibbonGroupMetrics
            {
                Widths = widths,
                ScalingPriority = group.ScalingPriority,
                MinSize = group.MinSize,
            });
        }

        double available = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;

        // The decision uses the FULL gap, the layout uses the effective one. Deciding against the larger
        // value is deliberately conservative: the row it picks then fits with room to spare, whereas
        // deciding against the smaller value could under-degrade and clip. Determinism is unaffected — both
        // gaps are pure functions of the width and the chosen set.
        _chosen = RibbonScaling.Resolve(metrics, available, Preferred, Gap);
        double gap = EffectiveGap(_chosen);

        // Last resort, below even an all-Popup row: drop the group name from every chunk button.
        // Decided here rather than in the policy because it is a rendering choice, not a variant —
        // and it stays a pure function of the width, so determinism holds.
        _compact = false;
        if (System.Array.TrueForAll(_chosen, size => size == RibbonGroupSize.Popup)
            && _groups.TrueForAll(g => g.CompactPopup is not null))
        {
            double labelled = 0;
            for (int i = 0; i < _groups.Count; i++)
            {
                var chunk = _groups[i].Controls[RibbonGroupSize.Popup];
                chunk.Measure(Size.Infinity);
                labelled += chunk.DesiredSize.Width + (i > 0 ? gap : 0);
            }
            _compact = labelled > available;
        }

        double width = 0, height = 0;
        for (int i = 0; i < _groups.Count; i++)
        {
            var picked = Picked(i);
            foreach (var control in AllControls(_groups[i]))
            {
                bool shown = control == picked;
                control.IsHitTestVisible = shown;
                // IsEnabled, not just hit-testing: a parked variant is off-screen but still FOCUSABLE, so
                // Tab walked into invisible controls — three extra stops per group for a keyboard user, on
                // commands they cannot see. Disabling takes the whole subtree out of the tab order, which
                // hit-testing alone does not do. It does not affect measurement.
                if (control.IsEnabled != shown) control.IsEnabled = shown;
            }

            picked.Measure(availableSize);
            if (i > 0) width += gap;
            width += picked.DesiredSize.Width;
            height = System.Math.Max(height, picked.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double gap = EffectiveGap(_chosen);
        double x = 0;
        for (int i = 0; i < _groups.Count && i < _chosen.Length; i++)
        {
            var picked = Picked(i);
            foreach (var control in AllControls(_groups[i]))
            {
                if (control == picked) continue;
                // Parked far off-screen rather than hidden: it must stay measurable (see the class
                // remarks). ClipToBounds stops it painting, IsHitTestVisible stops it being clicked.
                control.Arrange(new Rect(-100_000, 0, control.DesiredSize.Width, control.DesiredSize.Height));
            }

            if (i > 0) x += gap;
            picked.Arrange(new Rect(x, 0, picked.DesiredSize.Width, finalSize.Height));
            x += picked.DesiredSize.Width;
        }
        return finalSize;
    }

    /// <summary>The control actually shown for a group — the compact chunk at the row's extreme.</summary>
    private Control Picked(int i) =>
        _compact && _chosen[i] == RibbonGroupSize.Popup && _groups[i].CompactPopup is not null
            ? _groups[i].CompactPopup!
            : _groups[i].Controls[_chosen[i]];

    private static IEnumerable<Control> AllControls(GroupVariants group)
    {
        foreach (var control in group.Controls.Values) yield return control;
        if (group.CompactPopup is not null) yield return group.CompactPopup;
    }

    /// <summary>Whether chunk buttons are drawn without their group names — the narrowest row possible.</summary>
    internal bool IsCompact => _compact;

    /// <summary>
    /// Space between groups, tightened as the row does. A collapsed group is a single button, and sitting
    /// those a full group-gap apart wastes the width the collapse just bought — Office packs them close.
    /// This matters more than it sounds: the gap sets the row's hard minimum, so six groups at
    /// <see cref="RibbonGroupSize.Popup"/> with a 24px gap could not fit under ~550px, and everything
    /// narrower than that clipped.
    /// </summary>
    private double EffectiveGap(IReadOnlyList<RibbonGroupSize> chosen)
    {
        var tightest = RibbonGroupSize.Large;
        foreach (var size in chosen) if (size > tightest) tightest = size;

        return tightest switch
        {
            RibbonGroupSize.Popup => Gap * 0.25,
            RibbonGroupSize.Small => Gap * 0.5,
            _ => Gap,
        };
    }

    /// <summary>The variant each group ended up at — for tests and for the ribbon's own diagnostics.</summary>
    internal IReadOnlyList<RibbonGroupSize> Chosen => _chosen;
}
