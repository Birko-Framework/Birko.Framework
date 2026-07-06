namespace Birko.Xaml.Core.Ribbon;

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
}

/// <summary>A ribbon tab holding groups of commands (the <c>BAppShell</c> ribbon model).</summary>
public sealed class RibbonTab
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public IReadOnlyList<RibbonGroup> Groups { get; init; } = Array.Empty<RibbonGroup>();
}
