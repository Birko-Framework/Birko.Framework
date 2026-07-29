namespace Birko.Xaml.Core.Ribbon;

/// <summary>
/// What the degrade pass needs to know about one group: how wide it renders at each variant, how late
/// it should degrade, and the tightest variant it may reach.
/// </summary>
/// <remarks>
/// Widths are supplied by the renderer (it is the only thing that can measure), which is what keeps
/// <see cref="RibbonScaling"/> platform-neutral and directly unit-testable.
/// </remarks>
public sealed class RibbonGroupMetrics
{
    /// <summary>Rendered width at each variant. A variant absent from the map is treated as unavailable.</summary>
    public required IReadOnlyDictionary<RibbonGroupSize, double> Widths { get; init; }

    /// <summary>Importance — a <b>lower</b> value degrades <b>first</b>. See <see cref="RibbonGroup.ScalingPriority"/>.</summary>
    public int ScalingPriority { get; init; }

    /// <summary>
    /// The tightest variant this group should reach. A <b>preference, not a guarantee</b>: it is breached
    /// (least-important-first) rather than letting the row overflow, because unreachable commands are worse
    /// than a group being less legible than its author wanted.
    /// </summary>
    public RibbonGroupSize MinSize { get; init; } = RibbonGroupSize.Popup;
}

/// <summary>
/// Office-style progressive group scaling: choose a variant per group so the row fits the available
/// width, degrading the least important groups first.
/// </summary>
/// <remarks>
/// Platform-neutral and renderer-free on purpose (EPIC-015 constraint #1 keeps <c>Birko.Xaml.Core</c>
/// Avalonia-free). Both skins call this with their own measurements, so the *policy* cannot drift
/// between them even though the *rendering* is forked — and the policy is unit-testable without a
/// window. The web <c>b-ribbon</c> mirrors this algorithm; keep the two in step.
/// <para>
/// <b>Determinism is a requirement, not a side effect.</b> The result depends only on the arguments —
/// never on the currently-applied layout. Feeding the applied layout back in is what makes a scaling
/// ribbon oscillate at a boundary (shrink → now it fits → grow → now it doesn't → shrink), which reads
/// as flicker. So the same available width always yields the same variants, whichever direction the
/// resize came from.
/// </para>
/// </remarks>
public static class RibbonScaling
{
    /// <summary>Variants ordered roomiest-first — the order groups degrade through.</summary>
    private static readonly RibbonGroupSize[] Ladder =
    {
        RibbonGroupSize.Large, RibbonGroupSize.Medium, RibbonGroupSize.Small, RibbonGroupSize.Popup,
    };

    /// <summary>
    /// Resolve a variant for every group.
    /// </summary>
    /// <param name="groups">Per-group metrics, in display order (left to right).</param>
    /// <param name="available">Width the row has to fit into, in the same units as the metrics.</param>
    /// <param name="preferred">
    /// The roomiest variant any group may start at — the ribbon's look at full width. Defaults to
    /// <see cref="RibbonGroupSize.Medium"/>, matching what both skins shipped before this pass existed,
    /// so an existing consumer's ribbon does not change height. Pass <see cref="RibbonGroupSize.Large"/>
    /// for the Office-like look.
    /// </param>
    /// <param name="gap">Space between groups, counted between each adjacent pair.</param>
    /// <returns>One variant per group, positionally matching <paramref name="groups"/>.</returns>
    public static RibbonGroupSize[] Resolve(
        IReadOnlyList<RibbonGroupMetrics> groups,
        double available,
        RibbonGroupSize preferred = RibbonGroupSize.Medium,
        double gap = 0)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var chosen = new RibbonGroupSize[groups.Count];
        if (groups.Count == 0) return chosen;

        // Start at the ribbon's preferred look, except where a floor forbids being that tight: MinSize
        // caps how far DOWN a group may go, so a group whose floor is roomier than `preferred` starts at
        // its floor. It never makes a group roomier than `preferred` otherwise.
        for (int i = 0; i < groups.Count; i++)
            chosen[i] = Roomier(preferred, groups[i].MinSize);

