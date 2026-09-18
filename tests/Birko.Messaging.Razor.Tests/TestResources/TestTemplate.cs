namespace Birko.Messaging.Razor.Tests.TestResources;

internal class TestTemplate : IMessageTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
}
