using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Forms;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

public sealed class Contact
{
    public string? Name { get; set; }
    public bool Active { get; set; }
    public string? Role { get; set; }
}

/// <summary>STORY-033 building blocks: schema-driven Form, Drawer overlay, responsive SplitPanel.</summary>
public class FormTests
{
    private static (Form form, Contact model) Built(params FormField[] fields)
    {
        var model = new Contact { Name = "Ada", Active = true };
        var form = new Form { Fields = fields, Model = model };
        var window = new Window { Content = form, Width = 400, Height = 400 };
        window.Show();
        window.Measure(new Size(400, 400));
        window.Arrange(new Rect(0, 0, 400, 400));
        return (form, model);
    }

    [AvaloniaFact]
    public void Generates_an_input_per_field()
    {
        var (form, _) = Built(
            new FormField { Name = "Name", Type = FieldType.Text },
            new FormField { Name = "Active", Type = FieldType.Checkbox },
            new FormField { Name = "Role", Type = FieldType.Select, Options = new object[] { "Admin", "User" } });

        form.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle();
        form.GetVisualDescendants().OfType<CheckBox>().Should().ContainSingle();
        form.GetVisualDescendants().OfType<ComboBox>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Binds_model_to_inputs_initially()
    {
        var (form, _) = Built(new FormField { Name = "Name", Type = FieldType.Text });
        var box = form.GetVisualDescendants().OfType<TextBox>().Single();
        box.Text.Should().Be("Ada", "model value flows into the generated input");
    }

    [AvaloniaFact]
    public void Checkbox_two_way_writes_back_to_model()
    {
        var (form, model) = Built(new FormField { Name = "Active", Type = FieldType.Checkbox });
        var check = form.GetVisualDescendants().OfType<CheckBox>().Single();
        check.IsChecked.Should().BeTrue();

        check.IsChecked = false;
        model.Active.Should().BeFalse("editing the input updates the model (two-way)");
    }

    [AvaloniaFact]
    public void Required_field_shows_an_asterisk()
    {
        var (form, _) = Built(new FormField { Name = "Name", Type = FieldType.Text, Required = true });
        form.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Should().Contain("*");
    }
}

public class DrawerTests
{
    [AvaloniaFact]
    public void IsOpen_toggles_visibility()
    {
        var drawer = new Drawer { IsOpen = false, Content = new TextBlock { Text = "panel" } };
        var window = new Window { Content = drawer, Width = 400, Height = 300 };
        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        drawer.IsVisible.Should().BeFalse("closed drawer is hidden");

        drawer.IsOpen = true;
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
        drawer.IsVisible.Should().BeTrue("open drawer is shown");
    }
}

public class SplitPanelTests
{
    private static SplitPanel Arrange(double width)
    {
        var split = new SplitPanel
        {
            Master = new TextBlock { Text = "master" },
            Detail = new TextBlock { Text = "detail" },
            CollapseWidth = 640,
            Width = width,               // pin the control's own width (headless window size isn't reliable)
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
        };
        var window = new Window { Content = split, Width = 1000, Height = 400 };
        window.Show();
        window.Measure(new Size(1000, 400));
        window.Arrange(new Rect(0, 0, 1000, 400));
        return split;
    }

    [AvaloniaFact]
    public void Wide_layout_shows_master()
    {
        Arrange(900).IsCollapsed.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Narrow_layout_collapses_master()
    {
        Arrange(400).IsCollapsed.Should().BeTrue();
    }
}