        double gaps = gap * System.Math.Max(0, groups.Count - 1);

        // Degrade one step at a time, always taking from the least important group that can still give.
        // One step at a time (rather than dropping a group straight to its floor) is what makes the
        // result feel authored: the row gives up the least it can to fit.
        while (Total(groups, chosen) + gaps > available)
        {
            int victim = NextToDegrade(groups, chosen, respectFloors: true);
            if (victim < 0) break;
            chosen[victim] = Next(chosen[victim]);
        }

        // Floors are a PREFERENCE, not a guarantee. If honouring every floor leaves the row too wide, the
        // floors give way rather than the row overflowing — because an overflowing row means commands the
        // user cannot see or click, and that is strictly worse than a group being less legible than its
        // author wanted. (Found in review: a hero group floored at Small kept its width and pushed the
        // last group off the edge entirely. Office has no hard floor either; groups always collapse.)
        while (Total(groups, chosen) + gaps > available)
        {
            int victim = NextToDegrade(groups, chosen, respectFloors: false);
            if (victim < 0) break; // everything is at Popup — the row simply cannot be narrower
            chosen[victim] = Next(chosen[victim]);
        }

        return chosen;
    }

    /// <summary>
    /// The group that should give up room next: lowest <see cref="RibbonGroupMetrics.ScalingPriority"/>,
    /// leftmost on a tie. With <paramref name="respectFloors"/> it skips groups already at their
    /// <see cref="RibbonGroupMetrics.MinSize"/>; without it, only <see cref="RibbonGroupSize.Popup"/> stops a
    /// group. Returns -1 when nothing can give any more.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the pass. Degrading uniformly is easier and worse — it turns the ribbon
    /// into a row of anonymous icons instead of keeping the primary group legible.
    /// </remarks>
    private static int NextToDegrade(
        IReadOnlyList<RibbonGroupMetrics> groups, RibbonGroupSize[] chosen, bool respectFloors)
    {
        int best = -1;
        for (int i = 0; i < groups.Count; i++)
        {
            if (respectFloors && chosen[i] >= groups[i].MinSize) continue; // at (or past) its floor
            if (chosen[i] == RibbonGroupSize.Popup) continue;              // nothing tighter exists
            if (best < 0 || groups[i].ScalingPriority < groups[best].ScalingPriority) best = i;
        }
        return best;
    }

    private static double Total(IReadOnlyList<RibbonGroupMetrics> groups, RibbonGroupSize[] chosen)
    {
        double sum = 0;
        for (int i = 0; i < groups.Count; i++) sum += WidthOf(groups[i], chosen[i]);
        return sum;
    }

    /// <summary>
    /// Width at <paramref name="size"/>, falling back to the nearest roomier measured variant. A renderer
    /// that only measured some variants therefore over-estimates rather than treating the group as free —
    /// a missing measurement must never let the row "fit" by accident.
    /// </summary>
    private static double WidthOf(RibbonGroupMetrics group, RibbonGroupSize size)
    {
        if (group.Widths.TryGetValue(size, out double w)) return w;
        for (int i = System.Array.IndexOf(Ladder, size) - 1; i >= 0; i--)
            if (group.Widths.TryGetValue(Ladder[i], out double fallback)) return fallback;
        return 0;
    }

    /// <summary>One step tighter, saturating at <see cref="RibbonGroupSize.Popup"/>.</summary>
    private static RibbonGroupSize Next(RibbonGroupSize size) =>
        size == RibbonGroupSize.Popup ? size : (RibbonGroupSize)((int)size + 1);

    /// <summary>The roomier of two variants (the enum is declared roomiest-first, so the lower value).</summary>
    private static RibbonGroupSize Roomier(RibbonGroupSize a, RibbonGroupSize b) => a < b ? a : b;
}
