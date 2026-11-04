using System.Globalization;

namespace Birko.Messaging.Templates;

/// <summary>
/// Turns a merged value into text for a placeholder that names it: <c>{{Path|name}}</c> or <c>{{Path|name:ArgPath}}</c>
/// (TASK-815). Registered by name in <see cref="TemplateRenderOptions.Filters"/>.
/// </summary>
public interface ITemplateValueFilter
{
    /// <param name="value">The placeholder's value, resolved from the model (may be <c>null</c>).</param>
    /// <param name="argument">The value at <c>ArgPath</c>, resolved from the same model; <c>null</c> when the placeholder names no argument.</param>
    /// <param name="culture">The culture the template is being rendered in (<see cref="CultureInfo.CurrentCulture"/>).</param>
    /// <exception cref="TemplateRenderException">The value or argument cannot be formatted.</exception>
    string Apply(object? value, object? argument, CultureInfo culture);
}
