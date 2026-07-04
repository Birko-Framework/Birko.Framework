using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Birko.Xaml.Core.Forms;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Schema-driven form (the XAML port of <c>b-form</c>): a bound <see cref="Fields"/> schema
/// generates labeled inputs two-way bound to <see cref="Model"/>. This is what keeps
/// <c>CrudViewModelBase</c>/<c>DetailPageViewModel</c> declarative — a view binds
/// <c>Fields</c> + <c>Model</c> (the VM's <c>EditingItem</c>/<c>Model</c>) instead of hand-rolling
/// XAML per screen. Every value comes from design tokens; the generated inputs reuse the Birko
/// restyled <c>TextBox</c>/<c>CheckBox</c>/<c>ComboBox</c> themes.
/// </summary>
public class Form : ContentControl
{
    public static readonly StyledProperty<IEnumerable<FormField>?> FieldsProperty =
        AvaloniaProperty.Register<Form, IEnumerable<FormField>?>(nameof(Fields));

    public static readonly StyledProperty<object?> ModelProperty =
        AvaloniaProperty.Register<Form, object?>(nameof(Model));

    static Form()
    {
        FieldsProperty.Changed.AddClassHandler<Form>((f, _) => f.Rebuild());
        ModelProperty.Changed.AddClassHandler<Form>((f, _) => f.Rebuild());
    }

    public IEnumerable<FormField>? Fields
    {
        get => GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    public object? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private void Rebuild()
    {
        if (Fields is null || Model is null)
        {
            Content = null;
            return;
        }

        var panel = new StackPanel();
        foreach (var field in Fields)
            panel.Children.Add(BuildRow(field));
        Content = panel;
    }

    private Control BuildRow(FormField field)
    {
        var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 12) };

        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var label = new TextBlock { Text = field.Label ?? field.Name };
        Themed(label, TextBlock.ForegroundProperty, "BTextSecondaryBrush");
        Themed(label, TextBlock.FontFamilyProperty, "BFont");
        labelRow.Children.Add(label);
        if (field.Required)
        {
            var star = new TextBlock { Text = "*" };
            Themed(star, TextBlock.ForegroundProperty, "BColorDangerBrush");
            labelRow.Children.Add(star);
        }
        row.Children.Add(labelRow);

        row.Children.Add(BuildInput(field));
        return row;
    }

    private Control BuildInput(FormField field)
    {
        var mode = field.ReadOnly ? BindingMode.OneWay : BindingMode.TwoWay;
        var binding = new Binding(field.Name) { Source = Model, Mode = mode };

        switch (field.Type)
        {
            case FieldType.Checkbox:
                var check = new CheckBox { IsEnabled = !field.ReadOnly };
                check.Bind(ToggleButton.IsCheckedProperty, binding);
                return check;

            case FieldType.Select:
                var combo = new ComboBox
                {
                    ItemsSource = field.Options,
                    IsEnabled = !field.ReadOnly,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                combo.Bind(SelectingItemsControl.SelectedItemProperty, binding);
                return combo;

            case FieldType.TextArea:
                var area = new TextBox
                {
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Watermark = field.Placeholder,
                    IsReadOnly = field.ReadOnly,
                };
                area.Bind(TextBox.TextProperty, binding);
                return area;

            default: // Text, Number
                var box = new TextBox { Watermark = field.Placeholder, IsReadOnly = field.ReadOnly };
                box.Bind(TextBox.TextProperty, binding);
                return box;
        }
    }

    // Follow a design token by observable so generated controls re-theme with the app.
    private static void Themed(Control control, AvaloniaProperty property, string tokenKey) =>
        control.Bind(property, control.GetResourceObservable(tokenKey));
}
