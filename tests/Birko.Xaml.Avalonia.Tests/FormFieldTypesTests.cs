using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Forms;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

// Covers the Form field-type parity work (EPIC-016 / TASK-055): the new FieldTypes map to the right
// restyled controls, two-way binding + defaults + numeric clamp + hint all behave.
public class FormFieldTypesTests
{
    private sealed class Model
    {
        public bool Active { get; set; }
        public string? Text { get; set; }
        public string? Color { get; set; }
        public string? Plan { get; set; }
        public double Amount { get; set; }
    }

    private static Form Show(object model, params FormField[] fields)
    {
        var form = new Form { Model = model, Fields = fields };
        var window = new Window { Content = form, Width = 420, Height = 420 };
        window.Show();
        window.Measure(new global::Avalonia.Size(420, 420));
        window.Arrange(new global::Avalonia.Rect(0, 0, 420, 420));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return form;
    }

    [AvaloniaFact]
    public void Switch_field_renders_toggleswitch_and_writes_back()
    {
        var model = new Model();
        var form = Show(model, new FormField { Name = nameof(Model.Active), Type = FieldType.Switch });

        var toggle = form.GetVisualDescendants().OfType<ToggleSwitch>().Should().ContainSingle().Subject;
        toggle.IsChecked = true;
        model.Active.Should().BeTrue("toggling the switch writes back to the model");
    }

    [AvaloniaFact]
    public void Markdown_field_renders_markdown_editor()
    {
        var form = Show(new Model(), new FormField { Name = nameof(Model.Text), Type = FieldType.Markdown });
        form.GetVisualDescendants().OfType<MarkdownEditor>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Password_field_masks_input()
    {
        var form = Show(new Model(), new FormField { Name = nameof(Model.Text), Type = FieldType.Password });
        var box = form.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle().Subject;
        box.PasswordChar.Should().Be('●');
    }

    [AvaloniaFact]
    public void Radio_field_renders_option_per_choice_and_reflects_the_model()
    {
        var model = new Model { Color = "Green" };
        var form = Show(model, new FormField
        {
            Name = nameof(Model.Color), Type = FieldType.Radio,
            Options = new object[] { "Red", "Green", "Blue" },
        });

        var radios = form.GetVisualDescendants().OfType<RadioButton>().ToList();
        radios.Should().HaveCount(3);
        radios.Single(r => (string?)r.Content == "Green").IsChecked.Should().BeTrue("initial model value selects its radio");

        // Selecting another writes it back.
        radios.Single(r => (string?)r.Content == "Blue").IsChecked = true;
        model.Color.Should().Be("Blue");
    }

    [AvaloniaFact]
    public void OptionGroup_lays_radios_out_horizontally()
    {
        var form = Show(new Model(), new FormField
        {
            Name = nameof(Model.Color), Type = FieldType.OptionGroup,
            Options = new object[] { "A", "B" },
        });
        var panel = form.GetVisualDescendants().OfType<StackPanel>()
            .First(p => p.Children.OfType<RadioButton>().Any()); // the radio-group panel (direct children)
        panel.Orientation.Should().Be(Orientation.Horizontal);
    }

    [AvaloniaFact]
    public void Default_is_applied_when_the_model_property_is_null()
    {
        var model = new Model(); // Plan == null
        Show(model, new FormField { Name = nameof(Model.Plan), Type = FieldType.Text, Default = "Pro" });
        model.Plan.Should().Be("Pro");
    }

    [AvaloniaFact]
    public void Number_field_clamps_to_min_max_on_commit()
    {
        var model = new Model();
        var form = Show(model, new FormField
        {
            Name = nameof(Model.Amount), Type = FieldType.Number, Min = 0, Max = 100,
        });
        var box = form.GetVisualDescendants().OfType<TextBox>().Single();

        box.Text = "150";
        box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        box.Text.Should().Be("100", "over-max input clamps to Max on commit");
        model.Amount.Should().Be(100);
    }

    [AvaloniaFact]
    public void Hint_is_rendered_under_the_field()
    {
        var form = Show(new Model(), new FormField { Name = nameof(Model.Text), Type = FieldType.Text, Hint = "we never share it" });
        form.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "we never share it").Should().BeTrue();
    }
}
