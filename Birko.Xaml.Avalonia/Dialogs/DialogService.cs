using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Dialogs;
using Birko.Xaml.Core.Forms;
using Birko.Xaml.Core.Localization;

namespace Birko.Xaml.Avalonia.Dialogs;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/> — the XAML port of
/// <c>birko-web-components/dialogs</c>. Each dialog is rendered as a token-styled <see cref="Modal"/>
/// (or a spinner/toast overlay) added to a host <see cref="Panel"/> that spans the window, and
/// removed when answered. The host is supplied by a provider so a view-model can hold only the
/// Avalonia-free <see cref="IDialogService"/>; a shell/app wires the provider to its overlay layer.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Func<Panel?> _hostProvider;
    private readonly II18n? _i18n;

    public DialogService(Func<Panel?> hostProvider, II18n? i18n = null)
    {
        _hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
        _i18n = i18n;
    }

    /// <summary>Convenience: host dialogs in the given panel (e.g. the shell's root Grid).</summary>
    public DialogService(Panel host, II18n? i18n = null) : this(() => host, i18n) { }

    private Panel Host => _hostProvider() ?? throw new InvalidOperationException(
        "DialogService has no host panel — wire the provider to a window-spanning Panel before showing a dialog.");

    /// <summary>Resolve a localized string, falling back to <paramref name="fallback"/> when the key is
    /// absent (the II18n indexer echoes the key back when unknown).</summary>
    private string Loc(string key, string fallback)
    {
        var v = _i18n?[key];
        return string.IsNullOrEmpty(v) || v == key ? fallback : v!;
    }

    // ── confirm / confirmDelete ──────────────────────────────────────────────

    public Task<bool> ConfirmAsync(string message, ConfirmOptions? options = null)
    {
        var opts = options ?? new ConfirmOptions();
        var tcs = new TaskCompletionSource<bool>();

        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(MessageText(message));

        var buttons = ButtonRow();
        var cancel = MakeButton(opts.CancelText ?? Loc("bxaml.dialog.cancel", "Cancel"), DialogVariant.Primary, secondary: true);
        var ok = MakeButton(opts.ConfirmText ?? Loc("bxaml.dialog.confirm", "Confirm"), opts.Variant);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);

        var modal = new Modal { Title = opts.Title ?? Loc("bxaml.dialog.confirmTitle", "Confirm"), Content = body, IsOpen = true };
        Present(modal);

        // Set the result BEFORE dismissing: Dismiss() flips IsOpen=false, which re-enters the
        // backdrop-cancel handler — guarding on TrySetResult's return keeps the first answer.
        void Close(bool result)
        {
            if (tcs.TrySetResult(result)) Dismiss(modal);
        }
        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        WireBackdropCancel(modal, () => Close(false));
        return tcs.Task;
    }

    public Task<bool> ConfirmDeleteAsync(string message, ConfirmOptions? options = null)
    {
        var opts = options ?? new ConfirmOptions();
        return ConfirmAsync(message, new ConfirmOptions
        {
            Title = opts.Title,
            Variant = opts.Variant == DialogVariant.Primary ? DialogVariant.Danger : opts.Variant,
            ConfirmText = opts.ConfirmText ?? Loc("bxaml.dialog.delete", "Delete"),
            CancelText = opts.CancelText ?? Loc("bxaml.dialog.cancel", "Cancel"),
        });
    }

    // ── alert ─────────────────────────────────────────────────────────────────

    public Task AlertAsync(string message, string? title = null, string? okText = null)
    {
        var tcs = new TaskCompletionSource();

        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(MessageText(message));
        var buttons = ButtonRow();
        var ok = MakeButton(okText ?? Loc("bxaml.dialog.ok", "OK"), DialogVariant.Primary);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);

        var modal = new Modal { Title = title ?? Loc("bxaml.dialog.noticeTitle", "Notice"), Content = body, IsOpen = true };
        Present(modal);

        void Close()
        {
            if (tcs.TrySetResult()) Dismiss(modal);
        }
        ok.Click += (_, _) => Close();
        WireBackdropCancel(modal, Close);
        return tcs.Task;
    }

    // ── prompt ──────────────────────────────────────────────────────────────

    public Task<string?> PromptAsync(string message, PromptOptions? options = null)
    {
        var opts = options ?? new PromptOptions();
        var tcs = new TaskCompletionSource<string?>();

        var body = new StackPanel { Spacing = 12 };
        if (!string.IsNullOrEmpty(message)) body.Children.Add(MessageText(message));
        var input = new TextBox { Text = opts.DefaultValue ?? string.Empty, Watermark = opts.Placeholder };
        body.Children.Add(input);
        var error = new TextBlock { IsVisible = false, FontSize = 12 };
        error[!TextBlock.ForegroundProperty] = Dyn("BColorDangerBrush");
        body.Children.Add(error);

        var buttons = ButtonRow();
        var cancel = MakeButton(opts.CancelText ?? Loc("bxaml.dialog.cancel", "Cancel"), DialogVariant.Primary, secondary: true);
        var ok = MakeButton(opts.ConfirmText ?? Loc("bxaml.dialog.ok", "OK"), DialogVariant.Primary);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);

        var modal = new Modal { Title = opts.Title ?? Loc("bxaml.dialog.promptTitle", "Prompt"), Content = body, IsOpen = true };
        Present(modal);

        void Finish(string? value)
        {
            if (tcs.TrySetResult(value)) Dismiss(modal);
        }
        void Submit()
        {
            var value = input.Text ?? string.Empty;
            if (opts.Required && string.IsNullOrEmpty(value))
            {
                error.Text = Loc("bxaml.dialog.required", "This field is required");
                error.IsVisible = true;
                return;
            }
            Finish(value);
        }
        ok.Click += (_, _) => Submit();
        cancel.Click += (_, _) => Finish(null);
        WireBackdropCancel(modal, () => Finish(null));
        return tcs.Task;
    }

    // ── choose ────────────────────────────────────────────────────────────────

    public Task<T?> ChooseAsync<T>(string message, IReadOnlyList<ChooseOption<T>> options, string? title = null)
    {
        var tcs = new TaskCompletionSource<T?>();

        var body = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(message)) body.Children.Add(MessageText(message));
        var list = new StackPanel { Spacing = 8 };
        body.Children.Add(list);
        var buttons = ButtonRow();
        var cancel = MakeButton(Loc("bxaml.dialog.cancel", "Cancel"), DialogVariant.Primary, secondary: true);
        buttons.Children.Add(cancel);
        body.Children.Add(buttons);

        var modal = new Modal { Title = title ?? Loc("bxaml.dialog.chooseTitle", "Choose"), Content = body, IsOpen = true };

        void Finish(T? value)
        {
            if (tcs.TrySetResult(value)) Dismiss(modal);
        }
        foreach (var opt in options)
        {
            var btn = MakeButton(opt.Label, opt.Variant, secondary: opt.Variant == DialogVariant.Primary ? false : false);
            btn.HorizontalAlignment = HorizontalAlignment.Stretch;
            var captured = opt.Value;
            btn.Click += (_, _) => Finish(captured);
            list.Children.Add(btn);
        }
        cancel.Click += (_, _) => Finish(default);
        Present(modal);
        WireBackdropCancel(modal, () => Finish(default));
        return tcs.Task;
    }

    // ── promptForm ────────────────────────────────────────────────────────────

    public Task<T?> PromptFormAsync<T>(T model, IReadOnlyList<FormField> fields, string? title = null) where T : class
    {
        var tcs = new TaskCompletionSource<T?>();

        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(new Form { Fields = fields, Model = model });
        var buttons = ButtonRow();
        var cancel = MakeButton(Loc("bxaml.dialog.cancel", "Cancel"), DialogVariant.Primary, secondary: true);
        var save = MakeButton(Loc("bxaml.dialog.save", "Save"), DialogVariant.Primary);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        body.Children.Add(buttons);

        var modal = new Modal { Title = title ?? Loc("bxaml.dialog.formTitle", "Details"), Content = body, IsOpen = true };
        Present(modal);

        void Finish(T? value)
        {
            if (tcs.TrySetResult(value)) Dismiss(modal);
        }
        save.Click += (_, _) => Finish(model);
        cancel.Click += (_, _) => Finish(null);
        WireBackdropCancel(modal, () => Finish(null));
        return tcs.Task;
    }

    // ── busy ────────────────────────────────────────────────────────────────

    public async Task<T> BusyAsync<T>(Func<Task<T>> work, string? message = null)
    {
        var overlay = BuildBusyOverlay(message);
        Host.Children.Add(overlay);
        try { return await work().ConfigureAwait(true); }
        finally { Host.Children.Remove(overlay); }
    }

    public async Task BusyAsync(Func<Task> work, string? message = null)
    {
        var overlay = BuildBusyOverlay(message);
        Host.Children.Add(overlay);
        try { await work().ConfigureAwait(true); }
        finally { Host.Children.Remove(overlay); }
    }

    private Control BuildBusyOverlay(string? message)
    {
        var stack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new BusySpinner { Width = 40, Height = 40 });
        if (!string.IsNullOrEmpty(message))
        {
            var msg = new TextBlock { Text = message, HorizontalAlignment = HorizontalAlignment.Center };
            msg[!TextBlock.ForegroundProperty] = Dyn("BTextInverseBrush");
            stack.Children.Add(msg);
        }
        return new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.4),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true, // swallow clicks — non-dismissable
        };
    }

    // ── notify (toast) ────────────────────────────────────────────────────────

    public void Notify(string message, NotifyVariant variant = NotifyVariant.Info)
    {
        var host = Host;
        var container = FindOrCreateToastContainer(host);

        var toast = new Border
        {
            Padding = new Thickness(16, 10),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new TextBlock { Text = message, Foreground = Brushes.White },
        };
        toast[!TemplatedControl.BackgroundProperty] = Dyn(variant switch
        {
            NotifyVariant.Success => "BColorSuccessBrush",
            NotifyVariant.Warning => "BColorWarningBrush",
            NotifyVariant.Error => "BColorDangerBrush",
            _ => "BColorInfoBrush",
        });
        container.Children.Add(toast);

        DispatcherTimer.RunOnce(() => container.Children.Remove(toast),
            TimeSpan.FromMilliseconds(variant == NotifyVariant.Error ? 6000 : 4000));
    }

    private static StackPanel FindOrCreateToastContainer(Panel host)
    {
        foreach (var child in host.Children)
            if (child is StackPanel { Name: "PART_ToastContainer" } sp)
                return sp;

        var container = new StackPanel
        {
            Name = "PART_ToastContainer",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16),
            Spacing = 4,
            ZIndex = 10000,
        };
        host.Children.Add(container);
        return container;
    }

    // ── shared helpers ────────────────────────────────────────────────────────

    private void Present(Modal modal) => Host.Children.Add(modal);

    private void Dismiss(Modal modal)
    {
        modal.IsOpen = false;
        Host.Children.Remove(modal);
    }

    /// <summary>Resolve as cancel when the Modal's backdrop click flips IsOpen to false.</summary>
    private static void WireBackdropCancel(Modal modal, Action onCancel)
    {
        modal.PropertyChanged += (_, e) =>
        {
            if (e.Property == Modal.IsOpenProperty && !modal.IsOpen) onCancel();
        };
    }

    private static StackPanel ButtonRow() => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8,
    };

    private TextBlock MessageText(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        [!TextBlock.ForegroundProperty] = Dyn("BTextSecondaryBrush"),
    };

    /// <summary>A token-styled button. The framework ships only a primary Button theme, so danger /
    /// secondary emphasis is applied here via token brushes.</summary>
    private static Button MakeButton(string text, DialogVariant variant, bool secondary = false)
    {
        var btn = new Button { Content = text };
        if (secondary)
        {
            btn[!TemplatedControl.BackgroundProperty] = Dyn("BColorSecondaryBrush");
            btn[!TemplatedControl.ForegroundProperty] = Dyn("BTextBrush");
        }
        else if (variant == DialogVariant.Danger)
        {
            btn[!TemplatedControl.BackgroundProperty] = Dyn("BColorDangerBrush");
            btn[!TemplatedControl.ForegroundProperty] = Dyn("BTextInverseBrush");
        }
        return btn;
    }

    private static DynamicResourceExtension Dyn(string key) => new(key);
}
