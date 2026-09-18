namespace Birko.Xaml.Core.Charts;

/// <summary>How a <see cref="ChartSeries"/> is drawn (the XAML analogue of b-chart types).</summary>
public enum ChartKind
{
    Line,
    Column,
}

/// <summary>
/// One named numeric series for a chart. Platform-neutral data model — the Avalonia <c>BChart</c>
/// (and a future WPF one) renders it via its plotting engine and colors it from design tokens.
/// </summary>
public sealed class ChartSeries
{
    public required string Name { get; init; }
    public required IReadOnlyList<double> Values { get; init; }
}
