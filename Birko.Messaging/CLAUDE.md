# Birko.Messaging

## Overview
Core messaging framework providing unified interfaces for email, SMS, and push notifications, plus a built-in SMTP email sender and string template engine.

## Project Location
`C:\Source\Birko.Messaging\` (shared project: `.shproj` + `.projitems`)

## Components

### Core (`Birko.Messaging`)
- **IMessage** - Base interface (Id, Recipients, Body, ScheduledAt, Metadata)
- **IMessageSender\<T\>** - Generic sender (SendAsync, SendBatchAsync)
- **IMessageTemplate** - Template definition (Name, Subject, BodyTemplate, IsHtml)
- **MessageAddress** - Recipient address (Value + DisplayName), case-insensitive equality
- **MessageAttachment** - File attachment (FileName, ContentType, Stream, IsInline, ContentId)
- **MessageResult** - Result with static factories (Succeeded/Failed), includes MessageId and Timestamp
- **MessagingException** - Base exception hierarchy (MessageDeliveryException, InvalidRecipientException, TemplateRenderException)

### Email (`Birko.Messaging.Email`)
- **EmailMessage** - IMessage with From, To, Cc, Bcc, ReplyTo, Subject, IsHtml, PlainTextBody, Attachments, Priority, Headers
- **EmailSettings** - Extends RemoteSettings (Host/Port/UserName/Password + UseSecure, Timeout, DefaultFrom)
- **IEmailSender** - Extends IMessageSender\<EmailMessage\> with convenience overload
- **SmtpEmailSender** - Built-in SMTP implementation using System.Net.Mail, IDisposable

### SMS (`Birko.Messaging.Sms`)
- **SmsMessage** - IMessage with From (phone number)
- **ISmsSender** - Interface only, implementations in provider projects

### Push (`Birko.Messaging.Push`)
- **PushMessage** - IMessage with Title, ImageUrl, ClickAction, Data, Badge, Sound
- **IPushSender** - Interface only, implementations in provider projects

### Templates (`Birko.Messaging.Templates`)
- **ITemplateEngine** - RenderAsync(template, model) and RenderAsync(IMessageTemplate, model)
- **StringTemplateEngine** - `{{Property.SubProperty}}` replacement via reflection, GeneratedRegex

## Dependencies
- **Birko.Data.Core** - ILoadable\<T\> interface
- **Birko.Data.Stores** - RemoteSettings base class (for EmailSettings)
- **System.Net.Mail** - Built-in .NET SMTP

## Key Patterns
- MessageResult uses static factory pattern (Succeeded/Failed), not constructors
- EmailSettings extends RemoteSettings for host/port/credentials consistency
- SmtpEmailSender is IDisposable (wraps SmtpClient)
- StringTemplateEngine uses GeneratedRegex for performance
- All senders return MessageResult (never throw for delivery failures)

## Maintenance
- When adding new message types, implement IMessage
- When adding new providers, implement IMessageSender\<T\> or the channel-specific interface
- When adding new template engines, implement ITemplateEngine
