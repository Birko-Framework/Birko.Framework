namespace Birko.Xaml.Core.Ribbon;

/// <summary>
/// How much room a <see cref="RibbonGroup"/> is being given, smallest last. The ribbon picks the
/// largest set of variants that fits the available width and degrades from there — Office's model,
/// where the ribbon body <b>resizes rather than scrolls</b>, because a scroll offset destroys the
/// spatial memory the ribbon exists to provide ("Cut is top-left of Clipboard").
/// </summary>
/// <remarks>
/// Declared in the order they degrade, so <c>&gt;</c> / <c>&lt;</c> comparisons read as
/// "roomier than" / "tighter than".
/// <para>
/// <b>Neither skin renders all four yet</b> — STORY-049/TASK-099 and TASK-100 build them. As of
/// TASK-098 the Avalonia <c>Ribbon</c> renders every item as <see cref="Large"/> (icon above a
/// centred label) while the web <c>b-ribbon</c> renders every item as <see cref="Medium"/>
/// (16px icon, label to its right) — a pre-existing parity gap this enum names so TASK-099 has to
/// reconcile it deliberately instead of discovering it.
/// </para>
/// </remarks>
public enum RibbonGroupSize
{
    /// <summary>32px icon, label underneath, one item per column. The roomiest form.</summary>
    Large,

    /// <summary>16px icon with the label to its right, three items stacked per column.</summary>
    Medium,

    /// <summary>
    /// 16px icon only, three per column. The label is not drawn, so the item's tooltip carries
    /// its name — an icon-only item with no tooltip is unidentifiable.
    /// </summary>
    Small,

    /// <summary>
    /// The whole group collapses to a single button (group <see cref="RibbonGroup.Icon"/> +
    /// <see cref="RibbonGroup.Label"/> + a ▾ affordance) whose flyout holds the group's items at
    /// <see cref="Large"/>. Lossless: the group keeps its identity and its position in the row,
    /// which is what separates this from a flat overflow menu.
    /// </summary>
    Popup,
}

/// <summary>One command button in a ribbon group (the XAML analogue of <c>b-ribbon</c> items).</summary>
public sealed class RibbonItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }

    /// <summary>Optional glyph shown above the label.</summary>
    public string? Icon { get; init; }

    /// <summary>Invoked when the item is clicked.</summary>
    public Action? Run { get; init; }
}

/// <summary>A labeled group of related <see cref="RibbonItem"/>s within a ribbon tab.</summary>
public sealed class RibbonGroup
{
    public required string Label { get; init; }
    public IReadOnlyList<RibbonItem> Items { get; init; } = Array.Empty<RibbonItem>();

    /// <summary>
    /// Optional glyph naming the group. Shown on the collapsed chunk button when the group degrades
    /// to <see cref="RibbonGroupSize.Popup"/>; unused at the roomier sizes.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// How important this group is, and therefore how late it degrades: a <b>lower</b> value
    /// degrades <b>first</b>. Groups sharing a value degrade left-to-right. Default 0, so a ribbon
    /// that sets nothing degrades uniformly — which is the outcome to avoid, since shrinking every
    /// group at the same rate turns the ribbon into a row of anonymous icons instead of keeping the
    /// primary group legible. Give the hero group (Clipboard, Font) a higher value.
    /// </summary>
    /// <remarks>
    /// The direction is <b>Birko's convention</b> — priority means importance. Office's RibbonX has
    /// its own <c>scalingPriority</c> whose numeric sense is not what is documented here; do not
    /// assume they agree.
    /// </remarks>
    public int ScalingPriority { get; init; }

    /// <summary>
    /// The tightest variant this group may degrade to. Defaults to <see cref="RibbonGroupSize.Popup"/>
    /// (fully collapsible). Raise it to protect a group — <c>MinSize = RibbonGroupSize.Small</c> keeps
    /// it visible as icons rather than folding into a flyout, at the cost of another group degrading
    /// further.
    /// </summary>
    public RibbonGroupSize MinSize { get; init; } = RibbonGroupSize.Popup;
}

/// <summary>A ribbon tab holding groups of commands (the <c>BAppShell</c> ribbon model).</summary>
public sealed class RibbonTab
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public IReadOnlyList<RibbonGroup> Groups { get; init; } = Array.Empty<RibbonGroup>();
}
