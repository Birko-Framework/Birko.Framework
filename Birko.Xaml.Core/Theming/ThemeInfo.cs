namespace Birko.Xaml.Core.Theming;

/// <summary>
/// Platform-neutral description of a design-system theme. Mirrors the Birko.Web shell's
/// <c>ThemeOption</c> (<c>{ id, label, icon }</c>) so a XAML app exposes the same switcher
/// metadata as its web counterpart. Deliberately Avalonia-free: the Avalonia (and future WPF)
/// skins map <see cref="Id"/> to their own theme mechanism.
/// </summary>
/// <param name="Id">Stable theme id — matches the CSS <c>data-theme</c> value and the token
/// sheet theme id (<c>light</c>, <c>dark</c>, <c>neon</c>, <c>finstat</c>).</param>
/// <param name="Label">Human-facing switcher label.</param>
/// <param name="IsDark">Whether the theme is dark-based — used by a skin to pick the built-in
/// variant a custom theme inherits from (its light/dark fallback).</param>
/// <param name="Icon">Optional glyph for the switcher (parity with the web shell's icon slot).</param>
public sealed record ThemeInfo(string Id, string Label, bool IsDark, string? Icon = null);
