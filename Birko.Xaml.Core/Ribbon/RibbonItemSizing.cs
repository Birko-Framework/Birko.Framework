namespace Birko.Xaml.Core.Ribbon;

/// <summary>
/// The size each item of a group renders at, for a step the group is at (TASK-120, FEATURE-015 D13 / D14). The one
/// place the rule lives: both renderers and the scaling ladder ask it, and b-ribbon's <c>ribbonItemSizes</c> in
/// <c>ribbon-scaling.ts</c> mirrors it case for case.
/// </summary>
/// <remarks>
/// <para>
/// <b>The anchor rule (D13).</b> An item with a <see cref="RibbonItem.Size"/> keeps its offset from the group's
/// <em>starting</em> step — the step the ladder starts the group at — and moves with the group: at step S it renders at
/// S shifted by that offset, clamped between <see cref="RibbonGroupSize.Large"/> and <see cref="RibbonGroupSize.Small"/>.
/// An item with no size has offset 0: it renders at the group's step, exactly as before.
/// </para>
/// <para>
/// <b>Declared layouts (D14).</b> <see cref="RibbonGroup.Layouts"/> overrides the rule for the items it lists at the
/// step it is declared for.
/// </para>
/// <para>
/// At <see cref="RibbonGroupSize.Popup"/> the group is one button whose flyout holds its items as at
/// <see cref="RibbonGroupSize.Large"/>, so the sizes returned are those of the Large step.
/// </para>
/// The result depends only on the arguments, never on a layout applied earlier (STORY-049's determinism rule).
/// </remarks>
public static class RibbonItemSizing
{
    /// <summary>
    /// The size of every item of <paramref name="group"/> at <paramref name="step"/>, in item order, for a group whose
    /// ladder starts at <paramref name="start"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An item size or a declared size of <see cref="RibbonGroupSize.Popup"/>, a layout declared for the Popup step, a
    /// layout naming an item the group does not have, a <paramref name="start"/> of Popup, or layouts that make an item
    /// roomier at a tighter step (see the remarks on monotony).
    /// </exception>
    /// <remarks>
    /// <b>Monotony (TASK-121).</b> Every item is the same size or smaller at each tighter step (Large → Medium → Small).
    /// <see cref="RibbonScaling"/> reads a missing width as the nearest <em>roomier</em> step's, which over-estimates only
    /// while no tighter layout is wider; the anchor rule is monotone by construction, so only a declared layout can
    /// break it, and that is refused rather than letting the row "fit" by accident.
    /// </remarks>
    public static IReadOnlyList<RibbonGroupSize> SizesAt(RibbonGroup group, RibbonGroupSize step, RibbonGroupSize start)
    {
        ArgumentNullException.ThrowIfNull(group);
        Validate(group);
        if (start == RibbonGroupSize.Popup)
            throw new ArgumentException("A group's starting step cannot be Popup: the ladder starts groups at Large, Medium or Small.", nameof(start));

        var large = Resolve(group, RibbonGroupSize.Large, start);
        var medium = Resolve(group, RibbonGroupSize.Medium, start);
        var small = Resolve(group, RibbonGroupSize.Small, start);
        for (int i = 0; i < large.Length; i++)
        {
            if (medium[i] < large[i] || small[i] < medium[i])
                throw new ArgumentException(
                    $"Group '{group.Label}': item '{group.Items[i].Id}' is {large[i]}, {medium[i]}, {small[i]} at the Large, Medium and "
                    + "Small steps; an item may only stay the same or get smaller at a tighter step.");
        }

        return step switch
        {
            RibbonGroupSize.Medium => medium,
            RibbonGroupSize.Small => small,
            _ => large, // Large, and Popup's flyout
        };
    }

    private static RibbonGroupSize[] Resolve(RibbonGroup group, RibbonGroupSize at, RibbonGroupSize start)
    {
        var declared = group.Layouts != null && group.Layouts.TryGetValue(at, out var layout) ? layout : null;
        var sizes = new RibbonGroupSize[group.Items.Count];
        for (int i = 0; i < sizes.Length; i++)
        {
            var item = group.Items[i];
            sizes[i] = declared != null && declared.TryGetValue(item.Id, out var size)
                ? size
                : Anchor(item.Size, at, start);
        }
        return sizes;
    }

    /// <summary>The anchor rule for one item: its offset from <paramref name="start"/>, applied at <paramref name="step"/>.</summary>
    private static RibbonGroupSize Anchor(RibbonGroupSize? size, RibbonGroupSize step, RibbonGroupSize start)
    {
        if (size is null) return step;
        int rank = (int)step + ((int)size.Value - (int)start);
        return (RibbonGroupSize)System.Math.Clamp(rank, (int)RibbonGroupSize.Large, (int)RibbonGroupSize.Small);
    }

    /// <summary>Refuses what the model cannot mean, rather than rendering something nobody declared.</summary>
    private static void Validate(RibbonGroup group)
    {
        foreach (var item in group.Items)
        {
            if (item.Size == RibbonGroupSize.Popup)
                throw new ArgumentException($"Ribbon item '{item.Id}' in group '{group.Label}' cannot be Popup: Popup is a group step, not an item size.");
        }
        if (group.Layouts == null) return;
        var ids = new HashSet<string>(group.Items.Select(i => i.Id));
        foreach (var (step, layout) in group.Layouts)
        {
            if (step == RibbonGroupSize.Popup)
                throw new ArgumentException($"Group '{group.Label}' declares a layout for Popup: the Popup flyout shows the Large layout.");
            foreach (var (id, size) in layout)
            {
                if (!ids.Contains(id))
                    throw new ArgumentException($"Group '{group.Label}' declares a {step} layout for '{id}', which is not one of its items.");
                if (size == RibbonGroupSize.Popup)
                    throw new ArgumentException($"Group '{group.Label}' declares '{id}' as Popup at {step}: Popup is a group step, not an item size.");
            }
        }
    }
}
