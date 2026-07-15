using System;

namespace Birko.Messaging;

public class MessagingException : Exception
{
    public MessagingException(string message) : base(message) { }

    public MessagingException(string message, Exception innerException)
        : base(message, innerException) { }
}

public class MessageDeliveryException : MessagingException
{
    public string? MessageId { get; }

    public MessageDeliveryException(string message, string? messageId = null)
        : base(message)
    {
        MessageId = messageId;
    }

    public MessageDeliveryException(string message, Exception innerException, string? messageId = null)
        : base(message, innerException)
    {
        MessageId = messageId;
    }
}

public class InvalidRecipientException : MessagingException
{
    public string Recipient { get; }

    public InvalidRecipientException(string recipient)
        : base($"Invalid recipient: {recipient}")
    {
        Recipient = recipient;
    }
}

public class TemplateRenderException : MessagingException
{
    public string TemplateName { get; }

    public TemplateRenderException(string templateName, string message)
        : base($"Failed to render template '{templateName}': {message}")
    {
        TemplateName = templateName;
    }

    public TemplateRenderException(string templateName, string message, Exception innerException)
        : base($"Failed to render template '{templateName}': {message}", innerException)
    {
        TemplateName = templateName;
    }
}

/// <summary>
/// Raised when a named template cannot be located (e.g. no matching file). A <see cref="TemplateRenderException"/>
/// subtype so callers can distinguish "not found" (which may fall back to another source) from other render
/// failures such as a path-traversal rejection, which must not be silently swallowed (CR-L299).
/// </summary>
public class TemplateNotFoundException : TemplateRenderException
{
    public TemplateNotFoundException(string templateName, string message)
        : base(templateName, message)
    {
    }
}
