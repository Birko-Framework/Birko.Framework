namespace Birko.Xaml.Core.Forms;

/// <summary>The input kind a <see cref="FormField"/> renders as (the XAML analogue of b-form field types).</summary>
public enum FieldType
{
    Text,
    TextArea,
    Number,
    Checkbox,
    Select,
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

    /// <summary>Options for <see cref="FieldType.Select"/>.</summary>
    public IReadOnlyList<object>? Options { get; init; }

    /// <summary>Disable editing.</summary>
    public bool ReadOnly { get; init; }
}
