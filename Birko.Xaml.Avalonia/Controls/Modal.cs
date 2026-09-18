using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Centered modal dialog (the XAML port of <c>b-modal</c>): a token-styled card over a dimming
/// backdrop, shown while <see cref="IsOpen"/>, with an optional <see cref="Title"/> header. Place it
/// in a Grid/Panel spanning the region it overlays; clicking the backdrop closes it. Compose a
/// "FormModal" by putting a <see cref="Form"/> + Save/Cancel in its content.
/// </summary>
[PseudoClasses(":open")]
public class Modal : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<Modal, bool>(nameof(IsOpen));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Modal, string?>(nameof(Title));

    static Modal() => IsOpenProperty.Changed.AddClassHandler<Modal>((m, _) => m.UpdatePseudoClasses());

    public Modal() => UpdatePseudoClasses();

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Control>("PART_Backdrop") is { } backdrop)
            backdrop.PointerPressed += (_, _) => IsOpen = false;
    }

    private void UpdatePseudoClasses() => PseudoClasses.Set(":open", IsOpen);
}
