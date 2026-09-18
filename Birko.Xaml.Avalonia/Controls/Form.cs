using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Birko.Xaml.Core.Forms;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Schema-driven form (the XAML port of <c>b-form</c>): a bound <see cref="Fields"/> schema
/// generates labeled inputs two-way bound to <see cref="Model"/>. This is what keeps
/// <c>CrudViewModelBase</c>/<c>DetailPageViewModel</c> declarative — a view binds
/// <c>Fields</c> + <c>Model</c> (the VM's <c>EditingItem</c>/<c>Model</c>) instead of hand-rolling
/// XAML per screen. Every value comes from design tokens; the generated inputs reuse the Birko
/// restyled control themes (<c>TextBox</c>/<c>CheckBox</c>/<c>ToggleSwitch</c>/<c>ComboBox</c>/
/// <c>RadioButton</c>/<c>MarkdownEditor</c>).
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

        ApplyDefaults();

        var panel = new StackPanel();
        foreach (var field in Fields)
            panel.Children.Add(BuildRow(field));
        Content = panel;
    }

    // Seed the model property from FormField.Default when it is currently null (a "new record" default).
    private void ApplyDefaults()
    {
        foreach (var field in Fields!)
        {
            if (field.Default is null) continue;
            var prop = Model!.GetType().GetProperty(field.Name);
            if (prop is null || !prop.CanWrite || prop.GetValue(Model) is not null) continue;
            try
            {
                var target = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                prop.SetValue(Model, Convert.ChangeType(field.Default, target, CultureInfo.InvariantCulture));
            }
            catch { /* best-effort: an incompatible default is ignored */ }
        }
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

        if (!string.IsNullOrEmpty(field.Hint))
        {
            var hint = new TextBlock { Text = field.Hint, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            Themed(hint, TextBlock.ForegroundProperty, "BTextMutedBrush");
            Themed(hint, TextBlock.FontFamilyProperty, "BFont");
            row.Children.Add(hint);
        }

        return row;
    }

    private Control BuildInput(FormField field)
    {
        var mode = field.ReadOnly ? BindingMode.OneWay : BindingMode.TwoWay;
        Binding Bound() => new Binding(field.Name) { Source = Model, Mode = mode };

        switch (field.Type)
        {
            case FieldType.Checkbox:
                var check = new CheckBox { IsEnabled = !field.ReadOnly };
                check.Bind(ToggleButton.IsCheckedProperty, Bound());
                return check;

            case FieldType.Switch:
                var toggle = new ToggleSwitch { IsEnabled = !field.ReadOnly };
                toggle.Bind(ToggleButton.IsCheckedProperty, Bound());
                return toggle;

            case FieldType.Select:
                var combo = new ComboBox
                {
                    ItemsSource = field.Options,
                    IsEnabled = !field.ReadOnly,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                combo.Bind(SelectingItemsControl.SelectedItemProperty, Bound());
                return combo;

            case FieldType.MultiSelect:
                return BuildMultiSelect(field);

            case FieldType.Tags:
                return BuildTags(field);

            case FieldType.File:
                return BuildFile(field);

            case FieldType.Radio:
                return BuildRadioGroup(field, Orientation.Vertical);

            case FieldType.OptionGroup:
                return BuildRadioGroup(field, Orientation.Horizontal);

            case FieldType.TextArea:
                var area = new TextBox
                {
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Watermark = field.Placeholder,
                    IsReadOnly = field.ReadOnly,
                };
                area.Bind(TextBox.TextProperty, Bound());
                return area;

            case FieldType.Markdown:
                var md = new MarkdownEditor { IsEnabled = !field.ReadOnly };
                md.Bind(MarkdownEditor.MarkdownProperty, Bound());
                return md;

            case FieldType.Date:
                var date = new CalendarDatePicker { IsEnabled = !field.ReadOnly };
                date.Bind(CalendarDatePicker.SelectedDateProperty, Bound());
                return date;

            case FieldType.Time:
                var time = new TimePicker { IsEnabled = !field.ReadOnly };
                time.Bind(TimePicker.SelectedTimeProperty, Bound());
                return time;

            case FieldType.DateTime:
                return BuildDateTime(field);

            case FieldType.DateRange:
                return BuildDateRange(field);

            case FieldType.Password:
                var pwd = new TextBox
                {
                    Watermark = field.Placeholder,
                    IsReadOnly = field.ReadOnly,
                    PasswordChar = '●', // ●
                };
                pwd.Bind(TextBox.TextProperty, Bound());
                return pwd;

            case FieldType.Range:
                var slider = new Slider
                {
                    Minimum = field.Min ?? 0,
                    Maximum = field.Max ?? 100,
                    IsEnabled = !field.ReadOnly,
                };
                if (field.Step is double step)
                {
                    slider.SmallChange = step;
                    slider.TickFrequency = step;
                    slider.IsSnapToTickEnabled = true;
                }
                slider.Bind(RangeBase.ValueProperty, Bound());
                return slider;

            case FieldType.Number:
            case FieldType.Percent:
                var num = new TextBox { Watermark = field.Placeholder, IsReadOnly = field.ReadOnly };
                num.Bind(TextBox.TextProperty, Bound());
                if (!field.ReadOnly && (field.Min is not null || field.Max is not null))
                    num.LostFocus += (_, _) => ClampNumeric(num, field);
                return num;

            default: // Text, Email, Search — plain text box (semantic type; desktop has no input-type widget)
                var box = new TextBox { Watermark = field.Placeholder, IsReadOnly = field.ReadOnly };
                box.Bind(TextBox.TextProperty, Bound());
                return box;
        }
    }

    // Multi-select over Options: a restyled multi-ListBox synced to an IList model prop (SelectedItems
    // isn't bindable, so we sync on SelectionChanged).
    private Control BuildMultiSelect(FormField field)
    {
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            ItemsSource = field.Options,
            IsEnabled = !field.ReadOnly,
            MaxHeight = 160,
        };
        var prop = Model!.GetType().GetProperty(field.Name);
        var current = prop?.GetValue(Model) as IList;
        if (current is not null)
            foreach (var item in current)
                list.SelectedItems!.Add(item);

        if (!field.ReadOnly && prop is not null && prop.CanWrite)
        {
            list.SelectionChanged += (_, _) =>
            {
                if (prop.GetValue(Model) is not IList target) return;
                target.Clear();
                foreach (var it in list.SelectedItems!) target.Add(it);
            };
        }
        return list;
    }

    // Freeform tags: a WrapPanel of removable chips + a text box (Enter adds, Backspace removes the last).
    private Control BuildTags(FormField field)
    {
        var prop = Model!.GetType().GetProperty(field.Name);
        var tags = prop?.GetValue(Model) as IList<string>;
        if (tags is null && prop is not null && prop.CanWrite)
        {
            tags = new List<string>();
            try { prop.SetValue(Model, tags); } catch { tags = null; }
        }

        var wrap = new WrapPanel();
        var input = new TextBox { Watermark = field.Placeholder, MinWidth = 90, IsReadOnly = field.ReadOnly, BorderThickness = new Thickness(0) };

        void Rebuild()
        {
            wrap.Children.Clear();
            if (tags is not null)
            {
                foreach (var t in tags.ToList())
                {
                    var value = t;
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 2),
                        Margin = new Thickness(0, 0, 4, 4),
                    };
                    Themed(chip, Border.BackgroundProperty, "BColorPrimaryLightBrush");
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    row.Children.Add(new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center });
                    if (!field.ReadOnly)
                    {
                        var x = new Button { Content = "✕", Background = null, BorderThickness = new Thickness(0), Padding = new Thickness(2, 0), FontSize = 11 };
                        // A glyph is not a name: this announced as "✕" with no clue WHICH tag it removes, and
                        // a tag list renders one per tag. Found sweeping the skin after the ribbon's commands
                        // turned out to be anonymous (see this project's CLAUDE.md § Accessibility gotchas).
                        global::Avalonia.Automation.AutomationProperties.SetName(x, $"Remove {value}");
                        ToolTip.SetTip(x, $"Remove {value}");
                        x.Click += (_, _) => { tags!.Remove(value); Rebuild(); };
                        row.Children.Add(x);
                    }
                    chip.Child = row;
                    wrap.Children.Add(chip);
                }
            }
            wrap.Children.Add(input);
        }

        if (!field.ReadOnly && tags is not null)
        {
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text))
                {
                    tags.Add(input.Text.Trim());
                    input.Text = string.Empty;
                    Rebuild();
                }
                else if (e.Key == Key.Back && string.IsNullOrEmpty(input.Text) && tags.Count > 0)
                {
                    tags.RemoveAt(tags.Count - 1);
                    Rebuild();
                }
            };
        }

        Rebuild();
        return wrap;
    }

    // File pick: a read-only path box (bound to a string prop) + a Browse button using the platform StorageProvider.
    private Control BuildFile(FormField field)
    {
        var box = new TextBox { IsReadOnly = true, Watermark = field.Placeholder ?? "No file selected", MinWidth = 200 };
        box.Bind(TextBox.TextProperty, new Binding(field.Name) { Source = Model, Mode = BindingMode.TwoWay });

        var browse = new Button { Content = "Browse…", IsEnabled = !field.ReadOnly, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } storage) return; // headless / unsupported → no-op
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = field.Label ?? "Select a file",
            });
            if (files.Count > 0)
                box.Text = files[0].TryGetLocalPath() ?? files[0].Name;
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(box);
        panel.Children.Add(browse);
        return panel;
    }

    // A single-select group of RadioButtons over Options, each two-way bound to the model value via an
    // equality converter (checked ⇔ model == option); clicking one writes that option back.
    private Control BuildRadioGroup(FormField field, Orientation orientation)
    {
        var panel = new StackPanel
        {
            Orientation = orientation,
            Spacing = orientation == Orientation.Horizontal ? 12 : 4,
        };
        foreach (var option in field.Options ?? Array.Empty<object>())
        {
            var rb = new RadioButton
            {
                Content = option?.ToString(),
                GroupName = "grp_" + field.Name,
                IsEnabled = !field.ReadOnly,
            };
            rb.Bind(ToggleButton.IsCheckedProperty, new Binding(field.Name)
            {
                Source = Model,
                Mode = field.ReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                Converter = OptionEquals,
                ConverterParameter = option,
            });
            panel.Children.Add(rb);
        }
        return panel;
    }

    // DateTime = a date picker + a time picker; either change recombines into the single DateTime? model prop.
    private Control BuildDateTime(FormField field)
    {
        var prop = Model!.GetType().GetProperty(field.Name);
        var current = prop?.GetValue(Model) as DateTime?;
        var datePick = new CalendarDatePicker { SelectedDate = current?.Date, IsEnabled = !field.ReadOnly };
        var timePick = new TimePicker { SelectedTime = current?.TimeOfDay, IsEnabled = !field.ReadOnly };

        if (!field.ReadOnly && prop is not null && prop.CanWrite)
        {
            void Combine()
            {
                var d = datePick.SelectedDate;
                DateTime? val = d is null ? null : d.Value.Date + (timePick.SelectedTime ?? TimeSpan.Zero);
                try { prop.SetValue(Model, val); } catch { /* prop not DateTime? — ignore */ }
            }
            datePick.PropertyChanged += (_, e) => { if (e.Property == CalendarDatePicker.SelectedDateProperty) Combine(); };
            timePick.PropertyChanged += (_, e) => { if (e.Property == TimePicker.SelectedTimeProperty) Combine(); };
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(datePick);
        panel.Children.Add(timePick);
        return panel;
    }

    // DateRange = two date pickers writing the From/To of a shared DateRange value on the model.
    private Control BuildDateRange(FormField field)
    {
        var prop = Model!.GetType().GetProperty(field.Name);
        var range = prop?.GetValue(Model) as DateRange;
        if (range is null && prop is not null && prop.CanWrite)
        {
            range = new DateRange();
            prop.SetValue(Model, range);
        }

        var from = new CalendarDatePicker { SelectedDate = range?.From, IsEnabled = !field.ReadOnly };
        var to = new CalendarDatePicker { SelectedDate = range?.To, IsEnabled = !field.ReadOnly };
        if (!field.ReadOnly && range is not null)
        {
            from.PropertyChanged += (_, e) => { if (e.Property == CalendarDatePicker.SelectedDateProperty) range.From = from.SelectedDate; };
            to.PropertyChanged += (_, e) => { if (e.Property == CalendarDatePicker.SelectedDateProperty) range.To = to.SelectedDate; };
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(from);
        var dash = new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center };
        Themed(dash, TextBlock.ForegroundProperty, "BTextMutedBrush");
        panel.Children.Add(dash);
        panel.Children.Add(to);
        return panel;
    }

    private static void ClampNumeric(TextBox box, FormField field)
    {
        if (!double.TryParse(box.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var v)) return;
        var clamped = v;
        if (field.Min is double min) clamped = Math.Max(clamped, min);
        if (field.Max is double max) clamped = Math.Min(clamped, max);
        if (clamped != v) box.Text = clamped.ToString(CultureInfo.CurrentCulture);
    }

    private static readonly IValueConverter OptionEquals = new OptionEqualsConverter();

    private sealed class OptionEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Equals(value, parameter);

        // Only the newly-checked radio writes back; unchecking is a no-op (its peer wrote the new value).
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? parameter : BindingOperations.DoNothing;
    }

    // Follow a design token by observable so generated controls re-theme with the app.
    private static void Themed(Control control, AvaloniaProperty property, string tokenKey) =>
        control.Bind(property, control.GetResourceObservable(tokenKey));
}
