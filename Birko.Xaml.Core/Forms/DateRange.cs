using CommunityToolkit.Mvvm.ComponentModel;

namespace Birko.Xaml.Core.Forms;

/// <summary>
/// A start/end date pair — the value type a <see cref="FieldType.DateRange"/> form field binds to
/// (the XAML analogue of the web date-range value). Mutable + observable so the two pickers in the
/// composite update the shared instance held on the model. Platform-neutral (Avalonia-free).
/// </summary>
public partial class DateRange : ObservableObject
{
    [ObservableProperty]
    private DateTime? _from;

    [ObservableProperty]
    private DateTime? _to;

    public DateRange() { }

    public DateRange(DateTime? from, DateTime? to)
    {
        _from = from;
        _to = to;
    }
}
