namespace Birko.Messaging;

public interface IMessageTemplate
{
    string Name { get; }
    string Subject { get; }
    string BodyTemplate { get; }
    bool IsHtml { get; }
}
