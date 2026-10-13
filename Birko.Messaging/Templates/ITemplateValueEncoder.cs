namespace Birko.Messaging.Templates;

/// <summary>
/// Encodes a merged placeholder value for the format the rendered template is in, so data cannot become markup in it.
/// Passed per render call in <see cref="TemplateRenderOptions.Encoder"/>.
/// </summary>
public interface ITemplateValueEncoder
{
    string Encode(string value);
}
