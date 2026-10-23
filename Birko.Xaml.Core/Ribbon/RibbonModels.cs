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

    /// <summary>Key tip letter or digit (TASK-758); unset, the first free letter of the label.</summary>
    public string? Keytip { get; init; }

    /// <summary>Invoked when the item is clicked.</summary>
    public Action? Run { get; init; }

    /// <summary>
    /// One line shown under the name in the command's tooltip, and its automation help text (TASK-753, as b-ribbon's
    /// <c>description</c>). Text.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Shown in the tooltip, e.g. <c>Ctrl+V</c>, and exposed as the automation accelerator key. Display only: the app
    /// binds the key itself.
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>
    /// An on/off command (Bold), TASK-754 as b-ribbon's <c>toggle</c>. A click flips it, raises
    /// <c>Ribbon.ItemToggled</c> with the new state, then runs <see cref="Run"/>. <see cref="Pressed"/> is the initial
    /// state; <c>Ribbon.SetPressed</c> changes it later without an event.
    /// </summary>
    public bool Toggle { get; init; }

    /// <summary>The initial state of a <see cref="Toggle"/> or of a <see cref="ToggleGroup"/> member.</summary>
    public bool Pressed { get; init; }

    /// <summary>
    /// Commands of one ribbon group sharing this name form a set where exactly one is on (alignment left / centre /
    /// right): pressing one turns the others off, and pressing the one that is on keeps it on. Announced as radio
    /// buttons. Implies <see cref="Toggle"/>.
    /// </summary>
    public string? ToggleGroup { get; init; }

    /// <summary>
    /// Options this command opens as a menu (TASK-755, as b-ribbon's <c>menu</c>). Without <see cref="Split"/> the
    /// whole button opens it (a menu button, drawn with ▾); with <see cref="Split"/> the button runs the command and a
    /// separate ▾ part opens the menu (Office's "Paste ▾"). A choice runs its own <see cref="RibbonMenuItem.Run"/> and
    /// raises <c>Ribbon.MenuItemInvoked</c> with this command as the parent. Not combined with <see cref="Toggle"/>.
    /// </summary>
    public IReadOnlyList<RibbonMenuEntry>? Menu { get; init; }

    /// <summary>With <see cref="Menu"/>: the button runs the command and a ▾ part opens the menu.</summary>
    public bool Split { get; init; }
}

/// <summary>A <see cref="RibbonItem.Menu"/> entry: a choice, or <see cref="Separator"/>, a line between sets of choices.</summary>
public abstract class RibbonMenuEntry
{
    private protected RibbonMenuEntry() { }

    /// <summary>The divider line between sets of choices.</summary>
    public static RibbonMenuEntry Separator { get; } = new RibbonMenuSeparator();

    private sealed class RibbonMenuSeparator : RibbonMenuEntry { }
}

/// <summary>One choice of a <see cref="RibbonItem.Menu"/>. Label and shortcut are text.</summary>
public sealed class RibbonMenuItem : RibbonMenuEntry
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Icon { get; init; }

    /// <summary>Key tip letter or digit (TASK-758); unset, the first free letter of the label.</summary>
    public string? Keytip { get; init; }

    /// <summary>Shown right-aligned, e.g. <c>Ctrl+Shift+V</c>. Display only: the app binds the key itself.</summary>
    public string? Shortcut { get; init; }

    public bool Disabled { get; init; }

    /// <summary>Invoked when the choice is picked.</summary>
    public Action? Run { get; init; }
}

/// <summary>A labeled group of related <see cref="RibbonItem"/>s within a ribbon tab.</summary>
public sealed class RibbonGroup
{
    /// <summary>Optional identity, reported by the launcher event (TASK-756). Without one, the label stands in.</summary>
    public string? Id { get; init; }

    public required string Label { get; init; }

    /// <summary>Key tip letter of the group's folded button (TASK-758); unset, the first free letter of the label.</summary>
    public string? Keytip { get; init; }
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

    /// <summary>
    /// Office's dialog launcher (TASK-756, as b-ribbon's <c>launcher</c>): a small corner arrow at the group's bottom
    /// right that opens the group's full dialog. It raises <c>Ribbon.GroupLauncherInvoked</c>.
    /// </summary>
    public bool Launcher { get; init; }

    /// <summary>The launcher's name and tooltip; default "More {Label} options".</summary>
    public string? LauncherLabel { get; init; }
}

/// <summary>A ribbon tab holding groups of commands (the <c>BAppShell</c> ribbon model).</summary>
public sealed class RibbonTab
{
    public required string Id { get; init; }
    public required string Label { get; init; }

    /// <summary>Key tip letter of the tab (TASK-758); unset, the first free letter of the label.</summary>
    public string? Keytip { get; init; }

    /// <summary>
    /// The contextual set this tab belongs to (TASK-760, as b-ribbon's <c>contextualSet</c>): hidden until the app shows
    /// that set, then after the normal tabs under the set's colour and name. Unset, a normal tab.
    /// </summary>
    public string? ContextualSet { get; init; }
    public IReadOnlyList<RibbonGroup> Groups { get; init; } = Array.Empty<RibbonGroup>();
}

/// <summary>The theme colour a contextual set is marked in (TASK-760).</summary>
public enum RibbonContextualColor { Primary, Success, Warning, Danger, Info }

/// <summary>
/// How a shown contextual set is marked (TASK-760), the three looks the user kept on the web: <see cref="Band"/>
/// (Office: a tinted band with the name above its tabs), <see cref="Pill"/> (the name as a pill), <see cref="Edge"/>
/// (a coloured line, no name).
/// </summary>
public enum RibbonContextualStyle { Band, Pill, Edge }

/// <summary>Tabs that appear only while something is selected, like Office's "Table Design" (TASK-760).</summary>
public sealed class RibbonContextualSet
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public RibbonContextualColor Color { get; init; } = RibbonContextualColor.Primary;

    /// <summary>Overrides the ribbon's style for this set; unset, the ribbon's.</summary>
    public RibbonContextualStyle? Style { get; init; }
}
