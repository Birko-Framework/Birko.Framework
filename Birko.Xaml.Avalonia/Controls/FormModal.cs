using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Birko.Xaml.Core.Forms;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// A create/edit dialog (the XAML port of <c>b-form-modal</c> / the epic's <c>FormModal&lt;T&gt;</c>):
/// composes a <see cref="Modal"/> hosting a schema-driven <see cref="Form"/> plus Save/Cancel. Bind
/// <see cref="IsOpen"/>, <see cref="Fields"/> and <see cref="Model"/> (e.g. a VM's <c>EditingItem</c>);
/// Save runs <see cref="SaveCommand"/> and Cancel runs <see cref="CancelCommand"/>, both then close.
/// </summary>
public class FormModal : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<FormModal, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<FormModal, string?>(nameof(Title));

    public static readonly StyledProperty<IEnumerable<FormField>?> FieldsProperty =
        AvaloniaProperty.Register<FormModal, IEnumerable<FormField>?>(nameof(Fields));

    public static readonly StyledProperty<object?> ModelProperty =
        AvaloniaProperty.Register<FormModal, object?>(nameof(Model));

    public static readonly StyledProperty<ICommand?> SaveCommandProperty =
        AvaloniaProperty.Register<FormModal, ICommand?>(nameof(SaveCommand));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<FormModal, ICommand?>(nameof(CancelCommand));

    public FormModal()
    {
        var form = new Form();
        form.Bind(Form.FieldsProperty, new Binding(nameof(Fields)) { Source = this });
        form.Bind(Form.ModelProperty, new Binding(nameof(Model)) { Source = this });

        var save = new Button { Content = "Save" };
        save.Click += (_, _) => { SaveCommand?.Execute(Model); IsOpen = false; };
        var cancel = new Button { Content = "Cancel" };
        cancel.Bind(BackgroundProperty, cancel.GetResourceObservable("BColorSecondaryBrush"));
        cancel.Click += (_, _) => { CancelCommand?.Execute(Model); IsOpen = false; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(form);
        body.Children.Add(buttons);

        var modal = new Modal { Content = body };
        modal.Bind(Modal.IsOpenProperty, new Binding(nameof(IsOpen)) { Source = this, Mode = BindingMode.TwoWay });
        modal.Bind(Modal.TitleProperty, new Binding(nameof(Title)) { Source = this });

        Content = modal;
    }

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public IEnumerable<FormField>? Fields { get => GetValue(FieldsProperty); set => SetValue(FieldsProperty, value); }
    public object? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public ICommand? SaveCommand { get => GetValue(SaveCommandProperty); set => SetValue(SaveCommandProperty, value); }
    public ICommand? CancelCommand { get => GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
}
