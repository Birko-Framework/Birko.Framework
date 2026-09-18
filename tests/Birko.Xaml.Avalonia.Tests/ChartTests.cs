using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Charts;
using FluentAssertions;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-035: the chart (b-chart) over LiveCharts2.</summary>
public class ChartTests
{
    private static BChart Show(BChart chart)
    {
        var window = new Window { Content = chart, Width = 500, Height = 300 };
        window.Show();
        window.Measure(new Size(500, 300));
        window.Arrange(new Rect(0, 0, 500, 300));
        Dispatcher.UIThread.RunJobs();
        return chart;
    }

    private static CartesianChart Inner(BChart chart) =>
        chart.GetVisualDescendants().OfType<CartesianChart>().Single();

    [AvaloniaFact]
    public void Configures_a_series_per_model_entry()
    {
        var chart = Show(new BChart
        {
            Series = new[]
            {
                new ChartSeries { Name = "Sales", Values = new double[] { 1, 3, 2, 5 } },
                new ChartSeries { Name = "Costs", Values = new double[] { 2, 2, 1, 3 } },
            },
            Labels = new[] { "Q1", "Q2", "Q3", "Q4" },
        });

        Inner(chart).Series!.Count().Should().Be(2);
        Inner(chart).Series!.First().Should().BeOfType<LineSeries<double>>();
    }

    [AvaloniaFact]
    public void Column_kind_uses_column_series()
    {
        var chart = Show(new BChart
        {
            Kind = ChartKind.Column,
            Series = new[] { new ChartSeries { Name = "A", Values = new double[] { 1, 2, 3 } } },
        });
        Inner(chart).Series!.First().Should().BeOfType<ColumnSeries<double>>();
    }

    [AvaloniaFact]
    public void Capture_chart_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var chart = new BChart
        {
            Margin = new Thickness(16),
            Kind = ChartKind.Line,
            Labels = new[] { "Jan", "Feb", "Mar", "Apr", "May" },
            Series = new[]
            {
                new ChartSeries { Name = "Revenue", Values = new double[] { 12, 18, 15, 24, 30 } },
                new ChartSeries { Name = "Costs", Values = new double[] { 8, 10, 9, 12, 14 } },
            },
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#FFFFFF")), Child = chart };
        var window = new Window { Content = page, Width = 560, Height = 340 };
        window.Show();
        window.Measure(new Size(560, 340));
        window.Arrange(new Rect(0, 0, 560, 340));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "chart.png"));
        Inner(chart).Series!.Count().Should().Be(2);
    }
}
