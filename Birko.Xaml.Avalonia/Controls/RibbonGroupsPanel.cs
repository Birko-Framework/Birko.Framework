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
/// Every variant of every group is built up front and kept as a child; only the chosen one is visible and
/// arranged. That costs tree size (three controls per group) and buys two things worth more: widths can be
/// measured without constructing controls during layout, and the decision needs no re-render — which is
/// the mistake that produced three separate flicker bugs in TASK-097.
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
        public int ScalingPriority { get; init; }
        public RibbonGroupSize MinSize { get; init; } = RibbonGroupSize.Popup;
    }

    private readonly List<GroupVariants> _groups = new();
    private RibbonGroupSize[] _chosen = System.Array.Empty<RibbonGroupSize>();

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
        _groups.Add(group);
        foreach (var control in group.Controls.Values)
        {
            control.IsVisible = false; // MeasureOverride reveals exactly one
            Children.Add(control);
        }
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
        _chosen = RibbonScaling.Resolve(metrics, available, Preferred, Gap);

        double width = 0, height = 0;
        for (int i = 0; i < _groups.Count; i++)
        {
            foreach (var pair in _groups[i].Controls)
            {
                bool on = pair.Key == _chosen[i];
                if (pair.Value.IsVisible != on) pair.Value.IsVisible = on;
            }

            var picked = _groups[i].Controls[_chosen[i]];
            picked.Measure(availableSize);
            if (i > 0) width += Gap;
            width += picked.DesiredSize.Width;
            height = System.Math.Max(height, picked.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < _groups.Count && i < _chosen.Length; i++)
        {
            foreach (var pair in _groups[i].Controls)
            {
                if (pair.Key == _chosen[i]) continue;
                pair.Value.Arrange(default); // parked; invisible, so it neither draws nor hit-tests
            }

            var picked = _groups[i].Controls[_chosen[i]];
            if (i > 0) x += Gap;
            picked.Arrange(new Rect(x, 0, picked.DesiredSize.Width, finalSize.Height));
            x += picked.DesiredSize.Width;
        }
        return finalSize;
    }

    /// <summary>The variant each group ended up at — for tests and for the ribbon's own diagnostics.</summary>
    internal IReadOnlyList<RibbonGroupSize> Chosen => _chosen;
}
