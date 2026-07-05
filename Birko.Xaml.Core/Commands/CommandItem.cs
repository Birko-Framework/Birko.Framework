namespace Birko.Xaml.Core.Commands;

/// <summary>
/// One entry in the command palette (the XAML analogue of Birko.Web's command-palette items).
/// Platform-neutral (Avalonia-free): <see cref="Run"/> is a plain delegate the palette invokes on
/// selection. A shell typically builds these from its modules/actions.
/// </summary>
public sealed class CommandItem
{
    public required string Id { get; init; }

    /// <summary>The searchable, displayed label.</summary>
    public required string Label { get; init; }

    /// <summary>Optional grouping / category (shown as secondary text).</summary>
    public string? Group { get; init; }

    /// <summary>Invoked when the item is chosen.</summary>
    public Action? Run { get; init; }

    public override string ToString() => Label;
}
