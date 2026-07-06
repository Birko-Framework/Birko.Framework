using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Birko.Xaml.Core.Charts;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using AvColor = Avalonia.Media.Color;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Chart (the XAML port of <c>b-chart</c>) over LiveCharts2 — modern, animated, MVVM-friendly, and
/// cross-platform (Avalonia + WPF). Bind <see cref="Series"/> (platform-neutral
/// <see cref="ChartSeries"/> from Core), pick <see cref="Kind"/> (line/column) and optional
/// <see cref="Labels"/>; series are colored from the Birko design tokens.
/// </summary>
public class BChart : ContentControl
{
    public static readonly StyledProperty<IEnumerable<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<BChart, IEnumerable<ChartSeries>?>(nameof(Series));

    public static readonly StyledProperty<ChartKind> KindProperty =
        AvaloniaProperty.Register<BChart, ChartKind>(nameof(Kind), ChartKind.Line);

    public static readonly StyledProperty<IEnumerable<string>?> LabelsProperty =
        AvaloniaProperty.Register<BChart, IEnumerable<string>?>(nameof(Labels));

    // Design-token palette keys, cycled across series.
    private static readonly string[] PaletteKeys =
        { "BColorPrimary", "BColorInfo", "BColorSuccess", "BColorWarning", "BColorDanger" };

    private static readonly SKColor[] Fallback =
    {
        new(0x25, 0x63, 0xEB), new(0x08, 0x91, 0xB2), new(0x16, 0xA3, 0x4A),
        new(0xD9, 0x77, 0x06), new(0xDC, 0x26, 0x26),
    };

    private readonly CartesianChart _chart = new();

    static BChart()
    {
        SeriesProperty.Changed.AddClassHandler<BChart>((c, _) => c.Rebuild());
        KindProperty.Changed.AddClassHandler<BChart>((c, _) => c.Rebuild());
        LabelsProperty.Changed.AddClassHandler<BChart>((c, _) => c.Rebuild());
    }

    public BChart()
    {
        Content = _chart;
        // Re-resolve the token palette once attached (resources are available then).
        AttachedToVisualTree += (_, _) => Rebuild();
    }

    public IEnumerable<ChartSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public ChartKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IEnumerable<string>? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }

    private void Rebuild()
    {
        var palette = ResolvePalette();
        var list = new List<ISeries>();
        int i = 0;
        foreach (var s in Series ?? Enumerable.Empty<ChartSeries>())
        {
            var color = palette[i % palette.Length];
            var values = s.Values.ToArray();
            if (Kind == ChartKind.Column)
            {
                list.Add(new ColumnSeries<double> { Name = s.Name, Values = values, Fill = new SolidColorPaint(color) });
            }
            else
            {
                list.Add(new LineSeries<double>
                {
                    Name = s.Name,
                    Values = values,
                    Fill = null,
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 3 },
                    GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 3 },
                    GeometryFill = new SolidColorPaint(new SKColor(0xFF, 0xFF, 0xFF)),
                    GeometrySize = 8,
                });
            }
            i++;
        }

        _chart.Series = list;

        if (Labels is not null)
            _chart.XAxes = new[] { new Axis { Labels = Labels.ToList() } };
    }

    private SKColor[] ResolvePalette()
    {
        var colors = new SKColor[PaletteKeys.Length];
        for (int i = 0; i < PaletteKeys.Length; i++)
            colors[i] = this.TryFindResource(PaletteKeys[i], out var v) && v is AvColor c
                ? new SKColor(c.R, c.G, c.B, c.A)
                : Fallback[i];
        return colors;
    }
}
