using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-034: the Avalonia DataGrid (data-table) restyled with Birko tokens.</summary>
public class DataGridTests
{
    private static Application App => Application.Current!;

    [AvaloniaFact]
    public void DataGrid_column_header_uses_token_foreground()
    {
        App.RequestedThemeVariant = BirkoThemeVariants.Light;
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = new[] { new Contact { Name = "Ada" }, new Contact { Name = "Grace" } },
            Columns = { new DataGridTextColumn { Header = "Name", Binding = new global::Avalonia.Data.Binding("Name") } },
        };
        var window = new Window { Content = grid, Width = 400, Height = 300 };
        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        var header = grid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => h.Content as string == "Name");
        header.Should().NotBeNull("the DataGrid rendered its column header");
        (header!.Foreground as ISolidColorBrush)!.Color.Should().Be(Color.Parse("#475569"),
            "BTableHeaderText (var(--b-text-secondary)) drives the header text — proves the Birko restyle applied");
    }
}
