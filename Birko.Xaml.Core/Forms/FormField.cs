namespace Birko.Xaml.Core.Forms;

/// <summary>The input kind a <see cref="FormField"/> renders as (the XAML analogue of b-form field types).</summary>
public enum FieldType
{
    Text,
    TextArea,
    Number,
    Percent,
    Range,
    Password,
    Email,
    Search,
    Checkbox,
    Switch,
    Select,
    Radio,
    OptionGroup,
    Markdown,
    Date,
    Time,
    DateTime,
    DateRange,
}

/// <summary>
/// One field in a schema-driven form. Platform-neutral (Avalonia-free) so the same schema drives
/// the Avalonia <c>Form</c> control today and a WPF one later. Bound to a model property by
/// <see cref="Name"/>.
/// </summary>
public sealed class FormField
{
    /// <summary>The model property this field reads/writes (the binding path).</summary>
    public required string Name { get; init; }

    /// <summary>Display label; falls back to <see cref="Name"/> when null.</summary>
    public string? Label { get; init; }

    public FieldType Type { get; init; } = FieldType.Text;

    /// <summary>Marks the field required (shows an asterisk; a consumer/VM enforces the rule).</summary>
    public bool Required { get; init; }

    /// <summary>Placeholder / watermark for text-like fields.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Options for <see cref="FieldType.Select"/> / <see cref="FieldType.Radio"/> / <see cref="FieldType.OptionGroup"/>.</summary>
    public IReadOnlyList<object>? Options { get; init; }

    /// <summary>Disable editing.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Optional helper text shown under the field.</summary>
    public string? Hint { get; init; }

    /// <summary>Initial value applied to the model property when it is null at bind time.</summary>
    public object? Default { get; init; }

    /// <summary>Lower bound for numeric / range fields (<see cref="FieldType.Number"/>, <see cref="FieldType.Percent"/>).</summary>
    public double? Min { get; init; }

    /// <summary>Upper bound for numeric / range fields.</summary>
    public double? Max { get; init; }

    /// <summary>Step increment for range / spinner fields (carried for slider-style consumers).</summary>
    public double? Step { get; init; }
}
