using Birko.Xaml.Core.Forms;

namespace Birko.Xaml.Core.Dialogs;

/// <summary>Visual emphasis for a dialog's primary action.</summary>
public enum DialogVariant
{
    Primary,
    Danger,
}

/// <summary>Toast severity (mirrors the web <c>notify</c> variants).</summary>
public enum NotifyVariant
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Options for <see cref="IDialogService.ConfirmAsync"/> / <see cref="IDialogService.ConfirmDeleteAsync"/>.</summary>
public sealed class ConfirmOptions
{
    public string? Title { get; set; }
    public string? ConfirmText { get; set; }
    public string? CancelText { get; set; }
    public DialogVariant Variant { get; set; } = DialogVariant.Primary;
}

/// <summary>Options for <see cref="IDialogService.PromptAsync"/>.</summary>
public sealed class PromptOptions
{
    public string? Title { get; set; }
    public string? DefaultValue { get; set; }
    public string? Placeholder { get; set; }
    public string? ConfirmText { get; set; }
    public string? CancelText { get; set; }
    /// <summary>When true, an empty value shows an inline error instead of resolving.</summary>
    public bool Required { get; set; }
}

/// <summary>One selectable option for <see cref="IDialogService.ChooseAsync{T}"/>.</summary>
public sealed class ChooseOption<T>
{
    public string Label { get; set; } = string.Empty;
    public T Value { get; set; } = default!;
    public DialogVariant Variant { get; set; } = DialogVariant.Primary;
}

/// <summary>
/// Imperative dialog helpers — the XAML analogue of <c>birko-web-components/dialogs</c>. Themed,
/// awaitable replacements for the platform's native message boxes, plus a few patterns with no
/// native equivalent (<see cref="ChooseAsync{T}"/>, <see cref="PromptFormAsync"/>, <see cref="BusyAsync{T}"/>).
///
/// The interface is Avalonia-free (Core constraint #1) so view-models depend on it; the Avalonia
/// skin supplies the concrete <c>DialogService</c> that renders each as a token-styled overlay.
/// </summary>
public interface IDialogService
{
    /// <summary>A confirm dialog. Resolves <c>true</c> on confirm, <c>false</c> on cancel / dismiss.</summary>
    Task<bool> ConfirmAsync(string message, ConfirmOptions? options = null);

    /// <summary>A destructive confirm — danger styling with "Delete" / "Cancel" defaults.</summary>
    Task<bool> ConfirmDeleteAsync(string message, ConfirmOptions? options = null);

    /// <summary>A modal acknowledgement (blocking OK). Resolves when dismissed.</summary>
    Task AlertAsync(string message, string? title = null, string? okText = null);

    /// <summary>A single-field text prompt. Resolves the entered string, or <c>null</c> on cancel / dismiss.</summary>
    Task<string?> PromptAsync(string message, PromptOptions? options = null);

    /// <summary>A single-choice dialog — pick one of N options. Resolves the chosen value, or <c>null</c> on dismiss.</summary>
    Task<T?> ChooseAsync<T>(string message, IReadOnlyList<ChooseOption<T>> options, string? title = null);

    /// <summary>A multi-field input dialog over the schema-driven <c>Form</c>, two-way bound to
    /// <paramref name="model"/>. Resolves the (mutated) model on save, or <c>null</c> on cancel / dismiss.
    /// Model-based (not a value dictionary) to match the XAML <c>Form</c>, which binds a POCO.</summary>
    Task<T?> PromptFormAsync<T>(T model, IReadOnlyList<FormField> fields, string? title = null) where T : class;

    /// <summary>Run <paramref name="work"/> behind a non-dismissable spinner overlay, returning its result.</summary>
    Task<T> BusyAsync<T>(Func<Task<T>> work, string? message = null);

    /// <summary>Run <paramref name="work"/> behind a non-dismissable spinner overlay.</summary>
    Task BusyAsync(Func<Task> work, string? message = null);

    /// <summary>Show a transient toast (the soft replacement for a native alert).</summary>
    void Notify(string message, NotifyVariant variant = NotifyVariant.Info);
}
