using System;
using System.IO;

namespace Birko.Messaging;

public sealed class MessageAttachment
{
    public string FileName { get; }
    public string ContentType { get; }
    public Stream Content { get; }
    public bool IsInline { get; }
    public string? ContentId { get; }

    public MessageAttachment(string fileName, string contentType, Stream content,
        bool isInline = false, string? contentId = null)
    {
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        IsInline = isInline;
        ContentId = contentId;
    }
}
