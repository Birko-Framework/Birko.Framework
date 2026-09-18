namespace Birko.Xaml.Core.Navigation;

/// <summary>
/// One crumb in a breadcrumb trail (the XAML analogue of Birko.Web's <c>b-breadcrumb</c> items,
/// which are <c>{ label, href? }</c>). Platform-neutral (Avalonia-free). The <b>last</b> crumb is
/// the current location and is never clickable; earlier crumbs become links when they carry a
/// <see cref="Run"/> action (or an <see cref="Href"/> a shell can route on via the control's
/// <c>ItemInvoked</c> event).
/// </summary>
public sealed class BreadcrumbItem
{
    /// <summary>The displayed crumb text.</summary>
    public required string Label { get; init; }

    /// <summary>Optional navigation target (a module id or route) for shells that route by href —
    /// mirrors the web item's <c>href</c>. Surfaced on the control's <c>ItemInvoked</c> event.</summary>
    public string? Href { get; init; }

    /// <summary>Invoked when the crumb is clicked.</summary>
    public Action? Run { get; init; }

    public override string ToString() => Label;
}
